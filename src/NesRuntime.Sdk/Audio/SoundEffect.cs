namespace NesRuntime.Sdk.Audio;

/// <summary>
/// A short, self-contained sound effect (a jump, a coin, an explosion) authored as a small
/// envelope of pitch/volume steps. Play it via <see cref="IAudioDevice.Play"/>; the SDK borrows an
/// appropriate channel for its duration and restores whatever was playing.
/// </summary>
public sealed class SoundEffect
{
    private readonly ToneStep[] _steps;

    public string Name { get; }

    /// <summary>Effect length in frames.</summary>
    public int DurationFrames => _steps.Length;

    private SoundEffect(string name, ToneStep[] steps)
    {
        Name = name;
        _steps = steps;
    }

    /// <summary>The per-frame tone sequence.</summary>
    public IReadOnlyList<ToneStep> Steps => _steps;

    /// <summary>Builds an effect from a per-frame sequence of tone steps.</summary>
    public static SoundEffect FromSteps(string name, IEnumerable<ToneStep> steps)
        => new(name, steps.ToArray());
}

/// <summary>One frame of a <see cref="SoundEffect"/>: a pitch and volume to hold for that frame.</summary>
public readonly record struct ToneStep(Note Note, int Volume);

/// <summary>
/// A DMC/PCM sample clip imported from audio data, played through <see cref="ISampleVoice"/>.
/// </summary>
public sealed class SampleClip
{
    public string Name { get; }

    /// <summary>Playback rate index (0-15) into the DMC hardware rate table.</summary>
    public int RateIndex { get; }

    private SampleClip(string name, int rateIndex) => (Name, RateIndex) = (name, rateIndex);
}
