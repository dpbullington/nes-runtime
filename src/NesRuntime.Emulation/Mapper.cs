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
        9 => new Mmc2Mapper(rom),
        10 => new Mmc4Mapper(rom),
        11 => new ColorDreamsMapper(rom),
        21 or 22 or 23 or 25 => new Vrc24Mapper(rom),
        24 or 26 => new Vrc6Mapper(rom),
        66 => new GxRomMapper(rom),
        69 => new Fme7Mapper(rom),
        _ => throw new NotSupportedException(
            $"Mapper {rom.MapperNumber} is not supported yet " +
            "(have: 0/NROM, 1/MMC1, 2/UxROM, 3/CNROM, 4/MMC3, 7/AxROM, 9/MMC2, 10/MMC4, " +
            "11/ColorDreams, 21-26/Konami VRC2·VRC4·VRC6, 66/GxROM, 69/FME-7)."),
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

/// <summary>
/// MMC2 (mapper 9, PxROM — Punch-Out!!): one switchable 8 KiB PRG bank at $8000 with the last three
/// 8 KiB banks fixed, and two 4 KiB CHR banks each chosen by a **fetch latch**. The latch for a
/// pattern table flips to "FD" or "FE" when the PPU fetches tile $FD/$FE from that table, letting the
/// board swap CHR mid-screen (the boxing-ring split) without CPU involvement.
/// </summary>
public sealed class Mmc2Mapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly int _prgBanks8k;
    private readonly int _chrBanks4k;
    private int _prgBank;
    private int _chrFD0, _chrFE0, _chrFD1, _chrFE1; // CHR banks selected by each latch state
    private bool _latch0FE, _latch1FE;               // false = FD, true = FE
    private Mirroring _mirroring;

    public Mmc2Mapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks8k = Math.Max(1, _prg.Length / 0x2000);
        _chrBanks4k = Math.Max(1, _chr.Length / 0x1000);
        _mirroring = rom.Mirroring;
    }

    public int Number => 9;
    public Mirroring Mirroring => _mirroring;

    public byte CpuRead(ushort address)
    {
        if (address < 0x8000) return 0;
        int bank = (address & 0x6000) switch
        {
            0x0000 => _prgBank,             // $8000-$9FFF switchable
            0x2000 => _prgBanks8k - 3,      // $A000-$BFFF fixed
            0x4000 => _prgBanks8k - 2,      // $C000-$DFFF fixed
            _ => _prgBanks8k - 1,           // $E000-$FFFF fixed
        };
        return _prg[(bank % _prgBanks8k) * 0x2000 + (address & 0x1FFF)];
    }

    public void CpuWrite(ushort address, byte value)
    {
        switch (address & 0xF000)
        {
            case 0xA000: _prgBank = value & 0x0F; break;
            case 0xB000: _chrFD0 = value & 0x1F; break;
            case 0xC000: _chrFE0 = value & 0x1F; break;
            case 0xD000: _chrFD1 = value & 0x1F; break;
            case 0xE000: _chrFE1 = value & 0x1F; break;
            case 0xF000: _mirroring = (value & 1) == 0 ? Mirroring.Vertical : Mirroring.Horizontal; break;
        }
    }

    public byte ChrRead(ushort address)
    {
        byte b = _chr[ChrOffset(address)];
        UpdateLatch(address); // the latch flips *after* the triggering tile is fetched
        return b;
    }

    public void ChrWrite(ushort address, byte value) { } // CHR-ROM

    private int ChrOffset(ushort address)
    {
        int bank = address < 0x1000
            ? (_latch0FE ? _chrFE0 : _chrFD0)
            : (_latch1FE ? _chrFE1 : _chrFD1);
        return (bank % _chrBanks4k) * 0x1000 + (address & 0x0FFF);
    }

    private void UpdateLatch(ushort address)
    {
        switch (address & 0x1FF8)
        {
            case 0x0FD8: _latch0FE = false; break;
            case 0x0FE8: _latch0FE = true; break;
            case 0x1FD8: _latch1FE = false; break;
            case 0x1FE8: _latch1FE = true; break;
        }
    }
}

/// <summary>
/// MMC4 (mapper 10, FxROM — Fire Emblem, Famicom Wars): the same fetch-latch CHR banking as MMC2,
/// but PRG is a switchable 16 KiB bank at $8000-$BFFF with the last 16 KiB fixed at $C000, plus
/// 8 KiB PRG-RAM at $6000.
/// </summary>
public sealed class Mmc4Mapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly int _prgBanks16k;
    private readonly int _chrBanks4k;
    private int _prgBank;
    private int _chrFD0, _chrFE0, _chrFD1, _chrFE1;
    private bool _latch0FE, _latch1FE;
    private Mirroring _mirroring;

    public Mmc4Mapper(INesRom rom)
    {
        _prg = rom.Prg;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks16k = Math.Max(1, _prg.Length / 0x4000);
        _chrBanks4k = Math.Max(1, _chr.Length / 0x1000);
        _mirroring = rom.Mirroring;
    }

    public int Number => 10;
    public Mirroring Mirroring => _mirroring;

    public byte CpuRead(ushort address)
    {
        if (address is >= 0x6000 and < 0x8000) return _prgRam[address - 0x6000];
        if (address < 0x8000) return 0;
        int bank = address < 0xC000 ? _prgBank : _prgBanks16k - 1; // switchable $8000, fixed last $C000
        return _prg[(bank % _prgBanks16k) * 0x4000 + (address & 0x3FFF)];
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000) { _prgRam[address - 0x6000] = value; return; }
        switch (address & 0xF000)
        {
            case 0xA000: _prgBank = value & 0x0F; break;
            case 0xB000: _chrFD0 = value & 0x1F; break;
            case 0xC000: _chrFE0 = value & 0x1F; break;
            case 0xD000: _chrFD1 = value & 0x1F; break;
            case 0xE000: _chrFE1 = value & 0x1F; break;
            case 0xF000: _mirroring = (value & 1) == 0 ? Mirroring.Vertical : Mirroring.Horizontal; break;
        }
    }

    public byte ChrRead(ushort address)
    {
        byte b = _chr[ChrOffset(address)];
        UpdateLatch(address);
        return b;
    }

    public void ChrWrite(ushort address, byte value) { } // CHR-ROM

    private int ChrOffset(ushort address)
    {
        int bank = address < 0x1000
            ? (_latch0FE ? _chrFE0 : _chrFD0)
            : (_latch1FE ? _chrFE1 : _chrFD1);
        return (bank % _chrBanks4k) * 0x1000 + (address & 0x0FFF);
    }

    private void UpdateLatch(ushort address)
    {
        switch (address & 0x1FF8)
        {
            case 0x0FD8: _latch0FE = false; break;
            case 0x0FE8: _latch0FE = true; break;
            case 0x1FD8: _latch1FE = false; break;
            case 0x1FE8: _latch1FE = true; break;
        }
    }
}

/// <summary>
/// The Konami VRC IRQ shared by VRC4 and VRC6. An 8-bit counter is reloaded from a latch and, on
/// overflow ($FF → reload), asserts an IRQ. In scanline mode a prescaler subtracts 3 CPU cycles from
/// 341 (the 114/114/113 sequence, ~one clock per scanline); in cycle mode the counter clocks every
/// CPU cycle. Writing control acknowledges + reloads; writing acknowledge clears the flag and copies
/// the "enable after acknowledge" bit back into enable.
/// </summary>
internal sealed class VrcIrq
{
    private byte _latch, _counter;
    private int _prescaler = 341;
    private bool _enabled, _enableAfterAck, _cycleMode;

    public bool Pending { get; private set; }

    public void WriteLatch8(byte v) => _latch = v;                                  // VRC6: one write
    public void WriteLatchLow(byte v) => _latch = (byte)((_latch & 0xF0) | (v & 0x0F));  // VRC4: nibble pair
    public void WriteLatchHigh(byte v) => _latch = (byte)((_latch & 0x0F) | ((v & 0x0F) << 4));

    public void WriteControl(byte v)
    {
        _enableAfterAck = (v & 1) != 0;
        _enabled = (v & 2) != 0;
        _cycleMode = (v & 4) != 0;
        Pending = false;                       // control write acknowledges
        if (_enabled) { _counter = _latch; _prescaler = 341; }
    }

    public void Acknowledge()
    {
        Pending = false;
        _enabled = _enableAfterAck;             // restore enable from the "after ack" bit
    }

    public void Clock(int cpuCycles)
    {
        if (!_enabled) return;
        for (int i = 0; i < cpuCycles; i++)
        {
            if (_cycleMode) { ClockCounter(); continue; }
            _prescaler -= 3;
            if (_prescaler <= 0) { _prescaler += 341; ClockCounter(); }
        }
    }

    private void ClockCounter()
    {
        if (_counter == 0xFF) { _counter = _latch; Pending = true; }
        else _counter++;
    }
}

/// <summary>
/// Konami VRC6 (mappers 24 and 26 — Castlevania III / Akumajou Densetsu). A 16 KiB switchable PRG
/// bank at $8000, an 8 KiB bank at $C000, the last 8 KiB fixed at $E000; eight 1 KiB CHR banks;
/// runtime mirroring from $B003; and the shared <see cref="VrcIrq"/>. The 6b variant (26) swaps the
/// A0/A1 register-select lines. VRC6 expansion audio is accepted (register writes ignored), not
/// synthesized, and only CHR banking mode 0 (eight 1 KiB banks) is modeled.
/// </summary>
public sealed class Vrc6Mapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly bool _chrRam;
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly int _prgBanks16k, _prgBanks8k, _chrBanks1k;
    private readonly int _number;
    private readonly int[] _chrReg = new int[8];
    private readonly VrcIrq _irq = new();

    private int _prg16, _prg8;
    private Mirroring _mirroring;

    public Vrc6Mapper(INesRom rom)
    {
        _number = rom.MapperNumber;
        _prg = rom.Prg;
        _chrRam = rom.ChrIsRam;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks16k = Math.Max(1, _prg.Length / 0x4000);
        _prgBanks8k = Math.Max(1, _prg.Length / 0x2000);
        _chrBanks1k = Math.Max(1, _chr.Length / 0x400);
        _mirroring = rom.Mirroring;
    }

    public int Number => _number;
    public Mirroring Mirroring => _mirroring;
    public bool IrqPending => _irq.Pending;

    // A0/A1 register-select bits — swapped on VRC6b (mapper 26).
    private int Sub(ushort a) => _number == 26
        ? ((a & 1) << 1) | ((a >> 1) & 1)
        : a & 3;

    public byte CpuRead(ushort address)
    {
        if (address is >= 0x6000 and < 0x8000) return _prgRam[address - 0x6000];
        if (address < 0x8000) return 0;
        if (address < 0xC000) return _prg[(_prg16 % _prgBanks16k) * 0x4000 + (address & 0x3FFF)];
        if (address < 0xE000) return _prg[(_prg8 % _prgBanks8k) * 0x2000 + (address & 0x1FFF)];
        return _prg[((_prgBanks8k - 1) % _prgBanks8k) * 0x2000 + (address & 0x1FFF)];
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000) { _prgRam[address - 0x6000] = value; return; }
        if (address < 0x8000) return;

        int sub = Sub(address);
        switch (address & 0xF000)
        {
            case 0x8000: _prg16 = value & 0x0F; break;
            case 0xB000: if (sub == 3) _mirroring = MirrorOf(value); break;
            case 0xC000: _prg8 = value & 0x1F; break;
            case 0xD000: _chrReg[sub] = value; break;      // R0-R3
            case 0xE000: _chrReg[4 + sub] = value; break;  // R4-R7
            case 0xF000:
                switch (sub)
                {
                    case 0: _irq.WriteLatch8(value); break;
                    case 1: _irq.WriteControl(value); break;
                    case 2: _irq.Acknowledge(); break;
                }
                break;
            // $9000/$A000: expansion-audio registers — accepted, not synthesized.
        }
    }

    public void OnCpuCycles(int cycles) => _irq.Clock(cycles);

    public byte ChrRead(ushort address) => _chr[ChrOffset(address)];
    public void ChrWrite(ushort address, byte value) { if (_chrRam) _chr[ChrOffset(address)] = value; }

    private int ChrOffset(ushort address)
    {
        int region = (address >> 10) & 7;
        return (_chrReg[region] % _chrBanks1k) * 0x400 + (address & 0x3FF);
    }

    private static Mirroring MirrorOf(byte v) => ((v >> 2) & 3) switch
    {
        0 => Mirroring.Vertical,
        1 => Mirroring.Horizontal,
        2 => Mirroring.SingleScreenLower,
        _ => Mirroring.SingleScreenUpper,
    };
}

/// <summary>
/// Konami VRC2/VRC4 (mappers 21, 22, 23, 25 — Gradius, Contra, TMNT, Wai Wai World, …). Two 8 KiB
/// switchable PRG banks with a swap-mode bit, eight 1 KiB CHR banks written as low-nibble/high-bits
/// pairs, runtime mirroring, and (VRC4 only) the shared <see cref="VrcIrq"/>. The board variants wire
/// different CPU address lines to the register's two select bits; because iNES 1.0 lacks submapper
/// info, the select bits are decoded by OR-ing every candidate line for the mapper number — the
/// standard trick that lets any single wiring's writes land correctly.
/// </summary>
public sealed class Vrc24Mapper : IMapper
{
    private readonly byte[] _prg;
    private readonly byte[] _chr;
    private readonly bool _chrRam;
    private readonly byte[] _prgRam = new byte[0x2000];
    private readonly int _prgBanks8k, _chrBanks1k;
    private readonly int _number;
    private readonly bool _isVrc4;   // 21/23/25 carry the VRC4 IRQ; 22 is IRQ-less VRC2
    private readonly bool _chrShift; // VRC2a (mapper 22) ignores the CHR low bit
    private readonly int[] _chrLow = new int[8];
    private readonly int[] _chrHigh = new int[8];
    private readonly VrcIrq _irq = new();

    private int _prgLow, _prgHigh, _swapMode;
    private Mirroring _mirroring;

    public Vrc24Mapper(INesRom rom)
    {
        _number = rom.MapperNumber;
        _isVrc4 = rom.MapperNumber != 22;
        _chrShift = rom.MapperNumber == 22;
        _prg = rom.Prg;
        _chrRam = rom.ChrIsRam;
        _chr = rom.Chr.Length > 0 ? rom.Chr : new byte[0x2000];
        _prgBanks8k = Math.Max(1, _prg.Length / 0x2000);
        _chrBanks1k = Math.Max(1, _chr.Length / 0x400);
        _mirroring = rom.Mirroring;
    }

    public int Number => _number;
    public Mirroring Mirroring => _mirroring;
    public bool IrqPending => _isVrc4 && _irq.Pending;

    // Register-select bits, OR-ing every candidate address line for this mapper number.
    private int Sub(ushort a) => _number switch
    {
        22 => ((a >> 1) & 1) | ((a & 1) << 1),                                   // VRC2a: A1,A0
        21 => (((a >> 1) | (a >> 6)) & 1) | ((((a >> 2) | (a >> 7)) & 1) << 1),  // A1|A6, A2|A7
        25 => (((a >> 1) | (a >> 3)) & 1) | (((a | (a >> 2)) & 1) << 1),         // A1|A3, A0|A2
        _ => ((a | (a >> 2)) & 1) | ((((a >> 1) | (a >> 3)) & 1) << 1),          // 23: A0|A2, A1|A3
    };

    public byte CpuRead(ushort address)
    {
        if (address is >= 0x6000 and < 0x8000) return _prgRam[address - 0x6000];
        if (address < 0x8000) return 0;
        int bank = (address & 0xE000) switch
        {
            0x8000 => _swapMode == 0 ? _prgLow : _prgBanks8k - 2,
            0xA000 => _prgHigh,
            0xC000 => _swapMode == 0 ? _prgBanks8k - 2 : _prgLow,
            _ => _prgBanks8k - 1,
        };
        return _prg[(bank % _prgBanks8k) * 0x2000 + (address & 0x1FFF)];
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000) { _prgRam[address - 0x6000] = value; return; }
        if (address < 0x8000) return;

        int sub = Sub(address);
        switch (address & 0xF000)
        {
            case 0x8000: _prgLow = value & 0x1F; break;
            case 0x9000:
                if (sub < 2) _mirroring = MirrorOf(value);
                else if (_isVrc4) _swapMode = (value >> 1) & 1;
                break;
            case 0xA000: _prgHigh = value & 0x1F; break;
            case 0xB000: case 0xC000: case 0xD000: case 0xE000:
            {
                int region = (((address >> 12) & 0xF) - 0xB) * 2 + (sub >> 1);
                if ((sub & 1) == 0) _chrLow[region] = value & 0x0F;
                else _chrHigh[region] = value & 0x1F;
                break;
            }
            case 0xF000:
                if (!_isVrc4) break;
                switch (sub)
                {
                    case 0: _irq.WriteLatchLow(value); break;
                    case 1: _irq.WriteLatchHigh(value); break;
                    case 2: _irq.WriteControl(value); break;
                    case 3: _irq.Acknowledge(); break;
                }
                break;
        }
    }

    public void OnCpuCycles(int cycles) { if (_isVrc4) _irq.Clock(cycles); }

    public byte ChrRead(ushort address) => _chr[ChrOffset(address)];
    public void ChrWrite(ushort address, byte value) { if (_chrRam) _chr[ChrOffset(address)] = value; }

    private int ChrOffset(ushort address)
    {
        int region = (address >> 10) & 7;
        int bank = (_chrHigh[region] << 4) | _chrLow[region];
        if (_chrShift) bank >>= 1;
        return (bank % _chrBanks1k) * 0x400 + (address & 0x3FF);
    }

    private Mirroring MirrorOf(byte v) => (_isVrc4 ? v & 3 : v & 1) switch
    {
        0 => Mirroring.Vertical,
        1 => Mirroring.Horizontal,
        2 => Mirroring.SingleScreenLower,
        _ => Mirroring.SingleScreenUpper,
    };
}
