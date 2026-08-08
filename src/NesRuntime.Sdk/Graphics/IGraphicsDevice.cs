namespace NesRuntime.Sdk.Graphics;

/// <summary>
/// The high-level drawing surface a game sees. It hides pattern tables, attribute bytes, OAM, and
/// the loopy scroll registers behind friendly objects — a tile set, palettes, a background layer,
/// a sprite layer, and a camera — while still enforcing every authentic NES limit underneath.
/// </summary>
public interface IGraphicsDevice
{
    /// <summary>The tile graphics available to backgrounds and sprites (the pattern tables).</summary>
    ITileSet Tiles { get; }

    /// <summary>The four background and four sprite palettes.</summary>
    IPaletteSet Palettes { get; }

    /// <summary>The scrolling tiled background.</summary>
    IBackgroundLayer Background { get; }

    /// <summary>The per-frame sprite list (capacity 64, 8-per-scanline enforced).</summary>
    ISpriteLayer Sprites { get; }

    /// <summary>The scroll position / viewport over the background.</summary>
    Camera Camera { get; }
}
