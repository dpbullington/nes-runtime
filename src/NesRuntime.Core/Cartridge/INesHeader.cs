using NesRuntime.Core.Ppu;

namespace NesRuntime.Core.Cartridge;

/// <summary>
/// The parsed cartridge header (iNES or its NES 2.0 superset). Describes ROM sizes, the mapper
/// number, nametable mirroring, the presence of battery-backed save RAM, and the target region.
/// </summary>
public sealed class INesHeader
{
    /// <summary>PRG-ROM size in 16 KiB banks.</summary>
    public int PrgRomBanks { get; init; }

    /// <summary>CHR-ROM size in 8 KiB banks. Zero means the board uses CHR-RAM instead.</summary>
    public int ChrRomBanks { get; init; }

    /// <summary>Mapper number identifying the board's banking hardware.</summary>
    public int MapperNumber { get; init; }

    public MirroringMode Mirroring { get; init; }

    /// <summary>True when the board has battery-backed PRG-RAM for saved games.</summary>
    public bool HasBattery { get; init; }

    /// <summary>True when this is an extended NES 2.0 header rather than plain iNES.</summary>
    public bool IsNes20 { get; init; }

    public Region Region { get; init; }

    /// <summary>Parses a 16-byte header from the start of a ROM image.</summary>
    public static INesHeader Parse(ReadOnlySpan<byte> header) => throw new NotImplementedException();
}
