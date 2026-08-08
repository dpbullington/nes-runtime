namespace NesRuntime.Core.Input;

/// <summary>
/// The Zapper light gun (Duck Hunt, etc.). It reports two things on the serial line: whether the
/// trigger is pulled and whether its photodiode currently senses light — games flash target
/// sprites white for a frame and check the light sensor to decide a hit.
/// </summary>
public sealed class Zapper : IInputDevice
{
    public string DeviceName => "Zapper";

    public bool TriggerPulled { get; set; }

    /// <summary>True when the sensor is pointed at a lit region of the screen this instant.</summary>
    public bool LightDetected { get; set; }

    /// <summary>Aim point in screen space, supplied by the host (mouse/touch).</summary>
    public (int X, int Y) AimPosition { get; set; }

    public void Strobe(bool high) => throw new NotImplementedException();

    public byte ReadBit() => throw new NotImplementedException();
}
