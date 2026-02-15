namespace NB65c02Asm.Disk;

internal static class DfsDsdBuilder
{
    public static byte[] CreateAutobootDsd(
        string diskTitleSide0,
        string diskTitleSide1,
        string directory,
        string bbcFileName,
        ReadOnlySpan<byte> bootFile,
        ReadOnlySpan<byte> payload,
        uint loadAddress,
        uint execAddress,
        int interleave = 2,
        DfsDsdOrdering ordering = DfsDsdOrdering.Side0ThenSide1)
    {
        var dsd = DfsDsdImage.CreateBlank(DfsSsdImage.Tracks, DfsSsdImage.SectorsPerTrack, DfsSsdImage.SectorSize, ordering);

        var side0 = BuildSide(diskTitleSide0, directory, bbcFileName, bootFile, payload, loadAddress, execAddress, interleave);
        var side1 = BuildSide(diskTitleSide1, directory, bbcFileName, bootFile, payload, loadAddress, execAddress, interleave);

        WriteSide(dsd, side: 0, side0);
        WriteSide(dsd, side: 1, side1);

        return dsd.GetBytes();
    }

    private static byte[] BuildSide(
        string title,
        string directory,
        string bbcFileName,
        ReadOnlySpan<byte> bootFile,
        ReadOnlySpan<byte> payload,
        uint loadAddress,
        uint execAddress,
        int interleave)
    {
        var side = new byte[DfsSsdImage.ImageSize];
        var ssd = DfsSsdImage.CreateOverExisting(side, title, interleave);
        ssd.AddFile("$", "!BOOT", bootFile, loadAddress: 0, execAddress: 0, locked: true);
        ssd.AddFile(directory, bbcFileName, payload, loadAddress, execAddress);
        ssd.Validate();
        return side;
    }

    private static void WriteSide(DfsDsdImage dsd, int side, byte[] ssdSideBytes)
    {
        // Copy the SSD side image into the DSD using the DSD's physical ordering.
        var bytesPerTrack = DfsSsdImage.SectorsPerTrack * DfsSsdImage.SectorSize;
        for (var track = 0; track < DfsSsdImage.Tracks; track++)
        {
            var srcTrack = ssdSideBytes.AsSpan(track * bytesPerTrack, bytesPerTrack);
            for (var s = 0; s < DfsSsdImage.SectorsPerTrack; s++)
            {
                srcTrack.Slice(s * DfsSsdImage.SectorSize, DfsSsdImage.SectorSize)
                    .CopyTo(dsd.Sector(side, track, s));
            }
        }
    }
}
