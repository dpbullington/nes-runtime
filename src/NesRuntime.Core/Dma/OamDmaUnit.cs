using NesRuntime.Core.Memory;
using NesRuntime.Core.Ppu;

namespace NesRuntime.Core.Dma;

/// <summary>
/// Sprite DMA. A write to $4014 copies a 256-byte page of CPU RAM straight into
/// <see cref="ObjectAttributeMemory"/> — the standard way games refresh all 64 sprites each
/// frame. The transfer stalls the CPU for ~513-514 cycles, which the runtime accounts for.
/// </summary>
public sealed class OamDmaUnit
{
    /// <summary>Approximate CPU cycles consumed by one 256-byte transfer.</summary>
    public const int TransferCycles = 513;

    /// <summary>Copies page ($XX00-$XXFF) from CPU memory into OAM and stalls the CPU.</summary>
    public void Transfer(byte pageHighByte, MemoryBus bus, ObjectAttributeMemory oam)
        => throw new NotImplementedException();
}
