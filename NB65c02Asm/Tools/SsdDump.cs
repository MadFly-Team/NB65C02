using System.Text;
using NB65c02Asm.Disk;

namespace NB65c02Asm.Tools;

internal static class SsdDump
{
    public static string DumpCatalog(ReadOnlySpan<byte> img)
    {
        // If this is a DSD (2 * 200KB) in side0-then-side1 ordering, dump both sides.
        if (img.Length == NB65c02Asm.Disk.DfsSsdImage.ImageSize * 2)
        {
            var sb2 = new StringBuilder();
            sb2.AppendLine("[DSD side0-then-side1]");
            sb2.AppendLine("[Side 0]");
            sb2.AppendLine(DumpCatalog(img[..NB65c02Asm.Disk.DfsSsdImage.ImageSize]));
            sb2.AppendLine("[Side 1]");
            sb2.AppendLine(DumpCatalog(img.Slice(NB65c02Asm.Disk.DfsSsdImage.ImageSize, NB65c02Asm.Disk.DfsSsdImage.ImageSize)));

            sb2.AppendLine("[DSD track-interleaved reconstruction]");
            var side0 = ReconstructInterleavedSide(img, side: 0);
            var side1 = ReconstructInterleavedSide(img, side: 1);
            sb2.AppendLine("[Side 0]");
            sb2.AppendLine(DumpCatalog(side0));
            sb2.AppendLine("[Side 1]");
            sb2.AppendLine(DumpCatalog(side1));

            return sb2.ToString();
        }

        if (img.Length < 512)
        {
            return "<too small>";
        }

        var cat0 = img[..256];
        var cat1 = img.Slice(256, 256);

        var title = Encoding.ASCII.GetString(cat0[..8]) + Encoding.ASCII.GetString(cat1[..4]);
        var fileCount = cat1[0x05] / 8;
        var cycle = cat1[0x04];
        var bootOption = (cat1[0x06] >> 4) & 0x03;
        var sectorCount = (cat1[0x07] | ((cat1[0x06] & 0x03) << 8));

        var sb = new StringBuilder();
        sb.AppendLine($"Title: '{title}'");
        sb.AppendLine($"FileCount: {fileCount}");
        sb.AppendLine($"Cycle: {cycle}");
        sb.AppendLine($"BootOption: {bootOption}");
        sb.AppendLine($"DiskSectors: {sectorCount}");

        var parsed = DfsCatalogParser.ParseSsd(img);
        sb.AppendLine("Parsed:");
        foreach (var e in parsed)
        {
            sb.AppendLine($"  {e.Directory}.{e.Name} load=&{e.LoadAddress:X} exec=&{e.ExecAddress:X} len={e.Length} start={e.StartSector} locked={e.Locked}");
        }

        for (var i = 0; i < fileCount; i++)
        {
            var base0 = 8 + (i * 8);
            var base1 = 0x08 + (i * 8);

            var name = Encoding.ASCII.GetString(cat0.Slice(base0, 7)).TrimEnd('\0', ' ');
            var dirRaw = cat0[base0 + 7];
            var dir = (char)(dirRaw & 0x7F);
            var locked = (dirRaw & 0x80) != 0;

            var packed = cat1[base1 + 6];
            var load = (uint)(cat1[base1 + 0] | (cat1[base1 + 1] << 8) | (((packed >> 2) & 0x03) << 16));
            var exec = (uint)(cat1[base1 + 2] | (cat1[base1 + 3] << 8) | (((packed >> 6) & 0x03) << 16));
            var len = (uint)(cat1[base1 + 4] | (cat1[base1 + 5] << 8) | (((packed >> 4) & 0x03) << 16));
            var start = (ushort)(cat1[base1 + 7] | ((packed & 0x03) << 8));

            sb.AppendLine($"{i}: dirRaw=0x{dirRaw:X2} {dir}.{name} locked={locked} load=&{load:X} exec=&{exec:X} len={len} start={start}");
        }

        sb.AppendLine("CAT0 (sector 0) first 64 bytes:");
        sb.AppendLine(Hex(img[..64]));
        sb.AppendLine("CAT1 (sector 1) first 64 bytes:");
        sb.AppendLine(Hex(img.Slice(256, 64)));

        return sb.ToString();
    }

    private static string Hex(ReadOnlySpan<byte> data)
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

    private static byte[] ReconstructInterleavedSide(ReadOnlySpan<byte> dsd, int side)
    {
        var sideBytes = new byte[NB65c02Asm.Disk.DfsSsdImage.ImageSize];
        var bytesPerTrack = NB65c02Asm.Disk.DfsSsdImage.SectorsPerTrack * NB65c02Asm.Disk.DfsSsdImage.SectorSize;
        for (var track = 0; track < NB65c02Asm.Disk.DfsSsdImage.Tracks; track++)
        {
            for (var sector = 0; sector < NB65c02Asm.Disk.DfsSsdImage.SectorsPerTrack; sector++)
            {
                var srcOffset = ((((track * 2) + side) * NB65c02Asm.Disk.DfsSsdImage.SectorsPerTrack) + sector) * NB65c02Asm.Disk.DfsSsdImage.SectorSize;
                var dstOffset = (track * bytesPerTrack) + (sector * NB65c02Asm.Disk.DfsSsdImage.SectorSize);
                dsd.Slice(srcOffset, NB65c02Asm.Disk.DfsSsdImage.SectorSize).CopyTo(sideBytes.AsSpan(dstOffset));
            }
        }
        return sideBytes;
    }
}
