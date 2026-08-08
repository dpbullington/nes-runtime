namespace NesRuntime.Core.Input;

/// <summary>
/// The eight buttons of a standard NES controller. Reported by the hardware in the fixed serial
/// order A, B, Select, Start, Up, Down, Left, Right.
/// </summary>
[Flags]
public enum NesButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    Select = 1 << 2,
    Start = 1 << 3,
    Up = 1 << 4,
    Down = 1 << 5,
    Left = 1 << 6,
    Right = 1 << 7,
}
