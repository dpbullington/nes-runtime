namespace NesRuntime.Core.Cpu;

/// <summary>Identifies which hardware unit asserted a maskable IRQ, so it can be cleared.</summary>
public enum InterruptSource
{
    ApuFrameCounter,
    ApuDmc,
    Mapper,
    ExpansionDevice,
}
