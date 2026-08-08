namespace NesRuntime.Core.Ppu;

/// <summary>
/// The PPU's 32-byte palette RAM ($3F00-$3F1F). Layout: one universal backdrop color, then four
/// background palettes and four sprite palettes of three colors each. Every entry is a
/// <see cref="NesColor"/> index into the <see cref="MasterPalette"/>.
/// </summary>
public sealed class PaletteMemory
{
    private readonly PaletteEntry[] _background = new PaletteEntry[NesHardware.BackgroundPaletteCount];
    private readonly PaletteEntry[] _sprite = new PaletteEntry[NesHardware.SpritePaletteCount];

    /// <summary>The universal background/backdrop color shown where nothing else is drawn.</summary>
    public NesColor Backdrop { get; set; }

    /// <summary>Reads one of the four background palettes (index 0-3).</summary>
    public PaletteEntry GetBackgroundPalette(int index) => _background[index];

    /// <summary>Reads one of the four sprite palettes (index 0-3).</summary>
    public PaletteEntry GetSpritePalette(int index) => _sprite[index];

    public void SetBackgroundPalette(int index, PaletteEntry palette) => _background[index] = palette;

    public void SetSpritePalette(int index, PaletteEntry palette) => _sprite[index] = palette;
}

/// <summary>
/// A single 4-color palette slot as seen by the PPU: color 0 is transparent (it shows the shared
/// backdrop), colors 1-3 are opaque system-palette indices.
/// </summary>
public readonly record struct PaletteEntry(NesColor Color1, NesColor Color2, NesColor Color3)
{
    /// <summary>Returns the opaque color for a 2bpp pixel value 1-3 (value 0 is transparent).</summary>
    public NesColor ColorFor(int pixelValue) => pixelValue switch
    {
        1 => Color1,
        2 => Color2,
        3 => Color3,
        _ => Color1,
    };
}
