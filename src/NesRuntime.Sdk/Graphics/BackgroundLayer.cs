namespace NesRuntime.Sdk.Graphics;

/// <summary>
/// The tiled background. Place tiles on a grid and assign each a background palette; the layer
/// writes the nametable and attribute table for you. It spans the console's addressable
/// background space (two nametables wide/tall depending on mirroring), which the camera scrolls
/// across. Palette assignment keeps the NES's coarse 16x16-pixel granularity.
/// </summary>
public interface IBackgroundLayer
{
    int WidthInTiles { get; }
    int HeightInTiles { get; }

    /// <summary>Places a tile at a grid cell using the given background palette (0-3).</summary>
    void SetTile(int tileX, int tileY, TileHandle tile, int paletteIndex);

    /// <summary>Stamps a rectangular block of tiles (e.g. a decoded room/level chunk).</summary>
    void SetArea(int tileX, int tileY, TileHandle[,] tiles, int paletteIndex);

    /// <summary>Clears the whole background to a single tile.</summary>
    void Clear(TileHandle tile, int paletteIndex);
}
