namespace NesRuntime.Core;

/// <summary>
/// Regional hardware variant. Determines master clock, CPU/PPU timing dividers, frame rate,
/// and the number of scanlines per frame.
/// </summary>
public enum Region
{
    /// <summary>North America / Japan. ~60.10 Hz, 262 scanlines.</summary>
    Ntsc,

    /// <summary>Europe / Australia. ~50.01 Hz, 312 scanlines.</summary>
    Pal,

    /// <summary>Famiclone hybrid (PAL video timing, NTSC-like CPU).</summary>
    Dendy,
}

/// <summary>
/// Derived timing characteristics for a <see cref="Region"/>. Exposed so the runtime host can
/// pace frames and the APU can resolve channel periods without hard-coding a single region.
/// </summary>
public sealed class ConsoleTiming
{
    public Region Region { get; }

    /// <summary>Master oscillator frequency in Hz.</summary>
    public double MasterClockHz => throw new NotImplementedException();

    /// <summary>Effective CPU frequency in Hz (master clock divided by the region divider).</summary>
    public double CpuClockHz => throw new NotImplementedException();

    /// <summary>Nominal frames per second.</summary>
    public double FramesPerSecond => throw new NotImplementedException();

    /// <summary>Scanlines rendered per frame (visible + post-render + vblank + pre-render).</summary>
    public int ScanlinesPerFrame => throw new NotImplementedException();

    private ConsoleTiming(Region region) => Region = region;

    /// <summary>Resolves the canonical timing table for the given region.</summary>
    public static ConsoleTiming For(Region region) => throw new NotImplementedException();
}
