namespace NesRuntime.Rendering;

/// <summary>
/// Sinks the APU's mixed audio. The runtime pushes mono samples at the backend's requested sample
/// rate; the host resamples/buffers to its output device. Implementations must be safe to feed
/// from the runtime's frame loop.
/// </summary>
public interface IAudioBackend
{
    /// <summary>Output sample rate the runtime should generate at (e.g. 44100 or 48000).</summary>
    int SampleRate { get; }

    /// <summary>Queues a block of normalized (-1.0..1.0) mono samples for playback.</summary>
    void QueueSamples(ReadOnlySpan<float> samples);
}
