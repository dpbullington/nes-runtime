using NesRuntime.Core.Input;

namespace NesRuntime.Rendering;

/// <summary>
/// Supplies controller state from the host's real input (keyboard, gamepad, touch). The runtime
/// polls it once per frame and latches the result into the console's controller ports, mapping
/// each player index to a port/multitap slot.
/// </summary>
public interface IInputBackend
{
    /// <summary>Reads the button state for a player (0-based) for the frame about to run.</summary>
    NesButtons PollButtons(int playerIndex);

    /// <summary>Reads Zapper/pointer aim and trigger for light-gun games, if the host provides it.</summary>
    bool TryPollPointer(int playerIndex, out int x, out int y, out bool trigger);
}
