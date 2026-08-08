namespace NesRuntime.Core.Cartridge;

/// <summary>
/// A mounted game cartridge: its parsed <see cref="INesHeader"/>, program and character memory,
/// optional battery-backed save RAM, and the <see cref="IMapper"/> that wires them onto the CPU
/// and PPU buses. This is the native-format ingest point; the SDK can also synthesize a cartridge
/// from high-level, PNG/WAV-authored content.
/// </summary>
public sealed class Cartridge
{
    public INesHeader Header { get; }
    public PrgRom Prg { get; }
    public ChrMemory Chr { get; }
    public IMapper Mapper { get; }

    /// <summary>Save RAM, present only when <see cref="INesHeader.HasBattery"/> is set.</summary>
    public BatteryBackedRam? SaveRam { get; }

    internal Cartridge(INesHeader header, PrgRom prg, ChrMemory chr, IMapper mapper, BatteryBackedRam? saveRam)
        => throw new NotImplementedException();

    /// <summary>Loads a cartridge from a raw iNES/NES 2.0 ROM image.</summary>
    public static Cartridge FromRom(ReadOnlySpan<byte> romImage, MapperRegistry? mappers = null)
        => throw new NotImplementedException();
}
