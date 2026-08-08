namespace NesRuntime.Core.Apu;

/// <summary>
/// The volume/decay envelope shared by the pulse and noise channels. Either produces a constant
/// volume or a saw-down decay, optionally looping.
/// </summary>
public sealed class Envelope
{
    public bool ConstantVolume { get; set; }
    public int Volume { get; set; }          // 0-15, also the reload period when decaying
    public bool Loop { get; set; }
    public int Output => throw new NotImplementedException();

    public void Clock() => throw new NotImplementedException();
    public void Restart() => throw new NotImplementedException();
}

/// <summary>
/// The frequency sweep unit on the pulse channels: periodically shifts the timer period up or
/// down to glide pitch, muting the channel when the target period goes out of range.
/// </summary>
public sealed class SweepUnit
{
    public bool Enabled { get; set; }
    public int Period { get; set; }    // 0-7
    public bool Negate { get; set; }
    public int ShiftCount { get; set; } // 0-7

    public void Clock() => throw new NotImplementedException();
}

/// <summary>
/// The length counter that gates a channel's duration. Loaded from a 5-bit index into a fixed
/// hardware lookup table; when it decrements to zero the channel is silenced (unless halted).
/// </summary>
public sealed class LengthCounter
{
    public bool Halt { get; set; }
    public int Value => throw new NotImplementedException();
    public bool Active => throw new NotImplementedException();

    public void Load(int lengthIndex) => throw new NotImplementedException();
    public void Clock() => throw new NotImplementedException();
    public void Clear() => throw new NotImplementedException();
}
