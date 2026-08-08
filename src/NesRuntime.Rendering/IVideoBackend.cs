namespace NesRuntime.Rendering;

/// <summary>
/// Presents finished frames to a display. A host (MAUI + SkiaSharp, a game window, a headless
/// test capture) implements this; the runtime calls <see cref="Present"/> once per frame. The
/// backend owns scaling, aspect correction, and any CRT-style post-processing.
/// </summary>
public interface IVideoBackend
{
    /// <summary>Uploads and displays the frame. Called on the runtime's frame boundary.</summary>
    void Present(FrameBuffer frame);
}
