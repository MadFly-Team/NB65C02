using System.Buffers.Binary;
using System.Text;

namespace NB65c02Asm.Disk;

internal static class DfsTemplatePatcher
{
    // Patches a DFS 1.2-created template SSD:
    // - sector0 name entries at 0x08 (7 chars + dir/lock byte, 8 bytes each)
    // - sector1 byte 0x05 = fileCount * 8
    // - sector1 info entries at 0x08, 8 bytes each:
    //   load(2 LE) | exec(2 LE) | len(2 LE) | packed(1) | startLow(1)
    //   packed byte: bits 7-6 = execHigh, 5-4 = lenHigh, 3-2 = loadHigh, 1-0 = startHigh
    public static byte[] PatchHello(byte[] templateSsd, ReadOnlySpan<byte> payload, uint loadAddress, uint execAddress)
    {
        ArgumentNullException.ThrowIfNull(templateSsd);
        if (templateSsd.Length != DfsSsdImage.ImageSize)
        {
            throw new ArgumentException($"Template SSD must be {DfsSsdImage.ImageSize} bytes (got {templateSsd.Length}). Make sure you are using a full .ssd image exported from BeebEm/DFS, not a truncated file.", nameof(templateSsd));
        }

        var img = (byte[])templateSsd.Clone();

        var cat0 = img.AsSpan(0, DfsSsdImage.SectorSize);
        var cat1 = img.AsSpan(DfsSsdImage.SectorSize, DfsSsdImage.SectorSize);

        var fileCount = cat1[0x05] / 8;
        if (fileCount == 0 || fileCount > 31)
        {
            throw new InvalidOperationException("Template SSD has invalid file count.");
        }

        var helloIndex = FindEntryIndex(cat0, fileCount, "HELLO");
        if (helloIndex < 0)
        {
            throw new InvalidOperationException("Template SSD does not contain HELLO entry.");
        }

        var infoOff = 0x08 + (helloIndex * 8);
        var packed = cat1[infoOff + 6];
        var startLow = cat1[infoOff + 7];
        var startHigh = packed & 0x03;
        var start = (ushort)(startLow | (startHigh << 8));
        if (start < 2 || start >= DfsSsdImage.TotalSectors)
        {
            throw new InvalidOperationException($"Template HELLO start sector out of range: {start}.");
        }

        var sectorsNeeded = (payload.Length + (DfsSsdImage.SectorSize - 1)) / DfsSsdImage.SectorSize;
        var end = start + (ushort)sectorsNeeded;
        if (end > DfsSsdImage.TotalSectors)
        {
            throw new InvalidOperationException("Payload does not fit on disk at template start sector.");
        }

        // Write payload into the existing allocated region.
        var offset = start * DfsSsdImage.SectorSize;
        payload.CopyTo(img.AsSpan(offset));

        // Zero-fill remainder of last sector to keep deterministic.
        var bytesWritten = sectorsNeeded * DfsSsdImage.SectorSize;
        if (payload.Length < bytesWritten)
        {
            img.AsSpan(offset + payload.Length, bytesWritten - payload.Length).Clear();
        }

        // Update HELLO catalog info – low 16 bits of load, exec, length.
        BinaryPrimitives.WriteUInt16LittleEndian(cat1.Slice(infoOff + 0, 2), (ushort)(loadAddress & 0xFFFF));
        BinaryPrimitives.WriteUInt16LittleEndian(cat1.Slice(infoOff + 2, 2), (ushort)(execAddress & 0xFFFF));
        BinaryPrimitives.WriteUInt16LittleEndian(cat1.Slice(infoOff + 4, 2), (ushort)(payload.Length & 0xFFFF));

        // Re-pack byte 6: bits 7-6 = exec high, 5-4 = len high, 3-2 = load high, 1-0 = start high.
        var execHigh = (execAddress >> 16) & 0x03;
        var lenHigh = ((uint)payload.Length >> 16) & 0x03;
        var loadHigh = (loadAddress >> 16) & 0x03;
        cat1[infoOff + 6] = (byte)((execHigh << 6) | (lenHigh << 4) | (loadHigh << 2) | startHigh);
        // byte 7 (start sector low) unchanged.

        return img;
    }

    private static int FindEntryIndex(ReadOnlySpan<byte> cat0, int fileCount, string name)
    {
        var target = name.Trim().ToUpperInvariant();
        for (var i = 0; i < fileCount; i++)
        {
            var off = 0x08 + (i * 8);
            var entryName = Encoding.ASCII.GetString(cat0.Slice(off, 7)).TrimEnd(' ', '\0');
            if (string.Equals(entryName, target, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
