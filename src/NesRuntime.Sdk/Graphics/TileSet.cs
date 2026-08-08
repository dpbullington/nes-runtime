namespace NesRuntime.Sdk.Graphics;

/// <summary>
/// A lightweight handle to a tile loaded into a pattern table. Games pass these around instead of
/// raw indices; the SDK tracks which pattern table and slot each occupies.
/// </summary>
public readonly record struct TileHandle(int Id)
{
    public bool IsValid => Id >= 0;
}

/// <summary>
/// The game's tile graphics — the CHR pattern tables, presented as a friendly catalog. Tiles come
/// from high-level imports (a PNG sliced into 8x8 cells) or native CHR data. The NES holds two
/// 256-tile tables at once; the SDK reports when a load would exceed that.
/// </summary>
public interface ITileSet
{
    /// <summary>Tiles currently loaded across both pattern tables.</summary>
    int Count { get; }

    /// <summary>Capacity across both pattern tables (512).</summary>
    int Capacity { get; }

    /// <summary>Adds one 8x8 tile from 2bpp pixel data (values 0-3), returning its handle.</summary>
    TileHandle Add(ReadOnlySpan<byte> pixels2bpp);

    /// <summary>Adds a named 8x8 tile so it can be looked up later via the string indexer.</summary>
    TileHandle Add(string name, ReadOnlySpan<byte> pixels2bpp);

    /// <summary>Looks up a previously added tile by name (assigned during import).</summary>
    TileHandle this[string name] { get; }
}
