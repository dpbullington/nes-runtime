namespace NesRuntime.Rendering;

/// <summary>
/// The frame-producing engine behind a <see cref="RuntimeHost"/>. Each tick the host asks the core
/// to advance one frame — reading input and generating audio through the supplied backends — and
/// hands back the finished image to present. The SDK provides the concrete implementation (it runs
/// the game and drives the PPU); the host stays agnostic of how frames are made.
/// </summary>
public interface IRuntimeCore
{
    /// <summary>
    /// Advances the machine by exactly one video frame and returns the composited image. The same
    /// <see cref="FrameBuffer"/> instance may be reused across calls.
    /// </summary>
    FrameBuffer StepFrame(IInputBackend input, IAudioBackend audio);
}
