using NesRuntime.Core;
using NesRuntime.Core.Ppu;

namespace NesRuntime.Rendering;

/// <summary>
/// A completed 256x240 video frame produced by the PPU, as one system-palette index per pixel.
/// A video backend resolves these through a <see cref="MasterPalette"/> to RGB for display, which
/// keeps color decoding (and emphasis/filters) a host concern.
/// </summary>
public sealed class FrameBuffer
{
    private readonly byte[] _indices = new byte[NesHardware.ScreenWidth * NesHardware.ScreenHeight];

    public int Width => NesHardware.ScreenWidth;
    public int Height => NesHardware.ScreenHeight;

    /// <summary>Row-major palette indices, one byte per pixel (length = 256 * 240).</summary>
    public ReadOnlySpan<byte> Indices => _indices;

    /// <summary>The writable pixel buffer a frame producer (the PPU) composites into.</summary>
    public Span<byte> Pixels => _indices;

    /// <summary>Resolves the frame to packed 0xRRGGBB pixels using the given palette/emphasis.</summary>
    public void ResolveTo(Span<int> rgbDestination, MasterPalette palette, ColorEmphasis emphasis = default)
    {
        if (rgbDestination.Length < _indices.Length)
            throw new ArgumentException("Destination is smaller than the frame.", nameof(rgbDestination));

        for (int i = 0; i < _indices.Length; i++)
            rgbDestination[i] = palette.ToRgb(new NesColor(_indices[i]), emphasis);
    }
}
