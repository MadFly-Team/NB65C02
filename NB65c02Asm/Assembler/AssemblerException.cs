namespace NB65c02Asm.Assembler;

internal sealed class AssemblerException : Exception
{
    /// <summary>Source file name (may be null for in-memory sources).</summary>
    public string? FileName { get; }

    /// <summary>One-based line number in the source, or 0 if unknown.</summary>
    public int Line { get; }

    public AssemblerException(string message) : base(message)
    {
    }

    public AssemblerException(string message, string? fileName, int line)
        : base(message)
    {
        FileName = fileName;
        Line = line;
    }
}
