namespace NesRuntime.Core.Input;

/// <summary>
/// A peripheral plugged into a controller port. The console latches state on a strobe write to
/// $4016, then shifts it out one bit per read from $4016/$4017. Different devices (standard pad,
/// Zapper, paddle, Four Score) encode different data on that serial line.
/// </summary>
public interface IInputDevice
{
    string DeviceName { get; }

    /// <summary>Latches the current device state (a write to $4016 with the strobe bit set).</summary>
    void Strobe(bool high);

    /// <summary>Shifts out the next serial bit read by the CPU.</summary>
    byte ReadBit();
}
