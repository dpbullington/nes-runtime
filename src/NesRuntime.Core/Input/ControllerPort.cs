namespace NesRuntime.Core.Input;

/// <summary>
/// One of the console's two front controller ports ($4016 for port 1, $4017 for port 2). A single
/// <see cref="IInputDevice"/> is plugged in at a time; a multitap (Four Score) is itself a device
/// that fans out to several pads.
/// </summary>
public sealed class ControllerPort
{
    /// <summary>1 or 2.</summary>
    public int PortNumber { get; }

    public IInputDevice? Device { get; private set; }

    public ControllerPort(int portNumber) => throw new NotImplementedException();

    public void Connect(IInputDevice device) => throw new NotImplementedException();

    public void Disconnect() => throw new NotImplementedException();
}
