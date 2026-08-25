namespace NesRuntime.Emulation;

/// <summary>
/// A 2C02 PPU rendered per scanline. It implements the memory-mapped registers ($2000-$2007),
/// internal VRAM (nametables), palette RAM, OAM + OAM DMA, and the "loopy" scroll registers
/// (v/t/x/w). The machine drives it one scanline at a time — rendering each visible line from the
/// current scroll, incrementing the vertical scroll and reloading horizontal scroll between lines —
/// so mid-frame scroll changes and sprite-0-hit timing (status-bar splits) come out right.
/// </summary>
/// <remarks>
/// Scanline-accurate, not dot-accurate: a register write lands at scanline granularity rather than
/// at the exact dot. That handles the vast majority of games; the remaining edge cases (mid-scanline
/// splits, precise sprite-0 dot) would need a dot-level PPU.
/// </remarks>
public sealed class Ppu
{
    public const int Width = 256, Height = 240;

    private readonly IMapper _mapper;
    private readonly byte[] _vram = new byte[0x800];
    private readonly byte[] _palette = new byte[0x20];
    private readonly byte[] _oam = new byte[0x100];
    private readonly bool[] _bgLine = new bool[Width];

    private byte _ctrl, _mask, _oamAddr, _readBuffer, _lastWrite;
    private ushort _v, _t;
    private int _x;
    private bool _w;

    public bool VBlank { get; private set; }
    public bool Sprite0Hit { get; private set; }
    public bool SpriteOverflow { get; private set; }

    public Ppu(IMapper mapper) => _mapper = mapper;

    private bool NmiEnabled => (_ctrl & 0x80) != 0;
    private int VramIncrement => (_ctrl & 0x04) != 0 ? 32 : 1;
    private int BgPatternTable => (_ctrl & 0x10) != 0 ? 0x1000 : 0;
    private int SpritePatternTable => (_ctrl & 0x08) != 0 ? 0x1000 : 0;
    private bool Sprite16 => (_ctrl & 0x20) != 0;
    private bool ShowBg => (_mask & 0x08) != 0;
    private bool ShowSprites => (_mask & 0x10) != 0;
    private bool ShowBgLeft => (_mask & 0x02) != 0;
    private bool ShowSpritesLeft => (_mask & 0x04) != 0;

    /// <summary>True when background or sprite rendering is on (gates the mapper's scanline IRQ clock).</summary>
    public bool RenderingEnabled => ShowBg || ShowSprites;

    // --- Per-frame hooks called by the machine ---

    /// <summary>Vertical blank begins (scanline 241). Returns true if an NMI should fire.</summary>
    public bool EnterVBlank() { VBlank = true; return NmiEnabled; }

    /// <summary>Pre-render scanline (261): clear the per-frame flags and reload scroll from t.</summary>
    public void PreRender()
    {
        VBlank = false;
        Sprite0Hit = false;
        SpriteOverflow = false;
        if (RenderingEnabled) { CopyHorizontal(); CopyVertical(); }
    }

    /// <summary>After a visible scanline: advance vertical scroll and reload horizontal from t.</summary>
    public void EndVisibleScanline()
    {
        if (!RenderingEnabled) return;
        IncrementScrollY();
        CopyHorizontal();
    }

    // --- CPU register interface ($2000-$2007) ---

    public byte ReadRegister(int reg)
    {
        switch (reg & 7)
        {
            case 2:
                byte s = (byte)((VBlank ? 0x80 : 0) | (Sprite0Hit ? 0x40 : 0) | (SpriteOverflow ? 0x20 : 0));
                VBlank = false; _w = false;
                return (byte)(s | (_lastWrite & 0x1F));
            case 4:
                return _oam[_oamAddr];
            case 7:
                byte result;
                ushort a = (ushort)(_v & 0x3FFF);
                if (a < 0x3F00) { result = _readBuffer; _readBuffer = ReadVram(a); }
                else { result = ReadVram(a); _readBuffer = ReadVram((ushort)(a - 0x1000)); }
                _v += (ushort)VramIncrement;
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
            case 0: _ctrl = value; _t = (ushort)((_t & 0xF3FF) | ((value & 3) << 10)); break;
            case 1: _mask = value; break;
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
                _v += (ushort)VramIncrement;
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
            _v += 0x1000; // increment fine Y
        }
        else
        {
            _v = (ushort)(_v & ~0x7000);
            int y = (_v & 0x03E0) >> 5;
            if (y == 29) { y = 0; _v = (ushort)(_v ^ 0x0800); } // wrap, toggle vertical nametable
            else if (y == 31) { y = 0; }
            else { y++; }
            _v = (ushort)((_v & ~0x03E0) | (y << 5));
        }
    }

    private void CopyHorizontal() => _v = (ushort)((_v & 0x7BE0) | (_t & 0x041F));
    private void CopyVertical() => _v = (ushort)((_v & 0x041F) | (_t & 0x7BE0));

    // --- VRAM access ---

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

    private byte Palette(int i) => (byte)(_palette[PaletteIndex((ushort)(0x3F00 + i))] & 0x3F);

    // --- Rendering ---

    /// <summary>Renders one visible scanline (0-239) into <paramref name="fb"/> using the current scroll.</summary>
    public void RenderScanline(int scanline, Span<byte> fb)
    {
        int rowBase = scanline * Width;
        byte backdrop = Palette(0);
        fb.Slice(rowBase, Width).Fill(backdrop);
        Array.Clear(_bgLine, 0, Width);

        if (ShowBg) RenderBackgroundLine(fb, rowBase);
        if (ShowSprites) RenderSpriteLine(scanline, fb, rowBase);
    }

    private void RenderBackgroundLine(Span<byte> fb, int rowBase)
    {
        int fineY = (_v >> 12) & 7;
        int coarseY = (_v >> 5) & 0x1F;
        int originX = ((_v >> 10) & 1) * 256 + (_v & 0x1F) * 8 + _x; // fine X included
        int ntBaseY = 0x2000 + ((_v >> 11) & 1) * 0x800;

        for (int screenX = 0; screenX < Width; screenX++)
        {
            if (screenX < 8 && !ShowBgLeft) continue;

            int px = (originX + screenX) & 0x1FF; // wrap two horizontal nametables (512)
            int localX = px & 0xFF;
            int tileCol = localX >> 3;
            int ntBase = ntBaseY + (px >> 8) * 0x400;

            byte tile = ReadVram((ushort)(ntBase + coarseY * 32 + tileCol));
            byte attr = ReadVram((ushort)(ntBase + 0x3C0 + (coarseY / 4) * 8 + (tileCol / 4)));
            int group = (attr >> (((coarseY & 2) << 1) | (tileCol & 2))) & 3;

            int chr = BgPatternTable + tile * 16 + fineY;
            int bit = 7 - (localX & 7);
            int value = ((_mapper.ChrRead((ushort)chr) >> bit) & 1)
                      | (((_mapper.ChrRead((ushort)(chr + 8)) >> bit) & 1) << 1);
            if (value == 0) continue;

            fb[rowBase + screenX] = Palette(group * 4 + value);
            _bgLine[screenX] = true;
        }
    }

    private void RenderSpriteLine(int y, Span<byte> fb, int rowBase)
    {
        int height = Sprite16 ? 16 : 8;
        Span<int> line = stackalloc int[8];
        int count = 0;
        for (int i = 0; i < 64 && count < 8; i++)
        {
            int top = _oam[i * 4] + 1;
            if (y < top || y >= top + height) continue;
            line[count++] = i;
        }
        if (count == 8) SpriteOverflow = true;

        for (int x = 0; x < Width; x++)
        {
            if (x < 8 && !ShowSpritesLeft) continue;

            for (int k = 0; k < count; k++)
            {
                int i = line[k];
                int x0 = _oam[i * 4 + 3];
                if (x < x0 || x >= x0 + 8) continue;

                byte tile = _oam[i * 4 + 1], attr = _oam[i * 4 + 2];
                bool behind = (attr & 0x20) != 0, flipH = (attr & 0x40) != 0, flipV = (attr & 0x80) != 0;

                int row = y - (_oam[i * 4] + 1); if (flipV) row = height - 1 - row;
                int col = x - x0; if (flipH) col = 7 - col;

                int chr;
                if (Sprite16)
                {
                    int table = (tile & 1) * 0x1000, baseTile = tile & 0xFE;
                    chr = table + (row < 8 ? baseTile : baseTile + 1) * 16 + (row & 7);
                }
                else
                {
                    chr = SpritePatternTable + tile * 16 + row;
                }

                int bit = 7 - col;
                int value = ((_mapper.ChrRead((ushort)chr) >> bit) & 1)
                          | (((_mapper.ChrRead((ushort)(chr + 8)) >> bit) & 1) << 1);
                if (value == 0) continue;

                if (i == 0 && _bgLine[x] && x != 255)
                    Sprite0Hit = true;

                if (!(behind && _bgLine[x]))
                    fb[rowBase + x] = Palette(0x10 + (attr & 3) * 4 + value);

                break; // first opaque sprite pixel wins
            }
        }
    }
}
