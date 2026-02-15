namespace NB65c02Asm;

internal static class PrgWriter
{
    public static byte[] Write(ushort loadAddress, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[payload.Length + 2];
        bytes[0] = (byte)(loadAddress & 0xFF);
        bytes[1] = (byte)((loadAddress >> 8) & 0xFF);
        payload.CopyTo(bytes.AsSpan(2));
        return bytes;
    }
}
