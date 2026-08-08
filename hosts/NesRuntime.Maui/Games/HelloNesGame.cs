using NesRuntime.Sdk.Audio;
using NesRuntime.Sdk.Game;
using NesRuntime.Sdk.Graphics;
using NesRuntime.Sdk.Input;

// MAUI's implicit usings pull in Microsoft.Maui.Controls.Button; disambiguate to the SDK's button enum.
using Button = NesRuntime.Sdk.Input.Button;

namespace NesRuntime.Maui.Games;

/// <summary>
/// A sample game that exercises the SDK end-to-end: it draws a background border and bitmap-font
/// text, plays an intro jingle on load and a blip when the hero bumps a wall, and moves a hero
/// sprite with the D-pad. This is what the runtime renders and sounds like on the MAUI host.
/// </summary>
public sealed class HelloNesGame : NesGame
{
    private const int MaxX = 256 - 8;
    private const int MaxY = 240 - 8;

    private const int TextPalette = 1;

    private readonly Dictionary<char, TileHandle> _glyphs = new();
    private TileHandle _hero;
    private SoundEffect _intro = null!;
    private SoundEffect _blip = null!;

    private int _x = 120;
    private int _y = 120;
    private bool _bonked;

    public override GameInfo Info => new()
    {
        Title = "Hello NES",
        Author = "NesRuntime sample",
        MaxPlayers = 1,
    };

    protected override void Load()
    {
        Graphics.Palettes.Backdrop = SystemColors.Black;
        Graphics.Palettes.SetBackground(0, SystemColors.Blue, SystemColors.Gray, SystemColors.White);
        Graphics.Palettes.SetBackground(TextPalette, SystemColors.White, SystemColors.Gray, SystemColors.Blue);
        Graphics.Palettes.SetSprite(0, SystemColors.White, SystemColors.Red, SystemColors.Blue);

        TileHandle block = Graphics.Tiles.Add("block", SolidTile(1));
        _hero = Graphics.Tiles.Add("hero", HeroTile());
        LoadFont();

        DrawBorder(block);
        DrawText("Hello, NES!", tileX: 10, tileY: 5, TextPalette);
        DrawText("Full ASCII font", tileX: 8, tileY: 7, TextPalette);
        DrawText("Move me!  :)", tileX: 10, tileY: 23, TextPalette);

        _intro = BuildIntroJingle();
        _blip = BuildBlip();
        Audio.Play(_intro);
    }

    protected override void Update()
    {
        GamePad pad = Input.GetPad(0);

        int dx = (pad.IsDown(Button.Right) ? 1 : 0) - (pad.IsDown(Button.Left) ? 1 : 0);
        int dy = (pad.IsDown(Button.Down) ? 1 : 0) - (pad.IsDown(Button.Up) ? 1 : 0);

        int nx = _x + dx, ny = _y + dy;
        bool bonk = nx < 0 || nx > MaxX || ny < 0 || ny > MaxY;

        _x = Math.Clamp(nx, 0, MaxX);
        _y = Math.Clamp(ny, 0, MaxY);

        if (bonk && !_bonked)
            Audio.Play(_blip); // only on the frame we first hit the wall
        _bonked = bonk;
    }

    protected override void Render()
    {
        Graphics.Sprites.Draw(new SpriteDraw(_x, _y, _hero, PaletteIndex: 0));
    }

    // --- Background helpers ---

    private void DrawBorder(TileHandle block)
    {
        IBackgroundLayer bg = Graphics.Background;
        int w = bg.WidthInTiles, h = bg.HeightInTiles;

        for (int x = 0; x < w; x++)
        {
            bg.SetTile(x, 0, block, 0);
            bg.SetTile(x, h - 1, block, 0);
        }
        for (int y = 0; y < h; y++)
        {
            bg.SetTile(0, y, block, 0);
            bg.SetTile(w - 1, y, block, 0);
        }
    }

    private void DrawText(string text, int tileX, int tileY, int palette)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (_glyphs.TryGetValue(text[i], out TileHandle glyph))
                Graphics.Background.SetTile(tileX + i, tileY, glyph, palette);
            // unmapped chars (e.g. space) are left as the transparent tile 0
        }
    }

    private void LoadFont()
    {
        foreach ((char c, string[] rows) in Font)
            _glyphs[c] = Graphics.Tiles.Add($"glyph_{c}", GlyphPixels(rows));
    }

    // --- Audio helpers ---

    private static SoundEffect BuildIntroJingle()
    {
        var steps = new List<ToneStep>();
        foreach (Note note in new[] { Note.FromMidi(60), Note.FromMidi(64), Note.FromMidi(67), Note.FromMidi(72) })
            for (int f = 0; f < 8; f++) // hold each note ~8 frames
                steps.Add(new ToneStep(note, 12));
        return SoundEffect.FromSteps("intro", steps);
    }

    private static SoundEffect BuildBlip()
    {
        var steps = new List<ToneStep>();
        for (int f = 0; f < 4; f++)
            steps.Add(new ToneStep(Note.FromMidi(81), 10)); // short A5 tick
        return SoundEffect.FromSteps("blip", steps);
    }

    // --- Tile pixel data (8x8, one byte per pixel, values 0-3 index the palette). ---

    private static byte[] SolidTile(byte value)
    {
        var pixels = new byte[64];
        Array.Fill(pixels, value);
        return pixels;
    }

    private static byte[] HeroTile() =>
    [
        0, 1, 1, 1, 1, 1, 1, 0,
        1, 2, 2, 2, 2, 2, 2, 1,
        1, 2, 3, 3, 3, 3, 2, 1,
        1, 2, 3, 3, 3, 3, 2, 1,
        1, 2, 3, 3, 3, 3, 2, 1,
        1, 2, 3, 3, 3, 3, 2, 1,
        1, 2, 2, 2, 2, 2, 2, 1,
        0, 1, 1, 1, 1, 1, 1, 0,
    ];

    private static byte[] GlyphPixels(string[] rows)
    {
        var pixels = new byte[64];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            pixels[(y * 8) + x] = rows[y][x] == 'X' ? (byte)1 : (byte)0;
        return pixels;
    }

    // An 8x8 bitmap font covering every printable ASCII character (0x20 space .. 0x7E '~').
    // Each glyph is 8 rows of 8 chars: 'X' = a set pixel, '.' = clear. Verified by rendering the
    // full sheet (structural check: all 95 present, every row 8x8; plus a visual legibility pass).
    private static readonly Dictionary<char, string[]> Font = new()
    {
        [' '] = ["........", "........", "........", "........", "........", "........", "........", "........"],
        ['!'] = ["...X....", "...X....", "...X....", "...X....", "...X....", "........", "...X....", "........"],
        ['"'] = ["..X.X...", "..X.X...", "........", "........", "........", "........", "........", "........"],
        ['#'] = ["........", "..X.X...", ".XXXXX..", "..X.X...", ".XXXXX..", "..X.X...", "........", "........"],
        ['$'] = ["...X....", "..XXXX..", ".X.X....", "..XXX...", "...X.X..", ".XXXX...", "...X....", "........"],
        ['%'] = [".XX.....", ".XX..X..", "....X...", "...X....", "..X.....", ".X..XX..", "....XX..", "........"],
        ['&'] = ["..XX....", ".X..X...", ".X..X...", "..XX....", ".X.X.X..", ".X..X...", "..XX.X..", "........"],
        ['\''] = ["...X....", "...X....", "........", "........", "........", "........", "........", "........"],
        ['('] = ["....X...", "...X....", "..X.....", "..X.....", "..X.....", "...X....", "....X...", "........"],
        [')'] = ["..X.....", "...X....", "....X...", "....X...", "....X...", "...X....", "..X.....", "........"],
        ['*'] = ["........", "..X.X...", "...X....", ".XXXXX..", "...X....", "..X.X...", "........", "........"],
        ['+'] = ["........", "...X....", "...X....", ".XXXXX..", "...X....", "...X....", "........", "........"],
        [','] = ["........", "........", "........", "........", "........", "...X....", "...X....", "..X....."],
        ['-'] = ["........", "........", "........", ".XXXXX..", "........", "........", "........", "........"],
        ['.'] = ["........", "........", "........", "........", "........", "........", "...X....", "........"],
        ['/'] = [".....X..", "....X...", "....X...", "...X....", "..X.....", "..X.....", ".X......", "........"],
        ['0'] = ["..XXX...", ".X...X..", ".X..XX..", ".X.X.X..", ".XX..X..", ".X...X..", "..XXX...", "........"],
        ['1'] = ["...X....", "..XX....", "...X....", "...X....", "...X....", "...X....", "..XXX...", "........"],
        ['2'] = ["..XXX...", ".X...X..", ".....X..", "...XX...", "..X.....", ".X......", ".XXXXX..", "........"],
        ['3'] = ["..XXX...", ".X...X..", ".....X..", "...XX...", ".....X..", ".X...X..", "..XXX...", "........"],
        ['4'] = ["....X...", "...XX...", "..X.X...", ".X..X...", ".XXXXX..", "....X...", "....X...", "........"],
        ['5'] = [".XXXXX..", ".X......", ".XXXX...", ".....X..", ".....X..", ".X...X..", "..XXX...", "........"],
        ['6'] = ["...XX...", "..X.....", ".X......", ".XXXX...", ".X...X..", ".X...X..", "..XXX...", "........"],
        ['7'] = [".XXXXX..", ".....X..", "....X...", "...X....", "..X.....", "..X.....", "..X.....", "........"],
        ['8'] = ["..XXX...", ".X...X..", ".X...X..", "..XXX...", ".X...X..", ".X...X..", "..XXX...", "........"],
        ['9'] = ["..XXX...", ".X...X..", ".X...X..", "..XXXX..", ".....X..", "....X...", "..XX....", "........"],
        [':'] = ["........", "........", "...X....", "........", "........", "...X....", "........", "........"],
        [';'] = ["........", "........", "...X....", "........", "........", "...X....", "...X....", "..X....."],
        ['<'] = ["........", "....X...", "...X....", "..X.....", "...X....", "....X...", "........", "........"],
        ['='] = ["........", "........", ".XXXXX..", "........", ".XXXXX..", "........", "........", "........"],
        ['>'] = ["........", "..X.....", "...X....", "....X...", "...X....", "..X.....", "........", "........"],
        ['?'] = ["..XXX...", ".X...X..", ".....X..", "....X...", "...X....", "........", "...X....", "........"],
        ['@'] = ["..XXX...", ".X...X..", ".X.XXX..", ".X.X.X..", ".X.XXX..", ".X......", "..XXX...", "........"],
        ['A'] = ["..XXX...", ".X...X..", ".X...X..", ".XXXXX..", ".X...X..", ".X...X..", ".X...X..", "........"],
        ['B'] = [".XXXX...", ".X...X..", ".X...X..", ".XXXX...", ".X...X..", ".X...X..", ".XXXX...", "........"],
        ['C'] = ["..XXX...", ".X...X..", ".X......", ".X......", ".X......", ".X...X..", "..XXX...", "........"],
        ['D'] = [".XXXX...", ".X...X..", ".X...X..", ".X...X..", ".X...X..", ".X...X..", ".XXXX...", "........"],
        ['E'] = [".XXXXX..", ".X......", ".X......", ".XXXX...", ".X......", ".X......", ".XXXXX..", "........"],
        ['F'] = [".XXXXX..", ".X......", ".X......", ".XXXX...", ".X......", ".X......", ".X......", "........"],
        ['G'] = ["..XXX...", ".X...X..", ".X......", ".X.XXX..", ".X...X..", ".X...X..", "..XXX...", "........"],
        ['H'] = [".X...X..", ".X...X..", ".X...X..", ".XXXXX..", ".X...X..", ".X...X..", ".X...X..", "........"],
        ['I'] = [".XXXXX..", "...X....", "...X....", "...X....", "...X....", "...X....", ".XXXXX..", "........"],
        ['J'] = ["...XXX..", "....X...", "....X...", "....X...", "....X...", ".X..X...", "..XX....", "........"],
        ['K'] = [".X...X..", ".X..X...", ".X.X....", ".XX.....", ".X.X....", ".X..X...", ".X...X..", "........"],
        ['L'] = [".X......", ".X......", ".X......", ".X......", ".X......", ".X......", ".XXXXX..", "........"],
        ['M'] = [".X...X..", ".XX.XX..", ".X.X.X..", ".X.X.X..", ".X...X..", ".X...X..", ".X...X..", "........"],
        ['N'] = [".X...X..", ".XX..X..", ".X.X.X..", ".X..XX..", ".X...X..", ".X...X..", ".X...X..", "........"],
        ['O'] = ["..XXX...", ".X...X..", ".X...X..", ".X...X..", ".X...X..", ".X...X..", "..XXX...", "........"],
        ['P'] = [".XXXX...", ".X...X..", ".X...X..", ".XXXX...", ".X......", ".X......", ".X......", "........"],
        ['Q'] = ["..XXX...", ".X...X..", ".X...X..", ".X...X..", ".X.X.X..", ".X..X...", "..XX.X..", "........"],
        ['R'] = [".XXXX...", ".X...X..", ".X...X..", ".XXXX...", ".X.X....", ".X..X...", ".X...X..", "........"],
        ['S'] = ["..XXXX..", ".X......", ".X......", "..XXX...", ".....X..", ".....X..", ".XXXX...", "........"],
        ['T'] = [".XXXXX..", "...X....", "...X....", "...X....", "...X....", "...X....", "...X....", "........"],
        ['U'] = [".X...X..", ".X...X..", ".X...X..", ".X...X..", ".X...X..", ".X...X..", "..XXX...", "........"],
        ['V'] = [".X...X..", ".X...X..", ".X...X..", ".X...X..", ".X...X..", "..X.X...", "...X....", "........"],
        ['W'] = [".X...X..", ".X...X..", ".X...X..", ".X.X.X..", ".X.X.X..", ".XX.XX..", ".X...X..", "........"],
        ['X'] = [".X...X..", ".X...X..", "..X.X...", "...X....", "..X.X...", ".X...X..", ".X...X..", "........"],
        ['Y'] = [".X...X..", ".X...X..", "..X.X...", "...X....", "...X....", "...X....", "...X....", "........"],
        ['Z'] = [".XXXXX..", ".....X..", "....X...", "...X....", "..X.....", ".X......", ".XXXXX..", "........"],
        ['['] = ["...XX...", "...X....", "...X....", "...X....", "...X....", "...X....", "...XX...", "........"],
        ['\\'] = [".X......", "..X.....", "..X.....", "...X....", "....X...", "....X...", ".....X..", "........"],
        [']'] = ["...XX...", "....X...", "....X...", "....X...", "....X...", "....X...", "...XX...", "........"],
        ['^'] = ["...X....", "..X.X...", ".X...X..", "........", "........", "........", "........", "........"],
        ['_'] = ["........", "........", "........", "........", "........", "........", "........", ".XXXXX.."],
        ['`'] = ["..X.....", "...X....", "........", "........", "........", "........", "........", "........"],
        ['a'] = ["........", "........", "..XXX...", ".....X..", "..XXXX..", ".X...X..", "..XXXX..", "........"],
        ['b'] = [".X......", ".X......", ".XXXX...", ".X...X..", ".X...X..", ".X...X..", ".XXXX...", "........"],
        ['c'] = ["........", "........", "..XXX...", ".X......", ".X......", ".X...X..", "..XXX...", "........"],
        ['d'] = [".....X..", ".....X..", "..XXXX..", ".X...X..", ".X...X..", ".X...X..", "..XXXX..", "........"],
        ['e'] = ["........", "........", "..XXX...", ".X...X..", ".XXXXX..", ".X......", "..XXX...", "........"],
        ['f'] = ["...XX...", "..X.....", ".XXX....", "..X.....", "..X.....", "..X.....", "..X.....", "........"],
        ['g'] = ["........", "........", "..XXXX..", ".X...X..", ".X...X..", "..XXXX..", ".....X..", "..XXX..."],
        ['h'] = [".X......", ".X......", ".XXXX...", ".X...X..", ".X...X..", ".X...X..", ".X...X..", "........"],
        ['i'] = ["...X....", "........", "..XX....", "...X....", "...X....", "...X....", "..XXX...", "........"],
        ['j'] = [".....X..", "........", "....XX..", ".....X..", ".....X..", ".....X..", ".X...X..", "..XXX..."],
        ['k'] = [".X......", ".X......", ".X..X...", ".X.X....", ".XX.....", ".X.X....", ".X..X...", "........"],
        ['l'] = ["..XX....", "...X....", "...X....", "...X....", "...X....", "...X....", "...XX...", "........"],
        ['m'] = ["........", "........", ".XX.X...", ".X.X.X..", ".X.X.X..", ".X.X.X..", ".X.X.X..", "........"],
        ['n'] = ["........", "........", ".XXXX...", ".X...X..", ".X...X..", ".X...X..", ".X...X..", "........"],
        ['o'] = ["........", "........", "..XXX...", ".X...X..", ".X...X..", ".X...X..", "..XXX...", "........"],
        ['p'] = ["........", "........", ".XXXX...", ".X...X..", ".X...X..", ".XXXX...", ".X......", ".X......"],
        ['q'] = ["........", "........", "..XXXX..", ".X...X..", ".X...X..", "..XXXX..", ".....X..", ".....X.."],
        ['r'] = ["........", "........", ".X.XX...", ".XX..X..", ".X......", ".X......", ".X......", "........"],
        ['s'] = ["........", "........", "..XXXX..", ".X......", "..XXX...", ".....X..", ".XXXX...", "........"],
        ['t'] = ["..X.....", "..X.....", ".XXXX...", "..X.....", "..X.....", "..X.X...", "...X....", "........"],
        ['u'] = ["........", "........", ".X...X..", ".X...X..", ".X...X..", ".X...X..", "..XXXX..", "........"],
        ['v'] = ["........", "........", ".X...X..", ".X...X..", ".X...X..", "..X.X...", "...X....", "........"],
        ['w'] = ["........", "........", ".X...X..", ".X.X.X..", ".X.X.X..", ".X.X.X..", "..X.X...", "........"],
        ['x'] = ["........", "........", ".X...X..", "..X.X...", "...X....", "..X.X...", ".X...X..", "........"],
        ['y'] = ["........", "........", ".X...X..", ".X...X..", ".X...X..", "..XXXX..", ".....X..", "..XXX..."],
        ['z'] = ["........", "........", ".XXXXX..", "....X...", "...X....", "..X.....", ".XXXXX..", "........"],
        ['{'] = ["....XX..", "...X....", "...X....", "..X.....", "...X....", "...X....", "....XX..", "........"],
        ['|'] = ["...X....", "...X....", "...X....", "...X....", "...X....", "...X....", "...X....", "........"],
        ['}'] = ["..XX....", "...X....", "...X....", "....X...", "...X....", "...X....", "..XX....", "........"],
        ['~'] = ["........", "........", "..XX.X..", ".X.XX...", "........", "........", "........", "........"],
    };
}
