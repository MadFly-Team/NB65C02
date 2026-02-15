using System.Text;

namespace NB65c02Asm.Assembler;

internal sealed class Assembler65C02
{
    public AssemblyResult Assemble(string sourceText, string? fileName = null, SourceMap? sourceMap = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        // Resolve the base directory for relative .include paths.
        var baseDir = fileName is not null
            ? Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;

        // Build a source map so error messages report the original file and
        // line rather than the post-expansion line number.  A caller (e.g.
        // BuildProject) may supply a pre-built map when it has already
        // expanded includes and concatenated multiple source files.
        sourceMap ??= new SourceMap();
        sourceText = ExpandIncludes(sourceText, baseDir, [], sourceMap, fileName);

        var lexer = new Lexer(sourceText, fileName, sourceMap);
        var tokens = new List<Token>(capacity: Math.Min(sourceText.Length / 3, 32_768));
        for (;;)
        {
            var t = lexer.NextToken();
            tokens.Add(t);
            if (t.Kind == TokenKind.Eof)
            {
                break;
            }
        }

        // Pass 1a: collect labels.  Forward-referenced symbols use a
        // placeholder value (0x100) which forces Absolute addressing mode.
        var pass1 = new Pass(tokens, fileName: fileName, sourceMap: sourceMap);
        pass1.Run(collectLabels: true);

        // Pass 1b: re-collect labels now that every symbol is known.
        // Without this, a forward reference to a zero-page constant
        // (e.g. SPRITEX = $70 defined in a later file) would be sized
        // as Absolute (3 bytes) on pass 1 but ZeroPage (2 bytes) on
        // pass 2, causing a PC drift that corrupts all subsequent
        // addresses.  Re-running label collection with the full symbol
        // table lets every instruction pick the correct addressing mode
        // and produces stable label addresses for the emit pass.
        var pass1b = new Pass(tokens, pass1.Symbols, fileName, sourceMap);
        pass1b.Run(collectLabels: true);

        var pass2 = new Pass(tokens, pass1b.Symbols, fileName, sourceMap);
        pass2.Run(collectLabels: false);

        return new AssemblyResult
        {
            Origin = pass2.Origin,
            OutputPath = pass2.OutputPath,
            BytesByAddress = pass2.BytesByAddress,
        };
    }

    /// <summary>
    /// Recursively expands <c>.include "path"</c> directives by inlining the
    /// referenced file contents.  Circular includes are detected and rejected.
    /// When a <see cref="SourceMap"/> is supplied, each output line is recorded
    /// so that error messages can report the original file and line number.
    /// </summary>
    internal static string ExpandIncludes(string source, string baseDir, HashSet<string> visited,
        SourceMap? map = null, string? currentFile = null)
    {
        var sb = new StringBuilder(source.Length);
        using var reader = new StringReader(source);
        string? line;
        int lineNum = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNum++;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(".include", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Length > 8 && trimmed[8] is ' ' or '\t')
            {
                var rest = trimmed[8..].Trim();

                // Strip trailing comment so ".include \"file.asm\" ; comment" works.
                var semi = rest.IndexOf(';');
                if (semi >= 0)
                    rest = rest[..semi].TrimEnd();

                if (rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"')
                {
                    var includePath = rest[1..^1];
                    var fullPath = Path.GetFullPath(Path.Combine(baseDir, includePath));

                    if (!visited.Add(fullPath))
                    {
                        throw new AssemblerException($"Circular .include detected: {includePath}");
                    }

                    if (!File.Exists(fullPath))
                    {
                        throw new AssemblerException($"Include file not found: {includePath}");
                    }

                    var includeSource = File.ReadAllText(fullPath);
                    var includeDir = Path.GetDirectoryName(fullPath) ?? baseDir;
                    // Recursive call populates the map with entries for the
                    // included file's lines.  Use Append (not AppendLine) to
                    // avoid inserting an extra blank line that would shift
                    // subsequent map entries.
                    var expanded = ExpandIncludes(includeSource, includeDir, visited, map, fullPath);
                    sb.Append(expanded);

                    visited.Remove(fullPath);
                    continue;
                }
            }

            map?.Add(currentFile, lineNum);
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private sealed class Pass
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly string? _fileName;
        private readonly SourceMap? _sourceMap;
        private int _i;
        private ushort _pc;
        private bool _pcSet;
        private bool _collectLabels;

        public Pass(IReadOnlyList<Token> tokens, Dictionary<string, ushort>? symbols = null,
            string? fileName = null, SourceMap? sourceMap = null)
        {
            _tokens = tokens;
            _fileName = fileName;
            _sourceMap = sourceMap;
            Symbols = symbols ?? new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, ushort> Symbols { get; }
        public SortedDictionary<ushort, byte> BytesByAddress { get; } = new();
        public ushort? Origin { get; private set; }
        public string? OutputPath { get; private set; }

        public void Run(bool collectLabels)
        {
            _collectLabels = collectLabels;
            _i = 0;
            while (!Is(TokenKind.Eof))
            {
                if (Is(TokenKind.Eol))
                {
                    _i++;
                    continue;
                }

                if (Is(TokenKind.Identifier) && PeekKind(1) == TokenKind.Colon)
                {
                    var label = Current().Text;
                    if (!_pcSet)
                    {
                        throw Error($"Label '{label}' defined before .org", Current());
                    }

                    if (collectLabels)
                    {
                        Symbols[label] = _pc;
                    }

                    _i += 2;
                    continue;
                }

                // Constant assignment: SYMBOL = expression
                if (Is(TokenKind.Identifier) && PeekKind(1) == TokenKind.Equals)
                {
                    var sym = Current().Text;
                    _i += 2; // skip identifier and '='
                    var value = (ushort)EvalExpression();
                    if (collectLabels)
                    {
                        Symbols[sym] = value;
                    }
                    ConsumeToEol();
                    continue;
                }

                // Dot-prefixed label definition: .label:
                if (Is(TokenKind.Dot) && PeekKind(1) == TokenKind.Identifier && PeekKind(2) == TokenKind.Colon)
                {
                    _i++; // skip dot
                    var label = Current().Text;
                    if (!_pcSet)
                    {
                        throw Error($"Label '{label}' defined before .org", Current());
                    }

                    if (collectLabels)
                    {
                        Symbols[label] = _pc;
                    }

                    _i += 2; // skip identifier and colon
                    continue;
                }

                if (Is(TokenKind.Dot))
                {
                    ParseDirective(collectLabels);
                    continue;
                }

                if (Is(TokenKind.Identifier))
                {
                    ParseInstruction(collectLabels);
                    continue;
                }

                throw Error($"Unexpected token '{Current().Text}'", Current());
            }
        }

        private void ParseDirective(bool collectLabels)
        {
            Expect(TokenKind.Dot);
            var nameTok = Expect(TokenKind.Identifier);
            var name = nameTok.Text;

            switch (name.ToLowerInvariant())
            {
                case "org":
                {
                    var value = (ushort)EvalExpression();
                    _pc = value;
                    _pcSet = true;
                    Origin ??= value;
                    ConsumeToEol();
                    break;
                }
                case "byte":
                {
                    EnsurePcSet(nameTok);
                    var first = true;
                    while (!Is(TokenKind.Eol) && !Is(TokenKind.Eof))
                    {
                        if (!first)
                        {
                            Expect(TokenKind.Comma);
                        }

                        var v = EvalExpression();
                        if (!collectLabels)
                        {
                            EmitByte(checked((byte)v));
                        }
                        else
                        {
                            _pc++;
                        }
                        first = false;
                    }
                    ConsumeToEol();
                    break;
                }
                case "word":
                {
                    EnsurePcSet(nameTok);
                    var first = true;
                    while (!Is(TokenKind.Eol) && !Is(TokenKind.Eof))
                    {
                        if (!first)
                        {
                            Expect(TokenKind.Comma);
                        }

                        var v = EvalExpression();
                        if (!collectLabels)
                        {
                            EmitWord(checked((ushort)v));
                        }
                        else
                        {
                            _pc += 2;
                        }
                        first = false;
                    }
                    ConsumeToEol();
                    break;
                }
                case "text":
                {
                    EnsurePcSet(nameTok);
                    var strTok = Expect(TokenKind.String);
                    var bytes = Encoding.ASCII.GetBytes(strTok.Text);
                    if (!collectLabels)
                    {
                        foreach (var b in bytes)
                        {
                            EmitByte(b);
                        }
                    }
                    else
                    {
                        _pc = (ushort)(_pc + bytes.Length);
                    }
                    ConsumeToEol();
                    break;
                }
                case "output":
                {
                    var strTok = Expect(TokenKind.String);
                    OutputPath = strTok.Text;
                    ConsumeToEol();
                    break;
                }
                case "include":
                    ConsumeToEol();
                    throw Error(".include was not resolved — check the file path", nameTok);
                default:
                    throw Error($"Unknown directive .{name}", nameTok);
            }
        }

        private void ParseInstruction(bool collectLabels)
        {
            EnsurePcSet(Current());
            var mnemonicTok = Expect(TokenKind.Identifier);
            var mnemonic = mnemonicTok.Text;

            // Implied — no operand on line
            if (Is(TokenKind.Eol) || Is(TokenKind.Eof))
            {
                EmitOp(collectLabels, mnemonic, AddressingMode.Implied, operand: null);
                ConsumeToEol();
                return;
            }

            AddressingMode mode;
            int operand;

            // #imm
            if (TryConsume(TokenKind.Hash))
            {
                mode = AddressingMode.Immediate;
                operand = EvalExpression();
            }
            // Accumulator — "ASL A", "ROL A", etc.
            else if (Is(TokenKind.Identifier) &&
                     string.Equals(Current().Text, "A", StringComparison.OrdinalIgnoreCase) &&
                     (PeekKind(1) == TokenKind.Eol || PeekKind(1) == TokenKind.Eof))
            {
                _i++;
                EmitOp(collectLabels, mnemonic, AddressingMode.Accumulator, operand: null);
                ConsumeToEol();
                return;
            }
            // ( ... ) — indirect modes
            else if (TryConsume(TokenKind.LParen))
            {
                operand = EvalExpression();

                if (TryConsume(TokenKind.Comma))
                {
                    // (expr,X)
                    var reg = Expect(TokenKind.Identifier);
                    if (!string.Equals(reg.Text, "X", StringComparison.OrdinalIgnoreCase))
                        throw Error("Expected X register", reg);
                    Expect(TokenKind.RParen);
                    mode = AddressingMode.IndirectX;
                }
                else
                {
                    Expect(TokenKind.RParen);
                    if (TryConsume(TokenKind.Comma))
                    {
                        // (expr),Y
                        var reg = Expect(TokenKind.Identifier);
                        if (!string.Equals(reg.Text, "Y", StringComparison.OrdinalIgnoreCase))
                            throw Error("Expected Y register", reg);
                        mode = AddressingMode.IndirectY;
                    }
                    else if (operand <= 0xFF)
                    {
                        // (zp) — 65C02 zero-page indirect
                        mode = AddressingMode.ZeroPageIndirect;
                    }
                    else
                    {
                        // (abs) — JMP indirect
                        mode = AddressingMode.Indirect;
                    }
                }
            }
            else
            {
                // expr or expr,X or expr,Y
                operand = EvalExpression();

                if (TryConsume(TokenKind.Comma))
                {
                    var reg = Expect(TokenKind.Identifier);
                    if (string.Equals(reg.Text, "X", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = operand <= 0xFF ? AddressingMode.ZeroPageX : AddressingMode.AbsoluteX;
                    }
                    else if (string.Equals(reg.Text, "Y", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = operand <= 0xFF ? AddressingMode.ZeroPageY : AddressingMode.AbsoluteY;
                    }
                    else
                    {
                        throw Error("Expected X or Y register", reg);
                    }
                }
                else
                {
                    mode = operand <= 0xFF ? AddressingMode.ZeroPage : AddressingMode.Absolute;
                }
            }

            // Override mode for branch instructions
            if (OpcodeTable.IsBranch(mnemonic))
            {
                mode = AddressingMode.Relative;
            }

            EmitOp(collectLabels, mnemonic, mode, operand);
            ConsumeToEol();
        }

        private void EmitOp(bool collectLabels, string mnemonic, AddressingMode mode, int? operand)
        {
            if (!OpcodeTable.TryGet(mnemonic, mode, out var enc))
            {
                throw Error($"Unsupported instruction '{mnemonic}' mode {mode}", Current());
            }

            if (collectLabels)
            {
                _pc = (ushort)(_pc + enc.Size);
                return;
            }

            EmitByte(enc.Opcode);
            if (enc.Size == 1)
            {
                return;
            }

            if (operand is null)
            {
                throw Error($"Missing operand for '{mnemonic}'", Current());
            }

            if (mode is AddressingMode.Immediate or AddressingMode.ZeroPage
                    or AddressingMode.ZeroPageX or AddressingMode.ZeroPageY
                    or AddressingMode.IndirectX or AddressingMode.IndirectY
                    or AddressingMode.ZeroPageIndirect)
            {
                EmitByte(unchecked((byte)operand.Value));
                return;
            }

            if (mode == AddressingMode.Relative)
            {
                var target = operand.Value;
                var nextPc = _pc + 1;
                var delta = target - nextPc;
                if (delta is < -128 or > 127)
                {
                    throw Error("Branch target out of range", Current());
                }

                EmitByte(unchecked((byte)(sbyte)delta));
                return;
            }

            EmitWord(checked((ushort)operand.Value));
        }

        private int EvalExpression()
        {
            var value = EvalTerm();
            while (Is(TokenKind.Plus) || Is(TokenKind.Minus))
            {
                var op = Current().Kind;
                _i++;
                var rhs = EvalTerm();
                value = op == TokenKind.Plus ? value + rhs : value - rhs;
            }
            return value;
        }

        private int EvalTerm()
        {
            if (Is(TokenKind.Number))
            {
                var t = Current();
                _i++;
                if (!NumberParser.TryParse(t.Text, out var v))
                {
                    throw Error($"Invalid number '{t.Text}'", t);
                }
                return v;
            }

            if (Is(TokenKind.Char))
            {
                var t = Current();
                _i++;
                var ch = t.Text.Length > 0 ? t.Text[0] : '\0';
                return (byte)ch;
            }

            if (Is(TokenKind.Identifier))
            {
                var t = Current();
                _i++;
                if (!Symbols.TryGetValue(t.Text, out var v))
                {
                    if (_collectLabels)
                        return 0x100; // forward reference — assume Absolute sizing
                    throw Error($"Undefined symbol '{t.Text}'", t);
                }
                return v;
            }

            // Dot-prefixed label reference: .label
            if (Is(TokenKind.Dot) && PeekKind(1) == TokenKind.Identifier)
            {
                _i++; // skip dot
                var t = Current();
                _i++;
                if (!Symbols.TryGetValue(t.Text, out var v))
                {
                    if (_collectLabels)
                        return 0x100; // forward reference — assume Absolute sizing
                    throw Error($"Undefined symbol '.{t.Text}'", t);
                }
                return v;
            }

            if (TryConsume(TokenKind.LParen))
            {
                var v = EvalExpression();
                Expect(TokenKind.RParen);
                return v;
            }

            throw Error("Expected expression", Current());
        }

        private void EmitByte(byte b)
        {
            BytesByAddress[_pc] = b;
            _pc++;
        }

        private void EmitWord(ushort w)
        {
            EmitByte((byte)(w & 0xFF));
            EmitByte((byte)((w >> 8) & 0xFF));
        }

        private void EnsurePcSet(Token at)
        {
            if (!_pcSet)
            {
                throw Error("Missing .org before code/data", at);
            }
        }

        private void ConsumeToEol()
        {
            while (!Is(TokenKind.Eol) && !Is(TokenKind.Eof))
            {
                _i++;
            }
            if (Is(TokenKind.Eol))
            {
                _i++;
            }
        }

        private Token Current() => _tokens[Math.Min(_i, _tokens.Count - 1)];
        private bool Is(TokenKind kind) => Current().Kind == kind;
        private TokenKind PeekKind(int offset) => (_i + offset) < _tokens.Count ? _tokens[_i + offset].Kind : TokenKind.Eof;

        /// <summary>Formats a location string as "file(line,col)" or "line:col".</summary>
        private string Loc(Token t)
        {
            var (file, mappedLine) = _sourceMap?.Lookup(t.Line) ?? (null, t.Line);
            var displayFile = file ?? _fileName;
            return displayFile is not null
                ? $"{Path.GetFileName(displayFile)}({mappedLine},{t.Column})"
                : $"{mappedLine}:{t.Column}";
        }

        private AssemblerException Error(string message, Token t)
        {
            var (file, mappedLine) = _sourceMap?.Lookup(t.Line) ?? (null, t.Line);
            return new($"{message} at {Loc(t)}.", file ?? _fileName, mappedLine);
        }

        private bool TryConsume(TokenKind kind)
        {
            if (Is(kind))
            {
                _i++;
                return true;
            }
            return false;
        }

        private Token Expect(TokenKind kind)
        {
            if (!Is(kind))
            {
                throw Error($"Expected {kind}", Current());
            }

            var t = Current();
            _i++;
            return t;
        }
    }
}
