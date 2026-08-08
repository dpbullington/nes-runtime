using NesRuntime.Core.Memory;

namespace NesRuntime.Core.Cpu;

/// <summary>
/// The Ricoh 2A03 CPU core (a 6502 without decimal mode, with the APU and DMA integrated).
/// In a runtime — as opposed to an emulator — the game logic runs as C#, so this type does not
/// execute 6502 opcodes. It models the parts the runtime still needs: the interrupt lines
/// (NMI at vblank, IRQ from the APU/DMC/mapper), the reset vector, and cycle accounting used to
/// pace DMA stalls and audio.
/// </summary>
public sealed class Cpu2A03
{
    /// <summary>Total CPU cycles elapsed since power-on. Used to schedule DMA and APU events.</summary>
    public long CycleCount => throw new NotImplementedException();

    /// <summary>Raised when the PPU asserts NMI at the start of vertical blank.</summary>
    public event EventHandler? NonMaskableInterrupt;

    /// <summary>Raised when a maskable IRQ source (APU frame counter, DMC, mapper) is asserted.</summary>
    public event EventHandler<InterruptSource>? MaskableInterrupt;

    /// <summary>Whether IRQs are currently masked (the 6502 I flag).</summary>
    public bool InterruptsDisabled { get; set; }

    public void AssertNmi() => throw new NotImplementedException();

    public void AssertIrq(InterruptSource source) => throw new NotImplementedException();

    public void ClearIrq(InterruptSource source) => throw new NotImplementedException();

    /// <summary>Stalls the CPU for the given number of cycles (e.g. during OAM/DMC DMA).</summary>
    public void Stall(int cycles) => throw new NotImplementedException();

    public void Reset(MemoryBus bus) => throw new NotImplementedException();
}
