using System.Text;

namespace NB65c02Asm.Tools;

internal static class HexDump
{
    public static string Dump(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i != 0)
            {
                sb.Append(i % 16 == 0 ? "\n" : " ");
            }

            sb.Append(data[i].ToString("X2"));
        }

        return sb.ToString();
    }
}
