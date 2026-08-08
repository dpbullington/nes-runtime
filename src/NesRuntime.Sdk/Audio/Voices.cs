using NesRuntime.Core.Apu;

namespace NesRuntime.Sdk.Audio;

/// <summary>A musical pitch, expressed by name and octave (e.g. A4). The SDK converts it to the
/// nearest achievable NES timer period for the target channel.</summary>
public readonly record struct Note(int Semitone, int Octave)
{
    public static Note A4 => new(9, 4);

    /// <summary>The MIDI note number for this pitch (A4 = 69, middle C = 60).</summary>
    public int MidiNumber => ((Octave + 1) * 12) + Semitone;

    /// <summary>The equal-tempered frequency in Hz (A4 = 440 Hz).</summary>
    public double Frequency => 440.0 * Math.Pow(2.0, (MidiNumber - 69) / 12.0);

    /// <summary>Builds a note from a MIDI number (60 = middle C).</summary>
    public static Note FromMidi(int midi) => new(((midi % 12) + 12) % 12, (midi / 12) - 1);
}

/// <summary>A tuned voice: the pulse and triangle channels. Play a pitch, bend it, or stop it.</summary>
public interface IToneVoice
{
    /// <summary>Starts (or retunes to) a pitch at the given volume (0-15; ignored by triangle).</summary>
    void PlayNote(Note note, int volume = 15);

    /// <summary>Selects the duty cycle (pulse channels only; ignored by triangle).</summary>
    DutyCycle Duty { get; set; }

    void Stop();
}

/// <summary>The noise voice: pitched by a period index rather than a musical note.</summary>
public interface INoiseVoice
{
    void Play(int periodIndex, int volume = 15, NoiseMode mode = NoiseMode.Long);
    void Stop();
}

/// <summary>The DMC sample voice: plays imported PCM/DPCM clips (drums, voice).</summary>
public interface ISampleVoice
{
    /// <summary>Plays a previously loaded sample, optionally looping.</summary>
    void Play(SampleClip clip, bool loop = false);
    void Stop();
}
