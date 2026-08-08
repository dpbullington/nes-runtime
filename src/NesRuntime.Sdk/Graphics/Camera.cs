namespace NesRuntime.Sdk.Graphics;

/// <summary>
/// The scrolling viewport over the background. Move it to scroll the world; the SDK translates
/// the position into the PPU's fine/coarse scroll each frame. Mid-frame scroll splits (a fixed
/// status bar over a scrolling playfield) are available via <see cref="SetSplit"/>, which the SDK
/// implements with a sprite-0 hit or mapper IRQ under the hood.
/// </summary>
public sealed class Camera
{
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>The scanline at which a scroll split takes effect, or <c>null</c> for none.</summary>
    public int? SplitScanline { get; private set; }

    /// <summary>The scroll applied below <see cref="SplitScanline"/>.</summary>
    public (int X, int Y) SplitScroll { get; private set; }

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void MoveBy(int dx, int dy)
    {
        X += dx;
        Y += dy;
    }

    /// <summary>
    /// Splits the screen at a scanline, applying a different scroll below it — e.g. a fixed HUD on
    /// top and a scrolling level beneath. Pass <c>null</c> to remove the split. (The split is
    /// recorded here; the renderer applies it once mid-frame scroll changes are wired.)
    /// </summary>
    public void SetSplit(int? atScanline, int belowX = 0, int belowY = 0)
    {
        SplitScanline = atScanline;
        SplitScroll = (belowX, belowY);
    }
}
