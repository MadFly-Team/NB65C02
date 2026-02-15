using System.Text;

namespace NB65c02Asm.Disk;

internal sealed class DfsSsdImage
{
    public const int Tracks          = 80;
    public const int SectorsPerTrack = 10;
    public const int SectorSize      = 256;
    public const int TotalSectors    = Tracks * SectorsPerTrack;
    public const int ImageSize       = TotalSectors * SectorSize;

    private readonly byte[ ] _image;
    private int      _nextFreeSector;
    private readonly List<Entry> _entries = [ ];
    private readonly int         _interleave;

    private DfsSsdImage( byte[ ] image, int interleave )
    {
        _image          = image;
        _nextFreeSector = 2; // sectors 0-1 reserved for catalog
        _interleave     = interleave;
    }

    public static DfsSsdImage CreateOverExisting( byte[ ] image, string title, int interleave = 2 )
    {
        ArgumentNullException.ThrowIfNull( image );
        if ( image.Length != ImageSize )
        {
            throw new ArgumentException( $"Image must be exactly {ImageSize} bytes.", nameof( image ) );
        }

        var ssd = new DfsSsdImage( image, interleave );
        ssd.WriteEmptyCatalog( title );
        return ssd;
    }

    public static DfsSsdImage CreateBlank( string title, int interleave = 2 )
    {
        if ( string.IsNullOrWhiteSpace( title ) )
        {
            throw new ArgumentException( "Disk title is required.", nameof( title ) );
        }

        if ( interleave is<1 or> SectorsPerTrack )
        {
            throw new ArgumentOutOfRangeException( nameof( interleave ) );
        }

        var image = new byte[ ImageSize ];
        var ssd   = new DfsSsdImage( image, interleave );
        ssd.WriteEmptyCatalog( title );
        return ssd;
    }

    public void AddFile( string directory, string name, ReadOnlySpan<byte> data, uint loadAddress, uint execAddress, bool locked = false )
    {
        if ( string.IsNullOrWhiteSpace( directory ) )
        {
            throw new ArgumentException( "Directory is required.", nameof( directory ) );
        }

        if ( string.IsNullOrWhiteSpace( name ) )
        {
            throw new ArgumentException( "Name is required.", nameof( name ) );
        }

        directory = directory.Trim().ToUpperInvariant();
        name      = name.Trim().ToUpperInvariant();

        if ( directory.Length == 0 )
        {
            throw new ArgumentException( "Directory is required.", nameof( directory ) );
        }

        if ( directory.Length != 1 || ( ( directory[ 0 ] is < 'A' or > 'Z' ) && directory[ 0 ] != '$' ) )
        {
            throw new ArgumentException( "Directory must be '$' or a single letter A-Z.", nameof( directory ) );
        }

        if ( name.Length > 7 )
        {
            name = name[ ..7 ];
        }

        if ( _entries.Count >= 31 )
        {
            throw new InvalidOperationException( "DFS catalog full (max 31 files)." );
        }

        var sectorsNeeded = ( data.Length + ( SectorSize - 1 ) ) / SectorSize;
        var startSector   = Allocate( sectorsNeeded );

        WriteSectors( startSector, data );

        _entries.Add( new Entry {
            Directory   = directory[ 0 ],
            Name        = name,
            Locked      = locked,
            LoadAddress = loadAddress,
            ExecAddress = execAddress,
            Length      = (uint)data.Length,
            StartSector = (ushort)startSector,
        } );

        WriteCatalog();
    }

    public byte[ ] GetBytes() => _image;

    private int Allocate( int sectorsNeeded )
    {
        if ( sectorsNeeded <= 0 )
        {
            throw new ArgumentOutOfRangeException( nameof( sectorsNeeded ) );
        }

        var start = _nextFreeSector;
        var end   = start + sectorsNeeded;
        if ( end > TotalSectors )
        {
            throw new InvalidOperationException( "Disk full." );
        }

        _nextFreeSector = end;
        return start;
    }

    private void WriteEmptyCatalog( string title )
    {
        var cat0 = Sector( 0 );
        var cat1 = Sector( 1 );
        cat0.Clear();
        cat1.Clear();

        // DFS-created images commonly leave catalog title bytes as 0x00.
        // We avoid writing the title into the catalog to maximize DFS 1.2 compatibility.

        WriteCatalogMeta();

        WriteCatalog();
    }

    private void WriteCatalogMeta()
    {
        var cat1 = Sector( 1 );

        // DFS 1.2 sector 1 header/meta:
        // 0x04: BCD disk cycle number
        // 0x05: file count * 8 (byte offset past last entry)
        // 0x06: bits 4-5 = boot option, bits 0-1 = sector count bits 8-9
        // 0x07: sector count bits 0-7

        cat1[0x05] = (byte)(_entries.Count * 8);

        var sectorCount = TotalSectors;
        var bootOption = 3; // EXEC $.!BOOT
        cat1[0x06] = (byte)((bootOption << 4) | ((sectorCount >> 8) & 0x03));
        cat1[0x07] = (byte)(sectorCount & 0xFF);
    }

    private static string NormalizeTitle( string title )
    {
        title = title.Trim().ToUpperInvariant();
        if ( title.Length > 12 )
        {
            title = title[ ..12 ];
        }

        var sb = new StringBuilder( 12 );
        foreach ( var ch in title )
        {
            sb.Append( ch is >= ' ' and <= '~' ? ch : '_' );
        }

        while ( sb.Length < 12 )
        {
            sb.Append( ' ' );
        }

        return sb.ToString();
    }

    private void WriteCatalog()
    {
        var cat0 = Sector( 0 );
        var cat1 = Sector( 1 );

        // Clear file areas, preserve title and meta bytes.
        // Sector 0: bytes 0-7 title, 8.. are name/dir entries.
        // Sector 1: bytes 0-3 title, 4.. are info entries.
        // Bytes 5-11 hold meta (file count, boot option/disk size bits, next free),
        // so don't clear them.
        // Clear name/dir entries area using 0x20 padding. Some DFS parsers treat 0x00 as terminator.
        cat0.Slice(8).Fill((byte)' ');

        // Clear per-file info entries (31 * 8 bytes = 248 bytes) starting at offset 0x08.
        // Keep header bytes 0x00..0x07 intact.
        cat1.Slice(0x08).Clear();

        WriteCatalogMeta();

        for ( var i = 0; i < _entries.Count; i++ )
        {
            WriteEntry( i, _entries[ i ] );
        }
    }

    public void Validate()
    {
        var cat1  = Sector( 1 );

        // Boot option is in bits 4-5 of sector 1 byte 0x06.
        var bootOption = (cat1[0x06] >> 4) & 0x03;
        if (bootOption != 3)
        {
            throw new InvalidOperationException("SSD boot option not set to EXEC !BOOT.");
        }
    }

    private void WriteEntry( int index, Entry e )
    {
        var        cat0      = Sector( 0 );
        var        cat1      = Sector( 1 );

        var        base0     = 8 + ( index * 8 );
        var        base1     = 0x08 + ( index * 8 );

        Span<byte> nameBytes = stackalloc byte[ 7 ];
        nameBytes.Fill( (byte)' ' );
        Encoding.ASCII.GetBytes( e.Name ).AsSpan().CopyTo( nameBytes );
        nameBytes.CopyTo( cat0.Slice( base0, 7 ) );

        // Dir + locked flag in 8th byte.
        // Many DFS implementations store the directory as an ASCII character in
        // bits 0-6. Root is '$'. Bit 7 indicates locked. BeebEm/DFS tends to expect
        // the directory byte as an ASCII letter, with root effectively 'A'. So
        // encode '$' as 'A' in the directory byte.
        // DFS 1.2 expects directory stored as ASCII in bits 0-6 (root is '$'), with lock in bit 7.
        var dirChar = e.Directory == '$' ? '$' : e.Directory;
        if (dirChar is < 'A' or > 'Z')
        {
            dirChar = '$';
        }

        var dirByte = (byte)dirChar;
        if ( e.Locked )
        {
            dirByte |= 0x80;
        }
        cat0[ base0 + 7 ] = dirByte;

        // DFS 1.2 file info entry (8 bytes):
        // 0-1: load address low 16
        // 2-3: exec address low 16
        // 4-5: length low 16
        // 6: packed extra bits (exec:7-6, len:5-4, load:3-2, start:1-0)
        // 7: start sector low 8

        cat1[base1 + 0] = (byte)(e.LoadAddress & 0xFF);
        cat1[base1 + 1] = (byte)((e.LoadAddress >> 8) & 0xFF);
        cat1[base1 + 2] = (byte)(e.ExecAddress & 0xFF);
        cat1[base1 + 3] = (byte)((e.ExecAddress >> 8) & 0xFF);
        cat1[base1 + 4] = (byte)(e.Length & 0xFF);
        cat1[base1 + 5] = (byte)((e.Length >> 8) & 0xFF);

        var highLoad = (byte)((e.LoadAddress >> 16) & 0x03);
        var highExec = (byte)((e.ExecAddress >> 16) & 0x03);
        var highLen = (byte)((e.Length >> 16) & 0x03);
        var highStart = (byte)((e.StartSector >> 8) & 0x03);
        cat1[base1 + 6] = (byte)((highExec << 6) | (highLen << 4) | (highLoad << 2) | highStart);
        cat1[base1 + 7] = (byte)(e.StartSector & 0xFF);
    }

    private Span<byte> Sector( int logicalSectorIndex )
    {
        // SSD files are typically stored in linear order (track-major,
        // sector-minor), i.e. logical sector N is stored at offset N*256.
        return _image.AsSpan( logicalSectorIndex * SectorSize, SectorSize );
    }

    private void WriteSectors( int startLogicalSector, ReadOnlySpan<byte> data )
    {
        var offset  = 0;
        var logical = startLogicalSector;
        while ( offset < data.Length )
        {
            var chunk = Math.Min( SectorSize, data.Length - offset );
            data.Slice( offset, chunk ).CopyTo( Sector( logical ) );
            offset += chunk;
            logical++;
        }
    }

    // Note: _interleave is retained for potential future use in allocation
    // strategy, but we intentionally do not remap SSD sector storage order.

    private readonly record struct Entry
    {
        public required char   Directory { get; init; }
        public required string Name { get; init; }
        public required bool   Locked { get; init; }
        public required uint   LoadAddress { get; init; }
        public required uint   ExecAddress { get; init; }
        public required uint   Length { get; init; }
        public required ushort StartSector { get; init; }
    }
}
