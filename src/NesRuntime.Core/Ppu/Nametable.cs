namespace NesRuntime.Core.Ppu;

/// <summary>
/// A background layout map: a 32x30 grid of tile indices (960 bytes) plus a 64-byte attribute
/// table that assigns one of four background palettes to each 2x2 group of tiles (a 16x16 pixel
/// region). The PPU has 2 KiB of nametable RAM addressing four logical nametables; how those four
/// alias onto real RAM is set by <see cref="MirroringMode"/>.
/// </summary>
public sealed class Nametable
{
    public const int WidthInTiles = 32;
    public const int HeightInTiles = 30;

    private readonly byte[] _tiles = new byte[WidthInTiles * HeightInTiles];

    public AttributeTable Attributes { get; } = new();

    /// <summary>Gets the pattern-table tile index at a tile coordinate.</summary>
    public byte GetTileIndex(int tileX, int tileY) => _tiles[(tileY * WidthInTiles) + tileX];

    public void SetTileIndex(int tileX, int tileY, byte patternIndex)
        => _tiles[(tileY * WidthInTiles) + tileX] = patternIndex;
}

/// <summary>
/// The 64-byte attribute table. Each byte packs the palette selection (0-3) for four 2x2 tile
/// quadrants, so palette assignment has a coarse 16x16-pixel granularity — an authentic NES
/// constraint the SDK surfaces rather than hides.
/// </summary>
public sealed class AttributeTable
{
    // One palette (0-3) per 2x2-tile block: 16 blocks across, 15 down.
    private const int BlocksX = Nametable.WidthInTiles / 2;
    private const int BlocksY = (Nametable.HeightInTiles + 1) / 2;

    private readonly byte[] _palettes = new byte[BlocksX * BlocksY];

    /// <summary>Gets the background palette index (0-3) covering the given tile.</summary>
    public int GetPalette(int tileX, int tileY) => _palettes[BlockIndex(tileX, tileY)];

    public void SetPalette(int tileX, int tileY, int paletteIndex)
        => _palettes[BlockIndex(tileX, tileY)] = (byte)(paletteIndex & 0x03);

    private static int BlockIndex(int tileX, int tileY) => ((tileY / 2) * BlocksX) + (tileX / 2);
}
