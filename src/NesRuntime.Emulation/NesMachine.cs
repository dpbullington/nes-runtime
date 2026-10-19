namespace NesRuntime.Emulation;

/// <summary>Standard controller buttons, in the hardware serial-report order (A first).</summary>
[Flags]
public enum NesButton : byte
{
    None = 0, A = 1 << 0, B = 1 << 1, Select = 1 << 2, Start = 1 << 3,
    Up = 1 << 4, Down = 1 << 5, Left = 1 << 6, Right = 1 << 7,
}

/// <summary>
/// The composed NES: CPU, PPU, cartridge mapper, 2 KiB work RAM, and two controllers, tied together
/// by the CPU address map. <see cref="StepFrame"/> runs one video frame and renders it.
/// </summary>
public sealed class NesMachine : IBus
{
    private readonly byte[] _ram = new byte[0x800];
    private readonly Mos6502 _cpu;
    private readonly Ppu _ppu;
    private readonly Apu _apu;
    private readonly IMapper _mapper;

    private byte _btn1, _btn2, _shift1, _shift2;
    private bool _strobe;
    private long _cycles;

    public NesMachine(INesRom rom, int sampleRate = 44_100)
    {
        _mapper = IMapper.Create(rom);
        _ppu = new Ppu(_mapper);
        _apu = new Apu(sampleRate, Read, () => _mapper.AudioSample()); // Read = DMC DMA; delegate = cartridge sound
        _cpu = new Mos6502(this);
        _cpu.Reset();
    }

    public Ppu Ppu => _ppu;

    /// <summary>PPUMASK color-emphasis bits (R|G|B in bits 0-2) for the host to tint the frame.</summary>
    public int Emphasis => _ppu.EmphasisBits;

    /// <summary>True when the cartridge has battery-backed PRG-RAM the host should persist.</summary>
    public bool HasBattery => _mapper.HasBattery;

    /// <summary>The current battery-backed PRG-RAM to save, or empty if the cartridge has none.</summary>
    public ReadOnlySpan<byte> BatteryRam => _mapper.BatteryRam;

    /// <summary>Restores battery-backed PRG-RAM from a previously saved image (truncated/zero-padded to fit).</summary>
    public void LoadBattery(ReadOnlySpan<byte> data)
    {
        Span<byte> ram = _mapper.BatteryRam;
        if (ram.IsEmpty || data.IsEmpty) return;
        data[..Math.Min(data.Length, ram.Length)].CopyTo(ram);
    }

    /// <summary>Serializes the complete machine state (CPU/PPU/APU/mapper/RAM/controllers) to a byte
    /// array — a save state. Take it at a frame boundary (between <see cref="StepFrame"/> calls).</summary>
    public byte[] SaveState() => StateSerializer.Save(Serialize);

    /// <summary>Restores a state produced by <see cref="SaveState"/> for the <em>same</em> ROM/mapper.
    /// Throws <see cref="InvalidOperationException"/> on a malformed or mismatched state.</summary>
    public void LoadState(ReadOnlySpan<byte> data) => StateSerializer.Load(data, Serialize);

    private const int StateMagic = 0x4E455353; // "NESS"
    private const int StateVersion = 2; // v2 added PPU grayscale/emphasis

    private void Serialize(StateSerializer s)
    {
        int magic = StateMagic, version = StateVersion, mapper = _mapper.Number;
        s.Transfer(ref magic); s.Transfer(ref version); s.Transfer(ref mapper);
        if (!s.IsWriting && (magic != StateMagic || version != StateVersion || mapper != _mapper.Number))
            throw new InvalidOperationException(
                $"Save state mismatch (magic={magic:X8}, version={version}, mapper={mapper}); expected mapper {_mapper.Number}.");

        _cpu.SerializeState(s);
        _ppu.SerializeState(s);
        _apu.SerializeState(s);
        _mapper.SerializeState(s);
        s.TransferBytes(_ram);
        s.Transfer(ref _btn1); s.Transfer(ref _btn2); s.Transfer(ref _shift1); s.Transfer(ref _shift2);
        s.Transfer(ref _strobe);
        s.Transfer(ref _cycles);
    }

    /// <summary>Copies this frame's synthesized audio into <paramref name="dest"/> (padded to fit).</summary>
    public void CopyAudio(Span<float> dest) => _apu.CopyAudio(dest);

    private void RefreshIrq() => _cpu.IrqLine = _mapper.IrqPending || _apu.IrqPending;

    /// <summary>Sets the held buttons for a controller (0 or 1) before the next frame.</summary>
    public void SetButtons(int player, NesButton buttons)
    {
        if (player == 0) _btn1 = (byte)buttons;
        else _btn2 = (byte)buttons;
    }

    /// <summary>
    /// Runs one full frame. The CPU executes an instruction at a time and the PPU is caught up three
    /// dots per CPU cycle after each one, so register/CHR/palette writes take effect between
    /// instructions and only affect the pixels the PPU draws afterward — mid-scanline raster effects.
    /// The PPU sets vblank / sprite-0 hit / mapper scanline IRQ at their exact dots as it steps.
    /// </summary>
    public void StepFrame(Span<byte> framebuffer)
    {
        _cycles = 0;
        _apu.BeginFrame();
        _ppu.BeginFrame();

        while (!_ppu.FrameComplete)
        {
            long before = _cycles;
            _cycles += _cpu.Step();
            int elapsed = (int)(_cycles - before);
            _apu.Clock(elapsed);                        // advance audio by the instruction's cycles

            int stall = _apu.TakeDmcStall();            // DMC sample DMA halts the CPU ~4 cycles per fetch
            if (stall > 0) { _cycles += stall; _apu.Clock(stall); elapsed += stall; }

            _mapper.OnCpuCycles(elapsed);               // clock any CPU-cycle mapper IRQ (FME-7/VRC)
            _ppu.Run(elapsed * 3);                      // three PPU dots per CPU cycle

            RefreshIrq();
            if (_ppu.ConsumeNmi()) _cycles += _cpu.Nmi(); // vblank NMI, taken at the instruction boundary
        }

        _ppu.CopyFrame(framebuffer);
    }

    // --- IBus: the CPU address map ---

    public byte Read(ushort address)
    {
        if (address < 0x2000) return _ram[address & 0x07FF];
        if (address < 0x4000) return _ppu.ReadRegister(address & 7);
        if (address == 0x4015) { byte s = _apu.ReadStatus(); RefreshIrq(); return s; } // read clears frame IRQ
        if (address == 0x4016) return ReadController(ref _shift1, _btn1);
        if (address == 0x4017) return ReadController(ref _shift2, _btn2);
        if (address < 0x4020) return 0; // other IO reads not modeled
        return _mapper.CpuRead(address);
    }

    public void Write(ushort address, byte value)
    {
        if (address < 0x2000) { _ram[address & 0x07FF] = value; return; }
        if (address < 0x4000) { _ppu.WriteRegister(address & 7, value); return; }
        if (address == 0x4014) { OamDma(value); return; }
        if (address == 0x4016)
        {
            _strobe = (value & 1) != 0;
            if (_strobe) { _shift1 = _btn1; _shift2 = _btn2; }
            return;
        }
        if (address <= 0x4017) { _apu.WriteRegister(address, value); RefreshIrq(); return; } // $4000-$4017 APU/frame counter
        if (address < 0x4020) return; // $4018-$401F: disabled test registers
        _ppu.CatchUp();               // draw the current line up to now before CHR/mirroring may change
        _mapper.CpuWrite(address, value);
        RefreshIrq(); // a write may acknowledge ($E000) or (de)assert the mapper IRQ
    }

    private void OamDma(byte pageHigh)
    {
        Span<byte> page = stackalloc byte[256];
        for (int i = 0; i < 256; i++)
            page[i] = Read((ushort)((pageHigh << 8) | i));
        _ppu.OamDma(page);
        _cycles += 513; // DMA stalls the CPU ~513-514 cycles
    }

    private byte ReadController(ref byte shift, byte state)
    {
        if (_strobe)
            return (byte)(0x40 | (state & 1));
        byte bit = (byte)(0x40 | (shift & 1));
        shift = (byte)(0x80 | (shift >> 1)); // shift in 1s after 8 reads (open-bus behavior)
        return bit;
    }
}
