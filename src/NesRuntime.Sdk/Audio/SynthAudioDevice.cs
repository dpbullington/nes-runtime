using NesRuntime.Core.Apu;

namespace NesRuntime.Sdk.Audio;

/// <summary>
/// A lightweight software synthesizer standing in for the APU until the register-accurate channels
/// are implemented. It renders the two pulse voices, triangle, noise, and one-shot sound effects to
/// a mono float sample stream that the runtime pushes to the host each frame. The timbres are
/// deliberately NES-flavored (square/triangle/LFSR-ish noise) but not cycle-accurate.
/// </summary>
public sealed class SynthAudioDevice : IAudioDevice
{
    private readonly int _sampleRate;

    private readonly SquareVoice _pulse1 = new();
    private readonly SquareVoice _pulse2 = new();
    private readonly TriangleVoice _triangle = new();
    private readonly NoiseVoice _noise = new();
    private readonly SfxPlayer _sfx = new();

    public SynthAudioDevice(int sampleRate) => _sampleRate = sampleRate;

    public IToneVoice Pulse1 => _pulse1;
    public IToneVoice Pulse2 => _pulse2;
    public IToneVoice Triangle => _triangle;
    public INoiseVoice Noise => _noise;
    public ISampleVoice Samples { get; } = new NullSampleVoice();
    public IMusicPlayer Music { get; } = new NullMusicPlayer();

    public void Play(SoundEffect effect) => _sfx.Play(effect);

    /// <summary>
    /// Renders one video frame's worth of audio into <paramref name="buffer"/>. Sound effects
    /// advance one step per frame; sustained voices generate continuously until stopped.
    /// </summary>
    internal void Render(Span<float> buffer)
    {
        _sfx.AdvanceFrame();

        for (int i = 0; i < buffer.Length; i++)
        {
            float s = _pulse1.NextSample(_sampleRate)
                    + _pulse2.NextSample(_sampleRate)
                    + _triangle.NextSample(_sampleRate)
                    + _noise.NextSample(_sampleRate)
                    + _sfx.NextSample(_sampleRate);

            buffer[i] = Math.Clamp(s, -1f, 1f);
        }
    }

    // --- Voices ---

    private sealed class SquareVoice : IToneVoice
    {
        private double _phase;
        private double _frequency;
        private float _amplitude;
        private bool _on;

        public DutyCycle Duty { get; set; } = DutyCycle.Half;

        public void PlayNote(Note note, int volume = 15)
        {
            _frequency = note.Frequency;
            _amplitude = Volume01(volume);
            _on = _amplitude > 0f;
        }

        public void Stop() => _on = false;

        public float NextSample(int sampleRate)
        {
            if (!_on)
                return 0f;

            _phase += _frequency / sampleRate;
            _phase -= Math.Floor(_phase);
            return _phase < DutyFraction(Duty) ? _amplitude : -_amplitude;
        }

        private static double DutyFraction(DutyCycle duty) => duty switch
        {
            DutyCycle.Eighth => 0.125,
            DutyCycle.Quarter => 0.25,
            DutyCycle.ThreeQuarters => 0.75,
            _ => 0.5,
        };
    }

    private sealed class TriangleVoice : IToneVoice
    {
        private double _phase;
        private double _frequency;
        private float _amplitude;
        private bool _on;

        public DutyCycle Duty { get; set; } // ignored by the triangle

        public void PlayNote(Note note, int volume = 15)
        {
            _frequency = note.Frequency;
            _amplitude = Volume01(volume);
            _on = _amplitude > 0f;
        }

        public void Stop() => _on = false;

        public float NextSample(int sampleRate)
        {
            if (!_on)
                return 0f;

            _phase += _frequency / sampleRate;
            _phase -= Math.Floor(_phase);
            // 0..1 phase -> -1..1 triangle.
            float tri = (float)(4.0 * Math.Abs(_phase - 0.5) - 1.0);
            return tri * _amplitude;
        }
    }

    private sealed class NoiseVoice : INoiseVoice
    {
        private uint _lfsr = 1;
        private float _amplitude;
        private bool _on;

        public void Play(int periodIndex, int volume = 15, NoiseMode mode = NoiseMode.Long)
        {
            _amplitude = Volume01(volume);
            _on = _amplitude > 0f;
        }

        public void Stop() => _on = false;

        public float NextSample(int sampleRate)
        {
            if (!_on)
                return 0f;

            // 15-bit Galois LFSR, the NES noise generator's feedback shape.
            uint bit = ((_lfsr ^ (_lfsr >> 1)) & 1);
            _lfsr = (_lfsr >> 1) | (bit << 14);
            return (_lfsr & 1) == 0 ? _amplitude : -_amplitude;
        }
    }

    /// <summary>Plays a <see cref="SoundEffect"/> as one tone step per frame on its own square voice.</summary>
    private sealed class SfxPlayer
    {
        private readonly SquareVoice _voice = new();
        private SoundEffect? _effect;
        private int _frame;

        public void Play(SoundEffect effect)
        {
            _effect = effect;
            _frame = 0;
        }

        public void AdvanceFrame()
        {
            if (_effect is null)
                return;

            if (_frame >= _effect.DurationFrames)
            {
                _voice.Stop();
                _effect = null;
                return;
            }

            ToneStep step = _effect.Steps[_frame++];
            if (step.Volume <= 0)
                _voice.Stop();
            else
                _voice.PlayNote(step.Note, step.Volume);
        }

        public float NextSample(int sampleRate) => _voice.NextSample(sampleRate);
    }

    private sealed class NullSampleVoice : ISampleVoice
    {
        public void Play(SampleClip clip, bool loop = false) { }
        public void Stop() { }
    }

    private sealed class NullMusicPlayer : IMusicPlayer
    {
        public MusicTrack? Current => null;
        public bool IsPlaying => false;
        public void Play(MusicTrack track, bool loop = true) { }
        public void Pause() { }
        public void Resume() { }
        public void Stop() { }
    }

    // Map a 0-15 NES volume onto a conservative linear amplitude with headroom for mixing voices.
    private static float Volume01(int volume) => Math.Clamp(volume, 0, 15) / 15f * 0.18f;
}
