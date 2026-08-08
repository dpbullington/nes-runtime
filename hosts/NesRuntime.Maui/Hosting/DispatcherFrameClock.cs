using NesRuntime.Rendering;

namespace NesRuntime.Maui.Hosting;

/// <summary>
/// An <see cref="IFrameClock"/> backed by the MAUI UI dispatcher. It ticks at the console's frame
/// rate so the whole runtime loop (input poll → step → present) runs on the UI thread, which keeps
/// framebuffer presentation and on-screen input trivially thread-safe for this host.
/// </summary>
/// <remarks>
/// A dispatcher timer is only approximately periodic; a production host would prefer a
/// display-synced callback (CADisplayLink on Apple platforms, Choreographer on Android). This is
/// deliberately the simple, portable option for the skeleton.
/// </remarks>
public sealed class DispatcherFrameClock : IFrameClock
{
    private readonly IDispatcher _dispatcher;
    private IDispatcherTimer? _timer;
    private long _frame;

    public DispatcherFrameClock(IDispatcher dispatcher, double targetFps)
    {
        _dispatcher = dispatcher;
        TargetFps = targetFps;
    }

    public double TargetFps { get; }

    public event EventHandler<FrameTick>? Tick;

    public void Start()
    {
        if (_timer is not null)
            return;

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.0 / TargetFps);
        _timer.IsRepeating = true;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer is null)
            return;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
        => Tick?.Invoke(this, new FrameTick(_frame++, 1.0 / TargetFps));
}
