using System.Runtime.CompilerServices;

namespace NesRuntime.Emulation;

/// <summary>
/// A dot-accurate 2C02 PPU. It renders one dot (pixel clock) per internal tick and <see cref="Run"/>
/// advances a batch of dots — the machine runs it three dots per CPU cycle, applying CPU register
/// writes between instructions — so a mid-scanline change to scroll ($2005/$2006), mask/palette
/// ($2001/$3Fxx), or the mapper's CHR/nametable banking affects only the pixels drawn after it. That
/// makes mid-frame raster tricks (split scrolls, per-line palette/CHR swaps) come out right.
/// </summary>
/// <remarks>
/// The background runs the real fetch pipeline (nametable / attribute / two pattern bytes every 8
/// dots into 16-bit shift registers, with the loopy v/t/x scroll increments and copies), and sprites
/// are evaluated + pattern-fetched at the end of each line into per-sprite shifters, then composited
/// per dot with priority, transparency, flipping, 8×8 / 8×16, and a dot-exact sprite-0 hit. The one
/// gap versus silicon: because the CPU executes whole instructions atomically, a register write lands
/// at instruction granularity (within a few CPU cycles / a handful of dots of its true position)
/// rather than at the exact store cycle — enough for the raster effects games actually use.
///
/// Performance-sensitive: the per-dot loop caches the $2000/$2001 decode into fields (updated on
/// write) so the hot path never re-masks control bits, and <see cref="Run"/> keeps the whole dot loop
/// inside one method so the JIT can inline the tick.
/// </remarks>
public sealed class Ppu
{
    public const int Width = 256, Height = 240;

    private readonly IMapper _mapper;
    private readonly byte[] _vram = new byte[0x800];
    private readonly byte[] _palette = new byte[0x20];
    private readonly byte[] _oam = new byte[0x100];
    private readonly byte[] _fb = new byte[Width * Height];

    private byte _oamAddr, _readBuffer, _lastWrite;
    private ushort _v, _t;
    private int _x;
    private bool _w;

    // Cached decode of $2000/$2001 so the per-dot hot path avoids re-masking control bits every dot.
    private bool _renderBg, _renderSprites, _renderBgLeft, _renderSpritesLeft, _rendering;
    private bool _sprite16, _nmiEnabled;
    private int _bgPatternBase, _spritePatternBase, _vramInc = 1;

    // Dot position and frame state.
    private int _scanline, _dot;
    private bool _frameComplete, _nmiLatch, _nmiPrev;

    // Background fetch pipeline.
    private byte _ntByte, _atByte, _patLoLatch, _patHiLatch;
    private ushort _bgPatLo, _bgPatHi, _bgAtLo, _bgAtHi;

    // Sprites evaluated for the next scanline.
    private readonly byte[] _spriteY = new byte[8];
    private readonly byte[] _spriteTile = new byte[8];
    private readonly byte[] _spriteAttr = new byte[8];
    private readonly byte[] _spriteX = new byte[8];
    private readonly byte[] _spriteShiftLo = new byte[8];
    private readonly byte[] _spriteShiftHi = new byte[8];
    private int _spriteCount;
    private bool _spriteZeroPossible, _spriteZeroRendered;

    public bool VBlank { get; private set; }
    public bool Sprite0Hit { get; private set; }
    public bool SpriteOverflow { get; private set; }

    public Ppu(IMapper mapper) => _mapper = mapper;

    /// <summary>True when background or sprite rendering is on.</summary>
    public bool RenderingEnabled => _rendering;

    // --- Machine-facing frame driver ---

    /// <summary>Resets to the top-left dot for a new frame.</summary>
    public void BeginFrame()
    {
        _scanline = 0;
        _dot = 0;
        _frameComplete = false;
    }

    /// <summary>True once the dot clock has wrapped past the pre-render line (a full frame elapsed).</summary>
    public bool FrameComplete => _frameComplete;

    /// <summary>Returns and clears a pending NMI edge (vblank start with NMI enabled).</summary>
    public bool ConsumeNmi() { bool n = _nmiLatch; _nmiLatch = false; return n; }

    /// <summary>Copies the completed frame's 256×240 palette indices into <paramref name="dest"/>.</summary>
    public void CopyFrame(Span<byte> dest) => _fb.AsSpan(0, Math.Min(dest.Length, _fb.Length)).CopyTo(dest);

    /// <summary>Advances the PPU by <paramref name="dots"/> dots, stopping early if the frame completes.</summary>
    public void Run(int dots)
    {
        while (dots-- > 0)
        {
            Tick();
            if (_frameComplete) return;
        }
    }

    private void Tick()
    {
        int dot = _dot, scanline = _scanline;
        bool visibleLine = scanline < Height;

        if (_rendering && (visibleLine || scanline == 261))
        {
            if ((dot >= 2 && dot <= 257) || (dot >= 321 && dot <= 337))
            {
                _bgPatLo <<= 1; _bgPatHi <<= 1; _bgAtLo <<= 1; _bgAtHi <<= 1;

                if (_renderSprites && dot < 258)
                {
                    for (int i = 0; i < _spriteCount; i++)
                    {
                        if (_spriteX[i] > 0) _spriteX[i]--;
                        else { _spriteShiftLo[i] <<= 1; _spriteShiftHi[i] <<= 1; }
                    }
                }

                switch ((dot - 1) & 7)
                {
                    case 0: ReloadShifters(); _ntByte = ReadVram((ushort)(0x2000 | (_v & 0x0FFF))); break;
                    case 2: _atByte = FetchAttribute(); break;
                    case 4: _patLoLatch = FetchPattern(0); break;
                    case 6: _patHiLatch = FetchPattern(8); break;
                    case 7: IncrementScrollX(); break;
                }
            }

            if (dot == 256) IncrementScrollY();
            if (dot == 257) { ReloadShifters(); CopyHorizontal(); if (visibleLine) EvaluateSprites(); }
            if (dot == 260) _mapper.OnScanline();
            if (dot == 340 && visibleLine) FetchSprites();
            if (dot == 338 || dot == 340) _ntByte = ReadVram((ushort)(0x2000 | (_v & 0x0FFF)));
            if (scanline == 261 && dot >= 280 && dot <= 304) CopyVertical();
        }

        if (scanline == 241 && dot == 1) { VBlank = true; UpdateNmi(); }
        else if (scanline == 261 && dot == 1) { VBlank = false; Sprite0Hit = false; SpriteOverflow = false; UpdateNmi(); }

        if (visibleLine && dot >= 1 && dot <= Width) RenderPixel();

        if (++_dot > 340)
        {
            _dot = 0;
            if (++_scanline > 261) { _scanline = 0; _frameComplete = true; }
        }
    }

    // --- Per-dot rendering ---

    private void RenderPixel()
    {
        int px = _dot - 1;

        int bgPixel = 0, bgPal = 0;
        if (_renderBg && (px >= 8 || _renderBgLeft))
        {
            ushort mux = (ushort)(0x8000 >> _x);
            int p0 = (_bgPatLo & mux) != 0 ? 1 : 0;
            int p1 = (_bgPatHi & mux) != 0 ? 1 : 0;
            bgPixel = (p1 << 1) | p0;
            int a0 = (_bgAtLo & mux) != 0 ? 1 : 0;
            int a1 = (_bgAtHi & mux) != 0 ? 1 : 0;
            bgPal = (a1 << 1) | a0;
        }

        int fgPixel = 0, fgPal = 0; bool fgPriority = false;
        _spriteZeroRendered = false;
        if (_renderSprites && (px >= 8 || _renderSpritesLeft))
        {
            for (int i = 0; i < _spriteCount; i++)
            {
                if (_spriteX[i] != 0) continue;
                int p0 = (_spriteShiftLo[i] & 0x80) != 0 ? 1 : 0;
                int p1 = (_spriteShiftHi[i] & 0x80) != 0 ? 1 : 0;
                int p = (p1 << 1) | p0;
                if (p == 0) continue;
                fgPixel = p;
                fgPal = (_spriteAttr[i] & 3) + 4;
                fgPriority = (_spriteAttr[i] & 0x20) == 0;
                if (i == 0) _spriteZeroRendered = true;
                break;
            }
        }

        int pixel, pal;
        if (bgPixel == 0) { pixel = fgPixel; pal = fgPal; }
        else if (fgPixel == 0) { pixel = bgPixel; pal = bgPal; }
        else
        {
            if (fgPriority) { pixel = fgPixel; pal = fgPal; } else { pixel = bgPixel; pal = bgPal; }

            if (_spriteZeroPossible && _spriteZeroRendered && _renderBg && _renderSprites && px != 255)
            {
                int minPx = (!_renderBgLeft || !_renderSpritesLeft) ? 8 : 0;
                if (px >= minPx) Sprite0Hit = true;
            }
        }

        _fb[_scanline * Width + px] = Palette(pixel == 0 ? 0 : pal * 4 + pixel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte FetchAttribute()
    {
        ushort a = (ushort)(0x23C0 | (_v & 0x0C00) | ((_v >> 4) & 0x38) | ((_v >> 2) & 0x07));
        int shift = ((_v >> 4) & 4) | (_v & 2);
        return (byte)((ReadVram(a) >> shift) & 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte FetchPattern(int plane)
    {
        int fineY = (_v >> 12) & 7;
        return ReadVram((ushort)(_bgPatternBase + _ntByte * 16 + fineY + plane));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReloadShifters()
    {
        _bgPatLo = (ushort)((_bgPatLo & 0xFF00) | _patLoLatch);
        _bgPatHi = (ushort)((_bgPatHi & 0xFF00) | _patHiLatch);
        _bgAtLo = (ushort)((_bgAtLo & 0xFF00) | ((_atByte & 1) != 0 ? 0xFF : 0));
        _bgAtHi = (ushort)((_bgAtHi & 0xFF00) | ((_atByte & 2) != 0 ? 0xFF : 0));
    }

    private void EvaluateSprites()
    {
        _spriteCount = 0;
        _spriteZeroPossible = false;
        int height = _sprite16 ? 16 : 8;
        int i = 0;
        for (; i < 64 && _spriteCount < 8; i++)
        {
            int diff = _scanline - _oam[i * 4];
            if (diff < 0 || diff >= height) continue;
            if (i == 0) _spriteZeroPossible = true;
            _spriteY[_spriteCount] = _oam[i * 4];
            _spriteTile[_spriteCount] = _oam[i * 4 + 1];
            _spriteAttr[_spriteCount] = _oam[i * 4 + 2];
            _spriteX[_spriteCount] = _oam[i * 4 + 3];
            _spriteCount++;
        }
        for (; i < 64; i++)
        {
            int diff = _scanline - _oam[i * 4];
            if (diff >= 0 && diff < height) { SpriteOverflow = true; break; }
        }
    }

    private void FetchSprites()
    {
        for (int i = 0; i < _spriteCount; i++)
        {
            int tile = _spriteTile[i], attr = _spriteAttr[i];
            bool flipH = (attr & 0x40) != 0, flipV = (attr & 0x80) != 0;
            int row = _scanline - _spriteY[i];

            ushort addr;
            if (!_sprite16)
            {
                int r = flipV ? 7 - row : row;
                addr = (ushort)(_spritePatternBase + tile * 16 + r);
            }
            else
            {
                int r = flipV ? 15 - row : row;
                int baseTile = (tile & 0xFE) + (r < 8 ? 0 : 1);
                addr = (ushort)((tile & 1) * 0x1000 + baseTile * 16 + (r & 7));
            }

            byte lo = _mapper.ChrRead(addr), hi = _mapper.ChrRead((ushort)(addr + 8));
            if (flipH) { lo = Reverse(lo); hi = Reverse(hi); }
            _spriteShiftLo[i] = lo; _spriteShiftHi[i] = hi;
        }
    }

    private static byte Reverse(byte b)
    {
        b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
        b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
        b = (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
        return b;
    }

    private void UpdateNmi()
    {
        bool line = VBlank && _nmiEnabled;
        if (line && !_nmiPrev) _nmiLatch = true;
        _nmiPrev = line;
    }

    // --- CPU register interface ($2000-$2007) ---

    public byte ReadRegister(int reg)
    {
        switch (reg & 7)
        {
            case 2:
                byte s = (byte)((VBlank ? 0x80 : 0) | (Sprite0Hit ? 0x40 : 0) | (SpriteOverflow ? 0x20 : 0));
                VBlank = false; _w = false; UpdateNmi();
                return (byte)(s | (_lastWrite & 0x1F));
            case 4:
                return _oam[_oamAddr];
            case 7:
                byte result;
                ushort a = (ushort)(_v & 0x3FFF);
                if (a < 0x3F00) { result = _readBuffer; _readBuffer = ReadVram(a); }
                else { result = ReadVram(a); _readBuffer = ReadVram((ushort)(a - 0x1000)); }
                _v += (ushort)_vramInc;
                return result;
            default:
                return _lastWrite;
        }
    }

    public void WriteRegister(int reg, byte value)
    {
        _lastWrite = value;
        switch (reg & 7)
        {
            case 0:
                _t = (ushort)((_t & 0xF3FF) | ((value & 3) << 10));
                _nmiEnabled = (value & 0x80) != 0;
                _vramInc = (value & 0x04) != 0 ? 32 : 1;
                _bgPatternBase = (value & 0x10) != 0 ? 0x1000 : 0;
                _spritePatternBase = (value & 0x08) != 0 ? 0x1000 : 0;
                _sprite16 = (value & 0x20) != 0;
                UpdateNmi();
                break;
            case 1:
                _renderBg = (value & 0x08) != 0;
                _renderSprites = (value & 0x10) != 0;
                _renderBgLeft = (value & 0x02) != 0;
                _renderSpritesLeft = (value & 0x04) != 0;
                _rendering = _renderBg || _renderSprites;
                break;
            case 3: _oamAddr = value; break;
            case 4: _oam[_oamAddr++] = value; break;
            case 5:
                if (!_w) { _x = value & 7; _t = (ushort)((_t & 0xFFE0) | (value >> 3)); _w = true; }
                else { _t = (ushort)((_t & 0x8FFF) | ((value & 7) << 12)); _t = (ushort)((_t & 0xFC1F) | ((value & 0xF8) << 2)); _w = false; }
                break;
            case 6:
                if (!_w) { _t = (ushort)((_t & 0x00FF) | ((value & 0x3F) << 8)); _w = true; }
                else { _t = (ushort)((_t & 0xFF00) | value); _v = _t; _w = false; }
                break;
            case 7:
                WriteVram((ushort)(_v & 0x3FFF), value);
                _v += (ushort)_vramInc;
                break;
        }
    }

    /// <summary>OAM DMA ($4014): copies a 256-byte CPU page into OAM starting at OAMADDR.</summary>
    public void OamDma(ReadOnlySpan<byte> page)
    {
        for (int i = 0; i < 256; i++)
            _oam[(byte)(_oamAddr + i)] = page[i];
    }

    // --- Loopy scroll updates ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncrementScrollX()
    {
        if ((_v & 0x001F) == 31) { _v = (ushort)(_v & ~0x001F); _v ^= 0x0400; }
        else _v++;
    }

    private void IncrementScrollY()
    {
        if ((_v & 0x7000) != 0x7000)
        {
            _v += 0x1000;
        }
        else
        {
            _v = (ushort)(_v & ~0x7000);
            int y = (_v & 0x03E0) >> 5;
            if (y == 29) { y = 0; _v ^= 0x0800; }
            else if (y == 31) { y = 0; }
            else { y++; }
            _v = (ushort)((_v & ~0x03E0) | (y << 5));
        }
    }

    private void CopyHorizontal() => _v = (ushort)((_v & 0x7BE0) | (_t & 0x041F));
    private void CopyVertical() => _v = (ushort)((_v & 0x041F) | (_t & 0x7BE0));

    // --- VRAM access ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadVram(ushort a)
    {
        if (a < 0x2000) return _mapper.ChrRead(a);
        if (a < 0x3F00) return _vram[MirrorNametable(a)];
        return _palette[PaletteIndex(a)];
    }

    private void WriteVram(ushort a, byte value)
    {
        if (a < 0x2000) _mapper.ChrWrite(a, value);
        else if (a < 0x3F00) _vram[MirrorNametable(a)] = value;
        else _palette[PaletteIndex(a)] = value;
    }

    private int MirrorNametable(ushort a)
    {
        int rel = (a - 0x2000) & 0x0FFF;
        int table = rel >> 10, offset = rel & 0x3FF;
        int physical = _mapper.Mirroring switch
        {
            Mirroring.Horizontal => (table >> 1) & 1,
            Mirroring.Vertical => table & 1,
            Mirroring.SingleScreenLower => 0,
            Mirroring.SingleScreenUpper => 1,
            _ => table & 1,
        };
        return physical * 0x400 + offset;
    }

    private static int PaletteIndex(ushort a)
    {
        int i = a & 0x1F;
        if (i >= 0x10 && (i & 3) == 0) i -= 0x10;
        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Palette(int i) => (byte)(_palette[PaletteIndex((ushort)(0x3F00 + i))] & 0x3F);
}
