namespace NesRuntime.Core.Apu;

/// <summary>
/// A single APU sound channel. All five channels expose an enable flag, a length counter that
/// silences them when it reaches zero, and a current output sample used by the mixer.
/// </summary>
public interface IAudioChannel
{
    string Name { get; }

    bool Enabled { get; set; }

    /// <summary>Current unmixed output level for this channel (channel-specific range).</summary>
    int Output { get; }

    /// <summary>Advances the channel by one APU clock.</summary>
    void Clock();
}
