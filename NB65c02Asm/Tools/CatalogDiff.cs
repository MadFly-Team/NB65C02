namespace NB65c02Asm.Tools;

internal static class CatalogDiff
{
    public static string Diff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int start, int length)
    {
        var end = Math.Min(start + length, Math.Min(a.Length, b.Length));
        if (start < 0 || start >= end)
        {
            return "<invalid range>";
        }

        var sb = new System.Text.StringBuilder();
        for (var i = start; i < end; i++)
        {
            var ba = a[i];
            var bb = b[i];
            if (ba != bb)
            {
                sb.AppendLine($"0x{i:X4}: {ba:X2} -> {bb:X2}");
            }
        }
        return sb.ToString();
    }
}
