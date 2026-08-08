using NesRuntime.Core.Cartridge;
using NesRuntime.Sdk.Audio;
using NesRuntime.Sdk.Graphics;

namespace NesRuntime.Sdk.Content;

/// <summary>
/// Loaders for pre-existing NES-format assets, for authors who already have hardware-native data.
/// These sit alongside the high-level PNG/WAV importers and produce the same runtime objects.
/// </summary>
public interface INativeContent
{
    /// <summary>Loads raw CHR pattern-table data (8 KiB banks of 2bpp tiles) into a tile set.</summary>
    IReadOnlyList<TileHandle> LoadChr(ReadOnlySpan<byte> chrData);

    /// <summary>Loads an NSF (NES Sound Format) file as a playable music track.</summary>
    MusicTrack LoadNsf(ReadOnlySpan<byte> nsfBytes, int songIndex = 0);

    /// <summary>Mounts a complete iNES / NES 2.0 cartridge image (native ROM ingest).</summary>
    Cartridge LoadCartridge(ReadOnlySpan<byte> romImage);

    /// <summary>Loads a raw system-palette (.pal) definition for color decoding.</summary>
    void LoadPalette(ReadOnlySpan<byte> palData);
}
