namespace NesRuntime.Core.Memory;

/// <summary>
/// The CPU-visible 16-bit address space ($0000-$FFFF). Routes each access to a mapped
/// <see cref="IMemoryRegion"/>. The canonical NES layout is:
/// <list type="bullet">
///   <item>$0000-$1FFF internal 2 KiB RAM (mirrored every $0800)</item>
///   <item>$2000-$3FFF PPU registers (mirrored every 8 bytes)</item>
///   <item>$4000-$4017 APU and I/O registers</item>
///   <item>$4020-$FFFF cartridge space (PRG-ROM, PRG-RAM, mapper registers)</item>
/// </list>
/// </summary>
public sealed class MemoryBus
{
    public InternalRam Ram { get; }

    public MemoryBus() => throw new NotImplementedException();

    /// <summary>Maps a region into the address space. Later mappings win on overlap.</summary>
    public void Map(IMemoryRegion region) => throw new NotImplementedException();

    public void Unmap(IMemoryRegion region) => throw new NotImplementedException();

    public byte Read(ushort address) => throw new NotImplementedException();

    public ushort ReadWord(ushort address) => throw new NotImplementedException();

    public void Write(ushort address, byte value) => throw new NotImplementedException();
}
