using NesRuntime.Sdk.Audio;
using NesRuntime.Sdk.Graphics;

namespace NesRuntime.Sdk.Content;

/// <summary>
/// Turns authored assets into runtime content. Two paths, both first-class:
/// <list type="bullet">
///   <item><b>High-level import</b> — PNG images become tiles + palettes, WAV clips become DMC
///   samples. The pipeline quantizes to the NES palette and slices to 8x8 tiles for you.</item>
///   <item><b>Native ingest</b> — supply real NES data (CHR pattern tables, NSF songs, iNES
///   cartridges) via <see cref="Native"/> when you already have hardware-format assets.</item>
/// </list>
/// Either way the result honors the console's tile, palette, and channel limits.
/// </summary>
public interface IContentPipeline
{
    /// <summary>Imports an image, slicing it to 8x8 tiles and quantizing to a NES palette.</summary>
    ImageImportResult ImportImage(ReadOnlySpan<byte> pngBytes, ImageImportOptions? options = null);

    /// <summary>Imports an audio clip and encodes it for the DMC sample channel.</summary>
    SampleClip ImportSample(ReadOnlySpan<byte> wavBytes, string name);

    /// <summary>Native NES-format loaders for pre-existing hardware assets.</summary>
    INativeContent Native { get; }
}

/// <summary>Options controlling how an image is quantized and sliced.</summary>
public sealed record ImageImportOptions
{
    /// <summary>Prefix used to name the generated <see cref="TileHandle"/>s.</summary>
    public string NamePrefix { get; init; } = "tile";

    /// <summary>When set, quantize toward these palettes instead of choosing automatically.</summary>
    public IReadOnlyList<int>? PreferredPaletteIndices { get; init; }

    /// <summary>Fail (rather than dither) if the image needs more than the NES can show.</summary>
    public bool StrictColorLimits { get; init; } = true;
}

/// <summary>The tiles and palette produced by importing an image.</summary>
public sealed class ImageImportResult
{
    public IReadOnlyList<TileHandle> Tiles => throw new NotImplementedException();

    /// <summary>The tile layout as a grid, ready to stamp onto a background.</summary>
    public TileHandle[,] TileMap => throw new NotImplementedException();
}
