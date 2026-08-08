using NesRuntime.Core.Apu;

namespace NesRuntime.Sdk.Audio;

/// <summary>
/// A no-op <see cref="IAudioDevice"/> used until the APU synthesis path is implemented. It accepts
/// every call so games that produce sound run unchanged; nothing is emitted yet.
/// </summary>
public sealed class SilentAudioDevice : IAudioDevice
{
    public IToneVoice Pulse1 { get; } = new SilentToneVoice();
    public IToneVoice Pulse2 { get; } = new SilentToneVoice();
    public IToneVoice Triangle { get; } = new SilentToneVoice();
    public INoiseVoice Noise { get; } = new SilentNoiseVoice();
    public ISampleVoice Samples { get; } = new SilentSampleVoice();
    public IMusicPlayer Music { get; } = new SilentMusicPlayer();

    public void Play(SoundEffect effect) { }

    private sealed class SilentToneVoice : IToneVoice
    {
        public DutyCycle Duty { get; set; }
        public void PlayNote(Note note, int volume = 15) { }
        public void Stop() { }
    }

    private sealed class SilentNoiseVoice : INoiseVoice
    {
        public void Play(int periodIndex, int volume = 15, NoiseMode mode = NoiseMode.Long) { }
        public void Stop() { }
    }

    private sealed class SilentSampleVoice : ISampleVoice
    {
        public void Play(SampleClip clip, bool loop = false) { }
        public void Stop() { }
    }

    private sealed class SilentMusicPlayer : IMusicPlayer
    {
        public MusicTrack? Current => null;
        public bool IsPlaying => false;
        public void Play(MusicTrack track, bool loop = true) { }
        public void Pause() { }
        public void Resume() { }
        public void Stop() { }
    }
}
