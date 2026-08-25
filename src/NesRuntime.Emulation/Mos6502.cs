namespace NesRuntime.Emulation;

/// <summary>The CPU's view of memory. The machine implements the NES address map behind it.</summary>
public interface IBus
{
    byte Read(ushort address);
    void Write(ushort address, byte value);
}

/// <summary>
/// A MOS 6502 interpreter (the NES's 2A03 core, which lacks decimal mode). Implements the full
/// official opcode set with standard cycle counts including page-cross and branch penalties.
/// Unofficial/illegal opcodes are treated as NOPs (a known limitation — some games rely on them).
/// </summary>
public sealed class Mos6502
{
    private readonly IBus _bus;

    // Registers.
    public byte A, X, Y, S;
    public ushort PC;

    // Status flags.
    public bool C, Z, I, D, V, N;

    /// <summary>Level-triggered IRQ line held by a device (e.g. the MMC3 scanline counter).</summary>
    public bool IrqLine;

    public long Cycles { get; private set; }
    private int _pendingCycles;

    public Mos6502(IBus bus) => _bus = bus;

    public byte P
    {
        get => (byte)((C ? 0x01 : 0) | (Z ? 0x02 : 0) | (I ? 0x04 : 0) | (D ? 0x08 : 0)
                    | 0x20 | (V ? 0x40 : 0) | (N ? 0x80 : 0));
        set
        {
            C = (value & 0x01) != 0; Z = (value & 0x02) != 0; I = (value & 0x04) != 0;
            D = (value & 0x08) != 0; V = (value & 0x40) != 0; N = (value & 0x80) != 0;
        }
    }

    public void Reset()
    {
        S = 0xFD;
        I = true;
        PC = ReadWord(0xFFFC);
        Cycles = 0;
    }

    /// <summary>Services a non-maskable interrupt (vblank NMI). Returns the cycles consumed.</summary>
    public int Nmi()
    {
        PushWord(PC);
        Push((byte)(P & ~0x10)); // B flag clear
        I = true;
        PC = ReadWord(0xFFFA);
        Cycles += 7;
        return 7;
    }

    /// <summary>Services a maskable IRQ if interrupts are enabled. Returns the cycles consumed.</summary>
    public int Irq()
    {
        if (I) return 0;
        PushWord(PC);
        Push((byte)(P & ~0x10));
        I = true;
        PC = ReadWord(0xFFFE);
        Cycles += 7;
        return 7;
    }

    /// <summary>Executes one instruction (or services a pending IRQ) and returns the cycles used.</summary>
    public int Step()
    {
        if (IrqLine && !I)
        {
            PushWord(PC);
            Push((byte)(P & ~0x10)); // B flag clear
            I = true;
            PC = ReadWord(0xFFFE);
            Cycles += 7;
            return 7;
        }

        _pendingCycles = 0;
        byte op = Read(PC++);
        Execute(op);
        Cycles += _pendingCycles;
        return _pendingCycles;
    }

    // --- Memory helpers ---
    private byte Read(ushort a) => _bus.Read(a);
    private void Write(ushort a, byte v) => _bus.Write(a, v);
    private ushort ReadWord(ushort a) => (ushort)(Read(a) | (Read((ushort)(a + 1)) << 8));
    private void Push(byte v) { Write((ushort)(0x0100 + S), v); S--; }
    private byte Pop() { S++; return Read((ushort)(0x0100 + S)); }
    private void PushWord(ushort v) { Push((byte)(v >> 8)); Push((byte)(v & 0xFF)); }
    private ushort PopWord() { byte lo = Pop(); byte hi = Pop(); return (ushort)(lo | (hi << 8)); }

    private void SetZN(byte v) { Z = v == 0; N = (v & 0x80) != 0; }

    // --- Addressing modes: return the effective address; add cycle on page cross when noted. ---
    private ushort Imm() => PC++;
    private ushort Zp() => Read(PC++);
    private ushort ZpX() => (byte)(Read(PC++) + X);
    private ushort ZpY() => (byte)(Read(PC++) + Y);
    private ushort Abs() { ushort a = ReadWord(PC); PC += 2; return a; }

    private ushort AbsX(bool penalty)
    {
        ushort b = ReadWord(PC); PC += 2;
        ushort a = (ushort)(b + X);
        if (penalty && (b & 0xFF00) != (a & 0xFF00)) _pendingCycles++;
        return a;
    }

    private ushort AbsY(bool penalty)
    {
        ushort b = ReadWord(PC); PC += 2;
        ushort a = (ushort)(b + Y);
        if (penalty && (b & 0xFF00) != (a & 0xFF00)) _pendingCycles++;
        return a;
    }

    private ushort IndX() { byte p = (byte)(Read(PC++) + X); return (ushort)(Read(p) | (Read((byte)(p + 1)) << 8)); }

    private ushort IndY(bool penalty)
    {
        byte p = Read(PC++);
        ushort b = (ushort)(Read(p) | (Read((byte)(p + 1)) << 8));
        ushort a = (ushort)(b + Y);
        if (penalty && (b & 0xFF00) != (a & 0xFF00)) _pendingCycles++;
        return a;
    }

    // --- Operations ---
    private void Lda(ushort a) { A = Read(a); SetZN(A); }
    private void Ldx(ushort a) { X = Read(a); SetZN(X); }
    private void Ldy(ushort a) { Y = Read(a); SetZN(Y); }
    private void Ora(ushort a) { A |= Read(a); SetZN(A); }
    private void And(ushort a) { A &= Read(a); SetZN(A); }
    private void Eor(ushort a) { A ^= Read(a); SetZN(A); }

    private void AdcValue(byte m)
    {
        int sum = A + m + (C ? 1 : 0);
        C = sum > 0xFF;
        V = (~(A ^ m) & (A ^ sum) & 0x80) != 0;
        A = (byte)sum;
        SetZN(A);
    }

    private void Adc(ushort a) => AdcValue(Read(a));
    private void Sbc(ushort a) => AdcValue((byte)~Read(a));

    private void Cmp(byte reg, ushort a) { byte m = Read(a); int r = reg - m; C = reg >= m; SetZN((byte)r); }

    // --- Unofficial ("illegal") opcode operations ---
    private void Lax(ushort a) { byte v = Read(a); A = v; X = v; SetZN(v); }
    private void Sax(ushort a) => Write(a, (byte)(A & X));
    private void Dcp(ushort a) { byte v = (byte)(Read(a) - 1); Write(a, v); C = A >= v; SetZN((byte)(A - v)); }
    private void Isc(ushort a) { byte v = (byte)(Read(a) + 1); Write(a, v); AdcValue((byte)~v); }
    private void Slo(ushort a) { byte v = Read(a); C = (v & 0x80) != 0; v <<= 1; Write(a, v); A |= v; SetZN(A); }
    private void Rla(ushort a) { byte v = Read(a); bool c = C; C = (v & 0x80) != 0; v = (byte)((v << 1) | (c ? 1 : 0)); Write(a, v); A &= v; SetZN(A); }
    private void Sre(ushort a) { byte v = Read(a); C = (v & 1) != 0; v >>= 1; Write(a, v); A ^= v; SetZN(A); }
    private void Rra(ushort a) { byte v = Read(a); bool c = C; C = (v & 1) != 0; v = (byte)((v >> 1) | (c ? 0x80 : 0)); Write(a, v); AdcValue(v); }
    private void Anc(ushort a) { A &= Read(a); SetZN(A); C = (A & 0x80) != 0; }
    private void Alr(ushort a) { A &= Read(a); C = (A & 1) != 0; A >>= 1; SetZN(A); }
    private void Arr(ushort a) { A &= Read(a); A = (byte)((A >> 1) | (C ? 0x80 : 0)); SetZN(A); C = (A & 0x40) != 0; V = (((A >> 6) ^ (A >> 5)) & 1) != 0; }
    private void Sbx(ushort a) { int t = A & X; byte m = Read(a); C = t >= m; X = (byte)(t - m); SetZN(X); }
    // Unstable "high-byte" stores: reg AND (address high byte + 1).
    private void Shx(ushort a) => Write(a, (byte)(X & (((a >> 8) + 1) & 0xFF)));
    private void Shy(ushort a) => Write(a, (byte)(Y & (((a >> 8) + 1) & 0xFF)));
    private void Sha(ushort a) => Write(a, (byte)(A & X & (((a >> 8) + 1) & 0xFF)));
    private void Las(ushort a) { byte v = (byte)(Read(a) & S); A = v; X = v; S = v; SetZN(v); }

    private void Bit(ushort a) { byte m = Read(a); Z = (A & m) == 0; V = (m & 0x40) != 0; N = (m & 0x80) != 0; }

    private void Inc(ushort a) { byte v = (byte)(Read(a) + 1); Write(a, v); SetZN(v); }
    private void Dec(ushort a) { byte v = (byte)(Read(a) - 1); Write(a, v); SetZN(v); }

    private byte AslV(byte v) { C = (v & 0x80) != 0; v <<= 1; SetZN(v); return v; }
    private byte LsrV(byte v) { C = (v & 0x01) != 0; v >>= 1; SetZN(v); return v; }
    private byte RolV(byte v) { bool c = C; C = (v & 0x80) != 0; v = (byte)((v << 1) | (c ? 1 : 0)); SetZN(v); return v; }
    private byte RorV(byte v) { bool c = C; C = (v & 0x01) != 0; v = (byte)((v >> 1) | (c ? 0x80 : 0)); SetZN(v); return v; }

    private void AslM(ushort a) => Write(a, AslV(Read(a)));
    private void LsrM(ushort a) => Write(a, LsrV(Read(a)));
    private void RolM(ushort a) => Write(a, RolV(Read(a)));
    private void RorM(ushort a) => Write(a, RorV(Read(a)));

    private void Branch(bool take)
    {
        sbyte off = (sbyte)Read(PC++);
        if (!take) return;
        ushort t = (ushort)(PC + off);
        _pendingCycles += (PC & 0xFF00) != (t & 0xFF00) ? 2 : 1;
        PC = t;
    }

    private void Execute(byte op)
    {
        switch (op)
        {
            // ORA
            case 0x09: _pendingCycles += 2; Ora(Imm()); break;
            case 0x05: _pendingCycles += 3; Ora(Zp()); break;
            case 0x15: _pendingCycles += 4; Ora(ZpX()); break;
            case 0x0D: _pendingCycles += 4; Ora(Abs()); break;
            case 0x1D: _pendingCycles += 4; Ora(AbsX(true)); break;
            case 0x19: _pendingCycles += 4; Ora(AbsY(true)); break;
            case 0x01: _pendingCycles += 6; Ora(IndX()); break;
            case 0x11: _pendingCycles += 5; Ora(IndY(true)); break;

            // AND
            case 0x29: _pendingCycles += 2; And(Imm()); break;
            case 0x25: _pendingCycles += 3; And(Zp()); break;
            case 0x35: _pendingCycles += 4; And(ZpX()); break;
            case 0x2D: _pendingCycles += 4; And(Abs()); break;
            case 0x3D: _pendingCycles += 4; And(AbsX(true)); break;
            case 0x39: _pendingCycles += 4; And(AbsY(true)); break;
            case 0x21: _pendingCycles += 6; And(IndX()); break;
            case 0x31: _pendingCycles += 5; And(IndY(true)); break;

            // EOR
            case 0x49: _pendingCycles += 2; Eor(Imm()); break;
            case 0x45: _pendingCycles += 3; Eor(Zp()); break;
            case 0x55: _pendingCycles += 4; Eor(ZpX()); break;
            case 0x4D: _pendingCycles += 4; Eor(Abs()); break;
            case 0x5D: _pendingCycles += 4; Eor(AbsX(true)); break;
            case 0x59: _pendingCycles += 4; Eor(AbsY(true)); break;
            case 0x41: _pendingCycles += 6; Eor(IndX()); break;
            case 0x51: _pendingCycles += 5; Eor(IndY(true)); break;

            // ADC
            case 0x69: _pendingCycles += 2; Adc(Imm()); break;
            case 0x65: _pendingCycles += 3; Adc(Zp()); break;
            case 0x75: _pendingCycles += 4; Adc(ZpX()); break;
            case 0x6D: _pendingCycles += 4; Adc(Abs()); break;
            case 0x7D: _pendingCycles += 4; Adc(AbsX(true)); break;
            case 0x79: _pendingCycles += 4; Adc(AbsY(true)); break;
            case 0x61: _pendingCycles += 6; Adc(IndX()); break;
            case 0x71: _pendingCycles += 5; Adc(IndY(true)); break;

            // SBC
            case 0xE9: _pendingCycles += 2; Sbc(Imm()); break;
            case 0xE5: _pendingCycles += 3; Sbc(Zp()); break;
            case 0xF5: _pendingCycles += 4; Sbc(ZpX()); break;
            case 0xED: _pendingCycles += 4; Sbc(Abs()); break;
            case 0xFD: _pendingCycles += 4; Sbc(AbsX(true)); break;
            case 0xF9: _pendingCycles += 4; Sbc(AbsY(true)); break;
            case 0xE1: _pendingCycles += 6; Sbc(IndX()); break;
            case 0xF1: _pendingCycles += 5; Sbc(IndY(true)); break;

            // LDA
            case 0xA9: _pendingCycles += 2; Lda(Imm()); break;
            case 0xA5: _pendingCycles += 3; Lda(Zp()); break;
            case 0xB5: _pendingCycles += 4; Lda(ZpX()); break;
            case 0xAD: _pendingCycles += 4; Lda(Abs()); break;
            case 0xBD: _pendingCycles += 4; Lda(AbsX(true)); break;
            case 0xB9: _pendingCycles += 4; Lda(AbsY(true)); break;
            case 0xA1: _pendingCycles += 6; Lda(IndX()); break;
            case 0xB1: _pendingCycles += 5; Lda(IndY(true)); break;

            // LDX
            case 0xA2: _pendingCycles += 2; Ldx(Imm()); break;
            case 0xA6: _pendingCycles += 3; Ldx(Zp()); break;
            case 0xB6: _pendingCycles += 4; Ldx(ZpY()); break;
            case 0xAE: _pendingCycles += 4; Ldx(Abs()); break;
            case 0xBE: _pendingCycles += 4; Ldx(AbsY(true)); break;

            // LDY
            case 0xA0: _pendingCycles += 2; Ldy(Imm()); break;
            case 0xA4: _pendingCycles += 3; Ldy(Zp()); break;
            case 0xB4: _pendingCycles += 4; Ldy(ZpX()); break;
            case 0xAC: _pendingCycles += 4; Ldy(Abs()); break;
            case 0xBC: _pendingCycles += 4; Ldy(AbsX(true)); break;

            // STA
            case 0x85: _pendingCycles += 3; Write(Zp(), A); break;
            case 0x95: _pendingCycles += 4; Write(ZpX(), A); break;
            case 0x8D: _pendingCycles += 4; Write(Abs(), A); break;
            case 0x9D: _pendingCycles += 5; Write(AbsX(false), A); break;
            case 0x99: _pendingCycles += 5; Write(AbsY(false), A); break;
            case 0x81: _pendingCycles += 6; Write(IndX(), A); break;
            case 0x91: _pendingCycles += 6; Write(IndY(false), A); break;

            // STX / STY
            case 0x86: _pendingCycles += 3; Write(Zp(), X); break;
            case 0x96: _pendingCycles += 4; Write(ZpY(), X); break;
            case 0x8E: _pendingCycles += 4; Write(Abs(), X); break;
            case 0x84: _pendingCycles += 3; Write(Zp(), Y); break;
            case 0x94: _pendingCycles += 4; Write(ZpX(), Y); break;
            case 0x8C: _pendingCycles += 4; Write(Abs(), Y); break;

            // CMP / CPX / CPY
            case 0xC9: _pendingCycles += 2; Cmp(A, Imm()); break;
            case 0xC5: _pendingCycles += 3; Cmp(A, Zp()); break;
            case 0xD5: _pendingCycles += 4; Cmp(A, ZpX()); break;
            case 0xCD: _pendingCycles += 4; Cmp(A, Abs()); break;
            case 0xDD: _pendingCycles += 4; Cmp(A, AbsX(true)); break;
            case 0xD9: _pendingCycles += 4; Cmp(A, AbsY(true)); break;
            case 0xC1: _pendingCycles += 6; Cmp(A, IndX()); break;
            case 0xD1: _pendingCycles += 5; Cmp(A, IndY(true)); break;
            case 0xE0: _pendingCycles += 2; Cmp(X, Imm()); break;
            case 0xE4: _pendingCycles += 3; Cmp(X, Zp()); break;
            case 0xEC: _pendingCycles += 4; Cmp(X, Abs()); break;
            case 0xC0: _pendingCycles += 2; Cmp(Y, Imm()); break;
            case 0xC4: _pendingCycles += 3; Cmp(Y, Zp()); break;
            case 0xCC: _pendingCycles += 4; Cmp(Y, Abs()); break;

            // BIT
            case 0x24: _pendingCycles += 3; Bit(Zp()); break;
            case 0x2C: _pendingCycles += 4; Bit(Abs()); break;

            // INC / DEC
            case 0xE6: _pendingCycles += 5; Inc(Zp()); break;
            case 0xF6: _pendingCycles += 6; Inc(ZpX()); break;
            case 0xEE: _pendingCycles += 6; Inc(Abs()); break;
            case 0xFE: _pendingCycles += 7; Inc(AbsX(false)); break;
            case 0xC6: _pendingCycles += 5; Dec(Zp()); break;
            case 0xD6: _pendingCycles += 6; Dec(ZpX()); break;
            case 0xCE: _pendingCycles += 6; Dec(Abs()); break;
            case 0xDE: _pendingCycles += 7; Dec(AbsX(false)); break;

            // INX/DEX/INY/DEY
            case 0xE8: _pendingCycles += 2; X++; SetZN(X); break;
            case 0xCA: _pendingCycles += 2; X--; SetZN(X); break;
            case 0xC8: _pendingCycles += 2; Y++; SetZN(Y); break;
            case 0x88: _pendingCycles += 2; Y--; SetZN(Y); break;

            // Shifts/rotates — accumulator
            case 0x0A: _pendingCycles += 2; A = AslV(A); break;
            case 0x4A: _pendingCycles += 2; A = LsrV(A); break;
            case 0x2A: _pendingCycles += 2; A = RolV(A); break;
            case 0x6A: _pendingCycles += 2; A = RorV(A); break;
            // ASL
            case 0x06: _pendingCycles += 5; AslM(Zp()); break;
            case 0x16: _pendingCycles += 6; AslM(ZpX()); break;
            case 0x0E: _pendingCycles += 6; AslM(Abs()); break;
            case 0x1E: _pendingCycles += 7; AslM(AbsX(false)); break;
            // LSR
            case 0x46: _pendingCycles += 5; LsrM(Zp()); break;
            case 0x56: _pendingCycles += 6; LsrM(ZpX()); break;
            case 0x4E: _pendingCycles += 6; LsrM(Abs()); break;
            case 0x5E: _pendingCycles += 7; LsrM(AbsX(false)); break;
            // ROL
            case 0x26: _pendingCycles += 5; RolM(Zp()); break;
            case 0x36: _pendingCycles += 6; RolM(ZpX()); break;
            case 0x2E: _pendingCycles += 6; RolM(Abs()); break;
            case 0x3E: _pendingCycles += 7; RolM(AbsX(false)); break;
            // ROR
            case 0x66: _pendingCycles += 5; RorM(Zp()); break;
            case 0x76: _pendingCycles += 6; RorM(ZpX()); break;
            case 0x6E: _pendingCycles += 6; RorM(Abs()); break;
            case 0x7E: _pendingCycles += 7; RorM(AbsX(false)); break;

            // Branches
            case 0x10: _pendingCycles += 2; Branch(!N); break; // BPL
            case 0x30: _pendingCycles += 2; Branch(N); break;  // BMI
            case 0x50: _pendingCycles += 2; Branch(!V); break; // BVC
            case 0x70: _pendingCycles += 2; Branch(V); break;  // BVS
            case 0x90: _pendingCycles += 2; Branch(!C); break; // BCC
            case 0xB0: _pendingCycles += 2; Branch(C); break;  // BCS
            case 0xD0: _pendingCycles += 2; Branch(!Z); break; // BNE
            case 0xF0: _pendingCycles += 2; Branch(Z); break;  // BEQ

            // Jumps / calls
            case 0x4C: _pendingCycles += 3; PC = Abs(); break; // JMP abs
            case 0x6C: // JMP (indirect) with the page-boundary bug
                _pendingCycles += 5;
                {
                    ushort ptr = Abs();
                    byte lo = Read(ptr);
                    byte hi = Read((ushort)((ptr & 0xFF00) | ((ptr + 1) & 0x00FF)));
                    PC = (ushort)(lo | (hi << 8));
                }
                break;
            case 0x20: // JSR
                _pendingCycles += 6;
                {
                    ushort target = ReadWord(PC);
                    PushWord((ushort)(PC + 1)); // address of last JSR byte
                    PC = target;
                }
                break;
            case 0x60: _pendingCycles += 6; PC = (ushort)(PopWord() + 1); break; // RTS
            case 0x40: _pendingCycles += 6; P = Pop(); PC = PopWord(); break;     // RTI
            case 0x00: // BRK
                _pendingCycles += 7;
                PC++;
                PushWord(PC);
                Push((byte)(P | 0x10));
                I = true;
                PC = ReadWord(0xFFFE);
                break;

            // Stack
            case 0x48: _pendingCycles += 3; Push(A); break;                 // PHA
            case 0x68: _pendingCycles += 4; A = Pop(); SetZN(A); break;      // PLA
            case 0x08: _pendingCycles += 3; Push((byte)(P | 0x10)); break;   // PHP
            case 0x28: _pendingCycles += 4; P = Pop(); break;                // PLP

            // Transfers
            case 0xAA: _pendingCycles += 2; X = A; SetZN(X); break; // TAX
            case 0x8A: _pendingCycles += 2; A = X; SetZN(A); break; // TXA
            case 0xA8: _pendingCycles += 2; Y = A; SetZN(Y); break; // TAY
            case 0x98: _pendingCycles += 2; A = Y; SetZN(A); break; // TYA
            case 0xBA: _pendingCycles += 2; X = S; SetZN(X); break; // TSX
            case 0x9A: _pendingCycles += 2; S = X; break;           // TXS

            // Flags
            case 0x18: _pendingCycles += 2; C = false; break; // CLC
            case 0x38: _pendingCycles += 2; C = true; break;  // SEC
            case 0x58: _pendingCycles += 2; I = false; break; // CLI
            case 0x78: _pendingCycles += 2; I = true; break;  // SEI
            case 0xB8: _pendingCycles += 2; V = false; break; // CLV
            case 0xD8: _pendingCycles += 2; D = false; break; // CLD
            case 0xF8: _pendingCycles += 2; D = true; break;  // SED

            case 0xEA: _pendingCycles += 2; break; // NOP

            // --- Unofficial ("illegal") opcodes ---

            // Undocumented NOPs. Implied 1-byte forms.
            case 0x1A: case 0x3A: case 0x5A: case 0x7A: case 0xDA: case 0xFA:
                _pendingCycles += 2; break;
            // Immediate DOP (2-byte NOP #imm).
            case 0x80: case 0x82: case 0x89: case 0xC2: case 0xE2:
                _pendingCycles += 2; PC++; break;
            // Zero-page DOP.
            case 0x04: case 0x44: case 0x64:
                _pendingCycles += 3; Zp(); break;
            // Zero-page,X DOP.
            case 0x14: case 0x34: case 0x54: case 0x74: case 0xD4: case 0xF4:
                _pendingCycles += 4; ZpX(); break;
            // Absolute TOP (3-byte NOP).
            case 0x0C:
                _pendingCycles += 4; Abs(); break;
            // Absolute,X TOP (+1 on page cross).
            case 0x1C: case 0x3C: case 0x5C: case 0x7C: case 0xDC: case 0xFC:
                _pendingCycles += 4; AbsX(true); break;

            // LAX = LDA + LDX (load into A and X).
            case 0xA7: _pendingCycles += 3; Lax(Zp()); break;
            case 0xB7: _pendingCycles += 4; Lax(ZpY()); break;
            case 0xAF: _pendingCycles += 4; Lax(Abs()); break;
            case 0xBF: _pendingCycles += 4; Lax(AbsY(true)); break;
            case 0xA3: _pendingCycles += 6; Lax(IndX()); break;
            case 0xB3: _pendingCycles += 5; Lax(IndY(true)); break;

            // SAX = store (A AND X).
            case 0x87: _pendingCycles += 3; Sax(Zp()); break;
            case 0x97: _pendingCycles += 4; Sax(ZpY()); break;
            case 0x8F: _pendingCycles += 4; Sax(Abs()); break;
            case 0x83: _pendingCycles += 6; Sax(IndX()); break;

            // DCP = DEC then CMP.
            case 0xC7: _pendingCycles += 5; Dcp(Zp()); break;
            case 0xD7: _pendingCycles += 6; Dcp(ZpX()); break;
            case 0xCF: _pendingCycles += 6; Dcp(Abs()); break;
            case 0xDF: _pendingCycles += 7; Dcp(AbsX(false)); break;
            case 0xDB: _pendingCycles += 7; Dcp(AbsY(false)); break;
            case 0xC3: _pendingCycles += 8; Dcp(IndX()); break;
            case 0xD3: _pendingCycles += 8; Dcp(IndY(false)); break;

            // ISC (ISB) = INC then SBC.
            case 0xE7: _pendingCycles += 5; Isc(Zp()); break;
            case 0xF7: _pendingCycles += 6; Isc(ZpX()); break;
            case 0xEF: _pendingCycles += 6; Isc(Abs()); break;
            case 0xFF: _pendingCycles += 7; Isc(AbsX(false)); break;
            case 0xFB: _pendingCycles += 7; Isc(AbsY(false)); break;
            case 0xE3: _pendingCycles += 8; Isc(IndX()); break;
            case 0xF3: _pendingCycles += 8; Isc(IndY(false)); break;

            // SLO = ASL then ORA.
            case 0x07: _pendingCycles += 5; Slo(Zp()); break;
            case 0x17: _pendingCycles += 6; Slo(ZpX()); break;
            case 0x0F: _pendingCycles += 6; Slo(Abs()); break;
            case 0x1F: _pendingCycles += 7; Slo(AbsX(false)); break;
            case 0x1B: _pendingCycles += 7; Slo(AbsY(false)); break;
            case 0x03: _pendingCycles += 8; Slo(IndX()); break;
            case 0x13: _pendingCycles += 8; Slo(IndY(false)); break;

            // RLA = ROL then AND.
            case 0x27: _pendingCycles += 5; Rla(Zp()); break;
            case 0x37: _pendingCycles += 6; Rla(ZpX()); break;
            case 0x2F: _pendingCycles += 6; Rla(Abs()); break;
            case 0x3F: _pendingCycles += 7; Rla(AbsX(false)); break;
            case 0x3B: _pendingCycles += 7; Rla(AbsY(false)); break;
            case 0x23: _pendingCycles += 8; Rla(IndX()); break;
            case 0x33: _pendingCycles += 8; Rla(IndY(false)); break;

            // SRE = LSR then EOR.
            case 0x47: _pendingCycles += 5; Sre(Zp()); break;
            case 0x57: _pendingCycles += 6; Sre(ZpX()); break;
            case 0x4F: _pendingCycles += 6; Sre(Abs()); break;
            case 0x5F: _pendingCycles += 7; Sre(AbsX(false)); break;
            case 0x5B: _pendingCycles += 7; Sre(AbsY(false)); break;
            case 0x43: _pendingCycles += 8; Sre(IndX()); break;
            case 0x53: _pendingCycles += 8; Sre(IndY(false)); break;

            // RRA = ROR then ADC.
            case 0x67: _pendingCycles += 5; Rra(Zp()); break;
            case 0x77: _pendingCycles += 6; Rra(ZpX()); break;
            case 0x6F: _pendingCycles += 6; Rra(Abs()); break;
            case 0x7F: _pendingCycles += 7; Rra(AbsX(false)); break;
            case 0x7B: _pendingCycles += 7; Rra(AbsY(false)); break;
            case 0x63: _pendingCycles += 8; Rra(IndX()); break;
            case 0x73: _pendingCycles += 8; Rra(IndY(false)); break;

            // Immediate ALU combos.
            case 0x0B: case 0x2B: _pendingCycles += 2; Anc(Imm()); break; // ANC
            case 0x4B: _pendingCycles += 2; Alr(Imm()); break;            // ALR (ASR)
            case 0x6B: _pendingCycles += 2; Arr(Imm()); break;            // ARR
            case 0xCB: _pendingCycles += 2; Sbx(Imm()); break;            // SBX (AXS)
            case 0xEB: _pendingCycles += 2; Sbc(Imm()); break;            // undocumented SBC #imm

            // Unstable high-byte stores and LAS.
            case 0x9E: _pendingCycles += 5; Shx(AbsY(false)); break;      // SHX (SXA)
            case 0x9C: _pendingCycles += 5; Shy(AbsX(false)); break;      // SHY (SYA)
            case 0x9F: _pendingCycles += 5; Sha(AbsY(false)); break;      // SHA (AHX) abs,Y
            case 0x93: _pendingCycles += 6; Sha(IndY(false)); break;      // SHA (AHX) (ind),Y
            case 0x9B: _pendingCycles += 5; S = (byte)(A & X); Sha(AbsY(false)); break; // TAS (SHS)
            case 0xBB: _pendingCycles += 4; Las(AbsY(true)); break;       // LAS (LAR)

            // JAM/KIL (0x02, 0x12, ...) and truly unknown bytes: treat as a 2-cycle no-op so
            // execution continues rather than halting the CPU.
            default:
                _pendingCycles += 2;
                break;
        }
    }
}
