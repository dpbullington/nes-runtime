using NesRuntime.Core;
using NesRuntime.Core.Ppu;
using NesRuntime.Rendering;
using NesRuntime.Sdk.Audio;
using NesRuntime.Sdk.Graphics;
using NesRuntime.Sdk.Input;
using NesRuntime.Sdk.Storage;

namespace NesRuntime.Sdk.Game;

/// <summary>
/// Bootstraps a <see cref="NesGame"/> onto the runtime: constructs a PPU, wires the SDK services
/// over it, runs the game's load hook, and produces a <see cref="RuntimeHost"/> the presentation
/// layer (e.g. a MAUI app) drives. This is the one line of glue a host needs to launch a game.
/// </summary>
public static class GameRunner
{
    /// <summary>
    /// Prepares the given game for the supplied backends and returns a host ready to
    /// <see cref="RuntimeHost.Run"/>. The game's <c>Load</c> hook runs during this call.
    /// </summary>
    public static RuntimeHost Create<TGame>(
        IVideoBackend video,
        IAudioBackend audio,
        IInputBackend input,
        IFrameClock clock,
        Region region = Region.Ntsc)
        where TGame : NesGame, new()
    {
        var ppu = new Ppu2C02();
        var graphics = new SoftwareGraphicsDevice(ppu);
        var audioDevice = new SynthAudioDevice(audio.SampleRate);
        var inputManager = new SoftwareInputManager();
        var saveManager = new MemorySaveManager();

        var game = new TGame();
        game.Attach(graphics, audioDevice, inputManager, saveManager);
        game.RunLoad();

        var core = new SoftwareRuntimeCore(game, graphics, inputManager, audioDevice);
        return new RuntimeHost(core, video, audio, input, clock);
    }
}
