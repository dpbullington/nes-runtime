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
    private readonly IAudioBackend _silentAudio;
    private bool _running;

    public IVideoBackend Video { get; }
    public IAudioBackend Audio { get; }
    public IInputBackend Input { get; }
    public IFrameClock Clock { get; }

    /// <summary>
    /// Emulation frames to advance per displayed frame. 1 = realtime; &gt;1 = fast-forward (only the
    /// last frame of each tick is presented, and the skipped frames' audio is dropped so the audio
    /// buffer doesn't lag). Set it from the host (e.g. while a Fast-Forward control is held).
    /// </summary>
    public int SpeedMultiplier { get; set; } = 1;

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
        _silentAudio = new SilentAudioBackend(audio.SampleRate);
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
        // Fast-forward: advance extra frames with audio dropped, present only the last with real audio.
        int extra = Math.Max(1, SpeedMultiplier) - 1;
        for (int i = 0; i < extra; i++)
            _core.StepFrame(Input, _silentAudio);

        FrameBuffer frame = _core.StepFrame(Input, Audio);
        Video.Present(frame);
    }

    /// <summary>An audio sink that reports the output rate but discards samples (used for the skipped
    /// frames during fast-forward, so queuing N× audio can't back up the real buffer).</summary>
    private sealed class SilentAudioBackend(int sampleRate) : IAudioBackend
    {
        public int SampleRate { get; } = sampleRate;
        public void QueueSamples(ReadOnlySpan<float> samples) { }
    }
}
