namespace NesRuntime.Core.Cartridge;

/// <summary>
/// Battery-backed work RAM (PRG-RAM at $6000-$7FFF on most boards) that persists across power
/// cycles — the mechanism behind cartridge save files. The SDK's save API is layered on top of
/// this. The host is responsible for persisting and restoring the contents.
/// </summary>
public sealed class BatteryBackedRam
{
    public int SizeBytes => throw new NotImplementedException();

    /// <summary>True once the contents have changed and need to be written back to storage.</summary>
    public bool IsDirty => throw new NotImplementedException();

    public BatteryBackedRam(int sizeBytes) => throw new NotImplementedException();

    public byte Read(ushort address) => throw new NotImplementedException();

    public void Write(ushort address, byte value) => throw new NotImplementedException();

    /// <summary>Loads previously persisted contents (e.g. from a save file).</summary>
    public void Restore(ReadOnlySpan<byte> data) => throw new NotImplementedException();

    /// <summary>Snapshots the current contents for persistence.</summary>
    public byte[] Snapshot() => throw new NotImplementedException();
}
