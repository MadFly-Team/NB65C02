using System.Buffers.Binary;
using System.Text;

namespace NB65c02Asm.Disk;

internal static class DfsCatalogParser
{
    public static IReadOnlyList<DfsFileEntry> ParseSsd(ReadOnlySpan<byte> ssd)
    {
        if (ssd.Length < 512)
        {
            return [];
        }

        var cat0 = ssd[..256];
        var cat1 = ssd.Slice(256, 256);

        var fileCount = cat1[0x05] / 8;
        if (fileCount > 31)
        {
            fileCount = 31;
        }

        var list = new List<DfsFileEntry>(fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            var nameOff = 0x08 + (i * 8);
            var infoOff = 0x08 + (i * 8);

            var name = Encoding.ASCII.GetString(cat0.Slice(nameOff, 7)).TrimEnd(' ');
            var dirRaw = cat0[nameOff + 7];
            var locked = (dirRaw & 0x80) != 0;

            // DFS stores directory as ASCII character in bits 0-6 (root = '$').
            var dir = (char)(dirRaw & 0x7F);

            var packed = cat1[infoOff + 6];
            var load = (uint)(cat1[infoOff + 0] | (cat1[infoOff + 1] << 8) | (((packed >> 2) & 0x03) << 16));
            var exec = (uint)(cat1[infoOff + 2] | (cat1[infoOff + 3] << 8) | (((packed >> 6) & 0x03) << 16));
            var len = (uint)(cat1[infoOff + 4] | (cat1[infoOff + 5] << 8) | (((packed >> 4) & 0x03) << 16));
            var start = (ushort)(cat1[infoOff + 7] | ((packed & 0x03) << 8));

            list.Add(new DfsFileEntry(dir, name, locked, load, exec, len, start));
        }

        return list;
    }
}

internal readonly record struct DfsFileEntry(
    char Directory,
    string Name,
    bool Locked,
    uint LoadAddress,
    uint ExecAddress,
    uint Length,
    ushort StartSector);
