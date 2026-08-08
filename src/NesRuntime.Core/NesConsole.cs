using NesRuntime.Core.Apu;
using NesRuntime.Core.Cartridge;
using NesRuntime.Core.Cpu;
using NesRuntime.Core.Dma;
using NesRuntime.Core.Input;
using NesRuntime.Core.Memory;
using NesRuntime.Core.Ppu;

namespace NesRuntime.Core;

/// <summary>
/// The composed NES machine: CPU core, PPU, APU, memory bus, controller ports, DMA units and a
/// mounted cartridge, all bound to a <see cref="ConsoleTiming"/>. This is the hardware model the
/// SDK builds on; game authors never touch it directly.
/// </summary>
public sealed class NesConsole
{
    public ConsoleTiming Timing { get; }

    public Cpu2A03 Cpu { get; }
    public Ppu2C02 Ppu { get; }
    public Apu2A03 Apu { get; }
    public MemoryBus Bus { get; }
    public ControllerPort Port1 { get; }
    public ControllerPort Port2 { get; }
    public ExpansionPort Expansion { get; }
    public OamDmaUnit OamDma { get; }

    /// <summary>The currently mounted cartridge, or <c>null</c> when the tray is empty.</summary>
    public Cartridge.Cartridge? Cartridge { get; private set; }

    public NesConsole(Region region = Region.Ntsc) => throw new NotImplementedException();

    /// <summary>Inserts a cartridge and wires its mapper into the memory bus.</summary>
    public void InsertCartridge(Cartridge.Cartridge cartridge) => throw new NotImplementedException();

    /// <summary>Asserts the RESET line, returning the machine to power-on state.</summary>
    public void Reset() => throw new NotImplementedException();

    /// <summary>
    /// Advances the machine by exactly one video frame (runs CPU/PPU/APU to the next vblank).
    /// The presentation host calls this once per displayed frame.
    /// </summary>
    public void StepFrame() => throw new NotImplementedException();
}
