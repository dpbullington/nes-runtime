namespace NesRuntime.Core.Cartridge;

/// <summary>
/// Resolves an iNES mapper number to an <see cref="IMapper"/> implementation. Ships with the
/// common boards (NROM, MMC1, UxROM, CNROM, MMC3, AxROM, MMC5, …) and accepts registrations for
/// additional or custom boards.
/// </summary>
public sealed class MapperRegistry
{
    public static MapperRegistry Default => throw new NotImplementedException();

    /// <summary>Registers a factory for a mapper number, overriding any existing entry.</summary>
    public void Register(int mapperNumber, Func<Cartridge, IMapper> factory) => throw new NotImplementedException();

    public bool IsSupported(int mapperNumber) => throw new NotImplementedException();

    /// <summary>Instantiates the mapper for a cartridge, throwing if the number is unsupported.</summary>
    public IMapper Create(Cartridge cartridge) => throw new NotImplementedException();
}
