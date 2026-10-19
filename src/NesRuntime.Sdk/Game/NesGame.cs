using NesRuntime.Sdk.Audio;
using NesRuntime.Sdk.Graphics;
using NesRuntime.Sdk.Input;
using NesRuntime.Sdk.Storage;

namespace NesRuntime.Sdk.Game;

/// <summary>
/// Base class for a game written against the NES runtime. Derive from it and override the
/// lifecycle hooks; the runtime calls <see cref="Update"/> and <see cref="Render"/> once per
/// frame (~60 fps NTSC). You work entirely in C# — sprites, tiles, palettes, sound, input — and
/// never touch 6502 assembly or hardware registers. Everything you do is still bounded by real
/// NES limits, which the services enforce.
/// </summary>
public abstract class NesGame
{
    private IGraphicsDevice? _graphics;
    private IAudioDevice? _audio;
    private IInputManager? _input;
    private ISaveManager? _save;
    private long _frame;

    /// <summary>Background and sprite drawing, palettes, and scrolling.</summary>
    protected IGraphicsDevice Graphics => _graphics ?? throw NotAttached();

    /// <summary>Music and sound effects across the five NES audio channels.</summary>
    protected IAudioDevice Audio => _audio ?? throw NotAttached();

    /// <summary>Per-frame controller state with pressed/held/released edges.</summary>
    protected IInputManager Input => _input ?? throw NotAttached();

    /// <summary>Battery-backed persistence (save games / high scores).</summary>
    protected ISaveManager Save => _save ?? throw NotAttached();

    /// <summary>The current frame number since the game started.</summary>
    protected long FrameCount => _frame;

    /// <summary>Descriptive metadata (title, author, region) for tooling and the host shell.</summary>
    public virtual GameInfo Info => GameInfo.Unknown;

    /// <summary>Called once after services are ready — load tiles, palettes, and audio here.</summary>
    protected virtual void Load() { }

    /// <summary>Called once per frame before rendering — read input and advance game logic here.</summary>
    protected abstract void Update();

    /// <summary>Called once per frame after <see cref="Update"/> — issue draw calls here.</summary>
    protected abstract void Render();

    /// <summary>Called when the game is stopping — flush saves and release resources here.</summary>
    protected virtual void Unload() { }

    /// <summary>
    /// Optional escape hatch for games that produce a whole frame directly (e.g. a ROM emulator):
    /// fill <paramref name="framebuffer"/> with 256×240 system-palette indices and return true to
    /// present it verbatim, bypassing the tile/sprite compositor. Return false (the default) to use
    /// the normal SDK rendering path.
    /// </summary>
    protected virtual bool RenderFramebuffer(Span<byte> framebuffer) => false;

    /// <summary>
    /// Audio counterpart to <see cref="RenderFramebuffer"/>: fill <paramref name="buffer"/> with this
    /// frame's mono samples (normalized -1..1) and return true to output them directly, bypassing the
    /// SDK synth. Return false (the default) to use the normal SDK audio path.
    /// </summary>
    protected virtual bool RenderAudio(Span<float> buffer) => false;

    /// <summary>
    /// For framebuffer games (e.g. a ROM emulator): report this frame's PPUMASK color emphasis so the
    /// host tints the resolved RGB. No-op for the normal SDK path.
    /// </summary>
    protected void ReportEmphasis(NesRuntime.Core.Ppu.ColorEmphasis emphasis) => FrameEmphasis = emphasis;

    // --- Runtime plumbing (called by the SDK's runtime core, not by game code). ---

    /// <summary>The emphasis last reported via <see cref="ReportEmphasis"/> (default: none).</summary>
    internal NesRuntime.Core.Ppu.ColorEmphasis FrameEmphasis { get; private set; }

    internal void Attach(IGraphicsDevice graphics, IAudioDevice audio, IInputManager input, ISaveManager save)
    {
        _graphics = graphics;
        _audio = audio;
        _input = input;
        _save = save;
    }

    internal void RunLoad() => Load();
    internal void RunUpdate() => Update();
    internal void RunRender() => Render();
    internal void RunUnload() => Unload();
    internal bool RunRenderFramebuffer(Span<byte> framebuffer) => RenderFramebuffer(framebuffer);
    internal bool RunRenderAudio(Span<float> buffer) => RenderAudio(buffer);
    internal void AdvanceFrame() => _frame++;

    private static InvalidOperationException NotAttached()
        => new("The game has not been attached to a runtime yet. Launch it with GameRunner.Create.");
}
