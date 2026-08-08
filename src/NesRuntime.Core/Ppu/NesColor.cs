namespace NesRuntime.Core.Ppu;

/// <summary>
/// A single index (0-63) into the fixed NES system palette. This is <em>not</em> an RGB value:
/// the console can only display the 64 predefined colors. A <see cref="MasterPalette"/> resolves
/// an index to displayable RGB for a given host and optional color-emphasis state.
/// </summary>
public readonly record struct NesColor(byte Index)
{
    public static implicit operator NesColor(byte index) => new(index);

    /// <summary>Validates that the index falls within the 64-entry system palette.</summary>
    public bool IsValid => Index < NesHardware.MasterPaletteSize;
}
