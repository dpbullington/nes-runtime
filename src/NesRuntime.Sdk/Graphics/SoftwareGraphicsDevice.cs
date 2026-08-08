using NesRuntime.Core;
using NesRuntime.Core.Ppu;
using NesRuntime.Rendering;

namespace NesRuntime.Sdk.Graphics;

/// <summary>
/// The default <see cref="IGraphicsDevice"/>: a thin author-facing layer over a real
/// <see cref="Ppu2C02"/>. Draw calls write into the PPU's nametables, palette RAM, pattern tables,
/// and OAM; <see cref="Render"/> asks the PPU to composite a frame. All the authentic limits
/// (tile counts, palette structure, 64 sprites, 8-per-scanline) are enforced by the layers below.
/// </summary>
public sealed class SoftwareGraphicsDevice : IGraphicsDevice
{
    private readonly Ppu2C02 _ppu;
    private readonly SoftwareTileSet _tiles;
    private readonly SoftwarePaletteSet _palettes;
    private readonly SoftwareBackground _background;
    private readonly SoftwareSpriteLayer _sprites = new();
    private readonly Camera _camera = new();

    public SoftwareGraphicsDevice(Ppu2C02 ppu)
    {
        _ppu = ppu;
        _tiles = new SoftwareTileSet(ppu.LeftPatternTable, ppu.RightPatternTable);
        _palettes = new SoftwarePaletteSet(ppu.Palettes);
        _background = new SoftwareBackground(ppu.GetNametable(0));
    }

    public ITileSet Tiles => _tiles;
    public IPaletteSet Palettes => _palettes;
    public IBackgroundLayer Background => _background;
    public ISpriteLayer Sprites => _sprites;
    public Camera Camera => _camera;

    /// <summary>Resets per-frame draw state (the sprite list) before the game's Render pass.</summary>
    internal void BeginFrame() => _sprites.Clear();

    /// <summary>Flushes accumulated draw state to the PPU and composites the frame.</summary>
    internal void Render(FrameBuffer frame)
    {
        _ppu.Scroll.X = _camera.X;
        _ppu.Scroll.Y = _camera.Y;
        _ppu.Control.SpriteSize = _sprites.Size;

        _sprites.FlushTo(_ppu.Oam);
        _ppu.RenderTo(frame.Pixels);
        _sprites.ScanlineOverflowed = _ppu.Status.SpriteOverflow;
    }
}

/// <summary>Maps friendly tile handles onto the PPU's two pattern tables. Tile 0 is reserved as a
/// fully transparent tile so an untouched background shows the backdrop.</summary>
internal sealed class SoftwareTileSet : ITileSet
{
    private readonly PatternTable _left;
    private readonly PatternTable _right;
    private readonly Dictionary<string, TileHandle> _named = new();
    private int _count = 1; // index 0 stays the reserved transparent tile

    public SoftwareTileSet(PatternTable left, PatternTable right)
    {
        _left = left;
        _right = right;
    }

    public int Count => _count;
    public int Capacity => PatternTable.TileCount * NesHardware.PatternTableCount;

    public TileHandle Add(ReadOnlySpan<byte> pixels2bpp) => AddTile(name: null, pixels2bpp);

    public TileHandle Add(string name, ReadOnlySpan<byte> pixels2bpp) => AddTile(name, pixels2bpp);

    private TileHandle AddTile(string? name, ReadOnlySpan<byte> pixels2bpp)
    {
        if (_count >= Capacity)
            throw new InvalidOperationException($"Pattern tables are full ({Capacity} tiles).");

        int id = _count++;
        PatternTable table = id < PatternTable.TileCount ? _left : _right;
        table.SetTile(id % PatternTable.TileCount, Tile.FromPixels(pixels2bpp));

        var handle = new TileHandle(id);
        if (name is not null)
            _named[name] = handle;
        return handle;
    }

    public TileHandle this[string name]
        => _named.TryGetValue(name, out TileHandle handle) ? handle : new TileHandle(-1);
}

/// <summary>Writes the game's palette choices into the PPU's palette RAM.</summary>
internal sealed class SoftwarePaletteSet : IPaletteSet
{
    private readonly PaletteMemory _memory;

    public SoftwarePaletteSet(PaletteMemory memory) => _memory = memory;

    public NesColor Backdrop
    {
        get => _memory.Backdrop;
        set => _memory.Backdrop = value;
    }

    public ColorEmphasis Emphasis { get; set; }

    public void SetBackground(int index, NesColor color1, NesColor color2, NesColor color3)
        => _memory.SetBackgroundPalette(index, new PaletteEntry(color1, color2, color3));

    public void SetSprite(int index, NesColor color1, NesColor color2, NesColor color3)
        => _memory.SetSpritePalette(index, new PaletteEntry(color1, color2, color3));
}

/// <summary>Writes tile placements and palette assignments into a PPU nametable.</summary>
internal sealed class SoftwareBackground : IBackgroundLayer
{
    private readonly Nametable _nametable;

    public SoftwareBackground(Nametable nametable) => _nametable = nametable;

    public int WidthInTiles => Nametable.WidthInTiles;
    public int HeightInTiles => Nametable.HeightInTiles;

    public void SetTile(int tileX, int tileY, TileHandle tile, int paletteIndex)
    {
        _nametable.SetTileIndex(tileX, tileY, (byte)tile.Id);
        _nametable.Attributes.SetPalette(tileX, tileY, paletteIndex);
    }

    public void SetArea(int tileX, int tileY, TileHandle[,] tiles, int paletteIndex)
    {
        for (int dy = 0; dy < tiles.GetLength(0); dy++)
        for (int dx = 0; dx < tiles.GetLength(1); dx++)
            SetTile(tileX + dx, tileY + dy, tiles[dy, dx], paletteIndex);
    }

    public void Clear(TileHandle tile, int paletteIndex)
    {
        for (int y = 0; y < HeightInTiles; y++)
        for (int x = 0; x < WidthInTiles; x++)
            SetTile(x, y, tile, paletteIndex);
    }
}

/// <summary>Accumulates the frame's sprite draws (capped at 64) and flushes them into OAM.</summary>
internal sealed class SoftwareSpriteLayer : ISpriteLayer
{
    private readonly List<SpriteDraw> _draws = new(NesHardware.MaxSprites);

    public int Count => _draws.Count;
    public SpriteSize Size { get; set; } = SpriteSize.EightByEight;
    public bool ScanlineOverflowed { get; internal set; }

    public void Draw(in SpriteDraw sprite)
    {
        if (_draws.Count >= NesHardware.MaxSprites)
            throw new InvalidOperationException($"Exceeded the {NesHardware.MaxSprites}-sprite hardware limit.");

        _draws.Add(sprite);
    }

    public void Clear()
    {
        _draws.Clear();
        ScanlineOverflowed = false;
    }

    internal void FlushTo(ObjectAttributeMemory oam)
    {
        oam.HideAll();
        for (int i = 0; i < _draws.Count; i++)
        {
            SpriteDraw d = _draws[i];
            bool onScreen = d.X is >= 0 and < NesHardware.ScreenWidth
                         && d.Y is >= 0 and < NesHardware.ScreenHeight;
            if (!onScreen)
                continue; // leave it parked off-screen from HideAll

            oam[i] = new OamSprite
            {
                Y = (byte)d.Y,
                X = (byte)d.X,
                TileIndex = (byte)d.Tile.Id,
                Palette = (byte)d.PaletteIndex,
                FlipHorizontally = d.FlipHorizontally,
                FlipVertically = d.FlipVertically,
                BehindBackground = d.BehindBackground,
            };
        }
    }
}
