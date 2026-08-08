namespace NesRuntime.Core.Memory;

/// <summary>An inclusive 16-bit address window on the CPU or PPU bus.</summary>
public readonly record struct AddressRange(ushort Start, ushort End)
{
    public int Length => End - Start + 1;

    public bool Contains(ushort address) => address >= Start && address <= End;
}
