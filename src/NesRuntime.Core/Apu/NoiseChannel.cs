namespace NesRuntime.Core.Apu;

/// <summary>Feedback tap selection for the noise channel's shift register.</summary>
public enum NoiseMode
{
    /// <summary>Long sequence — hiss/white-noise timbres (percussion, wind).</summary>
    Long,

    /// <summary>Short sequence — a buzzier, more tonal metallic noise.</summary>
    Short,
}

/// <summary>
/// The noise channel: a pseudo-random bit generator (a 15-bit LFSR) fed through an
/// <see cref="Envelope"/> and <see cref="LengthCounter"/>. Used for percussion and explosions.
/// The period comes from a 16-entry hardware table, not an arbitrary timer.
/// </summary>
public sealed class NoiseChannel : IAudioChannel
{
    public string Name => "Noise";
    public bool Enabled { get; set; }
    public int Output => throw new NotImplementedException();

    public Envelope Envelope { get; } = new();
    public LengthCounter Length { get; } = new();
    public NoiseMode Mode { get; set; }

    /// <summary>Index (0-15) into the hardware noise-period table.</summary>
    public int PeriodIndex { get; set; }

    public void Clock() => throw new NotImplementedException();
}
