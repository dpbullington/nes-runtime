namespace NesRuntime.Rendering;

/// <summary>
/// Paces the runtime to the console's frame rate (≈60.10 Hz NTSC, ≈50.01 Hz PAL). A host backs
/// this with a display-synced callback (e.g. a MAUI/Skia render loop) or a timer. The runtime
/// advances one frame per tick.
/// </summary>
public interface IFrameClock
{
    /// <summary>Target frames per second the clock is pacing to.</summary>
    double TargetFps { get; }

    /// <summary>Raised once per frame; the runtime steps the console in the handler.</summary>
    event EventHandler<FrameTick> Tick;

    void Start();
    void Stop();
}

/// <summary>Timing data delivered with each <see cref="IFrameClock.Tick"/>.</summary>
public readonly record struct FrameTick(long FrameNumber, double DeltaSeconds);
