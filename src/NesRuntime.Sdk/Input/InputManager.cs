using NesRuntime.Core.Input;

namespace NesRuntime.Sdk.Input;

/// <summary>
/// Per-frame access to the controllers. Reads the latched state for each player and exposes it
/// through a <see cref="GamePad"/> with pressed/held/released edges, so game code checks
/// <c>WasPressed(Button.A)</c> without managing serial reads or previous-frame bookkeeping.
/// </summary>
public interface IInputManager
{
    /// <summary>Players connected this session (1-4, via a multitap for 3-4).</summary>
    int PlayerCount { get; }

    /// <summary>The pad for a player (0-based). Player 0 and 1 are the two front ports.</summary>
    GamePad GetPad(int playerIndex);

    /// <summary>The light-gun/pointer for a player, when a Zapper-style device is connected.</summary>
    Pointer? GetPointer(int playerIndex);
}

/// <summary>The eight NES buttons, as the game refers to them.</summary>
public enum Button
{
    A, B, Select, Start, Up, Down, Left, Right,
}

/// <summary>
/// A controller with edge detection. <see cref="IsDown"/> reports the held state;
/// <see cref="WasPressed"/> / <see cref="WasReleased"/> report transitions since last frame.
/// </summary>
public sealed class GamePad
{
    private NesButtons _current;
    private NesButtons _previous;

    /// <summary>Latches this frame's state, retaining last frame's for edge detection.</summary>
    internal void Update(NesButtons state)
    {
        _previous = _current;
        _current = state;
    }

    public bool IsDown(Button button) => (_current & ToFlag(button)) != 0;

    public bool WasPressed(Button button)
    {
        NesButtons flag = ToFlag(button);
        return (_current & flag) != 0 && (_previous & flag) == 0;
    }

    public bool WasReleased(Button button)
    {
        NesButtons flag = ToFlag(button);
        return (_current & flag) == 0 && (_previous & flag) != 0;
    }

    internal static NesButtons ToFlag(Button button) => button switch
    {
        Button.A => NesButtons.A,
        Button.B => NesButtons.B,
        Button.Select => NesButtons.Select,
        Button.Start => NesButtons.Start,
        Button.Up => NesButtons.Up,
        Button.Down => NesButtons.Down,
        Button.Left => NesButtons.Left,
        Button.Right => NesButtons.Right,
        _ => NesButtons.None,
    };
}

/// <summary>Aim and trigger state for a light-gun/pointer device.</summary>
public readonly record struct Pointer(int X, int Y, bool Trigger);
