using NesRuntime.Maui.Controls;
using NesRuntime.Rendering;

namespace NesRuntime.Maui.Hosting;

/// <summary>
/// Presents completed PPU frames onto a <see cref="NesGameView"/>. Because the runtime loop runs
/// on the UI thread (see <see cref="DispatcherFrameClock"/>), presentation is a direct hand-off:
/// the frame is forwarded to the view, which resolves palette indices to pixels and blits them
/// with nearest-neighbor scaling.
/// </summary>
public sealed class MauiVideoBackend : IVideoBackend
{
    private readonly NesGameView _view;

    public MauiVideoBackend(NesGameView view) => _view = view;

    public void Present(FrameBuffer frame) => _view.SubmitFrame(frame);
}
