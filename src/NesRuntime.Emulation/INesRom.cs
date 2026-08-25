namespace NesRuntime.Emulation;

public enum Mirroring { Horizontal, Vertical, SingleScreenLower, SingleScreenUpper, FourScreen }

/// <summary>
/// A parsed iNES / NES 2.0 ROM image: header fields plus the PRG-ROM and CHR (ROM or RAM) banks.
/// </summary>
public sealed class INesRom
{
    public int MapperNumber { get; init; }
    public Mirroring Mirroring { get; init; }
    public bool HasBattery { get; init; }
    public bool IsNes20 { get; init; }
    public byte[] Prg { get; init; } = [];
    public byte[] Chr { get; init; } = [];
    public bool ChrIsRam { get; init; }

    public static INesRom Parse(ReadOnlySpan<byte> image)
    {
        if (image.Length < 16 || image[0] != 'N' || image[1] != 'E' || image[2] != 'S' || image[3] != 0x1A)
            throw new ArgumentException("Not an iNES/NES 2.0 image (bad magic).");

        byte flags6 = image[6], flags7 = image[7];
        bool nes20 = (flags7 & 0x0C) == 0x08;

        int prgBanks = image[4];
        int chrBanks = image[5];
        int mapper = (flags7 & 0xF0) | (flags6 >> 4);
        if (nes20)
        {
            mapper |= (image[8] & 0x0F) << 8;                 // mapper high nibble
            prgBanks |= (image[9] & 0x0F) << 8;               // PRG size high bits
            chrBanks |= (image[9] & 0xF0) << 4;
        }

        bool fourScreen = (flags6 & 0x08) != 0;
        Mirroring mirror = fourScreen ? Mirroring.FourScreen
            : (flags6 & 0x01) != 0 ? Mirroring.Vertical : Mirroring.Horizontal;

        int offset = 16 + ((flags6 & 0x04) != 0 ? 512 : 0); // skip trainer if present
        int prgSize = Math.Max(1, prgBanks) * 16 * 1024;
        int chrSize = chrBanks * 8 * 1024;

        var prg = image.Slice(offset, Math.Min(prgSize, image.Length - offset)).ToArray();
        offset += prgSize;

        byte[] chr;
        bool chrRam = chrBanks == 0;
        if (chrRam)
            chr = new byte[8 * 1024]; // 8 KiB CHR-RAM
        else
            chr = image.Slice(offset, Math.Min(chrSize, Math.Max(0, image.Length - offset))).ToArray();

        return new INesRom
        {
            MapperNumber = mapper,
            Mirroring = mirror,
            HasBattery = (flags6 & 0x02) != 0,
            IsNes20 = nes20,
            Prg = prg,
            Chr = chr,
            ChrIsRam = chrRam,
        };
    }
}
