namespace NesRuntime.Core.Apu;

/// <summary>
/// The Delta Modulation Channel: plays back 1-bit delta-encoded PCM samples fetched from PRG
/// memory via its own DMA, used for drum kits and short voice clips. It can loop and can raise an
/// IRQ when a sample finishes. Its DMA steals CPU cycles, which the runtime accounts for.
/// </summary>
public sealed class DmcChannel : IAudioChannel
{
    public string Name => "DMC";
    public bool Enabled { get; set; }
    public int Output => throw new NotImplementedException();

    /// <summary>Loops the sample when it reaches the end.</summary>
    public bool Loop { get; set; }

    /// <summary>Raises an IRQ when a non-looping sample completes.</summary>
    public bool IrqEnabled { get; set; }

    /// <summary>Index (0-15) into the hardware playback-rate table.</summary>
    public int RateIndex { get; set; }

    /// <summary>7-bit output level ($4011), also directly writable for crude PCM.</summary>
    public int DirectLoad { get; set; }

    /// <summary>Sample start address in CPU space ($C000 + n*64).</summary>
    public ushort SampleAddress { get; set; }

    /// <summary>Sample length in bytes ((n*16)+1).</summary>
    public int SampleLength { get; set; }

    public void Clock() => throw new NotImplementedException();
}
