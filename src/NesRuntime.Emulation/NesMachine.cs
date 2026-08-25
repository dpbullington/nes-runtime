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
    // NTSC timing: 262 scanlines/frame, each 341 PPU dots = 341/3 CPU cycles.
    private const double CyclesPerScanline = 341.0 / 3.0;
    private const int ScanlinesPerFrame = 262;

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
        _apu = new Apu(sampleRate, Read); // Read supplies the DMC's sample DMA
        _cpu = new Mos6502(this);
        _cpu.Reset();
    }

    public Ppu Ppu => _ppu;

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
    /// Runs one full frame, rendering scanline by scanline and interleaving CPU execution in
    /// scanline-sized cycle chunks so mid-frame register writes and sprite-0 hit land at the right
    /// line.
    /// </summary>
    public void StepFrame(Span<byte> framebuffer)
    {
        _cycles = 0;
        double target = 0;
        _apu.BeginFrame();
        for (int scanline = 0; scanline < ScanlinesPerFrame; scanline++)
        {
            if (scanline == 241)
            {
                if (_ppu.EnterVBlank())
                    _cycles += _cpu.Nmi();
            }
            else if (scanline == 261)
            {
                _ppu.PreRender();
            }

            if (scanline < Ppu.Height)
            {
                _ppu.RenderScanline(scanline, framebuffer); // render before this line's CPU runs
                if (_ppu.RenderingEnabled)
                {
                    _mapper.OnScanline();               // clock the mapper's scanline IRQ counter
                    RefreshIrq();
                }
            }

            target += CyclesPerScanline;
            while (_cycles < (long)target)
            {
                long before = _cycles;
                _cycles += _cpu.Step();
                _apu.Clock((int)(_cycles - before));    // advance audio by the instruction's cycles (incl. DMA)
                RefreshIrq();
            }

            if (scanline < Ppu.Height)
                _ppu.EndVisibleScanline();
        }
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
