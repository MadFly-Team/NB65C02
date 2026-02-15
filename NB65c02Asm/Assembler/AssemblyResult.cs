namespace NB65c02Asm.Assembler;

internal sealed class AssemblyResult
{
    public ushort? Origin { get; set; }
    public string? OutputPath { get; set; }

    public required SortedDictionary<ushort, byte> BytesByAddress { get; init; }

    public byte[] GetContiguousBytes()
    {
        if (BytesByAddress.Count == 0)
        {
            return [];
        }

        var first = BytesByAddress.First().Key;
        var last = BytesByAddress.Last().Key;
        var length = last - first + 1;

        var bytes = new byte[length];
        foreach (var (addr, value) in BytesByAddress)
        {
            bytes[addr - first] = value;
        }

        return bytes;
    }
}
