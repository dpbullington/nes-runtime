using NesRuntime.Core.Input;
using NesRuntime.Rendering;

namespace NesRuntime.Maui.Hosting;

/// <summary>
/// Supplies controller state from the host UI. On-screen buttons (or a future keyboard/gamepad
/// mapping) call <see cref="Press"/>/<see cref="Release"/>; the runtime polls the latched state
/// once per frame via <see cref="PollButtons"/>. Supports up to four players.
/// </summary>
public sealed class MauiInputBackend : IInputBackend
{
    private readonly NesButtons[] _state = new NesButtons[4];

    /// <summary>Marks a button as held for a player (0-based).</summary>
    public void Press(int playerIndex, NesButtons button) => _state[playerIndex] |= button;

    /// <summary>Marks a button as released for a player (0-based).</summary>
    public void Release(int playerIndex, NesButtons button) => _state[playerIndex] &= ~button;

    public NesButtons PollButtons(int playerIndex)
        => (uint)playerIndex < (uint)_state.Length ? _state[playerIndex] : NesButtons.None;

    public bool TryPollPointer(int playerIndex, out int x, out int y, out bool trigger)
    {
        // No light-gun/pointer device wired in this host yet.
        x = y = 0;
        trigger = false;
        return false;
    }
}
