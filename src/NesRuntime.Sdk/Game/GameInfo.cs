using NesRuntime.Core;

namespace NesRuntime.Sdk.Game;

/// <summary>
/// Descriptive metadata for a game: what the host shell shows and what a build step stamps into a
/// generated cartridge header.
/// </summary>
public sealed record GameInfo
{
    public string Title { get; init; } = "Untitled";
    public string Author { get; init; } = "Unknown";
    public Region Region { get; init; } = Region.Ntsc;

    /// <summary>Number of simultaneous players the game supports (1-4 via a multitap).</summary>
    public int MaxPlayers { get; init; } = 1;

    /// <summary>True if the game uses battery-backed saves.</summary>
    public bool UsesSaves { get; init; }

    public static GameInfo Unknown => new();
}
