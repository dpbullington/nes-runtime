using NesRuntime.Core.Memory;

namespace NesRuntime.Core.Apu;

/// <summary>
/// The Audio Processing Unit integrated in the 2A03. Mixes five channels — two pulses, triangle,
/// noise, and the DMC sample channel — under a shared frame sequencer, exposed through registers
/// $4000-$4013, the $4015 status/enable register, and the $4017 frame-counter register.
/// </summary>
public sealed class Apu2A03 : IMemoryRegion
{
    public AddressRange Range => new(0x4000, 0x4017);

    public PulseChannel Pulse1 { get; }
    public PulseChannel Pulse2 { get; }
    public TriangleChannel Triangle { get; }
    public NoiseChannel Noise { get; }
    public DmcChannel Dmc { get; }
    public FrameCounter FrameCounter { get; }

    public Apu2A03() => throw new NotImplementedException();

    /// <summary>All five channels in a fixed order, for hosts that render per-channel.</summary>
    public IReadOnlyList<IAudioChannel> Channels => throw new NotImplementedException();

    /// <summary>The mixed output sample for the current APU cycle (normalized 0.0-1.0).</summary>
    public float Mix() => throw new NotImplementedException();

    // IMemoryRegion — $4000-$4017 register file (note $4014 OAMDMA and $4016 are I/O, not APU).
    public byte Read(ushort address) => throw new NotImplementedException();

    public void Write(ushort address, byte value) => throw new NotImplementedException();
}
