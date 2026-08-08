using NesRuntime.Core.Ppu;

namespace NesRuntime.Sdk.Graphics;

/// <summary>
/// The game's working palettes: four for backgrounds and four for sprites, each holding three
/// colors plus the shared backdrop. Colors are <see cref="NesColor"/> indices into the fixed
/// 64-entry system palette — you pick from the NES's real colors, not arbitrary RGB.
/// </summary>
public interface IPaletteSet
{
    /// <summary>The universal backdrop color shown behind everything.</summary>
    NesColor Backdrop { get; set; }

    /// <summary>Sets one of the four background palettes (index 0-3).</summary>
    void SetBackground(int index, NesColor color1, NesColor color2, NesColor color3);

    /// <summary>Sets one of the four sprite palettes (index 0-3).</summary>
    void SetSprite(int index, NesColor color1, NesColor color2, NesColor color3);

    /// <summary>Applies a frame-wide tint via the PPU color-emphasis bits.</summary>
    ColorEmphasis Emphasis { get; set; }
}

/// <summary>Convenience names for a handful of well-known system-palette indices.</summary>
public static class SystemColors
{
    public static readonly NesColor Black = new(0x0F);
    public static readonly NesColor White = new(0x30);
    public static readonly NesColor Gray = new(0x00);
    public static readonly NesColor Red = new(0x16);
    public static readonly NesColor Green = new(0x2A);
    public static readonly NesColor Blue = new(0x12);
}
