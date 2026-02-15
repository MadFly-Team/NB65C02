namespace NB65c02Asm.Bbc;

/// <summary>
/// Describes one of the eight BBC Micro display modes (MODE 0–7).
/// Each mode defines resolution, character grid size, number of available
/// logical colours and the default logical-to-physical colour mapping.
/// </summary>
internal sealed class BbcScreenMode
{
    public int ModeNumber { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    /// <summary>Number of text columns in this mode.</summary>
    public int Columns { get; }

    /// <summary>Number of text rows in this mode.</summary>
    public int Rows { get; }

    /// <summary>Number of logical colours available (2, 4 or 16).</summary>
    public int ColourCount { get; }

    /// <summary>Bits per pixel (1, 2 or 4).</summary>
    public int BitsPerPixel { get; }

    /// <summary>Whether this is a teletext (Mode 7) mode.</summary>
    public bool IsTeletext { get; }

    /// <summary>
    /// Default mapping from logical colour index to physical colour index.
    /// Length equals <see cref="ColourCount"/>.
    /// </summary>
    public IReadOnlyList<int> DefaultLogicalToPhysical { get; }

    private BbcScreenMode(
        int modeNumber,
        int pixelWidth,
        int pixelHeight,
        int columns,
        int rows,
        int colourCount,
        int bitsPerPixel,
        bool isTeletext,
        int[] defaultLogicalToPhysical)
    {
        ModeNumber = modeNumber;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Columns = columns;
        Rows = rows;
        ColourCount = colourCount;
        BitsPerPixel = bitsPerPixel;
        IsTeletext = isTeletext;
        DefaultLogicalToPhysical = defaultLogicalToPhysical;
    }

    /// <summary>
    /// All eight BBC B screen modes, indexed by mode number.
    /// </summary>
    public static IReadOnlyList<BbcScreenMode> AllModes { get; } =
    [
        // MODE 0: 640×256, 2 colours, 80×32
        new(0, 640, 256, 80, 32, 2, 1, false, [0, 7]),

        // MODE 1: 320×256, 4 colours, 40×32
        new(1, 320, 256, 40, 32, 4, 2, false, [0, 1, 3, 7]),

        // MODE 2: 160×256, 16 colours, 20×32
        new(2, 160, 256, 20, 32, 16, 4, false,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]),

        // MODE 3: 640×250, 2 colours, 80×25 (teletext-style text only)
        new(3, 640, 250, 80, 25, 2, 1, false, [0, 7]),

        // MODE 4: 320×256, 2 colours, 40×32
        new(4, 320, 256, 40, 32, 2, 1, false, [0, 7]),

        // MODE 5: 160×256, 4 colours, 20×32
        new(5, 160, 256, 20, 32, 4, 2, false, [0, 1, 3, 7]),

        // MODE 6: 320×250, 2 colours, 40×25
        new(6, 320, 250, 40, 25, 2, 1, false, [0, 7]),

        // MODE 7: 40×25 teletext (SAA5050), 8 colours handled by teletext chip
        new(7, 320, 250, 40, 25, 8, 0, true, [0, 1, 2, 3, 4, 5, 6, 7]),
    ];

    /// <summary>
    /// Gets a mode by number (0–7).
    /// </summary>
    public static BbcScreenMode Get(int modeNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(modeNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(modeNumber, 7);
        return AllModes[modeNumber];
    }

    public override string ToString() =>
        $"MODE {ModeNumber}: {PixelWidth}×{PixelHeight}, {Columns}×{Rows} text, {ColourCount} colours{(IsTeletext ? " (teletext)" : "")}";
}
