namespace NesRuntime.Core.Ppu;

/// <summary>
/// The PPU's scroll position. Backgrounds scroll smoothly with per-pixel (fine) and per-tile
/// (coarse) components on both axes, wrapping across nametables per the mirroring mode. Games
/// often change scroll mid-frame (via a sprite-0-hit or mapper IRQ) for split-screen effects.
/// </summary>
public sealed class ScrollState
{
    /// <summary>Horizontal scroll in pixels (0-511 across two horizontally-adjacent nametables).</summary>
    public int X { get; set; }

    /// <summary>Vertical scroll in pixels (0-479 across two vertically-adjacent nametables).</summary>
    public int Y { get; set; }

    /// <summary>Fine X within the current tile (0-7).</summary>
    public int FineX => X & 7;

    /// <summary>Fine Y within the current tile (0-7).</summary>
    public int FineY => Y & 7;
}
