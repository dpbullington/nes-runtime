namespace NesRuntime.Core.Apu;

/// <summary>
/// The APU frame sequencer ($4017). On a fixed cadence it clocks the envelopes, sweep units,
/// length counters, and linear counter. In 4-step mode it can also raise a periodic IRQ; 5-step
/// mode runs slightly slower and never interrupts.
/// </summary>
public sealed class FrameCounter
{
    public enum SequenceMode
    {
        /// <summary>Four steps per sequence; can raise the frame IRQ.</summary>
        FourStep,

        /// <summary>Five steps per sequence; no IRQ.</summary>
        FiveStep,
    }

    public SequenceMode Mode { get; set; }

    /// <summary>Suppresses the frame IRQ in 4-step mode.</summary>
    public bool InterruptInhibit { get; set; }

    /// <summary>Raised on the frame-IRQ step when enabled.</summary>
    public event EventHandler? InterruptRequested;

    public void Clock() => throw new NotImplementedException();
}
