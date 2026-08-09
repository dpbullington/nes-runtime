using NesRuntime.Core.Ppu;
using NesRuntime.Sdk.Audio;
using NesRuntime.Sdk.Game;
using NesRuntime.Sdk.Graphics;
using NesRuntime.Sdk.Input;

// MAUI's implicit usings pull in Microsoft.Maui.Controls.Button; disambiguate to the SDK's button enum.
using Button = NesRuntime.Sdk.Input.Button;

namespace NesRuntime.Maui.Games;

/// <summary>
/// A Breakout-style clone built on the runtime SDK. The brick wall is drawn with background tiles
/// (so it isn't bound by the sprite budget, and clearing a brick is a single nametable write); the
/// paddle, ball, and HUD are sprites. Move with Left/Right, launch with A. Clear the wall to win;
/// lose a life when the ball falls past the paddle.
/// </summary>
public sealed class BreakoutGame : NesGame
{
    // Playfield bounds (inside the 1-tile border), in pixels.
    private const int PlayLeft = 8, PlayRight = 248, PlayTop = 8, PlayBottom = 232;

    // Brick wall: 14 columns x 5 rows, each brick 16x8, on spaced tile rows so every row sits in
    // its own attribute block (letting each row have its own palette with no color bleed).
    private const int BrickCols = 14, BrickRows = 5, BrickW = 16, BrickH = 8;
    private const int BrickOriginX = 16;      // tile column 2
    private static int BrickTileRow(int r) => 3 + r * 2;   // tile rows 3,5,7,9,11
    private static int BrickX(int c) => BrickOriginX + c * BrickW;
    private static int BrickY(int r) => BrickTileRow(r) * 8;

    // Paddle and ball.
    private const int PaddleW = 32, PaddleH = 8, PaddleY = 216, PaddleSpeed = 3;
    private const int Ball = 8, BallSpeed = 2;

    private readonly bool[,] _brick = new bool[BrickRows, BrickCols];
    private int _bricksLeft;

    private int _paddleX;
    private int _ballX, _ballY, _ballVX, _ballVY;
    private bool _ballOnPaddle;
    private int _score, _lives;
    private bool _gameOver, _won;

    private TileHandle _block, _blank, _brickL, _brickR, _paddle, _ball;
    private readonly TileHandle[] _digit = new TileHandle[10];

    private SoundEffect _bounce = null!, _break = null!, _miss = null!, _launch = null!;

    public override GameInfo Info => new() { Title = "Breakout", Author = "NesRuntime sample", MaxPlayers = 1 };

    protected override void Load()
    {
        Graphics.Palettes.Backdrop = SystemColors.Black;
        Graphics.Palettes.SetBackground(0, SystemColors.Blue, SystemColors.Gray, SystemColors.White);   // border
        Graphics.Palettes.SetBackground(1, new NesColor(0x16), SystemColors.Gray, SystemColors.White);  // red bricks
        Graphics.Palettes.SetBackground(2, new NesColor(0x27), SystemColors.Gray, SystemColors.White);  // orange bricks
        Graphics.Palettes.SetBackground(3, new NesColor(0x2A), SystemColors.Gray, SystemColors.White);  // green bricks
        Graphics.Palettes.SetSprite(0, SystemColors.White, SystemColors.Red, SystemColors.Gray);        // paddle/ball/HUD

        _block = Graphics.Tiles.Add("block", Fill(1));
        _blank = Graphics.Tiles.Add("blank", Fill(0));
        _brickL = Graphics.Tiles.Add("brickL", BrickTile(leftEdge: true));
        _brickR = Graphics.Tiles.Add("brickR", BrickTile(leftEdge: false));
        _paddle = Graphics.Tiles.Add("paddle", PaddleTile());
        _ball = Graphics.Tiles.Add("ball", BallTile());
        for (int d = 0; d < 10; d++) _digit[d] = Graphics.Tiles.Add($"d{d}", Glyph(Digits[d]));

        _bounce = Tone(76, 3);   // paddle/wall blip
        _break = Tone(84, 3);    // brick pop (higher)
        _miss = Tone(48, 12);    // low buzz
        _launch = Tone(72, 4);

        DrawBorder();
        ResetGame();
    }

    protected override void Update()
    {
        GamePad pad = Input.GetPad(0);

        // Paddle follows Left/Right in all states.
        if (pad.IsDown(Button.Left)) _paddleX -= PaddleSpeed;
        if (pad.IsDown(Button.Right)) _paddleX += PaddleSpeed;
        _paddleX = Math.Clamp(_paddleX, PlayLeft, PlayRight - PaddleW);

        bool launch = pad.WasPressed(Button.A) || pad.WasPressed(Button.Up) || pad.WasPressed(Button.Start);

        if (_gameOver || _won)
        {
            if (launch) ResetGame();
            return;
        }

        if (_ballOnPaddle)
        {
            _ballX = _paddleX + (PaddleW - Ball) / 2;
            _ballY = PaddleY - Ball;
            if (launch)
            {
                _ballOnPaddle = false;
                _ballVX = BallSpeed;
                _ballVY = -BallSpeed;
                Audio.Play(_launch);
            }
            return;
        }

        _ballX += _ballVX;
        _ballY += _ballVY;

        // Walls.
        if (_ballX <= PlayLeft) { _ballX = PlayLeft; _ballVX = -_ballVX; Audio.Play(_bounce); }
        if (_ballX + Ball >= PlayRight) { _ballX = PlayRight - Ball; _ballVX = -_ballVX; Audio.Play(_bounce); }
        if (_ballY <= PlayTop) { _ballY = PlayTop; _ballVY = -_ballVY; Audio.Play(_bounce); }

        // Paddle (only while descending).
        if (_ballVY > 0 && Overlap(_ballX, _ballY, Ball, Ball, _paddleX, PaddleY, PaddleW, PaddleH))
        {
            _ballY = PaddleY - Ball;
            _ballVY = -_ballVY;
            int rel = (_ballX + Ball / 2) - (_paddleX + PaddleW / 2); // -16..+16 across the paddle
            _ballVX = rel < -6 ? -BallSpeed : rel > 6 ? BallSpeed : _ballVX;
            Audio.Play(_bounce);
        }

        HitBrick();

        // Missed the ball.
        if (_ballY + Ball >= PlayBottom)
        {
            _lives--;
            Audio.Play(_miss);
            if (_lives <= 0)
            {
                _gameOver = true;
                Graphics.Palettes.Backdrop = new NesColor(0x06); // dark red tint = game over
            }
            else
            {
                _ballOnPaddle = true;
            }
        }

        if (_bricksLeft == 0)
        {
            _won = true;
            Graphics.Palettes.Backdrop = new NesColor(0x1A); // dark green tint = cleared
        }
    }

    protected override void Render()
    {
        for (int i = 0; i < PaddleW / 8; i++)
            Graphics.Sprites.Draw(new SpriteDraw(_paddleX + i * 8, PaddleY, _paddle, 0));

        Graphics.Sprites.Draw(new SpriteDraw(_ballX, _ballY, _ball, 0));

        DrawNumber(_score, x: 16, y: 8, width: 4);
        for (int i = 0; i < _lives; i++)
            Graphics.Sprites.Draw(new SpriteDraw(232 - i * 8, 8, _ball, 0));
    }

    // --- Gameplay helpers ---

    private void HitBrick()
    {
        for (int r = 0; r < BrickRows; r++)
        for (int c = 0; c < BrickCols; c++)
        {
            if (!_brick[r, c])
                continue;
            if (!Overlap(_ballX, _ballY, Ball, Ball, BrickX(c), BrickY(r), BrickW, BrickH))
                continue;

            _brick[r, c] = false;
            _bricksLeft--;
            _score += 10;

            int tileCol = 2 + c * 2, tileRow = BrickTileRow(r);
            Graphics.Background.SetTile(tileCol, tileRow, _blank, 0);
            Graphics.Background.SetTile(tileCol + 1, tileRow, _blank, 0);

            _ballVY = -_ballVY;
            Audio.Play(_break);
            return; // one brick per frame
        }
    }

    private void ResetGame()
    {
        _score = 0;
        _lives = 3;
        _gameOver = false;
        _won = false;
        Graphics.Palettes.Backdrop = SystemColors.Black;

        _bricksLeft = 0;
        for (int r = 0; r < BrickRows; r++)
        for (int c = 0; c < BrickCols; c++)
        {
            _brick[r, c] = true;
            _bricksLeft++;
            int pal = 1 + (r % 3);
            int tileCol = 2 + c * 2, tileRow = BrickTileRow(r);
            Graphics.Background.SetTile(tileCol, tileRow, _brickL, pal);
            Graphics.Background.SetTile(tileCol + 1, tileRow, _brickR, pal);
        }

        _paddleX = (PlayLeft + PlayRight - PaddleW) / 2;
        _ballOnPaddle = true;
    }

    private void DrawBorder()
    {
        IBackgroundLayer bg = Graphics.Background;
        for (int x = 0; x < bg.WidthInTiles; x++)
        {
            bg.SetTile(x, 0, _block, 0);
            bg.SetTile(x, bg.HeightInTiles - 1, _block, 0);
        }
        for (int y = 0; y < bg.HeightInTiles; y++)
        {
            bg.SetTile(0, y, _block, 0);
            bg.SetTile(bg.WidthInTiles - 1, y, _block, 0);
        }
    }

    private void DrawNumber(int value, int x, int y, int width)
    {
        for (int i = width - 1; i >= 0; i--)
        {
            Graphics.Sprites.Draw(new SpriteDraw(x + i * 8, y, _digit[value % 10], 0));
            value /= 10;
        }
    }

    private static bool Overlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        => ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

    private SoundEffect Tone(int midi, int frames)
    {
        var steps = new List<ToneStep>();
        for (int f = 0; f < frames; f++) steps.Add(new ToneStep(Note.FromMidi(midi), 10));
        return SoundEffect.FromSteps($"t{midi}", steps);
    }

    // --- Tile pixel data (8x8, values 0-3). ---

    private static byte[] Fill(byte v)
    {
        var p = new byte[64];
        Array.Fill(p, v);
        return p;
    }

    private static byte[] BrickTile(bool leftEdge)
    {
        var p = new byte[64];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            bool mortar = y == 0 || (leftEdge ? x == 0 : x == 7); // 1px black grout on top + outer edge
            p[y * 8 + x] = mortar ? (byte)0 : (byte)1;
        }
        return p;
    }

    private static byte[] PaddleTile()
    {
        var p = new byte[64];
        for (int y = 2; y < 6; y++)
        for (int x = 0; x < 8; x++)
            p[y * 8 + x] = 1; // a flat white bar, 4px tall, spanning the tile
        return p;
    }

    private static byte[] BallTile() => Glyph(
    [
        "........",
        "..XXX...",
        ".XXXXX..",
        ".XXXXX..",
        ".XXXXX..",
        "..XXX...",
        "........",
        "........",
    ], on: 2); // value 2 -> red sprite color

    private static byte[] Glyph(string[] rows, byte on = 1)
    {
        var p = new byte[64];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            p[y * 8 + x] = rows[y][x] == 'X' ? on : (byte)0;
        return p;
    }

    private static readonly string[][] Digits =
    [
        ["..XXX...", ".X...X..", ".X..XX..", ".X.X.X..", ".XX..X..", ".X...X..", "..XXX...", "........"], // 0
        ["...X....", "..XX....", "...X....", "...X....", "...X....", "...X....", "..XXX...", "........"], // 1
        ["..XXX...", ".X...X..", ".....X..", "...XX...", "..X.....", ".X......", ".XXXXX..", "........"], // 2
        ["..XXX...", ".X...X..", ".....X..", "...XX...", ".....X..", ".X...X..", "..XXX...", "........"], // 3
        ["....X...", "...XX...", "..X.X...", ".X..X...", ".XXXXX..", "....X...", "....X...", "........"], // 4
        [".XXXXX..", ".X......", ".XXXX...", ".....X..", ".....X..", ".X...X..", "..XXX...", "........"], // 5
        ["...XX...", "..X.....", ".X......", ".XXXX...", ".X...X..", ".X...X..", "..XXX...", "........"], // 6
        [".XXXXX..", ".....X..", "....X...", "...X....", "..X.....", "..X.....", "..X.....", "........"], // 7
        ["..XXX...", ".X...X..", ".X...X..", "..XXX...", ".X...X..", ".X...X..", "..XXX...", "........"], // 8
        ["..XXX...", ".X...X..", ".X...X..", "..XXXX..", ".....X..", "....X...", "..XX....", "........"], // 9
    ];
}
