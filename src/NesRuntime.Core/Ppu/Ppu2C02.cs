using NesRuntime.Core.Memory;

namespace NesRuntime.Core.Ppu;

/// <summary>
/// The 2C02 Picture Processing Unit. Renders a 256x240 image from a background layer (nametables
/// + attribute tables + pattern tables, positioned by a scroll offset) and up to 64 sprites, all
/// colored through palette RAM. Exposes the memory-mapped registers ($2000-$2007), the fine
/// scroll state, sprite-0-hit / overflow signals, and the vblank NMI that paces the frame.
/// </summary>
/// <remarks>
/// The register file ($2000-$2007) is not yet wired for the runtime path — the SDK drives the PPU
/// through the object model (nametables, OAM, palettes, scroll) and calls <see cref="RenderTo"/>
/// to produce a frame. <see cref="Read"/>/<see cref="Write"/> remain to be implemented for a
/// future register-accurate path.
/// </remarks>
public sealed class Ppu2C02 : IMemoryRegion
{
    private const int Width = NesHardware.ScreenWidth;
    private const int Height = NesHardware.ScreenHeight;

    private readonly Nametable[] _nametables = [new(), new()];

    public AddressRange Range => new(0x2000, 0x3FFF);

    // Register state.
    public PpuControl Control;
    public PpuMask Mask;
    public PpuStatus Status;

    // Video memory.
    public PatternTable LeftPatternTable { get; } = new();
    public PatternTable RightPatternTable { get; } = new();
    public PaletteMemory Palettes { get; } = new();
    public ObjectAttributeMemory Oam { get; } = new();
    public MirroringMode Mirroring { get; set; } = MirroringMode.Horizontal;

    /// <summary>Fine/coarse scroll position written through $2005/$2006 (the "loopy" registers).</summary>
    public ScrollState Scroll { get; } = new();

    /// <summary>Raised at the start of vertical blank when <see cref="PpuControl.GenerateNmiAtVBlank"/> is set.</summary>
    public event EventHandler? VBlankStarted;

    /// <summary>Returns one of the pattern tables by half (0 = left/$0000, 1 = right/$1000).</summary>
    public PatternTable GetPatternTable(int half) => half == 0 ? LeftPatternTable : RightPatternTable;

    /// <summary>Returns the logical nametable (0-3), resolved through the current mirroring.</summary>
    public Nametable GetNametable(int index) => _nametables[ResolveNametable(index)];

    /// <summary>
    /// Composites the current background and sprites into <paramref name="framebuffer"/> as one
    /// system-palette index per pixel (length 256*240, row-major). Honors palette assignment,
    /// sprite flipping/priority, 8x8 vs 8x16 sprites, and the authentic 8-sprites-per-scanline
    /// limit (setting <see cref="PpuStatus.SpriteOverflow"/> and dropping the extras).
    /// </summary>
    public void RenderTo(Span<byte> framebuffer)
    {
        byte backdrop = Palettes.Backdrop.Index;
        framebuffer.Fill(backdrop);
        Status.SpriteOverflow = false;
        Status.Sprite0Hit = false;

        RenderBackground(framebuffer, backdrop);
        RenderSprites(framebuffer, backdrop);
    }

    private void RenderBackground(Span<byte> framebuffer, byte backdrop)
    {
        Nametable nt = GetNametable(0);
        PatternTable patterns = GetPatternTable(Control.BackgroundPatternTable);

        for (int y = 0; y < Height; y++)
        {
            int by = ((y + Scroll.Y) % Height + Height) % Height;
            int tileY = by >> 3, pixelY = by & 7;

            for (int x = 0; x < Width; x++)
            {
                int bx = ((x + Scroll.X) % Width + Width) % Width;
                int tileX = bx >> 3, pixelX = bx & 7;

                Tile tile = patterns.GetTile(nt.GetTileIndex(tileX, tileY));
                byte value = tile.GetPixel(pixelX, pixelY);
                if (value == 0)
                    continue; // transparent → keep backdrop

                int palette = nt.Attributes.GetPalette(tileX, tileY);
                framebuffer[(y * Width) + x] = Palettes.GetBackgroundPalette(palette).ColorFor(value).Index;
            }
        }
    }

    private void RenderSprites(Span<byte> framebuffer, byte backdrop)
    {
        int height = Control.SpriteSize == SpriteSize.EightBySixteen ? 16 : 8;
        Span<int> perScanline = stackalloc int[Height];

        // OAM order is priority order: sprite 0 wins, and lower indices overlay higher ones.
        for (int i = 0; i < ObjectAttributeMemory.Capacity; i++)
        {
            OamSprite sprite = Oam[i];
            if (sprite.Y >= Height)
                continue; // parked off-screen

            for (int row = 0; row < height; row++)
            {
                int scanline = sprite.Y + row;
                if (scanline >= Height)
                    break;

                if (perScanline[scanline] >= NesHardware.MaxSpritesPerScanline)
                {
                    Status.SpriteOverflow = true;
                    continue; // authentic dropout: no 9th sprite on this scanline
                }
                perScanline[scanline]++;

                DrawSpriteRow(framebuffer, sprite, row, height, scanline, backdrop, isSprite0: i == 0);
            }
        }
    }

    private void DrawSpriteRow(
        Span<byte> framebuffer, OamSprite sprite, int row, int height, int scanline, byte backdrop, bool isSprite0)
    {
        int srcRow = sprite.FlipVertically ? height - 1 - row : row;
        Tile tile = ResolveSpriteTile(sprite, srcRow, out int tileRow);
        PaletteEntry palette = Palettes.GetSpritePalette(sprite.Palette);

        for (int col = 0; col < 8; col++)
        {
            int srcCol = sprite.FlipHorizontally ? 7 - col : col;
            byte value = tile.GetPixel(srcCol, tileRow);
            if (value == 0)
                continue; // transparent sprite pixel

            int x = sprite.X + col;
            if (x >= NesHardware.ScreenWidth)
                break;

            int index = (scanline * NesHardware.ScreenWidth) + x;
            bool backgroundOpaque = framebuffer[index] != backdrop;

            if (isSprite0 && backgroundOpaque)
                Status.Sprite0Hit = true;

            if (sprite.BehindBackground && backgroundOpaque)
                continue; // background has priority over a low-priority sprite

            framebuffer[index] = palette.ColorFor(value).Index;
        }
    }

    private Tile ResolveSpriteTile(OamSprite sprite, int srcRow, out int tileRow)
    {
        if (Control.SpriteSize == SpriteSize.EightBySixteen)
        {
            // 8x16: bit 0 of the tile index selects the pattern table; the pair is index & 0xFE.
            PatternTable table = GetPatternTable(sprite.TileIndex & 1);
            int baseTile = sprite.TileIndex & 0xFE;
            bool bottomHalf = srcRow >= 8;
            tileRow = srcRow & 7;
            return table.GetTile(bottomHalf ? baseTile + 1 : baseTile);
        }

        tileRow = srcRow;
        return GetPatternTable(Control.SpritePatternTable).GetTile(sprite.TileIndex);
    }

    private int ResolveNametable(int index)
    {
        // Two physical tables; map the four logical ones per mirroring. (SingleScreen variants
        // collapse to one; FourScreen would need cartridge RAM and is treated as index-passthrough.)
        return Mirroring switch
        {
            MirroringMode.SingleScreenLower => 0,
            MirroringMode.SingleScreenUpper => 1,
            MirroringMode.Vertical => index & 1,
            MirroringMode.Horizontal => (index >> 1) & 1,
            _ => index & 1,
        };
    }

    /// <summary>The completed 256x240 frame as system-palette indices (one byte per pixel).</summary>
    public ReadOnlySpan<byte> Framebuffer => throw new NotImplementedException();

    // IMemoryRegion — the CPU's window onto $2000-$2007 (mirrored to $3FFF). Not yet wired.
    public byte Read(ushort address) => throw new NotImplementedException();

    public void Write(ushort address, byte value) => throw new NotImplementedException();

    internal void RaiseVBlank() => VBlankStarted?.Invoke(this, EventArgs.Empty);
}
