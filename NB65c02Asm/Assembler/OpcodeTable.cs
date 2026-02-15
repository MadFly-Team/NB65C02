namespace NB65c02Asm.Assembler;

internal enum AddressingMode
{
    Implied,
    Accumulator,
    Immediate,
    Absolute,
    AbsoluteX,
    AbsoluteY,
    ZeroPage,
    ZeroPageX,
    ZeroPageY,
    Indirect,
    IndirectX,
    IndirectY,
    ZeroPageIndirect,
    Relative,
}

internal readonly record struct OpEncoding(string Mnemonic, AddressingMode Mode, byte Opcode, int Size);

internal static class OpcodeTable
{
    private static readonly Dictionary<(string Mnemonic, AddressingMode Mode), OpEncoding> _map = Build();

    private static Dictionary<(string, AddressingMode), OpEncoding> Build()
    {
        var m = new Dictionary<(string, AddressingMode), OpEncoding>();

        void Add(string mn, AddressingMode am, byte op, int sz)
            => m[(mn, am)] = new OpEncoding(mn, am, op, sz);

        // ---- ADC ----
        Add("ADC", AddressingMode.Immediate,        0x69, 2);
        Add("ADC", AddressingMode.ZeroPage,          0x65, 2);
        Add("ADC", AddressingMode.ZeroPageX,         0x75, 2);
        Add("ADC", AddressingMode.Absolute,          0x6D, 3);
        Add("ADC", AddressingMode.AbsoluteX,         0x7D, 3);
        Add("ADC", AddressingMode.AbsoluteY,         0x79, 3);
        Add("ADC", AddressingMode.IndirectX,         0x61, 2);
        Add("ADC", AddressingMode.IndirectY,         0x71, 2);
        Add("ADC", AddressingMode.ZeroPageIndirect,  0x72, 2); // 65C02

        // ---- AND ----
        Add("AND", AddressingMode.Immediate,        0x29, 2);
        Add("AND", AddressingMode.ZeroPage,          0x25, 2);
        Add("AND", AddressingMode.ZeroPageX,         0x35, 2);
        Add("AND", AddressingMode.Absolute,          0x2D, 3);
        Add("AND", AddressingMode.AbsoluteX,         0x3D, 3);
        Add("AND", AddressingMode.AbsoluteY,         0x39, 3);
        Add("AND", AddressingMode.IndirectX,         0x21, 2);
        Add("AND", AddressingMode.IndirectY,         0x31, 2);
        Add("AND", AddressingMode.ZeroPageIndirect,  0x32, 2); // 65C02

        // ---- ASL ----
        Add("ASL", AddressingMode.Accumulator,       0x0A, 1);
        Add("ASL", AddressingMode.ZeroPage,          0x06, 2);
        Add("ASL", AddressingMode.ZeroPageX,         0x16, 2);
        Add("ASL", AddressingMode.Absolute,          0x0E, 3);
        Add("ASL", AddressingMode.AbsoluteX,         0x1E, 3);

        // ---- Branches ----
        Add("BCC", AddressingMode.Relative,          0x90, 2);
        Add("BCS", AddressingMode.Relative,          0xB0, 2);
        Add("BEQ", AddressingMode.Relative,          0xF0, 2);
        Add("BMI", AddressingMode.Relative,          0x30, 2);
        Add("BNE", AddressingMode.Relative,          0xD0, 2);
        Add("BPL", AddressingMode.Relative,          0x10, 2);
        Add("BVC", AddressingMode.Relative,          0x50, 2);
        Add("BVS", AddressingMode.Relative,          0x70, 2);
        Add("BRA", AddressingMode.Relative,          0x80, 2); // 65C02

        // ---- BIT ----
        Add("BIT", AddressingMode.ZeroPage,          0x24, 2);
        Add("BIT", AddressingMode.Absolute,          0x2C, 3);
        Add("BIT", AddressingMode.Immediate,         0x89, 2); // 65C02
        Add("BIT", AddressingMode.ZeroPageX,         0x34, 2); // 65C02
        Add("BIT", AddressingMode.AbsoluteX,         0x3C, 3); // 65C02

        // ---- BRK ----
        Add("BRK", AddressingMode.Implied,           0x00, 1);

        // ---- Flag clear/set ----
        Add("CLC", AddressingMode.Implied,           0x18, 1);
        Add("CLD", AddressingMode.Implied,           0xD8, 1);
        Add("CLI", AddressingMode.Implied,           0x58, 1);
        Add("CLV", AddressingMode.Implied,           0xB8, 1);
        Add("SEC", AddressingMode.Implied,           0x38, 1);
        Add("SED", AddressingMode.Implied,           0xF8, 1);
        Add("SEI", AddressingMode.Implied,           0x78, 1);

        // ---- CMP ----
        Add("CMP", AddressingMode.Immediate,        0xC9, 2);
        Add("CMP", AddressingMode.ZeroPage,          0xC5, 2);
        Add("CMP", AddressingMode.ZeroPageX,         0xD5, 2);
        Add("CMP", AddressingMode.Absolute,          0xCD, 3);
        Add("CMP", AddressingMode.AbsoluteX,         0xDD, 3);
        Add("CMP", AddressingMode.AbsoluteY,         0xD9, 3);
        Add("CMP", AddressingMode.IndirectX,         0xC1, 2);
        Add("CMP", AddressingMode.IndirectY,         0xD1, 2);
        Add("CMP", AddressingMode.ZeroPageIndirect,  0xD2, 2); // 65C02

        // ---- CPX ----
        Add("CPX", AddressingMode.Immediate,        0xE0, 2);
        Add("CPX", AddressingMode.ZeroPage,          0xE4, 2);
        Add("CPX", AddressingMode.Absolute,          0xEC, 3);

        // ---- CPY ----
        Add("CPY", AddressingMode.Immediate,        0xC0, 2);
        Add("CPY", AddressingMode.ZeroPage,          0xC4, 2);
        Add("CPY", AddressingMode.Absolute,          0xCC, 3);

        // ---- DEC ----
        Add("DEC", AddressingMode.Accumulator,       0x3A, 1); // 65C02
        Add("DEC", AddressingMode.ZeroPage,          0xC6, 2);
        Add("DEC", AddressingMode.ZeroPageX,         0xD6, 2);
        Add("DEC", AddressingMode.Absolute,          0xCE, 3);
        Add("DEC", AddressingMode.AbsoluteX,         0xDE, 3);
        Add("DEX", AddressingMode.Implied,           0xCA, 1);
        Add("DEY", AddressingMode.Implied,           0x88, 1);

        // ---- EOR ----
        Add("EOR", AddressingMode.Immediate,        0x49, 2);
        Add("EOR", AddressingMode.ZeroPage,          0x45, 2);
        Add("EOR", AddressingMode.ZeroPageX,         0x55, 2);
        Add("EOR", AddressingMode.Absolute,          0x4D, 3);
        Add("EOR", AddressingMode.AbsoluteX,         0x5D, 3);
        Add("EOR", AddressingMode.AbsoluteY,         0x59, 3);
        Add("EOR", AddressingMode.IndirectX,         0x41, 2);
        Add("EOR", AddressingMode.IndirectY,         0x51, 2);
        Add("EOR", AddressingMode.ZeroPageIndirect,  0x52, 2); // 65C02

        // ---- INC ----
        Add("INC", AddressingMode.Accumulator,       0x1A, 1); // 65C02
        Add("INC", AddressingMode.ZeroPage,          0xE6, 2);
        Add("INC", AddressingMode.ZeroPageX,         0xF6, 2);
        Add("INC", AddressingMode.Absolute,          0xEE, 3);
        Add("INC", AddressingMode.AbsoluteX,         0xFE, 3);
        Add("INX", AddressingMode.Implied,           0xE8, 1);
        Add("INY", AddressingMode.Implied,           0xC8, 1);

        // ---- JMP ----
        Add("JMP", AddressingMode.Absolute,          0x4C, 3);
        Add("JMP", AddressingMode.Indirect,          0x6C, 3);
        Add("JMP", AddressingMode.AbsoluteX,         0x7C, 3); // 65C02 JMP ($nnnn,X)

        // ---- JSR ----
        Add("JSR", AddressingMode.Absolute,          0x20, 3);

        // ---- LDA ----
        Add("LDA", AddressingMode.Immediate,        0xA9, 2);
        Add("LDA", AddressingMode.ZeroPage,          0xA5, 2);
        Add("LDA", AddressingMode.ZeroPageX,         0xB5, 2);
        Add("LDA", AddressingMode.Absolute,          0xAD, 3);
        Add("LDA", AddressingMode.AbsoluteX,         0xBD, 3);
        Add("LDA", AddressingMode.AbsoluteY,         0xB9, 3);
        Add("LDA", AddressingMode.IndirectX,         0xA1, 2);
        Add("LDA", AddressingMode.IndirectY,         0xB1, 2);
        Add("LDA", AddressingMode.ZeroPageIndirect,  0xB2, 2); // 65C02

        // ---- LDX ----
        Add("LDX", AddressingMode.Immediate,        0xA2, 2);
        Add("LDX", AddressingMode.ZeroPage,          0xA6, 2);
        Add("LDX", AddressingMode.ZeroPageY,         0xB6, 2);
        Add("LDX", AddressingMode.Absolute,          0xAE, 3);
        Add("LDX", AddressingMode.AbsoluteY,         0xBE, 3);

        // ---- LDY ----
        Add("LDY", AddressingMode.Immediate,        0xA0, 2);
        Add("LDY", AddressingMode.ZeroPage,          0xA4, 2);
        Add("LDY", AddressingMode.ZeroPageX,         0xB4, 2);
        Add("LDY", AddressingMode.Absolute,          0xAC, 3);
        Add("LDY", AddressingMode.AbsoluteX,         0xBC, 3);

        // ---- LSR ----
        Add("LSR", AddressingMode.Accumulator,       0x4A, 1);
        Add("LSR", AddressingMode.ZeroPage,          0x46, 2);
        Add("LSR", AddressingMode.ZeroPageX,         0x56, 2);
        Add("LSR", AddressingMode.Absolute,          0x4E, 3);
        Add("LSR", AddressingMode.AbsoluteX,         0x5E, 3);

        // ---- NOP ----
        Add("NOP", AddressingMode.Implied,           0xEA, 1);

        // ---- ORA ----
        Add("ORA", AddressingMode.Immediate,        0x09, 2);
        Add("ORA", AddressingMode.ZeroPage,          0x05, 2);
        Add("ORA", AddressingMode.ZeroPageX,         0x15, 2);
        Add("ORA", AddressingMode.Absolute,          0x0D, 3);
        Add("ORA", AddressingMode.AbsoluteX,         0x1D, 3);
        Add("ORA", AddressingMode.AbsoluteY,         0x19, 3);
        Add("ORA", AddressingMode.IndirectX,         0x01, 2);
        Add("ORA", AddressingMode.IndirectY,         0x11, 2);
        Add("ORA", AddressingMode.ZeroPageIndirect,  0x12, 2); // 65C02

        // ---- Stack ----
        Add("PHA", AddressingMode.Implied,           0x48, 1);
        Add("PHP", AddressingMode.Implied,           0x08, 1);
        Add("PLA", AddressingMode.Implied,           0x68, 1);
        Add("PLP", AddressingMode.Implied,           0x28, 1);
        Add("PHX", AddressingMode.Implied,           0xDA, 1); // 65C02
        Add("PHY", AddressingMode.Implied,           0x5A, 1); // 65C02
        Add("PLX", AddressingMode.Implied,           0xFA, 1); // 65C02
        Add("PLY", AddressingMode.Implied,           0x7A, 1); // 65C02

        // ---- ROL ----
        Add("ROL", AddressingMode.Accumulator,       0x2A, 1);
        Add("ROL", AddressingMode.ZeroPage,          0x26, 2);
        Add("ROL", AddressingMode.ZeroPageX,         0x36, 2);
        Add("ROL", AddressingMode.Absolute,          0x2E, 3);
        Add("ROL", AddressingMode.AbsoluteX,         0x3E, 3);

        // ---- ROR ----
        Add("ROR", AddressingMode.Accumulator,       0x6A, 1);
        Add("ROR", AddressingMode.ZeroPage,          0x66, 2);
        Add("ROR", AddressingMode.ZeroPageX,         0x76, 2);
        Add("ROR", AddressingMode.Absolute,          0x6E, 3);
        Add("ROR", AddressingMode.AbsoluteX,         0x7E, 3);

        // ---- RTI / RTS ----
        Add("RTI", AddressingMode.Implied,           0x40, 1);
        Add("RTS", AddressingMode.Implied,           0x60, 1);

        // ---- SBC ----
        Add("SBC", AddressingMode.Immediate,        0xE9, 2);
        Add("SBC", AddressingMode.ZeroPage,          0xE5, 2);
        Add("SBC", AddressingMode.ZeroPageX,         0xF5, 2);
        Add("SBC", AddressingMode.Absolute,          0xED, 3);
        Add("SBC", AddressingMode.AbsoluteX,         0xFD, 3);
        Add("SBC", AddressingMode.AbsoluteY,         0xF9, 3);
        Add("SBC", AddressingMode.IndirectX,         0xE1, 2);
        Add("SBC", AddressingMode.IndirectY,         0xF1, 2);
        Add("SBC", AddressingMode.ZeroPageIndirect,  0xF2, 2); // 65C02

        // ---- STA ----
        Add("STA", AddressingMode.ZeroPage,          0x85, 2);
        Add("STA", AddressingMode.ZeroPageX,         0x95, 2);
        Add("STA", AddressingMode.Absolute,          0x8D, 3);
        Add("STA", AddressingMode.AbsoluteX,         0x9D, 3);
        Add("STA", AddressingMode.AbsoluteY,         0x99, 3);
        Add("STA", AddressingMode.IndirectX,         0x81, 2);
        Add("STA", AddressingMode.IndirectY,         0x91, 2);
        Add("STA", AddressingMode.ZeroPageIndirect,  0x92, 2); // 65C02

        // ---- STX ----
        Add("STX", AddressingMode.ZeroPage,          0x86, 2);
        Add("STX", AddressingMode.ZeroPageY,         0x96, 2);
        Add("STX", AddressingMode.Absolute,          0x8E, 3);

        // ---- STY ----
        Add("STY", AddressingMode.ZeroPage,          0x84, 2);
        Add("STY", AddressingMode.ZeroPageX,         0x94, 2);
        Add("STY", AddressingMode.Absolute,          0x8C, 3);

        // ---- STZ (65C02) ----
        Add("STZ", AddressingMode.ZeroPage,          0x64, 2);
        Add("STZ", AddressingMode.ZeroPageX,         0x74, 2);
        Add("STZ", AddressingMode.Absolute,          0x9C, 3);
        Add("STZ", AddressingMode.AbsoluteX,         0x9E, 3);

        // ---- Transfers ----
        Add("TAX", AddressingMode.Implied,           0xAA, 1);
        Add("TAY", AddressingMode.Implied,           0xA8, 1);
        Add("TSX", AddressingMode.Implied,           0xBA, 1);
        Add("TXA", AddressingMode.Implied,           0x8A, 1);
        Add("TXS", AddressingMode.Implied,           0x9A, 1);
        Add("TYA", AddressingMode.Implied,           0x98, 1);

        // ---- 65C02 extras ----
        Add("TRB", AddressingMode.ZeroPage,          0x14, 2);
        Add("TRB", AddressingMode.Absolute,          0x1C, 3);
        Add("TSB", AddressingMode.ZeroPage,          0x04, 2);
        Add("TSB", AddressingMode.Absolute,          0x0C, 3);

        return m;
    }

    public static bool TryGet(string mnemonic, AddressingMode mode, out OpEncoding encoding) =>
        _map.TryGetValue((mnemonic.ToUpperInvariant(), mode), out encoding);

    private static readonly HashSet<string> _branchMnemonics = new(StringComparer.OrdinalIgnoreCase)
    {
        "BCC", "BCS", "BEQ", "BMI", "BNE", "BPL", "BVC", "BVS", "BRA",
    };

    public static bool IsBranch(string mnemonic) =>
        _branchMnemonics.Contains(mnemonic);
}
