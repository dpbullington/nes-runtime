using NesRuntime.Rendering;
#if MACCATALYST
using AudioToolbox;
using AVFoundation;
using Foundation;
#endif

namespace NesRuntime.Maui.Hosting;

/// <summary>
/// Sinks the APU's mixed samples and plays them. The runtime pushes mono float samples each frame
/// into a ring buffer; on Mac Catalyst an <c>AVAudioEngine</c> source node pulls from that buffer on
/// the audio thread and plays it. On platforms without this wired up yet (e.g. Windows) it degrades
/// to a no-op sink so the host still runs silently.
/// </summary>
public sealed class MauiAudioBackend : IAudioBackend
{
    private readonly object _lock = new();
    private readonly float[] _ring;
    private int _read;
    private int _write;
    private int _count;

#if MACCATALYST
    private AVAudioEngine? _engine;
    private AVAudioSourceNode? _source;
#endif

    public MauiAudioBackend(int sampleRate = 44_100)
    {
        SampleRate = sampleRate;
        _ring = new float[sampleRate]; // ~1 second of headroom
        StartOutput();
    }

    public int SampleRate { get; }

    /// <summary>Total samples received since start, for diagnostics.</summary>
    public long SamplesQueued { get; private set; }

    public void QueueSamples(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (float s in samples)
            {
                if (_count == _ring.Length)
                {
                    // Overrun: drop the oldest sample to make room (keeps latency bounded).
                    _read = (_read + 1) % _ring.Length;
                    _count--;
                }
                _ring[_write] = s;
                _write = (_write + 1) % _ring.Length;
                _count++;
            }
        }

        SamplesQueued += samples.Length;
    }

    /// <summary>Copies up to <paramref name="dest"/>.Length samples out of the ring; underrun → silence.</summary>
    private void Drain(Span<float> dest)
    {
        lock (_lock)
        {
            int available = Math.Min(dest.Length, _count);
            for (int i = 0; i < available; i++)
            {
                dest[i] = _ring[_read];
                _read = (_read + 1) % _ring.Length;
            }
            for (int i = available; i < dest.Length; i++)
                dest[i] = 0f;
            _count -= available;
        }
    }

#if MACCATALYST
    private void StartOutput()
    {
        _engine = new AVAudioEngine();
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, SampleRate, 1, interleaved: false);
        _source = new AVAudioSourceNode(format, RenderAudio);
        _engine.AttachNode(_source);
        _engine.Connect(_source, _engine.MainMixerNode, format);
        _engine.Prepare();
        _engine.StartAndReturnError(out _);
    }

    private unsafe int RenderAudio(ref bool isSilence, ref AudioTimeStamp timestamp, uint frameCount, AudioBuffers outputData)
    {
        int frames = (int)frameCount;
        Span<float> mixed = frames <= 4096 ? stackalloc float[frames] : new float[frames];
        Drain(mixed);

        for (int b = 0; b < outputData.Count; b++)
        {
            var channel = (float*)outputData[b].Data;
            for (int i = 0; i < frames; i++)
                channel[i] = mixed[i];
        }

        return 0;
    }
#else
    private void StartOutput()
    {
        // Real-time audio output is not wired for this platform yet; samples are accepted and dropped.
    }
#endif
}
