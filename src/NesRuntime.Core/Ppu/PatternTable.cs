namespace NesRuntime.Core.Ppu;

/// <summary>
/// A 4 KiB CHR pattern table holding 256 tiles ($0000 "left" and $1000 "right"). Each tile is an
/// 8x8, 2-bits-per-pixel bitmap stored as two bitplanes. Backgrounds and sprites source their
/// pixels here; the actual bytes come from cartridge CHR-ROM or CHR-RAM.
/// </summary>
public sealed class PatternTable
{
    public const int TileCount = NesHardware.TilesPerPatternTable;

    private readonly Tile[] _tiles = new Tile[TileCount];

    /// <summary>Fetches a tile's 2bpp bitmap by index (0-255).</summary>
    public Tile GetTile(int index) => _tiles[index];

    /// <summary>Writes a tile (only valid when backed by CHR-RAM).</summary>
    public void SetTile(int index, Tile tile) => _tiles[index] = tile;
}

/// <summary>
/// An 8x8, 2-bits-per-pixel tile. Each pixel is a value 0-3 selecting a color within whatever
/// palette the background/sprite assigns; value 0 is transparent.
/// </summary>
public readonly struct Tile
{
    private readonly byte[]? _pixels; // 64 entries, each 0-3, row-major

    private Tile(byte[] pixels) => _pixels = pixels;

    /// <summary>64 pixels, each 0-3, row-major.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels ?? [];

    /// <summary>Reads the 2bpp value (0-3) at a pixel coordinate; 0 for an uninitialized tile.</summary>
    public byte GetPixel(int x, int y) => _pixels is null ? (byte)0 : _pixels[(y << 3) + x];

    /// <summary>Builds a tile directly from 64 pixel values (0-3), row-major.</summary>
    public static Tile FromPixels(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != 64)
            throw new ArgumentException("A tile requires exactly 64 pixels (8x8).", nameof(pixels));

        return new Tile(pixels.ToArray());
    }

    /// <summary>Builds a tile from the NES two-bitplane encoding (16 bytes).</summary>
    public static Tile FromBitplanes(ReadOnlySpan<byte> sixteenBytes)
    {
        if (sixteenBytes.Length != 16)
            throw new ArgumentException("A tile requires exactly 16 bitplane bytes.", nameof(sixteenBytes));

        var pixels = new byte[64];
        for (int row = 0; row < 8; row++)
        {
            byte lo = sixteenBytes[row];
            byte hi = sixteenBytes[row + 8];
            for (int col = 0; col < 8; col++)
            {
                int bit = 7 - col;
                int value = ((lo >> bit) & 1) | (((hi >> bit) & 1) << 1);
                pixels[(row << 3) + col] = (byte)value;
            }
        }

        return new Tile(pixels);
    }

    /// <summary>Encodes this tile back to the 16-byte two-bitplane form.</summary>
    public byte[] ToBitplanes()
    {
        var bytes = new byte[16];
        if (_pixels is null)
            return bytes;

        for (int row = 0; row < 8; row++)
        {
            byte lo = 0, hi = 0;
            for (int col = 0; col < 8; col++)
            {
                int value = _pixels[(row << 3) + col];
                int bit = 7 - col;
                lo |= (byte)((value & 1) << bit);
                hi |= (byte)(((value >> 1) & 1) << bit);
            }
            bytes[row] = lo;
            bytes[row + 8] = hi;
        }

        return bytes;
    }
}
