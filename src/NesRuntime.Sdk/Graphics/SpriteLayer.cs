using NesRuntime.Core;
using NesRuntime.Core.Ppu;

namespace NesRuntime.Sdk.Graphics;

/// <summary>
/// How a sprite is drawn for one frame: position, which tile, which sprite palette (0-3),
/// flipping, and whether it sits behind the background. Sprite 0 (the first one submitted) can be
/// used for the classic sprite-0-hit split, exposed via <see cref="ISpriteLayer"/>.
/// </summary>
public readonly record struct SpriteDraw(
    int X,
    int Y,
    TileHandle Tile,
    int PaletteIndex,
    bool FlipHorizontally = false,
    bool FlipVertically = false,
    bool BehindBackground = false);

/// <summary>
/// The per-frame sprite list. Submit sprites each frame (immediate-mode); the layer packs them
/// into OAM. It enforces the hard cap of <see cref="NesHardware.MaxSprites"/> and reports the
/// authentic 8-sprites-per-scanline overflow so you can see when a scanline would drop sprites.
/// </summary>
public interface ISpriteLayer
{
    /// <summary>Sprites submitted so far this frame.</summary>
    int Count { get; }

    /// <summary>8x8 or 8x16 for all sprites this frame (a global PPU setting).</summary>
    SpriteSize Size { get; set; }

    /// <summary>Queues a sprite for this frame. Throws if the 64-sprite cap is exceeded.</summary>
    void Draw(in SpriteDraw sprite);

    /// <summary>True if any scanline this frame held more than 8 sprites (overflow / dropout).</summary>
    bool ScanlineOverflowed { get; }

    /// <summary>Clears the sprite list (called automatically at the top of each frame).</summary>
    void Clear();
}
