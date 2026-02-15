namespace NB65c02Asm.Assembler;

internal sealed class Lexer
{
    private readonly string _text;
    private readonly string? _fileName;
    private readonly SourceMap? _sourceMap;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public Lexer(string text, string? fileName = null, SourceMap? sourceMap = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _fileName = fileName;
        _sourceMap = sourceMap;
    }

    public Token NextToken()
    {
        while (_pos < _text.Length)
        {
            var ch = _text[_pos];
            if (ch is ' ' or '\t' or '\r')
            {
                Advance(ch);
                continue;
            }

            if (ch == ';')
            {
                while (_pos < _text.Length && _text[_pos] != '\n')
                {
                    Advance(_text[_pos]);
                }
                continue;
            }

            if (ch == '\n')
            {
                Advance(ch);
                return new Token(TokenKind.Eol, "\n", _line - 1, 1);
            }

            var startLine = _line;
            var startCol = _col;

            if (IsIdentStart(ch))
            {
                var start = _pos;
                Advance(ch);
                while (_pos < _text.Length && IsIdentPart(_text[_pos]))
                {
                    Advance(_text[_pos]);
                }

                return new Token(TokenKind.Identifier, _text[start.._pos], startLine, startCol);
            }

            if (ch is '"')
            {
                Advance(ch);
                var start = _pos;
                while (_pos < _text.Length && _text[_pos] != '"')
                {
                    if (_text[_pos] == '\n')
                        {
                            throw Error("Unterminated string", startLine, startCol);
                        }
                        Advance(_text[_pos]);
                    }

                    if (_pos >= _text.Length)
                    {
                        throw Error("Unterminated string", startLine, startCol);
                    }

                var value = _text[start.._pos];
                Advance('"');
                return new Token(TokenKind.String, value, startLine, startCol);
            }

            if (ch is '\'')
            {
                Advance(ch);
                if (_pos >= _text.Length)
                {
                    throw Error("Unterminated character literal", startLine, startCol);
                }

                char value;
                if (_text[_pos] == '\\')
                {
                    Advance('\\');
                    if (_pos >= _text.Length)
                    {
                        throw Error("Unterminated character literal", startLine, startCol);
                    }

                    value = _text[_pos] switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '\'' => '\'',
                        _ => _text[_pos]
                    };
                    Advance(_text[_pos]);
                }
                else
                {
                    value = _text[_pos];
                    if (value == '\n')
                    {
                        throw Error("Unterminated character literal", startLine, startCol);
                    }
                    Advance(_text[_pos]);
                }

                if (_pos >= _text.Length || _text[_pos] != '\'')
                {
                    throw Error("Unterminated character literal", startLine, startCol);
                }

                Advance('\'');
                return new Token(TokenKind.Char, value.ToString(), startLine, startCol);
            }

            if (IsNumberStart(ch))
            {
                var start = _pos;
                Advance(ch);
                while (_pos < _text.Length && IsNumberPart(_text[_pos]))
                {
                    Advance(_text[_pos]);
                }

                return new Token(TokenKind.Number, _text[start.._pos], startLine, startCol);
            }

            Advance(ch);
            return ch switch
            {
                ':' => new Token(TokenKind.Colon, ":", startLine, startCol),
                ',' => new Token(TokenKind.Comma, ",", startLine, startCol),
                '#' => new Token(TokenKind.Hash, "#", startLine, startCol),
                '(' => new Token(TokenKind.LParen, "(", startLine, startCol),
                ')' => new Token(TokenKind.RParen, ")", startLine, startCol),
                '+' => new Token(TokenKind.Plus, "+", startLine, startCol),
                '-' => new Token(TokenKind.Minus, "-", startLine, startCol),
                '.' => new Token(TokenKind.Dot, ".", startLine, startCol),
                '=' => new Token(TokenKind.Equals, "=", startLine, startCol),
                _ => throw Error($"Unexpected character '{ch}'", startLine, startCol)
            };
        }

        return new Token(TokenKind.Eof, string.Empty, _line, _col);
    }

    private void Advance(char ch)
    {
        _pos++;
        if (ch == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
    }

    private static bool IsIdentStart(char ch) => char.IsLetter(ch) || ch is '_' || ch == '@';
    private static bool IsIdentPart(char ch) => char.IsLetterOrDigit(ch) || ch is '_' || ch == '.';
    private static bool IsNumberStart(char ch) => char.IsDigit(ch) || ch is '$' || ch is '%';
    private static bool IsNumberPart(char ch) => char.IsLetterOrDigit(ch) || ch is '$' || ch is '%';

    /// <summary>Formats a location string as "file(line,col)" or "line:col".</summary>
    private string Loc(int line, int col)
    {
        var (file, mappedLine) = _sourceMap?.Lookup(line) ?? (null, line);
        var displayFile = file ?? _fileName;
        return displayFile is not null
            ? $"{Path.GetFileName(displayFile)}({mappedLine},{col})"
            : $"{mappedLine}:{col}";
    }

    private AssemblerException Error(string message, int line, int col)
    {
        var (file, mappedLine) = _sourceMap?.Lookup(line) ?? (null, line);
        return new($"{message} at {Loc(line, col)}.", file ?? _fileName, mappedLine);
    }
}
