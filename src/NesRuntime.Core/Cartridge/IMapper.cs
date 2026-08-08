using NesRuntime.Core.Cpu;
using NesRuntime.Core.Ppu;

namespace NesRuntime.Core.Cartridge;

/// <summary>
/// A cartridge board's memory-management hardware. Mappers extend the console past its tiny fixed
/// address space by bank-switching PRG and CHR, may control nametable mirroring, and some
/// (e.g. MMC3, MMC5) generate scanline IRQs for raster effects. The runtime resolves cartridge
/// accesses through the active mapper.
/// </summary>
public interface IMapper
{
    /// <summary>The iNES mapper number this implementation handles.</summary>
    int Number { get; }

    string Name { get; }

    /// <summary>Current nametable mirroring; some mappers switch this at runtime.</summary>
    MirroringMode Mirroring { get; }

    /// <summary>Raised when the mapper asserts a scanline/counter IRQ.</summary>
    event EventHandler<InterruptSource>? IrqRaised;

    // CPU space $4020-$FFFF (PRG-ROM, PRG-RAM, mapper registers).
    byte CpuRead(ushort address);
    void CpuWrite(ushort address, byte value);

    // PPU space $0000-$1FFF (CHR-ROM / CHR-RAM).
    byte PpuRead(ushort address);
    void PpuWrite(ushort address, byte value);

    /// <summary>Notifies mappers that count scanlines (e.g. MMC3) that a rendered line elapsed.</summary>
    void OnScanline();
}
