namespace NesRuntime.Core.Ppu;

/// <summary>Sprite dimensions selected by PPUCTRL bit 5.</summary>
public enum SpriteSize
{
    /// <summary>8x8 pixels; one tile.</summary>
    EightByEight,

    /// <summary>8x16 pixels; a stacked tile pair, tile index chooses the pattern table.</summary>
    EightBySixteen,
}

/// <summary>
/// PPUCTRL ($2000): base nametable, VRAM address increment, sprite/background pattern-table
/// selection, sprite size, and whether an NMI fires at vblank.
/// </summary>
public struct PpuControl
{
    public int BaseNametable;          // 0-3
    public bool IncrementBy32;         // false = +1 (across), true = +32 (down)
    public int SpritePatternTable;     // 0 or 1 (8x8 mode)
    public int BackgroundPatternTable; // 0 or 1
    public SpriteSize SpriteSize;
    public bool GenerateNmiAtVBlank;
}

/// <summary>
/// PPUMASK ($2001): rendering enables, left-column clipping, grayscale, and color emphasis.
/// </summary>
public struct PpuMask
{
    public bool Grayscale;
    public bool ShowBackgroundLeftColumn;
    public bool ShowSpritesLeftColumn;
    public bool ShowBackground;
    public bool ShowSprites;
    public ColorEmphasis Emphasis;

    /// <summary>True when either background or sprite rendering is enabled.</summary>
    public readonly bool RenderingEnabled => ShowBackground || ShowSprites;
}

/// <summary>
/// PPUSTATUS ($2002): the read-only flags the CPU polls — sprite overflow, sprite-0 hit, and the
/// vblank flag (cleared as a side effect of reading this register).
/// </summary>
public struct PpuStatus
{
    public bool SpriteOverflow;
    public bool Sprite0Hit;
    public bool VBlank;
}
