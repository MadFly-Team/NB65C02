namespace NB65c02Asm.Assembler;

internal enum TokenKind
{
    Identifier,
    Number,
    String,
    Char,
    Colon,
    Comma,
    Hash,
    LParen,
    RParen,
    Plus,
    Minus,
    Dot,
    Equals,
    Eol,
    Eof,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Line, int Column);
