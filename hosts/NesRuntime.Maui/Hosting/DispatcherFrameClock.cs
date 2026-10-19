using NesRuntime.Rendering;
#if MACCATALYST
using CoreAnimation;
using Foundation;
#endif

namespace NesRuntime.Maui.Hosting;

/// <summary>
/// An <see cref="IFrameClock"/> that paces the runtime on the UI thread. On Mac Catalyst it is driven
/// by a display-synced <c>CADisplayLink</c> (fires once per vsync at the panel's refresh rate); on
/// other platforms it falls back to a MAUI dispatcher timer.
/// </summary>
/// <remarks>
/// A dispatcher timer schedules its next tick <em>interval</em> after the previous handler returns, so
/// its real period is <c>interval + work</c> — at ~60 Hz that easily slips to ~50 fps once per-frame
/// step+present work is added. <c>CADisplayLink</c> is scheduled by the display, not by the handler,
/// so it holds the refresh rate regardless of how long a frame takes (short of dropping one).
/// </remarks>
public sealed class DispatcherFrameClock : IFrameClock
{
    private readonly IDispatcher _dispatcher;
    private long _frame;

#if MACCATALYST
    private CADisplayLink? _displayLink;
    private double _lastTimestamp;
#else
    private IDispatcherTimer? _timer;
#endif

    public DispatcherFrameClock(IDispatcher dispatcher, double targetFps)
    {
        _dispatcher = dispatcher;
        TargetFps = targetFps;
    }

    public double TargetFps { get; }

    public event EventHandler<FrameTick>? Tick;

    public void Start()
    {
#if MACCATALYST
        if (_displayLink is not null)
            return;

        _lastTimestamp = 0;
        _displayLink = CADisplayLink.Create(OnDisplayLink);
        // Cap to the console rate; on a 60 Hz panel this is already the natural vsync cadence, and on
        // a 120 Hz ProMotion panel it fires every other refresh instead of running the game double speed.
        // PreferredFramesPerSecond is obsoleted on Mac Catalyst 15+ in favor of PreferredFrameRateRange,
        // but still works and is simpler; suppress the platform-compat analyzer for this one call.
#pragma warning disable CA1422
        _displayLink.PreferredFramesPerSecond = (int)Math.Round(TargetFps);
#pragma warning restore CA1422
        _displayLink.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
#else
        if (_timer is not null)
            return;

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.0 / TargetFps);
        _timer.IsRepeating = true;
        _timer.Tick += OnTimerTick;
        _timer.Start();
#endif
    }

    public void Stop()
    {
#if MACCATALYST
        if (_displayLink is null)
            return;

        _displayLink.Invalidate();
        _displayLink.Dispose();
        _displayLink = null;
#else
        if (_timer is null)
            return;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
#endif
    }

#if MACCATALYST
    private void OnDisplayLink()
    {
        double now = _displayLink!.Timestamp;
        double delta = _lastTimestamp > 0 ? now - _lastTimestamp : 1.0 / TargetFps;
        _lastTimestamp = now;
        Tick?.Invoke(this, new FrameTick(_frame++, delta));
    }
#else
    private void OnTimerTick(object? sender, EventArgs e)
        => Tick?.Invoke(this, new FrameTick(_frame++, 1.0 / TargetFps));
#endif
}
