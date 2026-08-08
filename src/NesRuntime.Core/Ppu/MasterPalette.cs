namespace NesRuntime.Core.Ppu;

/// <summary>
/// The fixed 64-entry system palette that maps a <see cref="NesColor"/> index to a displayable
/// 24-bit RGB value. Several canonical decodings exist (2C02 measured, FCEUX, Nestopia); this ships
/// one common decoding. The active table plus the PPU color-emphasis bits determine the final color.
/// </summary>
public sealed class MasterPalette
{
    // A widely used decoding of the 2C02 palette, one 0xRRGGBB entry per system-palette index.
    private static readonly int[] DefaultTable =
    [
        0x7C7C7C, 0x0000FC, 0x0000BC, 0x4428BC, 0x940084, 0xA80020, 0xA81000, 0x881400,
        0x503000, 0x007800, 0x006800, 0x005800, 0x004058, 0x000000, 0x000000, 0x000000,
        0xBCBCBC, 0x0078F8, 0x0058F8, 0x6844FC, 0xD800CC, 0xE40058, 0xF83800, 0xE45C10,
        0xAC7C00, 0x00B800, 0x00A800, 0x00A844, 0x008888, 0x000000, 0x000000, 0x000000,
        0xF8F8F8, 0x3CBCFC, 0x6888FC, 0x9878F8, 0xF878F8, 0xF85898, 0xF87858, 0xFCA044,
        0xF8B800, 0xB8F818, 0x58D854, 0x58F898, 0x00E8D8, 0x787878, 0x000000, 0x000000,
        0xFCFCFC, 0xA4E4FC, 0xB8B8F8, 0xD8B8F8, 0xF8B8F8, 0xF8A4C0, 0xF0D0B0, 0xFCE0A8,
        0xF8D878, 0xD8F878, 0xB8F8B8, 0xB8F8D8, 0x00FCFC, 0xF8D8F8, 0x000000, 0x000000,
    ];

    private readonly int[] _table;

    private MasterPalette(int[] table) => _table = table;

    /// <summary>A widely used default RGB decoding of the 2C02 palette.</summary>
    public static MasterPalette Default { get; } = new(DefaultTable);

    /// <summary>Resolves a system-palette index to packed 0xRRGGBB, honoring emphasis.</summary>
    public int ToRgb(NesColor color, ColorEmphasis emphasis = default)
    {
        int rgb = _table[color.Index & 0x3F];
        return emphasis == ColorEmphasis.None ? rgb : ApplyEmphasis(rgb, emphasis);
    }

    // Emphasis attenuates the channels that are NOT emphasized. This is an approximation of the
    // analog tint the 2C02 produces; a measured decoding would use per-emphasis lookup tables.
    private static int ApplyEmphasis(int rgb, ColorEmphasis emphasis)
    {
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        if (!emphasis.HasFlag(ColorEmphasis.Red)) { g = Attenuate(g); b = Attenuate(b); }
        if (!emphasis.HasFlag(ColorEmphasis.Green)) { r = Attenuate(r); b = Attenuate(b); }
        if (!emphasis.HasFlag(ColorEmphasis.Blue)) { r = Attenuate(r); g = Attenuate(g); }
        return (r << 16) | (g << 8) | b;
    }

    private static int Attenuate(int channel) => channel * 3 / 4;
}

/// <summary>
/// The three PPUMASK color-emphasis bits. Each darkens the other two channels, tinting the whole
/// frame. Combinations are legal (e.g. red+blue).
/// </summary>
[Flags]
public enum ColorEmphasis
{
    None = 0,
    Red = 1 << 0,
    Green = 1 << 1,
    Blue = 1 << 2,
}
