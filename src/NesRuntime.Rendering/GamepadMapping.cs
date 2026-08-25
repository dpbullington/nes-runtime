using NesRuntime.Core.Input;

namespace NesRuntime.Rendering;

/// <summary>
/// Pure mapping helpers a host input backend uses to translate a physical controller (or keyboard)
/// into NES button state. Kept free of platform types so it can be reused across hosts and tested
/// in isolation.
/// </summary>
public static class GamepadMapping
{
    /// <summary>Analog stick magnitude past which a direction counts as pressed.</summary>
    public const float DefaultDeadzone = 0.5f;

    /// <summary>Combines individual pressed flags into a <see cref="NesButtons"/> value.</summary>
    public static NesButtons FromButtons(
        bool a, bool b, bool select, bool start, bool up, bool down, bool left, bool right)
    {
        NesButtons r = NesButtons.None;
        if (a) r |= NesButtons.A;
        if (b) r |= NesButtons.B;
        if (select) r |= NesButtons.Select;
        if (start) r |= NesButtons.Start;
        if (up) r |= NesButtons.Up;
        if (down) r |= NesButtons.Down;
        if (left) r |= NesButtons.Left;
        if (right) r |= NesButtons.Right;
        return r;
    }

    /// <summary>
    /// Converts an analog stick to 4-way directions using a deadzone. Y is up-positive (the
    /// GameController-framework convention).
    /// </summary>
    public static (bool Up, bool Down, bool Left, bool Right) StickToDirections(
        float x, float y, float deadzone = DefaultDeadzone)
        => (y > deadzone, y < -deadzone, x < -deadzone, x > deadzone);
}
