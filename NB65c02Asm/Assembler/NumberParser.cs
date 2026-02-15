using System.Globalization;

namespace NB65c02Asm.Assembler;

internal static class NumberParser
{
    public static ushort ParseU16(string text)
    {
        if (!TryParse(text, out var value))
        {
            throw new AssemblerException($"Invalid number '{text}'.");
        }

        if (value is < 0 or > 0xFFFF)
        {
            throw new AssemblerException($"Number out of range '{text}'.");
        }

        return (ushort)value;
    }

    public static bool TryParse(string text, out int value)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();

        if (text.StartsWith('$'))
        {
            return int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        if (text.StartsWith('%'))
        {
            value = 0;
            foreach (var ch in text[1..])
            {
                if (ch is not ('0' or '1'))
                {
                    return false;
                }

                value = (value << 1) | (ch - '0');
            }

            return true;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
