namespace NB65c02Asm.Assembler;

/// <summary>
/// Maps expanded source line numbers (after <c>.include</c> inlining and
/// multi-file concatenation) back to the original file name and line number.
/// Each entry corresponds to one output line (1-based indexing).
/// </summary>
internal sealed class SourceMap
{
    private readonly List<(string? FileName, int OriginalLine)> _entries = [];

    /// <summary>Records that the next output line came from the given file and line.</summary>
    public void Add(string? fileName, int originalLine) =>
        _entries.Add((fileName, originalLine));

    /// <summary>
    /// Looks up the original file and line for an expanded line number (1-based).
    /// Returns the original values if the line is not in the map.
    /// </summary>
    public (string? FileName, int Line) Lookup(int expandedLine)
    {
        var index = expandedLine - 1;
        if (index >= 0 && index < _entries.Count)
            return _entries[index];
        return (null, expandedLine);
    }
}
