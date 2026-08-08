namespace NesRuntime.Sdk.Audio;

/// <summary>
/// The NES's five voices, presented for direct play. Each property maps to a real channel, so the
/// authentic timbres and the "one note at a time per channel" constraint are preserved. For songs
/// and effects, layer <see cref="IMusicPlayer"/> and <see cref="SoundEffect"/> on top.
/// </summary>
public interface IAudioDevice
{
    /// <summary>The two square-wave voices — melody and harmony.</summary>
    IToneVoice Pulse1 { get; }
    IToneVoice Pulse2 { get; }

    /// <summary>The triangle voice — typically bass. Fixed volume.</summary>
    IToneVoice Triangle { get; }

    /// <summary>The noise voice — percussion and effects.</summary>
    INoiseVoice Noise { get; }

    /// <summary>The DMC sample voice — drum samples and short PCM clips.</summary>
    ISampleVoice Samples { get; }

    /// <summary>Plays a song across the channels while your game keeps running.</summary>
    IMusicPlayer Music { get; }

    /// <summary>Fires a one-shot sound effect, briefly borrowing a channel.</summary>
    void Play(SoundEffect effect);
}
