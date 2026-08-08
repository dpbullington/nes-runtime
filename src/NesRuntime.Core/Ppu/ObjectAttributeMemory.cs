namespace NesRuntime.Core.Ppu;

/// <summary>
/// Object Attribute Memory: 256 bytes describing up to 64 hardware sprites (4 bytes each). The
/// PPU can only draw <see cref="NesHardware.MaxSpritesPerScanline"/> per scanline; exceeding that
/// sets the sprite-overflow flag and drops sprites, a limit the SDK enforces and reports.
/// </summary>
public sealed class ObjectAttributeMemory
{
    public const int Capacity = NesHardware.MaxSprites;

    /// <summary>A Y value that parks a sprite entirely off the bottom of the screen.</summary>
    public const byte OffScreenY = 0xFF;

    private readonly OamSprite[] _sprites = new OamSprite[Capacity];

    public ObjectAttributeMemory() => HideAll();

    public OamSprite this[int index]
    {
        get => _sprites[index];
        set => _sprites[index] = value;
    }

    /// <summary>Parks every sprite off-screen (used at the start of each frame's OAM refresh).</summary>
    public void HideAll()
    {
        for (int i = 0; i < _sprites.Length; i++)
            _sprites[i] = new OamSprite { Y = OffScreenY };
    }

    /// <summary>Bulk-loads all 256 bytes, as an OAM DMA transfer does.</summary>
    public void LoadFrom(ReadOnlySpan<byte> page)
    {
        for (int i = 0; i < Capacity; i++)
        {
            int b = i * 4;
            byte attr = page[b + 2];
            _sprites[i] = new OamSprite
            {
                Y = page[b],
                TileIndex = page[b + 1],
                Palette = (byte)(attr & 0x03),
                BehindBackground = (attr & 0x20) != 0,
                FlipHorizontally = (attr & 0x40) != 0,
                FlipVertically = (attr & 0x80) != 0,
                X = page[b + 3],
            };
        }
    }
}

/// <summary>
/// A single hardware sprite entry. Sprite 0 participates in the sprite-0-hit signal used for
/// mid-frame raster tricks (e.g. a fixed status bar above a scrolling playfield).
/// </summary>
public struct OamSprite
{
    /// <summary>Top scanline of the sprite (the hardware draws it one line below this value).</summary>
    public byte Y;

    /// <summary>Tile index within the active sprite pattern table.</summary>
    public byte TileIndex;

    /// <summary>Sprite palette (0-3).</summary>
    public byte Palette;

    /// <summary>Drawn behind the background when set.</summary>
    public bool BehindBackground;

    public bool FlipHorizontally;
    public bool FlipVertically;

    /// <summary>Leftmost pixel column.</summary>
    public byte X;
}
