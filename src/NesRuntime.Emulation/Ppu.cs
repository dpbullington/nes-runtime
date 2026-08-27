using System.Runtime.CompilerServices;

namespace NesRuntime.Emulation;

/// <summary>
/// A 2C02 PPU with mid-scanline accuracy at scanline-batch speed. It renders each visible line as a
/// fast span (directly from the loopy scroll registers), but renders <em>lazily</em>: a line's pixels
/// are only emitted up to the current dot, and the machine forces a <see cref="CatchUp"/> right before
/// any write that changes the picture (PPU register, palette, or the mapper's CHR/nametable banking)
/// and before every $2002 read. So a mid-scanline scroll/mask/palette/CHR change affects only the
/// pixels drawn after it, and the sprite-0 hit becomes visible at the dot the CPU polls it — i.e.
/// mid-frame raster splits and status-bar splits come out right.
/// </summary>
/// <remarks>
/// The win over a pure per-dot PPU is speed: when a line has no mid-line writes it is drawn in a
/// single span at the end of its active display (dot 256), the same cost as a plain scanline
/// renderer; only the lines that actually do raster tricks pay the finer-grained catch-up cost. The
/// residual inaccuracy is the same as any instruction-atomic CPU: a write lands within a few CPU
/// cycles / a handful of dots of its true position, so a split lands on the right scanline and within
/// a few pixels horizontally, not at the exact dot.
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

    // Cached decode of $2000/$2001 so the render path avoids re-masking control bits.
    private bool _renderBg, _renderSprites, _renderBgLeft, _renderSpritesLeft, _rendering;
    private bool _sprite16, _nmiEnabled;
    private int _bgPatternBase, _spritePatternBase, _vramInc = 1;

    // Time position and per-line lazy-render state.
    private int _scanline, _dot;      // dot clock (scanline 0-261, dot 0-340)
    private int _renderX;             // next visible column already emitted on this line (0-256)
    private int _vBaseCol;            // column at which the current scroll v took effect
    private bool _lineEvaluated;      // sprites evaluated for this line yet
    private bool _frameComplete, _nmiLatch, _nmiPrev;

    // Sprites evaluated for the current visible line (pattern bytes pre-fetched once per line).
    private readonly int[] _sprX = new int[8];
    private readonly int[] _sprAttr = new int[8];
    private readonly byte[] _sprLo = new byte[8];
    private readonly byte[] _sprHi = new byte[8];
    private readonly bool[] _sprIsZero = new bool[8];
    private int _sprCount, _sprHeight = 8;

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
        _renderX = 0;
        _vBaseCol = 0;
        _lineEvaluated = false;
        _frameComplete = false;
    }

    /// <summary>True once the dot clock has wrapped past the pre-render line (a full frame elapsed).</summary>
    public bool FrameComplete => _frameComplete;

    /// <summary>Returns and clears a pending NMI edge (vblank start with NMI enabled).</summary>
    public bool ConsumeNmi() { bool n = _nmiLatch; _nmiLatch = false; return n; }

    /// <summary>Copies the completed frame's 256×240 palette indices into <paramref name="dest"/>.</summary>
    public void CopyFrame(Span<byte> dest) => _fb.AsSpan(0, Math.Min(dest.Length, _fb.Length)).CopyTo(dest);

    /// <summary>
    /// Advances the dot clock by <paramref name="dots"/>, firing scroll/flag events at their dots and
    /// emitting each visible line's pixels when its active display ends (or when <see cref="CatchUp"/>
    /// is forced by a write/read). Stops early if the frame completes.
    /// </summary>
    public void Run(int dots)
    {
        while (dots > 0 && !_frameComplete)
        {
            int seg = Math.Min(341 - _dot, dots);
            ProcessSegment(_dot, _dot + seg);
            _dot += seg;
            dots -= seg;

            if (_dot >= 341)
            {
                _dot = 0;
                _renderX = 0;
                _vBaseCol = 0;
                _lineEvaluated = false;
                if (++_scanline > 261) { _scanline = 0; _frameComplete = true; }
            }
        }
    }

    /// <summary>
    /// Emits the current visible line's pixels up to the current dot. Called before any write that
    /// changes the picture and before $2002 reads, so those observe/precede exactly the pixels that
    /// were already on screen.
    /// </summary>
    public void CatchUp()
    {
        if (_scanline >= Height) return;
        RenderLineUpTo(Math.Clamp(_dot - 1, 0, Width));
    }

    private void ProcessSegment(int from, int to)
    {
        int sl = _scanline;

        if (sl == 241 && from <= 1 && 1 < to) { VBlank = true; UpdateNmi(); }
        else if (sl == 261 && from <= 1 && 1 < to) { VBlank = false; Sprite0Hit = false; SpriteOverflow = false; UpdateNmi(); }

        bool visible = sl < Height;
        bool renderLine = visible || sl == 261;

        if (renderLine && _rendering && from <= 260 && 260 < to)
            _mapper.OnScanline();                       // MMC3-style scanline IRQ, ~A12 rise

        // End of active display: finish the visible line, then apply the hblank scroll increments.
        if (from <= 256 && 256 < to)
        {
            if (visible) RenderLineUpTo(Width);
            if (renderLine && _rendering) IncrementScrollY();
        }
        if (renderLine && _rendering)
        {
            if (from <= 257 && 257 < to) CopyHorizontal();
            if (sl == 261 && from < 305 && to > 280) CopyVertical();
        }
    }

    // --- Lazy scanline rendering ---

    private void RenderLineUpTo(int col)
    {
        if (_scanline >= Height || col <= _renderX) return;
        if (!_lineEvaluated) { if (_renderSprites) EvaluateSpriteLine(); _lineEvaluated = true; }
        RenderSpan(_renderX, col);
        _renderX = col;
    }

    private void RenderSpan(int cStart, int cEnd)
    {
        int rowBase = _scanline * Width;
        int fineY = (_v >> 12) & 7;
        int coarseY = (_v >> 5) & 0x1F;
        int originX = ((_v >> 10) & 1) * 256 + (_v & 0x1F) * 8 + _x;
        int ntBaseY = 0x2000 + ((_v >> 11) & 1) * 0x800;

        // Background tile fetch is cached across a tile's 8 pixels (refetched on a tile boundary).
        int lastTile = -1;
        byte patLo = 0, patHi = 0;
        int tilePal = 0;

        for (int c = cStart; c < cEnd; c++)
        {
            int bgPixel = 0, bgPal = 0;
            if (_renderBg && (c >= 8 || _renderBgLeft))
            {
                int px = (originX + (c - _vBaseCol)) & 0x1FF;
                int tileIndex = px >> 3;
                if (tileIndex != lastTile)
                {
                    lastTile = tileIndex;
                    int localX = px & 0xFF, tileCol = localX >> 3;
                    int ntBase = ntBaseY + (px >> 8) * 0x400;
                    byte tile = ReadVram((ushort)(ntBase + coarseY * 32 + tileCol));
                    byte attr = ReadVram((ushort)(ntBase + 0x3C0 + (coarseY / 4) * 8 + (tileCol / 4)));
                    tilePal = (attr >> (((coarseY & 2) << 1) | (tileCol & 2))) & 3;
                    int chr = _bgPatternBase + tile * 16 + fineY;
                    patLo = _mapper.ChrRead((ushort)chr);
                    patHi = _mapper.ChrRead((ushort)(chr + 8));
                }
                int bit = 7 - (px & 7);
                bgPixel = ((patLo >> bit) & 1) | (((patHi >> bit) & 1) << 1);
                bgPal = tilePal;
            }

            int outPixel = bgPixel, outPal = bgPal;
            if (_renderSprites && (c >= 8 || _renderSpritesLeft))
            {
                for (int k = 0; k < _sprCount; k++)
                {
                    int x0 = _sprX[k];
                    if (c < x0 || c >= x0 + 8) continue;

                    int bit = 7 - (c - x0);
                    int val = ((_sprLo[k] >> bit) & 1) | (((_sprHi[k] >> bit) & 1) << 1);
                    if (val == 0) continue;

                    if (_sprIsZero[k] && _renderBg && bgPixel != 0 && c != 255)
                        Sprite0Hit = true;

                    if (!((_sprAttr[k] & 0x20) != 0 && bgPixel != 0)) { outPixel = val; outPal = (_sprAttr[k] & 3) + 4; }
                    break; // first opaque sprite pixel wins
                }
            }

            _fb[rowBase + c] = Palette(outPixel == 0 ? 0 : outPal * 4 + outPixel);
        }
    }

    private void EvaluateSpriteLine()
    {
        _sprCount = 0;
        _sprHeight = _sprite16 ? 16 : 8;
        int i = 0;
        for (; i < 64 && _sprCount < 8; i++)
        {
            int top = _oam[i * 4] + 1;
            if (_scanline < top || _scanline >= top + _sprHeight) continue;

            int tile = _oam[i * 4 + 1], attr = _oam[i * 4 + 2];
            int row = _scanline - top;
            if ((attr & 0x80) != 0) row = _sprHeight - 1 - row; // flipV

            int chr;
            if (_sprite16)
            {
                int table = (tile & 1) * 0x1000, baseTile = tile & 0xFE;
                chr = table + (row < 8 ? baseTile : baseTile + 1) * 16 + (row & 7);
            }
            else
            {
                chr = _spritePatternBase + tile * 16 + row;
            }

            byte lo = _mapper.ChrRead((ushort)chr), hi = _mapper.ChrRead((ushort)(chr + 8));
            if ((attr & 0x40) != 0) { lo = Reverse(lo); hi = Reverse(hi); } // flipH

            _sprX[_sprCount] = _oam[i * 4 + 3];
            _sprAttr[_sprCount] = attr;
            _sprLo[_sprCount] = lo;
            _sprHi[_sprCount] = hi;
            _sprIsZero[_sprCount] = i == 0;
            _sprCount++;
        }
        for (; i < 64; i++)
        {
            int top = _oam[i * 4] + 1;
            if (_scanline >= top && _scanline < top + _sprHeight) { SpriteOverflow = true; break; }
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
                CatchUp(); // render up to now so a sprite-0-hit poll flips at the right dot
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
        CatchUp(); // any picture-affecting change applies only to pixels drawn after this dot
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
                _vBaseCol = _renderX; // a fine-X / scroll change re-bases the origin for the rest of the line
                break;
            case 6:
                if (!_w) { _t = (ushort)((_t & 0x00FF) | ((value & 0x3F) << 8)); _w = true; }
                else { _t = (ushort)((_t & 0xFF00) | value); _v = _t; _w = false; }
                _vBaseCol = _renderX; // a mid-line v reload jumps the scroll from here on
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

    private void CopyHorizontal() { _v = (ushort)((_v & 0x7BE0) | (_t & 0x041F)); _vBaseCol = 0; }
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
