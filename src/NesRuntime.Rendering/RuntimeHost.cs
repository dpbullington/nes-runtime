namespace NesRuntime.Rendering;

/// <summary>
/// Binds a frame-producing <see cref="IRuntimeCore"/> to a set of host backends and drives the
/// frame loop: on each <see cref="IFrameClock"/> tick it advances the core one frame (which polls
/// input and generates audio through the backends) and presents the resulting image. This is the
/// seam a MAUI (or any) front end plugs into — supply a core, the backends, and a clock.
/// </summary>
public sealed class RuntimeHost
{
    private readonly IRuntimeCore _core;
    private bool _running;

    public IVideoBackend Video { get; }
    public IAudioBackend Audio { get; }
    public IInputBackend Input { get; }
    public IFrameClock Clock { get; }

    public RuntimeHost(
        IRuntimeCore core,
        IVideoBackend video,
        IAudioBackend audio,
        IInputBackend input,
        IFrameClock clock)
    {
        _core = core;
        Video = video;
        Audio = audio;
        Input = input;
        Clock = clock;
    }

    /// <summary>Starts the frame clock and begins driving the core.</summary>
    public void Run()
    {
        if (_running)
            return;

        Clock.Tick += OnTick;
        Clock.Start();
        _running = true;
    }

    /// <summary>Stops the frame clock; the core state is preserved.</summary>
    public void Pause()
    {
        if (!_running)
            return;

        Clock.Stop();
        Clock.Tick -= OnTick;
        _running = false;
    }

    /// <summary>Advances exactly one frame, for stepping/debugging while paused.</summary>
    public void StepOnce() => PresentNextFrame();

    private void OnTick(object? sender, FrameTick tick) => PresentNextFrame();

    private void PresentNextFrame()
    {
        FrameBuffer frame = _core.StepFrame(Input, Audio);
        Video.Present(frame);
    }
}
