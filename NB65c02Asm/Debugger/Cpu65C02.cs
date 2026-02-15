namespace NB65c02Asm.Debugger;

internal sealed class Cpu65C02
{
    public byte A, X, Y, SP;
    public ushort PC;
    public bool N, V, D, I, Z, C;
    public readonly byte[] Memory = new byte[0x10000];
    public bool Halted;
    public long Cycles;

    public void Reset(ushort startAddress)
    {
        A = X = Y = 0;
        SP = 0xFD;
        PC = startAddress;
        N = V = D = I = Z = C = false;
        Halted = false;
        Cycles = 0;
    }

    public void Load(ReadOnlySpan<byte> data, ushort address) =>
        data.CopyTo(Memory.AsSpan(address));

    public byte GetP()
    {
        byte p = 0x20;
        if (C) p |= 0x01;
        if (Z) p |= 0x02;
        if (I) p |= 0x04;
        if (D) p |= 0x08;
        if (V) p |= 0x40;
        if (N) p |= 0x80;
        return p;
    }

    public void SetP(byte p)
    {
        C = (p & 0x01) != 0;
        Z = (p & 0x02) != 0;
        I = (p & 0x04) != 0;
        D = (p & 0x08) != 0;
        V = (p & 0x40) != 0;
        N = (p & 0x80) != 0;
    }

    private byte Read(ushort addr) => Memory[addr];
    private void Write(ushort addr, byte val) => Memory[addr] = val;
    private void Push(byte val) => Memory[0x0100 | SP--] = val;
    private byte Pull() => Memory[0x0100 | ++SP];
    private void Push16(ushort val) { Push((byte)(val >> 8)); Push((byte)(val & 0xFF)); }
    private ushort Pull16() { byte lo = Pull(); byte hi = Pull(); return (ushort)(hi << 8 | lo); }
    private void SetNZ(byte val) { N = (val & 0x80) != 0; Z = val == 0; }

    private void DoADC(byte val)
    {
        int sum = A + val + (C ? 1 : 0);
        C = sum > 0xFF;
        V = (~(A ^ val) & (A ^ sum) & 0x80) != 0;
        A = (byte)sum;
        SetNZ(A);
    }

    private void DoSBC(byte val) => DoADC((byte)~val);

    private void DoCompare(byte reg, byte val)
    {
        int diff = reg - val;
        C = reg >= val;
        SetNZ((byte)diff);
    }

    private byte DoASL(byte val) { C = (val & 0x80) != 0; val <<= 1; SetNZ(val); return val; }
    private byte DoLSR(byte val) { C = (val & 0x01) != 0; val >>= 1; SetNZ(val); return val; }

    private byte DoROL(byte val)
    {
        bool oldC = C;
        C = (val & 0x80) != 0;
        val = (byte)((val << 1) | (oldC ? 1 : 0));
        SetNZ(val);
        return val;
    }

    private byte DoROR(byte val)
    {
        bool oldC = C;
        C = (val & 0x01) != 0;
        val = (byte)((val >> 1) | (oldC ? 0x80 : 0));
        SetNZ(val);
        return val;
    }

    // --- Instruction table ---

    private enum Op : byte
    {
        ADC, AND, ASL, BCC, BCS, BEQ, BIT, BMI, BNE, BPL, BRA, BRK, BVC, BVS,
        CLC, CLD, CLI, CLV, CMP, CPX, CPY, DEC, DEX, DEY, EOR, INC, INX, INY,
        JMP, JMI, JSR, LDA, LDX, LDY, LSR, NOP, ORA, PHA, PHP, PHX, PHY, PLA, PLP, PLX, PLY,
        ROL, ROR, RTI, RTS, SBC, SEC, SED, SEI, STA, STX, STY, STZ,
        TAX, TAY, TRB, TSB, TSX, TXA, TXS, TYA, ILL,
    }

    internal enum Addr : byte
    {
        IMP, ACC, IMM, ZP, ZPX, ZPY, ABS, ABX, ABY, IND, IZX, IZY, ZPI, REL,
    }

    private static readonly (Op Op, Addr Mode, byte Size, byte Cyc)[] T = BuildTable();

    private static (Op, Addr, byte, byte)[] BuildTable()
    {
        var t = new (Op, Addr, byte, byte)[256];
        for (int i = 0; i < 256; i++) t[i] = (Op.ILL, Addr.IMP, 1, 2);

        // $0x
        t[0x00] = (Op.BRK, Addr.IMP, 1, 7);
        t[0x01] = (Op.ORA, Addr.IZX, 2, 6);
        t[0x04] = (Op.TSB, Addr.ZP,  2, 5); // 65C02
        t[0x05] = (Op.ORA, Addr.ZP,  2, 3);
        t[0x06] = (Op.ASL, Addr.ZP,  2, 5);
        t[0x08] = (Op.PHP, Addr.IMP, 1, 3);
        t[0x09] = (Op.ORA, Addr.IMM, 2, 2);
        t[0x0A] = (Op.ASL, Addr.ACC, 1, 2);
        t[0x0C] = (Op.TSB, Addr.ABS, 3, 6); // 65C02
        t[0x0D] = (Op.ORA, Addr.ABS, 3, 4);
        t[0x0E] = (Op.ASL, Addr.ABS, 3, 6);
        // $1x
        t[0x10] = (Op.BPL, Addr.REL, 2, 2);
        t[0x11] = (Op.ORA, Addr.IZY, 2, 5);
        t[0x12] = (Op.ORA, Addr.ZPI, 2, 5); // 65C02
        t[0x14] = (Op.TRB, Addr.ZP,  2, 5); // 65C02
        t[0x15] = (Op.ORA, Addr.ZPX, 2, 4);
        t[0x16] = (Op.ASL, Addr.ZPX, 2, 6);
        t[0x18] = (Op.CLC, Addr.IMP, 1, 2);
        t[0x19] = (Op.ORA, Addr.ABY, 3, 4);
        t[0x1C] = (Op.TRB, Addr.ABS, 3, 6); // 65C02
        t[0x1D] = (Op.ORA, Addr.ABX, 3, 4);
        t[0x1E] = (Op.ASL, Addr.ABX, 3, 7);
        // $2x
        t[0x20] = (Op.JSR, Addr.ABS, 3, 6);
        t[0x21] = (Op.AND, Addr.IZX, 2, 6);
        t[0x24] = (Op.BIT, Addr.ZP,  2, 3);
        t[0x25] = (Op.AND, Addr.ZP,  2, 3);
        t[0x26] = (Op.ROL, Addr.ZP,  2, 5);
        t[0x28] = (Op.PLP, Addr.IMP, 1, 4);
        t[0x29] = (Op.AND, Addr.IMM, 2, 2);
        t[0x2A] = (Op.ROL, Addr.ACC, 1, 2);
        t[0x2C] = (Op.BIT, Addr.ABS, 3, 4);
        t[0x2D] = (Op.AND, Addr.ABS, 3, 4);
        t[0x2E] = (Op.ROL, Addr.ABS, 3, 6);
        // $3x
        t[0x30] = (Op.BMI, Addr.REL, 2, 2);
        t[0x31] = (Op.AND, Addr.IZY, 2, 5);
        t[0x32] = (Op.AND, Addr.ZPI, 2, 5); // 65C02
        t[0x34] = (Op.BIT, Addr.ZPX, 2, 4); // 65C02
        t[0x35] = (Op.AND, Addr.ZPX, 2, 4);
        t[0x36] = (Op.ROL, Addr.ZPX, 2, 6);
        t[0x38] = (Op.SEC, Addr.IMP, 1, 2);
        t[0x39] = (Op.AND, Addr.ABY, 3, 4);
        t[0x3C] = (Op.BIT, Addr.ABX, 3, 4); // 65C02
        t[0x3D] = (Op.AND, Addr.ABX, 3, 4);
        t[0x3E] = (Op.ROL, Addr.ABX, 3, 7);
        // $4x
        t[0x40] = (Op.RTI, Addr.IMP, 1, 6);
        t[0x41] = (Op.EOR, Addr.IZX, 2, 6);
        t[0x45] = (Op.EOR, Addr.ZP,  2, 3);
        t[0x46] = (Op.LSR, Addr.ZP,  2, 5);
        t[0x48] = (Op.PHA, Addr.IMP, 1, 3);
        t[0x49] = (Op.EOR, Addr.IMM, 2, 2);
        t[0x4A] = (Op.LSR, Addr.ACC, 1, 2);
        t[0x4C] = (Op.JMP, Addr.ABS, 3, 3);
        t[0x4D] = (Op.EOR, Addr.ABS, 3, 4);
        t[0x4E] = (Op.LSR, Addr.ABS, 3, 6);
        // $5x
        t[0x50] = (Op.BVC, Addr.REL, 2, 2);
        t[0x51] = (Op.EOR, Addr.IZY, 2, 5);
        t[0x52] = (Op.EOR, Addr.ZPI, 2, 5); // 65C02
        t[0x55] = (Op.EOR, Addr.ZPX, 2, 4);
        t[0x56] = (Op.LSR, Addr.ZPX, 2, 6);
        t[0x58] = (Op.CLI, Addr.IMP, 1, 2);
        t[0x59] = (Op.EOR, Addr.ABY, 3, 4);
        t[0x5A] = (Op.PHY, Addr.IMP, 1, 3); // 65C02
        t[0x5D] = (Op.EOR, Addr.ABX, 3, 4);
        t[0x5E] = (Op.LSR, Addr.ABX, 3, 7);
        // $6x
        t[0x60] = (Op.RTS, Addr.IMP, 1, 6);
        t[0x61] = (Op.ADC, Addr.IZX, 2, 6);
        t[0x64] = (Op.STZ, Addr.ZP,  2, 3); // 65C02
        t[0x65] = (Op.ADC, Addr.ZP,  2, 3);
        t[0x66] = (Op.ROR, Addr.ZP,  2, 5);
        t[0x68] = (Op.PLA, Addr.IMP, 1, 4);
        t[0x69] = (Op.ADC, Addr.IMM, 2, 2);
        t[0x6A] = (Op.ROR, Addr.ACC, 1, 2);
        t[0x6C] = (Op.JMI, Addr.IND, 3, 5);
        t[0x6D] = (Op.ADC, Addr.ABS, 3, 4);
        t[0x6E] = (Op.ROR, Addr.ABS, 3, 6);
        // $7x
        t[0x70] = (Op.BVS, Addr.REL, 2, 2);
        t[0x71] = (Op.ADC, Addr.IZY, 2, 5);
        t[0x72] = (Op.ADC, Addr.ZPI, 2, 5); // 65C02
        t[0x74] = (Op.STZ, Addr.ZPX, 2, 4); // 65C02
        t[0x75] = (Op.ADC, Addr.ZPX, 2, 4);
        t[0x76] = (Op.ROR, Addr.ZPX, 2, 6);
        t[0x78] = (Op.SEI, Addr.IMP, 1, 2);
        t[0x79] = (Op.ADC, Addr.ABY, 3, 4);
        t[0x7A] = (Op.PLY, Addr.IMP, 1, 4); // 65C02
        t[0x7C] = (Op.JMP, Addr.ABX, 3, 6); // 65C02 JMP ($nnnn,X)
        t[0x7D] = (Op.ADC, Addr.ABX, 3, 4);
        t[0x7E] = (Op.ROR, Addr.ABX, 3, 7);
        // $8x
        t[0x80] = (Op.BRA, Addr.REL, 2, 3); // 65C02
        t[0x81] = (Op.STA, Addr.IZX, 2, 6);
        t[0x84] = (Op.STY, Addr.ZP,  2, 3);
        t[0x85] = (Op.STA, Addr.ZP,  2, 3);
        t[0x86] = (Op.STX, Addr.ZP,  2, 3);
        t[0x88] = (Op.DEY, Addr.IMP, 1, 2);
        t[0x89] = (Op.BIT, Addr.IMM, 2, 2); // 65C02
        t[0x8A] = (Op.TXA, Addr.IMP, 1, 2);
        t[0x8C] = (Op.STY, Addr.ABS, 3, 4);
        t[0x8D] = (Op.STA, Addr.ABS, 3, 4);
        t[0x8E] = (Op.STX, Addr.ABS, 3, 4);
        // $9x
        t[0x90] = (Op.BCC, Addr.REL, 2, 2);
        t[0x91] = (Op.STA, Addr.IZY, 2, 6);
        t[0x92] = (Op.STA, Addr.ZPI, 2, 5); // 65C02
        t[0x94] = (Op.STY, Addr.ZPX, 2, 4);
        t[0x95] = (Op.STA, Addr.ZPX, 2, 4);
        t[0x96] = (Op.STX, Addr.ZPY, 2, 4);
        t[0x98] = (Op.TYA, Addr.IMP, 1, 2);
        t[0x99] = (Op.STA, Addr.ABY, 3, 5);
        t[0x9A] = (Op.TXS, Addr.IMP, 1, 2);
        t[0x9C] = (Op.STZ, Addr.ABS, 3, 4); // 65C02
        t[0x9D] = (Op.STA, Addr.ABX, 3, 5);
        t[0x9E] = (Op.STZ, Addr.ABX, 3, 5); // 65C02
        // $Ax
        t[0xA0] = (Op.LDY, Addr.IMM, 2, 2);
        t[0xA1] = (Op.LDA, Addr.IZX, 2, 6);
        t[0xA2] = (Op.LDX, Addr.IMM, 2, 2);
        t[0xA4] = (Op.LDY, Addr.ZP,  2, 3);
        t[0xA5] = (Op.LDA, Addr.ZP,  2, 3);
        t[0xA6] = (Op.LDX, Addr.ZP,  2, 3);
        t[0xA8] = (Op.TAY, Addr.IMP, 1, 2);
        t[0xA9] = (Op.LDA, Addr.IMM, 2, 2);
        t[0xAA] = (Op.TAX, Addr.IMP, 1, 2);
        t[0xAC] = (Op.LDY, Addr.ABS, 3, 4);
        t[0xAD] = (Op.LDA, Addr.ABS, 3, 4);
        t[0xAE] = (Op.LDX, Addr.ABS, 3, 4);
        // $Bx
        t[0xB0] = (Op.BCS, Addr.REL, 2, 2);
        t[0xB1] = (Op.LDA, Addr.IZY, 2, 5);
        t[0xB2] = (Op.LDA, Addr.ZPI, 2, 5); // 65C02
        t[0xB4] = (Op.LDY, Addr.ZPX, 2, 4);
        t[0xB5] = (Op.LDA, Addr.ZPX, 2, 4);
        t[0xB6] = (Op.LDX, Addr.ZPY, 2, 4);
        t[0xB8] = (Op.CLV, Addr.IMP, 1, 2);
        t[0xB9] = (Op.LDA, Addr.ABY, 3, 4);
        t[0xBA] = (Op.TSX, Addr.IMP, 1, 2);
        t[0xBC] = (Op.LDY, Addr.ABX, 3, 4);
        t[0xBD] = (Op.LDA, Addr.ABX, 3, 4);
        t[0xBE] = (Op.LDX, Addr.ABY, 3, 4);
        // $Cx
        t[0xC0] = (Op.CPY, Addr.IMM, 2, 2);
        t[0xC1] = (Op.CMP, Addr.IZX, 2, 6);
        t[0xC4] = (Op.CPY, Addr.ZP,  2, 3);
        t[0xC5] = (Op.CMP, Addr.ZP,  2, 3);
        t[0xC6] = (Op.DEC, Addr.ZP,  2, 5);
        t[0xC8] = (Op.INY, Addr.IMP, 1, 2);
        t[0xC9] = (Op.CMP, Addr.IMM, 2, 2);
        t[0xCA] = (Op.DEX, Addr.IMP, 1, 2);
        t[0xCC] = (Op.CPY, Addr.ABS, 3, 4);
        t[0xCD] = (Op.CMP, Addr.ABS, 3, 4);
        t[0xCE] = (Op.DEC, Addr.ABS, 3, 6);
        // $Dx
        t[0xD0] = (Op.BNE, Addr.REL, 2, 2);
        t[0xD1] = (Op.CMP, Addr.IZY, 2, 5);
        t[0xD2] = (Op.CMP, Addr.ZPI, 2, 5); // 65C02
        t[0xD5] = (Op.CMP, Addr.ZPX, 2, 4);
        t[0xD6] = (Op.DEC, Addr.ZPX, 2, 6);
        t[0xD8] = (Op.CLD, Addr.IMP, 1, 2);
        t[0xD9] = (Op.CMP, Addr.ABY, 3, 4);
        t[0xDA] = (Op.PHX, Addr.IMP, 1, 3); // 65C02
        t[0xDD] = (Op.CMP, Addr.ABX, 3, 4);
        t[0xDE] = (Op.DEC, Addr.ABX, 3, 7);
        // $Ex
        t[0xE0] = (Op.CPX, Addr.IMM, 2, 2);
        t[0xE1] = (Op.SBC, Addr.IZX, 2, 6);
        t[0xE4] = (Op.CPX, Addr.ZP,  2, 3);
        t[0xE5] = (Op.SBC, Addr.ZP,  2, 3);
        t[0xE6] = (Op.INC, Addr.ZP,  2, 5);
        t[0xE8] = (Op.INX, Addr.IMP, 1, 2);
        t[0xE9] = (Op.SBC, Addr.IMM, 2, 2);
        t[0xEA] = (Op.NOP, Addr.IMP, 1, 2);
        t[0xEC] = (Op.CPX, Addr.ABS, 3, 4);
        t[0xED] = (Op.SBC, Addr.ABS, 3, 4);
        t[0xEE] = (Op.INC, Addr.ABS, 3, 6);
        // DEC A / INC A (65C02)
        t[0x3A] = (Op.DEC, Addr.ACC, 1, 2);
        t[0x1A] = (Op.INC, Addr.ACC, 1, 2);
        // $Fx
        t[0xF0] = (Op.BEQ, Addr.REL, 2, 2);
        t[0xF1] = (Op.SBC, Addr.IZY, 2, 5);
        t[0xF2] = (Op.SBC, Addr.ZPI, 2, 5); // 65C02
        t[0xF5] = (Op.SBC, Addr.ZPX, 2, 4);
        t[0xF6] = (Op.INC, Addr.ZPX, 2, 6);
        t[0xF8] = (Op.SED, Addr.IMP, 1, 2);
        t[0xF9] = (Op.SBC, Addr.ABY, 3, 4);
        t[0xFA] = (Op.PLX, Addr.IMP, 1, 4); // 65C02
        t[0xFD] = (Op.SBC, Addr.ABX, 3, 4);
        t[0xFE] = (Op.INC, Addr.ABX, 3, 7);

        return t;
    }

    // --- Execute one instruction ---

    public int Step()
    {
        if (Halted) return 0;

        var opByte = Read(PC);
        var (op, mode, size, cycles) = T[opByte];

        byte lo = size >= 2 ? Read((ushort)(PC + 1)) : (byte)0;
        byte hi = size >= 3 ? Read((ushort)(PC + 2)) : (byte)0;
        ushort operand = (ushort)(hi << 8 | lo);

        ushort addr = 0;
        switch (mode)
        {
            case Addr.IMP: case Addr.ACC: break;
            case Addr.IMM: addr = (ushort)(PC + 1); break;
            case Addr.ZP:  addr = lo; break;
            case Addr.ZPX: addr = (byte)(lo + X); break;
            case Addr.ZPY: addr = (byte)(lo + Y); break;
            case Addr.ABS: addr = operand; break;
            case Addr.ABX: addr = (ushort)(operand + X); break;
            case Addr.ABY: addr = (ushort)(operand + Y); break;
            case Addr.IND:
                addr = (ushort)(Read(operand) | Read((ushort)((operand & 0xFF00) | ((operand + 1) & 0xFF))) << 8);
                break;
            case Addr.IZX:
                var zpx = (byte)(lo + X);
                addr = (ushort)(Read(zpx) | Read((byte)(zpx + 1)) << 8);
                break;
            case Addr.IZY:
                addr = (ushort)((Read(lo) | Read((byte)(lo + 1)) << 8) + Y);
                break;
            case Addr.ZPI:
                addr = (ushort)(Read(lo) | Read((byte)(lo + 1)) << 8);
                break;
            case Addr.REL:
                addr = (ushort)(PC + 2 + (sbyte)lo);
                break;
        }

        PC += size;

        switch (op)
        {
            case Op.LDA: A = Read(addr); SetNZ(A); break;
            case Op.LDX: X = Read(addr); SetNZ(X); break;
            case Op.LDY: Y = Read(addr); SetNZ(Y); break;
            case Op.STA: Write(addr, A); break;
            case Op.STX: Write(addr, X); break;
            case Op.STY: Write(addr, Y); break;
            case Op.ADC: DoADC(Read(addr)); break;
            case Op.SBC: DoSBC(Read(addr)); break;
            case Op.CMP: DoCompare(A, Read(addr)); break;
            case Op.CPX: DoCompare(X, Read(addr)); break;
            case Op.CPY: DoCompare(Y, Read(addr)); break;
            case Op.AND: A &= Read(addr); SetNZ(A); break;
            case Op.ORA: A |= Read(addr); SetNZ(A); break;
            case Op.EOR: A ^= Read(addr); SetNZ(A); break;
            case Op.ASL:
                if (mode == Addr.ACC) A = DoASL(A);
                else Write(addr, DoASL(Read(addr)));
                break;
            case Op.LSR:
                if (mode == Addr.ACC) A = DoLSR(A);
                else Write(addr, DoLSR(Read(addr)));
                break;
            case Op.ROL:
                if (mode == Addr.ACC) A = DoROL(A);
                else Write(addr, DoROL(Read(addr)));
                break;
            case Op.ROR:
                if (mode == Addr.ACC) A = DoROR(A);
                else Write(addr, DoROR(Read(addr)));
                break;
            case Op.INC:
                if (mode == Addr.ACC) { A++; SetNZ(A); }
                else { var v = (byte)(Read(addr) + 1); Write(addr, v); SetNZ(v); }
                break;
            case Op.DEC:
                if (mode == Addr.ACC) { A--; SetNZ(A); }
                else { var v = (byte)(Read(addr) - 1); Write(addr, v); SetNZ(v); }
                break;
            case Op.INX: X++; SetNZ(X); break;
            case Op.DEX: X--; SetNZ(X); break;
            case Op.INY: Y++; SetNZ(Y); break;
            case Op.DEY: Y--; SetNZ(Y); break;
            case Op.BCC: if (!C) PC = addr; break;
            case Op.BCS: if (C)  PC = addr; break;
            case Op.BEQ: if (Z)  PC = addr; break;
            case Op.BNE: if (!Z) PC = addr; break;
            case Op.BMI: if (N)  PC = addr; break;
            case Op.BPL: if (!N) PC = addr; break;
            case Op.BVS: if (V)  PC = addr; break;
            case Op.BVC: if (!V) PC = addr; break;
            case Op.BRA: PC = addr; break;
            case Op.JMP:
                if (addr >= 0xC000) { /* skip OS/ROM jump */ break; }
                PC = addr; break;
            case Op.JMI:
                if (addr >= 0xC000) { /* skip OS/ROM jump */ break; }
                PC = addr; break;
            case Op.JSR:
                if (addr >= 0xC000) { /* skip OS/ROM call */ break; }
                Push16((ushort)(PC - 1)); PC = addr; break;
            case Op.RTS: PC = (ushort)(Pull16() + 1); break;
            case Op.RTI: SetP(Pull()); PC = Pull16(); break;
            case Op.PHA: Push(A); break;
            case Op.PLA: A = Pull(); SetNZ(A); break;
            case Op.PHP: Push((byte)(GetP() | 0x30)); break;
            case Op.PLP: SetP(Pull()); break;
            case Op.CLC: C = false; break;
            case Op.SEC: C = true; break;
            case Op.CLI: I = false; break;
            case Op.SEI: I = true; break;
            case Op.CLD: D = false; break;
            case Op.SED: D = true; break;
            case Op.CLV: V = false; break;
            case Op.TAX: X = A; SetNZ(X); break;
            case Op.TAY: Y = A; SetNZ(Y); break;
            case Op.TXA: A = X; SetNZ(A); break;
            case Op.TYA: A = Y; SetNZ(A); break;
            case Op.TSX: X = SP; SetNZ(X); break;
            case Op.TXS: SP = X; break;
            case Op.BIT: { var m = Read(addr); Z = (A & m) == 0; if (mode != Addr.IMM) { N = (m & 0x80) != 0; V = (m & 0x40) != 0; } break; }
            case Op.STZ: Write(addr, 0); break;
            case Op.TRB: { var m = Read(addr); Z = (A & m) == 0; Write(addr, (byte)(m & ~A)); break; }
            case Op.TSB: { var m = Read(addr); Z = (A & m) == 0; Write(addr, (byte)(m | A)); break; }
            case Op.PHX: Push(X); break;
            case Op.PHY: Push(Y); break;
            case Op.PLX: X = Pull(); SetNZ(X); break;
            case Op.PLY: Y = Pull(); SetNZ(Y); break;
            case Op.BRK:
                PC++;
                Push16(PC);
                Push((byte)(GetP() | 0x30));
                I = true;
                PC = (ushort)(Read(0xFFFE) | Read(0xFFFF) << 8);
                Halted = true;
                break;
            case Op.NOP: break;
            case Op.ILL: break;
        }

        Cycles += cycles;
        return cycles;
    }

    // --- Disassembler ---

    public static (string Text, int Size) Disassemble(byte[] memory, ushort address)
    {
        var opByte = memory[address];
        var (op, mode, size, _) = T[opByte];

        if (op == Op.ILL)
            return ($"???  (${opByte:X2})", 1);

        byte lo = size >= 2 ? memory[(ushort)(address + 1)] : (byte)0;
        byte hi = size >= 3 ? memory[(ushort)(address + 2)] : (byte)0;
        ushort operand = (ushort)(hi << 8 | lo);

        var mnemonic = op == Op.JMI ? "JMP" : op.ToString();

        var operandStr = mode switch
        {
            Addr.IMP => "",
            Addr.ACC => "A",
            Addr.IMM => $"#${lo:X2}",
            Addr.ZP  => $"${lo:X2}",
            Addr.ZPX => $"${lo:X2},X",
            Addr.ZPY => $"${lo:X2},Y",
            Addr.ABS => $"${operand:X4}",
            Addr.ABX => $"${operand:X4},X",
            Addr.ABY => $"${operand:X4},Y",
            Addr.IND => $"(${operand:X4})",
            Addr.IZX => $"(${lo:X2},X)",
            Addr.IZY => $"(${lo:X2}),Y",
            Addr.ZPI => $"(${lo:X2})",
            Addr.REL => $"${(ushort)(address + 2 + (sbyte)lo):X4}",
            _ => "",
        };

        var text = $"{mnemonic} {operandStr}".TrimEnd();

        // Annotate OS/ROM calls that the debugger will skip
        if ((op is Op.JSR or Op.JMP or Op.JMI) && mode is Addr.ABS && operand >= 0xC000)
        {
            if (MosNames.TryGetValue(operand, out var name))
                text += $"  [{name}]";
            else
                text += "  [OS]";
        }

        return (text, size);
    }

    public static int InstructionSize(byte opcode) => T[opcode].Size;

    // BBC Micro MOS (Machine Operating System) entry point names
    private static readonly Dictionary<ushort, string> MosNames = new()
    {
        [0xFFB9] = "OSDRM",
        [0xFFBC] = "VDUCHR",
        [0xFFBF] = "OSEVEN",
        [0xFFC2] = "OSINIT",
        [0xFFC5] = "OSREAD",
        [0xFFC8] = "GSINIT",
        [0xFFCB] = "GSREAD",
        [0xFFCE] = "NVRDCH",
        [0xFFD1] = "NVWRCH",
        [0xFFD4] = "OSFIND",
        [0xFFD7] = "OSGBPB",
        [0xFFDA] = "OSBPUT",
        [0xFFDD] = "OSBGET",
        [0xFFE0] = "OSARGS",
        [0xFFE3] = "OSASCI",
        [0xFFE7] = "OSNEWL",
        [0xFFEE] = "OSWRCH",
        [0xFFF1] = "OSWORD",
        [0xFFF4] = "OSBYTE",
        [0xFFF7] = "OSCLI",
        [0xFFFA] = "NMI",
        [0xFFFC] = "RESET",
        [0xFFFE] = "IRQ",
    };
}
