namespace NesRuntime.Core.Cartridge;

/// <summary>Program ROM: the game code and data, organized in 16 KiB banks the mapper switches
/// into the CPU's $8000-$FFFF window.</summary>
public sealed class PrgRom
{
    public const int BankSize = 0x4000; // 16 KiB

    public int BankCount => throw new NotImplementedException();

    public PrgRom(ReadOnlyMemory<byte> data) => throw new NotImplementedException();

    /// <summary>Reads a byte from a specific bank at an offset within it.</summary>
    public byte Read(int bank, int offset) => throw new NotImplementedException();
}

/// <summary>
/// Character memory: the tile bitmaps sampled by the PPU, in 8 KiB banks switched into
/// $0000-$1FFF. A board ships either fixed CHR-ROM or writable CHR-RAM (in which case the game
/// generates tiles at runtime); <see cref="IsWritable"/> distinguishes them.
/// </summary>
public sealed class ChrMemory
{
    public const int BankSize = 0x2000; // 8 KiB

    public bool IsWritable => throw new NotImplementedException();

    public int BankCount => throw new NotImplementedException();

    /// <summary>Creates fixed CHR-ROM from cartridge data.</summary>
    public static ChrMemory Rom(ReadOnlyMemory<byte> data) => throw new NotImplementedException();

    /// <summary>Creates writable CHR-RAM of the given size.</summary>
    public static ChrMemory Ram(int sizeBytes) => throw new NotImplementedException();

    public byte Read(int bank, int offset) => throw new NotImplementedException();

    public void Write(int bank, int offset, byte value) => throw new NotImplementedException();
}
