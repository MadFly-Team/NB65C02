namespace NB65c02Asm.Disk;

internal enum DfsDsdOrdering
{
    Side0ThenSide1,
    TrackInterleaved,
}

internal sealed class DfsDsdImage
{
    private readonly byte[] _image;
    private readonly int _tracks;
    private readonly int _sectorsPerTrack;
    private readonly int _sectorSize;
    private readonly DfsDsdOrdering _ordering;

    public DfsDsdImage(byte[] image, int tracks, int sectorsPerTrack, int sectorSize, DfsDsdOrdering ordering)
    {
        _image = image;
        _tracks = tracks;
        _sectorsPerTrack = sectorsPerTrack;
        _sectorSize = sectorSize;
        _ordering = ordering;
    }

    public static DfsDsdImage CreateBlank(int tracks, int sectorsPerTrack, int sectorSize, DfsDsdOrdering ordering)
    {
        var sideSize = tracks * sectorsPerTrack * sectorSize;
        var bytes = new byte[sideSize * 2];
        return new DfsDsdImage(bytes, tracks, sectorsPerTrack, sectorSize, ordering);
    }

    public Span<byte> GetSideSpan(int side)
    {
        if (side is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (_ordering == DfsDsdOrdering.Side0ThenSide1)
        {
            var sideSize = _tracks * _sectorsPerTrack * _sectorSize;
            return _image.AsSpan(side * sideSize, sideSize);
        }

        // Track-interleaved: layout is track0 side0, track0 side1, track1 side0, track1 side1...
        // Return a contiguous view is not possible without copying, so we expose whole image and provide sector addressing.
        throw new InvalidOperationException("TrackInterleaved does not support contiguous side spans.");
    }

    public Span<byte> Sector(int side, int track, int sector)
    {
        if (side is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (track is < 0 || track >= _tracks)
        {
            throw new ArgumentOutOfRangeException(nameof(track));
        }

        if (sector is < 0 || sector >= _sectorsPerTrack)
        {
            throw new ArgumentOutOfRangeException(nameof(sector));
        }

        var offset = _ordering switch
        {
            DfsDsdOrdering.Side0ThenSide1 => (((side * _tracks + track) * _sectorsPerTrack) + sector) * _sectorSize,
            DfsDsdOrdering.TrackInterleaved => ((((track * 2) + side) * _sectorsPerTrack) + sector) * _sectorSize,
            _ => throw new InvalidOperationException("Unknown ordering.")
        };

        return _image.AsSpan(offset, _sectorSize);
    }

    public byte[] GetBytes() => _image;
}
