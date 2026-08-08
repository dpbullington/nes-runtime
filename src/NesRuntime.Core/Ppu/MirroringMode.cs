namespace NesRuntime.Core.Ppu;

/// <summary>
/// How the four logical nametables alias onto the 2 KiB of physical nametable RAM. Fixed by the
/// cartridge wiring for simple boards, or switched at runtime by the mapper. Determines how
/// backgrounds wrap when scrolling.
/// </summary>
public enum MirroringMode
{
    /// <summary>Two vertical strips; scrolls horizontally without a seam.</summary>
    Horizontal,

    /// <summary>Two horizontal strips; scrolls vertically without a seam.</summary>
    Vertical,

    /// <summary>All four map to the first physical table.</summary>
    SingleScreenLower,

    /// <summary>All four map to the second physical table.</summary>
    SingleScreenUpper,

    /// <summary>Cartridge supplies extra RAM for four independent tables (no mirroring).</summary>
    FourScreen,
}
