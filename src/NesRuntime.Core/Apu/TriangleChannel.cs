namespace NesRuntime.Core.Apu;

/// <summary>
/// The triangle channel: a fixed-volume, 16-step triangle wave typically used for bass lines.
/// Gated by both a <see cref="LengthCounter"/> and a separate linear counter, and it has no
/// volume control — it is either running at full level or silent.
/// </summary>
public sealed class TriangleChannel : IAudioChannel
{
    public string Name => "Triangle";
    public bool Enabled { get; set; }
    public int Output => throw new NotImplementedException();

    public LengthCounter Length { get; } = new();

    /// <summary>The linear counter's reload value (0-127), a finer-grained duration gate.</summary>
    public int LinearCounterReload { get; set; }

    public bool ControlFlag { get; set; }

    /// <summary>11-bit timer period.</summary>
    public int TimerPeriod { get; set; }

    public void Clock() => throw new NotImplementedException();
}
