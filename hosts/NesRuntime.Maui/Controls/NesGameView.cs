using System.Runtime.InteropServices;
using NesRuntime.Core;
using NesRuntime.Core.Ppu;
using NesRuntime.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace NesRuntime.Maui.Controls;

/// <summary>
/// The display surface for a running game. It holds the latest <see cref="FrameBuffer"/>, resolves
/// its palette indices to pixels through a <see cref="MasterPalette"/>, and draws them into a
/// 256x240 bitmap scaled up with nearest-neighbor filtering (so pixels stay crisp) and centered
/// with the console's aspect ratio.
/// </summary>
public sealed class NesGameView : SKCanvasView
{
    private readonly int[] _rgb = new int[NesHardware.ScreenWidth * NesHardware.ScreenHeight];
    private readonly SKBitmap _bitmap = new(
        NesHardware.ScreenWidth, NesHardware.ScreenHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);

    private FrameBuffer? _pending;
    private MasterPalette? _palette;

    public NesGameView()
    {
        PaintSurface += OnPaintSurface;
    }

    /// <summary>Accepts a new frame from the video backend and requests a repaint. Called on the
    /// UI thread by the runtime loop.</summary>
    public void SubmitFrame(FrameBuffer frame)
    {
        _pending = frame;
        InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);

        if (_pending is not { } frame)
            return;

        try
        {
            _palette ??= MasterPalette.Default;
            frame.ResolveTo(_rgb, _palette);
            BlitToBitmap();

            SKRect dest = FitPreservingAspect(e.Info.Width, e.Info.Height);
            using SKImage image = SKImage.FromBitmap(_bitmap);
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        catch (NotImplementedException)
        {
            // The PPU/palette are still stubs — show that plainly rather than crashing the loop.
            DrawPlaceholder(canvas, e.Info);
        }
    }

    private void BlitToBitmap()
    {
        // ResolveTo yields 0x00RRGGBB; the Bgra8888 bitmap wants opaque pixels, so force alpha.
        for (int i = 0; i < _rgb.Length; i++)
            _rgb[i] = unchecked((int)0xFF000000) | _rgb[i];

        Marshal.Copy(_rgb, 0, _bitmap.GetPixels(), _rgb.Length);
    }

    private static SKRect FitPreservingAspect(int viewW, int viewH)
    {
        float scale = Math.Min((float)viewW / NesHardware.ScreenWidth, (float)viewH / NesHardware.ScreenHeight);
        float w = NesHardware.ScreenWidth * scale;
        float h = NesHardware.ScreenHeight * scale;
        float x = (viewW - w) / 2f;
        float y = (viewH - h) / 2f;
        return new SKRect(x, y, x + w, y + h);
    }

    private static void DrawPlaceholder(SKCanvas canvas, SKImageInfo info)
    {
        using var paint = new SKPaint { Color = SKColors.DimGray, IsAntialias = true };
        using var font = new SKFont { Size = 16 };
        const string message = "NesRuntime host ready — PPU not yet implemented";
        float width = font.MeasureText(message, paint);
        canvas.DrawText(message, (info.Width - width) / 2f, info.Height / 2f, SKTextAlign.Left, font, paint);
    }
}
