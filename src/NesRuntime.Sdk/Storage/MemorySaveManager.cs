namespace NesRuntime.Sdk.Storage;

/// <summary>
/// A simple in-memory <see cref="ISaveManager"/> for development and hosts without a persistence
/// story yet. It holds keyed values up to the emulated save-RAM capacity; <see cref="Commit"/> is a
/// no-op until a host wires real battery-backed storage.
/// </summary>
public sealed class MemorySaveManager : ISaveManager
{
    // Common battery-backed PRG-RAM size ($6000-$7FFF).
    private const int SaveRamBytes = 0x2000;

    private readonly Dictionary<string, object?> _values = new();

    public int Capacity => SaveRamBytes;

    public bool HasData => _values.Count > 0;

    public void Write<T>(string key, T value) => _values[key] = value;

    public T? Read<T>(string key)
        => _values.TryGetValue(key, out object? value) && value is T typed ? typed : default;

    public void Commit() { }

    public void Clear() => _values.Clear();
}
