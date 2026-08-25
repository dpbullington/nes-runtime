using NesRuntime.Rendering;
using NesRuntime.Sdk.Audio;
using NesRuntime.Sdk.Graphics;
using NesRuntime.Sdk.Input;

namespace NesRuntime.Sdk.Game;

/// <summary>
/// The default <see cref="IRuntimeCore"/>: it runs one game frame per <see cref="StepFrame"/> —
/// latch input, let the game update and issue draw calls, composite the PPU frame, then synthesize
/// this frame's audio. The <see cref="RuntimeHost"/> calls this on each clock tick, presents the
/// returned image, and the audio is pushed to the host backend.
/// </summary>
internal sealed class SoftwareRuntimeCore : IRuntimeCore
{
    // Frames per second the audio budget is sized against (NTSC ≈ 60).
    private const int FramesPerSecond = 60;

    private readonly NesGame _game;
    private readonly SoftwareGraphicsDevice _graphics;
    private readonly SoftwareInputManager _input;
    private readonly SynthAudioDevice _audioDevice;
    private readonly FrameBuffer _frame = new();
    private float[] _audioBuffer = [];

    public SoftwareRuntimeCore(
        NesGame game, SoftwareGraphicsDevice graphics, SoftwareInputManager input, SynthAudioDevice audioDevice)
    {
        _game = game;
        _graphics = graphics;
        _input = input;
        _audioDevice = audioDevice;
    }

    public FrameBuffer StepFrame(IInputBackend input, IAudioBackend audio)
    {
        _input.Poll(input);        // latch this frame's controller state
        _graphics.BeginFrame();    // clear the per-frame sprite list
        _game.RunUpdate();         // game logic (may trigger sounds)
        _game.RunRender();         // game issues draw calls

        // A game that produces a whole frame itself (e.g. a ROM emulator) presents it directly;
        // otherwise the SDK tile/sprite compositor renders the frame.
        if (!_game.RunRenderFramebuffer(_frame.Pixels))
            _graphics.Render(_frame);

        PumpAudio(audio);          // synthesize this frame's samples and hand them to the host
        _game.AdvanceFrame();
        return _frame;
    }

    private void PumpAudio(IAudioBackend audio)
    {
        int samplesThisFrame = Math.Max(1, audio.SampleRate / FramesPerSecond);
        if (_audioBuffer.Length != samplesThisFrame)
            _audioBuffer = new float[samplesThisFrame];

        // A game that generates its own audio (e.g. a ROM emulator's APU) supplies it directly;
        // otherwise the SDK synth renders this frame.
        if (!_game.RunRenderAudio(_audioBuffer))
            _audioDevice.Render(_audioBuffer);
        audio.QueueSamples(_audioBuffer);
    }
}
