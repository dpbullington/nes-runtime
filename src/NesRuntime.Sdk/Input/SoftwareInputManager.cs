using NesRuntime.Rendering;

namespace NesRuntime.Sdk.Input;

/// <summary>
/// The default <see cref="IInputManager"/>: it polls an <see cref="IInputBackend"/> once per frame
/// and feeds each player's latched button state into a <see cref="GamePad"/> for edge detection.
/// </summary>
public sealed class SoftwareInputManager : IInputManager
{
    private const int MaxPlayers = 4;

    private readonly GamePad[] _pads = [new(), new(), new(), new()];

    public int PlayerCount => MaxPlayers;

    public GamePad GetPad(int playerIndex) => _pads[playerIndex];

    public Pointer? GetPointer(int playerIndex) => null;

    /// <summary>Latches this frame's input for every player from the host backend.</summary>
    public void Poll(IInputBackend input)
    {
        for (int player = 0; player < MaxPlayers; player++)
            _pads[player].Update(input.PollButtons(player));
    }
}
