namespace NesRuntime.Core.Apu;

/// <summary>The four duty cycles a pulse channel can output.</summary>
public enum DutyCycle
{
    Eighth,        // 12.5%
    Quarter,       // 25%
    Half,          // 50%
    ThreeQuarters, // 75% (25% inverted)
}

/// <summary>
/// One of the two pulse (square-wave) channels — the workhorses for melody and sound effects.
/// Each combines a duty-cycle generator, an <see cref="Envelope"/>, a <see cref="SweepUnit"/>,
/// an 11-bit timer, and a <see cref="LengthCounter"/>.
/// </summary>
public sealed class PulseChannel : IAudioChannel
{
    public PulseChannel(int ordinal) => throw new NotImplementedException();

    public string Name => throw new NotImplementedException();
    public bool Enabled { get; set; }
    public int Output => throw new NotImplementedException();

    public DutyCycle Duty { get; set; }
    public Envelope Envelope { get; }
    public SweepUnit Sweep { get; }
    public LengthCounter Length { get; }

    /// <summary>11-bit period; pitch is the CPU clock divided by (16 * (timer + 1)).</summary>
    public int TimerPeriod { get; set; }

    public void Clock() => throw new NotImplementedException();
}
