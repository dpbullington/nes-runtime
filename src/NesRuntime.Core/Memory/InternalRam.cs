namespace NesRuntime.Core.Memory;

/// <summary>
/// The console's 2 KiB of internal work RAM ($0000-$07FF), mirrored three more times up to
/// $1FFF. Zero page ($0000-$00FF) and the hardware stack page ($0100-$01FF) live here.
/// </summary>
public sealed class InternalRam : IMemoryRegion
{
    public AddressRange Range => new(0x0000, 0x1FFF);

    public byte Read(ushort address) => throw new NotImplementedException();

    public void Write(ushort address, byte value) => throw new NotImplementedException();

    public void Clear() => throw new NotImplementedException();
}
