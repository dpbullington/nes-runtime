namespace NesRuntime.Emulation;

/// <summary>
/// A cartridge board. Handles CPU accesses in cartridge space ($4020-$FFFF) — PRG-ROM/RAM banking —
/// and provides CHR (pattern) data to the PPU. Additional mappers plug in behind this interface.
/// </summary>
public interface IMapper
{
    int Number { get; }
    Mirroring Mirroring { get; }

    byte CpuRead(ushort address);
    void CpuWrite(ushort address, byte value);

    byte ChrRead(ushort address);      // PPU $0000-$1FFF
    void ChrWrite(ushort address, byte value);

    // IRQ hooks. MMC3-style mappers count rendered scanlines (OnScanline); Sunsoft FME-7-style
    // mappers count CPU cycles (OnCpuCycles). Both drive IrqPending; unused ones get no-op defaults.
    bool IrqPending => false;
    void OnScanline() { }
    void OnCpuCycles(int cycles) { }

    static IMapper Create(INesRom rom) => rom.MapperNumber switch
    {
        0 => new NromMapper(rom),
        1 => new Mmc1Mapper(rom),
        2 => new UxRomMapper(rom),
        3 => new CnRomMapper(rom),
        4 => new Mmc3Mapper(rom),
        7 => new AxRomMapper(rom),
        11 => new ColorDreamsMapper(rom),
        66 => new GxRomMapper(rom),
        69 => new Fme7Mapper(rom),
        _ => throw new NotSupportedException(
            $"Mapper {rom.MapperNumber} is not supported yet " +
            "(have: 0/NROM, 1/MMC1, 2/UxROM, 3/CNROM, 4/MMC3, 7/AxROM, 11/ColorDreams, 66/GxROM, 69/FME-7)."),
    };
}

/// <summary>
/// NROM (mapper 0): 16 KiB or 32 KiB of fixed PRG-ROM at $8000-$FFFF (16 KiB is mirrored into
/// $C000), optional 8 KiB PRG-RAM at $6000, and fixed 8 KiB CHR (ROM or RAM).
/// </summary>
public sealed class NromMapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly bool _chrRam;
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly int _prgMask;

    public NromMapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr;
        _chrRam = rom.ChrIsRam;
        Mirroring = rom.Mirroring;
        _prgMask = _prg.Length > 0x4000 ? 0x7FFF : 0x3FFF;
    }

    public int Number => 0;
    public Mirroring Mirroring { get; }

    public byte CpuRead(ushort address)
    {
        if (address >= 0x8000) return _prg[address & _prgMask];
        if (address >= 0x6000) return _prgRam[address - 0x6000];
        return 0;
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000)
            _prgRam[address - 0x6000] = value;
    }

    public byte ChrRead(ushort address) => _chr[address & 0x1FFF];

    public void ChrWrite(ushort address, byte value)
    {
        if (_chrRam) _chr[address & 0x1FFF] = value;
    }
}

/// <summary>
/// UxROM (mapper 2): a switchable 16 KiB PRG bank at $8000-$BFFF, the last bank fixed at
/// $C000-$FFFF, and 8 KiB CHR-RAM. A write anywhere in $8000-$FFFF selects the low bank.
/// </summary>
public sealed class UxRomMapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly int _bankCount;
    private int _bank;

    public UxRomMapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _bankCount = Math.Max(1, _prg.Length / 0x4000);
        Mirroring = rom.Mirroring;
    }

    public int Number => 2;
    public Mirroring Mirroring { get; }

    public byte CpuRead(ushort address)
    {
        if (address >= 0xC000) return _prg[(_bankCount - 1) * 0x4000 + (address - 0xC000)];
        if (address >= 0x8000) return _prg[_bank * 0x4000 + (address - 0x8000)];
        return 0;
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address >= 0x8000) _bank = value & (_bankCount - 1);
    }

    public byte ChrRead(ushort address) => _chr[address & 0x1FFF];
    public void ChrWrite(ushort address, byte value) => _chr[address & 0x1FFF] = value; // CHR-RAM
}

/// <summary>
/// CNROM (mapper 3): fixed PRG (like NROM) with a switchable 8 KiB CHR-ROM bank. A write to
/// $8000-$FFFF selects the CHR bank.
/// </summary>
public sealed class CnRomMapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly int _prgMask;
    private readonly int _chrBankCount;
    private int _chrBank;

    public CnRomMapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgMask = _prg.Length > 0x4000 ? 0x7FFF : 0x3FFF;
        _chrBankCount = Math.Max(1, _chr.Length / 0x2000);
        Mirroring = rom.Mirroring;
    }

    public int Number => 3;
    public Mirroring Mirroring { get; }

    public byte CpuRead(ushort address) => address >= 0x8000 ? _prg[address & _prgMask] : (byte)0;

    public void CpuWrite(ushort address, byte value)
    {
        if (address >= 0x8000) _chrBank = value & (_chrBankCount - 1);
    }

    public byte ChrRead(ushort address) => _chr[_chrBank * 0x2000 + (address & 0x1FFF)];
    public void ChrWrite(ushort address, byte value) { } // CHR-ROM
}

/// <summary>
/// AxROM (mapper 7): a switchable 32 KiB PRG bank across $8000-$FFFF, 8 KiB CHR-RAM, and
/// single-screen mirroring selected by bit 4 of the bank write.
/// </summary>
public sealed class AxRomMapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly int _bankCount;
    private int _bank;

    public AxRomMapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _bankCount = Math.Max(1, _prg.Length / 0x8000);
        Mirroring = Mirroring.SingleScreenLower;
    }

    public int Number => 7;
    public Mirroring Mirroring { get; private set; }

    public byte CpuRead(ushort address) => address >= 0x8000 ? _prg[_bank * 0x8000 + (address - 0x8000)] : (byte)0;

    public void CpuWrite(ushort address, byte value)
    {
        if (address < 0x8000) return;
        _bank = value & (_bankCount - 1);
        Mirroring = (value & 0x10) != 0 ? Mirroring.SingleScreenUpper : Mirroring.SingleScreenLower;
    }

    public byte ChrRead(ushort address) => _chr[address & 0x1FFF];
    public void ChrWrite(ushort address, byte value) => _chr[address & 0x1FFF] = value; // CHR-RAM
}

/// <summary>
/// MMC1 (mapper 1): a serial-loaded controller. Writes to $8000-$FFFF shift one bit at a time into
/// a 5-bit register; the 5th write commits it to control / CHR bank 0 / CHR bank 1 / PRG bank based
/// on the address. Provides selectable PRG and CHR banking modes plus runtime mirroring.
/// </summary>
public sealed class Mmc1Mapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly bool _chrRam;
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly int _prgBankCount; // 16 KiB banks
    private readonly int _chrBankCount; // 4 KiB banks

    private int _shift, _count;
    private int _control = 0x0C; // power-on: PRG mode 3 (fix last bank at $C000)
    private int _chrBank0, _chrBank1, _prgBank;

    public Mmc1Mapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chrRam = rom.ChrIsRam;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBankCount = Math.Max(1, _prg.Length / 0x4000);
        _chrBankCount = Math.Max(1, _chr.Length / 0x1000);
    }

    public int Number => 1;

    public Mirroring Mirroring => (_control & 3) switch
    {
        0 => Mirroring.SingleScreenLower,
        1 => Mirroring.SingleScreenUpper,
        2 => Mirroring.Vertical,
        _ => Mirroring.Horizontal,
    };

    public byte CpuRead(ushort address)
    {
        if (address is >= 0x6000 and < 0x8000) return _prgRam[address - 0x6000];
        if (address < 0x8000) return 0;

        int offset = address - 0x8000; // 0..0x7FFF
        int inner = offset & 0x3FFF;
        int bank16 = ((_control >> 2) & 3) switch
        {
            0 or 1 => (_prgBank & 0x0E) + (offset >= 0x4000 ? 1 : 0),          // 32 KiB switch
            2 => offset < 0x4000 ? 0 : (_prgBank & 0x0F),                       // fix first, switch $C000
            _ => offset < 0x4000 ? (_prgBank & 0x0F) : (_prgBankCount - 1),     // switch $8000, fix last
        };
        return _prg[(bank16 & (_prgBankCount - 1)) * 0x4000 + inner];
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address < 0x8000)
        {
            if (address >= 0x6000) _prgRam[address - 0x6000] = value;
            return;
        }

        if ((value & 0x80) != 0) // reset
        {
            _shift = 0;
            _count = 0;
            _control |= 0x0C;
            return;
        }

        _shift |= (value & 1) << _count;
        if (++_count < 5)
            return;

        switch ((address >> 13) & 3)
        {
            case 0: _control = _shift; break;
            case 1: _chrBank0 = _shift; break;
            case 2: _chrBank1 = _shift; break;
            default: _prgBank = _shift & 0x0F; break;
        }
        _shift = 0;
        _count = 0;
    }

    public byte ChrRead(ushort address) => _chr[ChrOffset(address)];

    public void ChrWrite(ushort address, byte value)
    {
        if (_chrRam) _chr[ChrOffset(address)] = value;
    }

    private int ChrOffset(ushort address)
    {
        int bank4 = (_control & 0x10) == 0
            ? (_chrBank0 & 0x1E) + (address >= 0x1000 ? 1 : 0)   // 8 KiB mode
            : (address < 0x1000 ? _chrBank0 : _chrBank1);         // two 4 KiB banks
        return (bank4 & (_chrBankCount - 1)) * 0x1000 + (address & 0x0FFF);
    }
}

/// <summary>
/// MMC3 (mapper 4): the workhorse board. Eight bank registers select two switchable + two fixed 8 KiB
/// PRG banks and six CHR banks (two 2 KiB + four 1 KiB, with an A12-inversion mode), plus runtime
/// mirroring and a **scanline-counter IRQ** used for mid-frame splits. The counter is clocked once
/// per rendered scanline (the standard A12-per-scanline approximation).
/// </summary>
public sealed class Mmc3Mapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly bool _chrRam;
    private readonly bool _fourScreen;
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly int _prgBanks8k;
    private readonly int _chrBanks1k;
    private readonly int[] _bank = new int[8]; // R0-R7

    private int _select;
    private Mirroring _mirroring;
    private int _irqLatch, _irqCounter;
    private bool _irqReload, _irqEnabled, _irqPending;

    public Mmc3Mapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chrRam = rom.ChrIsRam;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks8k = Math.Max(1, _prg.Length / 0x2000);
        _chrBanks1k = Math.Max(1, _chr.Length / 0x400);
        _fourScreen = rom.Mirroring == Mirroring.FourScreen;
        _mirroring = _fourScreen ? Mirroring.FourScreen : rom.Mirroring;
    }

    public int Number => 4;
    public Mirroring Mirroring => _mirroring;
    public bool IrqPending => _irqPending;

    public byte CpuRead(ushort address)
    {
        if (address is >= 0x6000 and < 0x8000) return _prgRam[address - 0x6000];
        if (address < 0x8000) return 0;
        return _prg[(PrgBank(address) % _prgBanks8k) * 0x2000 + (address & 0x1FFF)];
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000) { _prgRam[address - 0x6000] = value; return; }
        if (address < 0x8000) return;

        bool odd = (address & 1) != 0;
        switch (address & 0xE000)
        {
            case 0x8000: if (odd) _bank[_select & 7] = value; else _select = value; break;
            case 0xA000: if (!odd && !_fourScreen) _mirroring = (value & 1) == 0 ? Mirroring.Vertical : Mirroring.Horizontal; break;
            case 0xC000: if (odd) { _irqCounter = 0; _irqReload = true; } else _irqLatch = value; break;
            case 0xE000: if (odd) _irqEnabled = true; else { _irqEnabled = false; _irqPending = false; } break;
        }
    }

    public byte ChrRead(ushort address) => _chr[ChrOffset(address)];
    public void ChrWrite(ushort address, byte value) { if (_chrRam) _chr[ChrOffset(address)] = value; }

    public void OnScanline()
    {
        if (_irqCounter == 0 || _irqReload) { _irqCounter = _irqLatch; _irqReload = false; }
        else _irqCounter--;
        if (_irqCounter == 0 && _irqEnabled) _irqPending = true;
    }

    private int PrgBank(ushort address)
    {
        bool swap = (_select & 0x40) != 0; // fix $8000 vs $C000
        return (address & 0x6000) switch
        {
            0x0000 => swap ? _prgBanks8k - 2 : _bank[6], // $8000-$9FFF
            0x2000 => _bank[7],                           // $A000-$BFFF
            0x4000 => swap ? _bank[6] : _prgBanks8k - 2,  // $C000-$DFFF
            _ => _prgBanks8k - 1,                          // $E000-$FFFF (fixed last)
        };
    }

    private int ChrOffset(ushort address)
    {
        int region = address >> 10;             // 0-7 (1 KiB regions)
        if ((_select & 0x80) != 0) region ^= 4; // A12 inversion swaps the two halves
        int bank1k = region switch
        {
            0 => (_bank[0] & 0xFE) + 0,          // R0 (2 KiB)
            1 => (_bank[0] & 0xFE) + 1,
            2 => (_bank[1] & 0xFE) + 0,          // R1 (2 KiB)
            3 => (_bank[1] & 0xFE) + 1,
            4 => _bank[2],
            5 => _bank[3],
            6 => _bank[4],
            _ => _bank[5],
        };
        return (bank1k % _chrBanks1k) * 0x400 + (address & 0x3FF);
    }
}

/// <summary>
/// GxROM (mapper 66): a single bank register at $8000-$FFFF selects a switchable 32 KiB PRG bank
/// (bits 4-5) and a switchable 8 KiB CHR-ROM bank (bits 0-1). Fixed header mirroring.
/// </summary>
public sealed class GxRomMapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly int _prgBanks32k;
    private readonly int _chrBanks8k;
    private int _prgBank, _chrBank;

    public GxRomMapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks32k = Math.Max(1, _prg.Length / 0x8000);
        _chrBanks8k = Math.Max(1, _chr.Length / 0x2000);
        Mirroring = rom.Mirroring;
    }

    public int Number => 66;
    public Mirroring Mirroring { get; }

    public byte CpuRead(ushort address) =>
        address >= 0x8000 ? _prg[(_prgBank % _prgBanks32k) * 0x8000 + (address - 0x8000)] : (byte)0;

    public void CpuWrite(ushort address, byte value)
    {
        if (address < 0x8000) return;
        _prgBank = (value >> 4) & 3;
        _chrBank = value & 3;
    }

    public byte ChrRead(ushort address) => _chr[(_chrBank % _chrBanks8k) * 0x2000 + (address & 0x1FFF)];
    public void ChrWrite(ushort address, byte value) { } // CHR-ROM
}

/// <summary>
/// Color Dreams (mapper 11): a single bank register at $8000-$FFFF selects a switchable 32 KiB PRG
/// bank (low 2 bits) and a switchable 8 KiB CHR-ROM bank (high 4 bits). Fixed header mirroring.
/// </summary>
public sealed class ColorDreamsMapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly int _prgBanks32k;
    private readonly int _chrBanks8k;
    private int _prgBank, _chrBank;

    public ColorDreamsMapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks32k = Math.Max(1, _prg.Length / 0x8000);
        _chrBanks8k = Math.Max(1, _chr.Length / 0x2000);
        Mirroring = rom.Mirroring;
    }

    public int Number => 11;
    public Mirroring Mirroring { get; }

    public byte CpuRead(ushort address) =>
        address >= 0x8000 ? _prg[(_prgBank % _prgBanks32k) * 0x8000 + (address - 0x8000)] : (byte)0;

    public void CpuWrite(ushort address, byte value)
    {
        if (address < 0x8000) return;
        _prgBank = value & 3;
        _chrBank = (value >> 4) & 0x0F;
    }

    public byte ChrRead(ushort address) => _chr[(_chrBank % _chrBanks8k) * 0x2000 + (address & 0x1FFF)];
    public void ChrWrite(ushort address, byte value) { } // CHR-ROM
}

/// <summary>
/// Sunsoft FME-7 (mapper 69): a command/parameter register pair drives eight 1 KiB CHR banks, four
/// PRG slots (the $6000 slot selectable as ROM or PRG-RAM, $E000 fixed to the last bank), runtime
/// mirroring, and a 16-bit **CPU-cycle** down-counter IRQ (clocked via <see cref="OnCpuCycles"/>).
/// The 5B expansion-audio registers ($C000/$E000 audio) are accepted but not synthesized.
/// </summary>
public sealed class Fme7Mapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly bool _chrRam;
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly int _prgBanks8k;
    private readonly int _chrBanks1k;
    private readonly int[] _chrBank = new int[8];
    private readonly int[] _prgBank = new int[4]; // slots: $6000, $8000, $A000, $C000

    private int _command;
    private bool _prgRamSelected, _prgRamEnabled;
    private Mirroring _mirroring;

    private int _irqCounter;
    private bool _irqCountEnabled, _irqEnabled, _irqPending;

    public Fme7Mapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chrRam = rom.ChrIsRam;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks8k = Math.Max(1, _prg.Length / 0x2000);
        _chrBanks1k = Math.Max(1, _chr.Length / 0x400);
        _mirroring = rom.Mirroring;
        _prgBank[3] = _prgBanks8k - 1; // sensible power-on default for the $C000 slot
    }

    public int Number => 69;
    public Mirroring Mirroring => _mirroring;
    public bool IrqPending => _irqPending;

    public byte CpuRead(ushort address)
    {
        if (address is >= 0x6000 and < 0x8000)
        {
            if (_prgRamSelected)
                return _prgRamEnabled ? _prgRam[address - 0x6000] : (byte)0;
            return _prg[(_prgBank[0] % _prgBanks8k) * 0x2000 + (address - 0x6000)];
        }
        if (address < 0x8000) return 0;
        if (address >= 0xE000) return _prg[((_prgBanks8k - 1) % _prgBanks8k) * 0x2000 + (address & 0x1FFF)];
        int slot = ((address - 0x8000) >> 13) + 1; // $8000->1, $A000->2, $C000->3
        return _prg[(_prgBank[slot] % _prgBanks8k) * 0x2000 + (address & 0x1FFF)];
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000)
        {
            if (_prgRamSelected && _prgRamEnabled) _prgRam[address - 0x6000] = value;
            return;
        }
        if (address < 0x8000) return;

        switch (address & 0xE000)
        {
            case 0x8000: _command = value & 0x0F; break;
            case 0xA000: WriteParameter(value); break;
            // $C000/$E000 select/write the 5B audio registers — accepted, not synthesized.
        }
    }

    private void WriteParameter(byte value)
    {
        switch (_command)
        {
            case 0x0: case 0x1: case 0x2: case 0x3:
            case 0x4: case 0x5: case 0x6: case 0x7:
                _chrBank[_command] = value;
                break;
            case 0x8:
                _prgRamSelected = (value & 0x40) != 0;
                _prgRamEnabled = (value & 0x80) != 0;
                _prgBank[0] = value & 0x3F;
                break;
            case 0x9: _prgBank[1] = value & 0x3F; break;
            case 0xA: _prgBank[2] = value & 0x3F; break;
            case 0xB: _prgBank[3] = value & 0x3F; break;
            case 0xC:
                _mirroring = (value & 3) switch
                {
                    0 => Mirroring.Vertical,
                    1 => Mirroring.Horizontal,
                    2 => Mirroring.SingleScreenLower,
                    _ => Mirroring.SingleScreenUpper,
                };
                break;
            case 0xD:
                _irqEnabled = (value & 0x80) != 0;
                _irqCountEnabled = (value & 0x01) != 0;
                _irqPending = false; // writing the control register acknowledges a pending IRQ
                break;
            case 0xE: _irqCounter = (_irqCounter & 0xFF00) | value; break;
            case 0xF: _irqCounter = (_irqCounter & 0x00FF) | (value << 8); break;
        }
    }

    public void OnCpuCycles(int cycles)
    {
        if (!_irqCountEnabled) return;
        for (int i = 0; i < cycles; i++)
        {
            if (_irqCounter == 0) { _irqCounter = 0xFFFF; if (_irqEnabled) _irqPending = true; }
            else _irqCounter = (_irqCounter - 1) & 0xFFFF;
        }
    }

    public byte ChrRead(ushort address) => _chr[ChrOffset(address)];
    public void ChrWrite(ushort address, byte value) { if (_chrRam) _chr[ChrOffset(address)] = value; }

    private int ChrOffset(ushort address)
    {
        int region = (address >> 10) & 7; // 0-7 (1 KiB regions)
        return (_chrBank[region] % _chrBanks1k) * 0x400 + (address & 0x3FF);
    }
}
