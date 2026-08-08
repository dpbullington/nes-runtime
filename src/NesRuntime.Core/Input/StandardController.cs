namespace NesRuntime.Core.Input;

/// <summary>
/// The standard NES controller: a D-pad plus A, B, Select and Start. The host feeds current
/// button state into <see cref="SetState"/>; the console reads it back through the serial protocol.
/// </summary>
public sealed class StandardController : IInputDevice
{
    public string DeviceName => "Standard Controller";

    /// <summary>The buttons currently held, as reported by the host each frame.</summary>
    public NesButtons State { get; private set; }

    public void SetState(NesButtons buttons) => throw new NotImplementedException();

    public void Strobe(bool high) => throw new NotImplementedException();

    public byte ReadBit() => throw new NotImplementedException();
}
