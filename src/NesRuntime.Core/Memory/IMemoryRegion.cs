namespace NesRuntime.Core.Memory;

/// <summary>
/// A device mapped into the CPU address space. The <see cref="MemoryBus"/> routes reads and
/// writes to the region whose <see cref="Range"/> contains the address. Implementations include
/// internal RAM, the PPU/APU register mirrors, and the cartridge mapper.
/// </summary>
public interface IMemoryRegion
{
    AddressRange Range { get; }

    /// <summary>
    /// Reads a byte. Some regions have read side effects (e.g. reading PPUSTATUS clears vblank),
    /// so a peek/read distinction is left to the region.
    /// </summary>
    byte Read(ushort address);

    void Write(ushort address, byte value);
}
