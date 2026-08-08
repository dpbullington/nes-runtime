namespace NesRuntime.Core.Input;

/// <summary>
/// The Famicom/NES expansion connector. Hosts additional peripherals beyond the two standard
/// ports — the Four Score multitap (players 3 and 4), the Family BASIC keyboard, the Power Pad,
/// paddles, and similar devices. Each is modeled as an <see cref="IInputDevice"/>.
/// </summary>
public sealed class ExpansionPort
{
    public IInputDevice? Device { get; private set; }

    public void Connect(IInputDevice device) => throw new NotImplementedException();

    public void Disconnect() => throw new NotImplementedException();
}

/// <summary>
/// The Four Score / Satellite multitap: aggregates up to four controllers across the two ports so
/// a game can read players 3 and 4. Presented to the console as one serial device per port.
/// </summary>
public sealed class FourScore : IInputDevice
{
    public string DeviceName => "Four Score";

    public StandardController Player1 { get; }
    public StandardController Player2 { get; }
    public StandardController Player3 { get; }
    public StandardController Player4 { get; }

    public FourScore() => throw new NotImplementedException();

    public void Strobe(bool high) => throw new NotImplementedException();

    public byte ReadBit() => throw new NotImplementedException();
}
