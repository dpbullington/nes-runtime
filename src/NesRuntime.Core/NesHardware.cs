namespace NesRuntime.Core;

/// <summary>
/// Authentic, non-negotiable constants of the NES hardware. The SDK enforces these limits so
/// that content authored against the runtime cannot exceed what the real console could produce.
/// </summary>
public static class NesHardware
{
    // --- Video (2C02 PPU) ---
    public const int ScreenWidth = 256;
    public const int ScreenHeight = 240;
    public const int TileSize = 8;

    /// <summary>Total hardware sprites available in OAM.</summary>
    public const int MaxSprites = 64;

    /// <summary>Sprites the PPU can render on a single scanline before overflow.</summary>
    public const int MaxSpritesPerScanline = 8;

    /// <summary>Tiles per pattern table (a 4 KiB table of 8x8, 2bpp tiles).</summary>
    public const int TilesPerPatternTable = 256;
    public const int PatternTableCount = 2;

    /// <summary>Background palettes and sprite palettes (4 each).</summary>
    public const int BackgroundPaletteCount = 4;
    public const int SpritePaletteCount = 4;

    /// <summary>Selectable colors per palette entry (a palette holds a shared backdrop + 3 colors).</summary>
    public const int ColorsPerPalette = 3;

    /// <summary>Entries in the fixed system (master) palette. 64 slots, ~52 visually distinct.</summary>
    public const int MasterPaletteSize = 64;

    // --- Memory ---
    /// <summary>Internal work RAM (2 KiB), mirrored across $0000-$1FFF.</summary>
    public const int InternalRamSize = 0x0800;

    // --- Audio (2A03 APU) ---
    /// <summary>Pulse 1, Pulse 2, Triangle, Noise, DMC.</summary>
    public const int AudioChannelCount = 5;
}
