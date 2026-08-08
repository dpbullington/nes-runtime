namespace NesRuntime.Sdk.Storage;

/// <summary>
/// Persistent save storage backed by the cartridge's battery-backed RAM. Games read and write a
/// fixed-size region that survives across sessions; the host persists it to a file. Space is
/// bounded by the board's save RAM (commonly 8 KiB), an authentic limit the SDK surfaces.
/// </summary>
public interface ISaveManager
{
    /// <summary>Total save capacity in bytes.</summary>
    int Capacity { get; }

    /// <summary>True when a save exists and has been loaded.</summary>
    bool HasData { get; }

    /// <summary>Serializes a value into the save region (bounded by <see cref="Capacity"/>).</summary>
    void Write<T>(string key, T value);

    /// <summary>Reads a previously saved value, or the default when absent.</summary>
    T? Read<T>(string key);

    /// <summary>Flushes pending changes to the host's persistent storage.</summary>
    void Commit();

    /// <summary>Erases all saved data.</summary>
    void Clear();
}
