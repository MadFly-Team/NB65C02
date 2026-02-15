using System.Text;

namespace NB65c02Asm.Disk;

internal sealed class D64Image
{
    private const int SectorsPerTrackDefault = 21;
    private static readonly int[] SectorsPerTrack =
    [
        // Tracks 1-17: 21 sectors
        ..Enumerable.Repeat(21, 17),
        // Tracks 18-24: 19 sectors
        ..Enumerable.Repeat(19, 7),
        // Tracks 25-30: 18 sectors
        ..Enumerable.Repeat(18, 6),
        // Tracks 31-35: 17 sectors
        ..Enumerable.Repeat(17, 5),
    ];

    private readonly byte[] _image;
    private readonly List<DirEntry> _entries = [];
    private readonly bool[,] _allocated;

    private D64Image(byte[] image)
    {
        _image = image;
        _allocated = new bool[35, 21];
        MarkSystemSectorsAllocated();
    }

    public static D64Image CreateBlank(string diskName)
    {
        if (string.IsNullOrWhiteSpace(diskName))
        {
            throw new ArgumentException("Disk name is required.", nameof(diskName));
        }

        var totalSectors = SectorsPerTrack.Sum();
        var image = new byte[totalSectors * 256];
        var d64 = new D64Image(image);
        d64.InitializeBamAndHeader(diskName);
        return d64;
    }

    public void AddPrg(string fileName, ReadOnlySpan<byte> prgBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        fileName = SanitizePetsciiName(fileName, maxLen: 16);

        var neededSectors = (prgBytes.Length + 253) / 254;
        var chain = AllocateSectors(neededSectors);

        var offset = 0;
        for (var i = 0; i < chain.Count; i++)
        {
            var (track, sector) = chain[i];
            var next = i == chain.Count - 1 ? (0, 0) : chain[i + 1];

            var sectorSpan = GetSectorSpan(track, sector);
            sectorSpan[0] = (byte)next.Item1;
            sectorSpan[1] = (byte)next.Item2;

            var payloadLen = Math.Min(254, prgBytes.Length - offset);
            prgBytes.Slice(offset, payloadLen).CopyTo(sectorSpan[2..(2 + payloadLen)]);

            if (i == chain.Count - 1)
            {
                sectorSpan[1] = (byte)(payloadLen + 1);
            }

            offset += payloadLen;
        }

        var entry = new DirEntry
        {
            FileType = 0x82,
            StartTrack = (byte)chain[0].Track,
            StartSector = (byte)chain[0].Sector,
            FileNamePetscii = EncodePetsciiPadded(fileName, 16),
            SizeInSectors = (ushort)neededSectors,
        };

        _entries.Add(entry);
        FlushDirectory();
        FlushBam();
    }

    public byte[] GetBytes() => _image;

    private void InitializeBamAndHeader(string diskName)
    {
        var bam = GetSectorSpan(track: 18, sector: 0);
        bam.Clear();

        bam[0] = 18;
        bam[1] = 1;
        bam[2] = 0x41;

        // Disk name at 0x90 (144) for 16 bytes.
        EncodePetsciiPadded(SanitizePetsciiName(diskName, 16), 16).CopyTo(bam[0x90..0xA0]);

        // Disk id (2 bytes) and DOS type (2 bytes)
        bam[0xA2] = (byte)'0';
        bam[0xA3] = (byte)'0';
        bam[0xA5] = (byte)'2';
        bam[0xA6] = (byte)'A';

        FlushBam();
        FlushDirectory();
    }

    private void FlushDirectory()
    {
        // Single directory sector at 18/1 for now (8 entries max).
        var dir = GetSectorSpan(track: 18, sector: 1);
        dir.Clear();

        dir[0] = 0;
        dir[1] = 0;

        for (var i = 0; i < Math.Min(_entries.Count, 8); i++)
        {
            var entrySpan = dir.Slice(2 + (i * 32), 32);
            _entries[i].WriteTo(entrySpan);
        }
    }

    private void FlushBam()
    {
        var bam = GetSectorSpan(track: 18, sector: 0);

        // BAM entries start at 0x04, 4 bytes per track.
        for (var track = 1; track <= 35; track++)
        {
            var sectors = SectorsPerTrack[track - 1];
            var freeCount = 0;
            var map = new byte[3];

            for (var sector = 0; sector < sectors; sector++)
            {
                if (!_allocated[track - 1, sector])
                {
                    freeCount++;
                    var bitIndex = sector;
                    map[bitIndex / 8] |= (byte)(1 << (bitIndex % 8));
                }
            }

            var offset = 0x04 + ((track - 1) * 4);
            bam[offset] = (byte)freeCount;
            bam[offset + 1] = map[0];
            bam[offset + 2] = map[1];
            bam[offset + 3] = map[2];
        }
    }

    private void MarkSystemSectorsAllocated()
    {
        // Directory and BAM.
        Allocate(track: 18, sector: 0);
        Allocate(track: 18, sector: 1);
    }

    private List<(int Track, int Sector)> AllocateSectors(int count)
    {
        var result = new List<(int Track, int Sector)>(count);

        for (var track = 1; track <= 35 && result.Count < count; track++)
        {
            if (track == 18)
            {
                continue;
            }

            var sectors = SectorsPerTrack[track - 1];
            for (var sector = 0; sector < sectors && result.Count < count; sector++)
            {
                if (_allocated[track - 1, sector])
                {
                    continue;
                }

                Allocate(track, sector);
                result.Add((track, sector));
            }
        }

        if (result.Count != count)
        {
            throw new InvalidOperationException("Disk full.");
        }

        return result;
    }

    private void Allocate(int track, int sector)
    {
        if (track is < 1 or > 35)
        {
            throw new ArgumentOutOfRangeException(nameof(track));
        }

        var max = SectorsPerTrack[track - 1];
        if (sector < 0 || sector >= max)
        {
            throw new ArgumentOutOfRangeException(nameof(sector));
        }

        _allocated[track - 1, sector] = true;
    }

    private Span<byte> GetSectorSpan(int track, int sector)
    {
        var offset = SectorOffset(track, sector);
        return _image.AsSpan(offset, 256);
    }

    private static int SectorOffset(int track, int sector)
    {
        var t = track - 1;
        if (t is < 0 or >= 35)
        {
            throw new ArgumentOutOfRangeException(nameof(track));
        }

        var max = SectorsPerTrack[t];
        if (sector < 0 || sector >= max)
        {
            throw new ArgumentOutOfRangeException(nameof(sector));
        }

        var sectorIndex = 0;
        for (var i = 0; i < t; i++)
        {
            sectorIndex += SectorsPerTrack[i];
        }
        sectorIndex += sector;
        return sectorIndex * 256;
    }

    private static string SanitizePetsciiName(string name, int maxLen)
    {
        name = name.Trim();
        if (name.Length > maxLen)
        {
            name = name[..maxLen];
        }

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(ch is >= ' ' and <= '~' ? ch : '_');
        }
        return sb.ToString();
    }

    private static byte[] EncodePetsciiPadded(string text, int len)
    {
        // Simplified: ASCII subset, padded with 0xA0.
        var bytes = Enumerable.Repeat((byte)0xA0, len).ToArray();
        var src = Encoding.ASCII.GetBytes(text.ToUpperInvariant());
        Array.Copy(src, bytes, Math.Min(len, src.Length));
        return bytes;
    }

    private readonly record struct DirEntry
    {
        public byte FileType { get; init; }
        public byte StartTrack { get; init; }
        public byte StartSector { get; init; }
        public byte[] FileNamePetscii { get; init; }
        public ushort SizeInSectors { get; init; }

        public void WriteTo(Span<byte> dest)
        {
            dest.Clear();
            dest[0] = FileType;
            dest[1] = StartTrack;
            dest[2] = StartSector;
            FileNamePetscii.AsSpan().CopyTo(dest[3..19]);
            dest[30] = (byte)(SizeInSectors & 0xFF);
            dest[31] = (byte)((SizeInSectors >> 8) & 0xFF);
        }
    }
}
