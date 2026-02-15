namespace NB65c02Asm.Bbc;

internal static class BbcExecutable
{
    public static (byte[] Data, uint LoadAddress, uint ExecAddress) Wrap(RawBinary input, uint execAddress)
    {
        ArgumentNullException.ThrowIfNull(input);

        // DFS stores load/exec addresses as metadata; the file contents are the raw binary.
        // We'll treat the assembled bytes as the file data.
        return (input.Bytes, input.LoadAddress, execAddress);
    }
}

internal sealed class RawBinary
{
    public RawBinary(byte[] bytes, uint loadAddress)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        LoadAddress = loadAddress;
    }

    public byte[] Bytes { get; }
    public uint LoadAddress { get; }
}
