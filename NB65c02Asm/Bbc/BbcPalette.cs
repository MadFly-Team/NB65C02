using System.Drawing;

namespace NB65c02Asm.Bbc;

/// <summary>
/// The BBC Micro's 16 physical colours and mode-specific palette helpers.
/// Physical colour indices 0–7 are the normal colours; 8–15 are the
/// flashing variants (which alternate between two colours on real hardware).
/// For rendering in a modern terminal, flashing colours are mapped to their
/// steady equivalent.
/// </summary>
internal static class BbcPalette
{
    /// <summary>
    /// Human-readable names for the 16 physical colours.
    /// </summary>
    public static readonly string[] PhysicalColourNames =
    [
        "Black",            // 0
        "Red",              // 1
        "Green",            // 2
        "Yellow",           // 3
        "Blue",             // 4
        "Magenta",          // 5
        "Cyan",             // 6
        "White",            // 7
        "Black (flash)",    // 8
        "Red (flash)",      // 9
        "Green (flash)",    // 10
        "Yellow (flash)",   // 11
        "Blue (flash)",     // 12
        "Magenta (flash)",  // 13
        "Cyan (flash)",     // 14
        "White (flash)",    // 15
    ];

    /// <summary>
    /// Maps physical colour index (0–15) to an RGB value suitable for display.
    /// Flashing colours (8–15) use the same RGB as their non-flashing counterpart.
    /// </summary>
    public static readonly Color[] PhysicalToRgb =
    [
        Color.FromArgb(0x00, 0x00, 0x00), // 0  Black
        Color.FromArgb(0xFF, 0x00, 0x00), // 1  Red
        Color.FromArgb(0x00, 0xFF, 0x00), // 2  Green
        Color.FromArgb(0xFF, 0xFF, 0x00), // 3  Yellow
        Color.FromArgb(0x00, 0x00, 0xFF), // 4  Blue
        Color.FromArgb(0xFF, 0x00, 0xFF), // 5  Magenta
        Color.FromArgb(0x00, 0xFF, 0xFF), // 6  Cyan
        Color.FromArgb(0xFF, 0xFF, 0xFF), // 7  White
        Color.FromArgb(0x00, 0x00, 0x00), // 8  Black  (flash)
        Color.FromArgb(0xFF, 0x00, 0x00), // 9  Red    (flash)
        Color.FromArgb(0x00, 0xFF, 0x00), // 10 Green  (flash)
        Color.FromArgb(0xFF, 0xFF, 0x00), // 11 Yellow (flash)
        Color.FromArgb(0x00, 0x00, 0xFF), // 12 Blue   (flash)
        Color.FromArgb(0xFF, 0x00, 0xFF), // 13 Magenta(flash)
        Color.FromArgb(0x00, 0xFF, 0xFF), // 14 Cyan   (flash)
        Color.FromArgb(0xFF, 0xFF, 0xFF), // 15 White  (flash)
    ];

    /// <summary>
    /// Maps a physical colour index to the nearest <see cref="Terminal.Gui.ColorName16"/>
    /// for rendering in Terminal.Gui views.
    /// </summary>
    public static Terminal.Gui.ColorName16 ToColorName16(int physicalColour)
    {
        return (physicalColour & 0x07) switch
        {
            0 => Terminal.Gui.ColorName16.Black,
            1 => Terminal.Gui.ColorName16.Red,
            2 => Terminal.Gui.ColorName16.Green,
            3 => Terminal.Gui.ColorName16.Yellow,
            4 => Terminal.Gui.ColorName16.Blue,
            5 => Terminal.Gui.ColorName16.Magenta,
            6 => Terminal.Gui.ColorName16.Cyan,
            7 => Terminal.Gui.ColorName16.White,
            _ => Terminal.Gui.ColorName16.Black,
        };
    }

    /// <summary>
    /// Resolves a logical colour index to a physical colour index
    /// using the given mode's default palette.
    /// </summary>
    public static int LogicalToPhysical(BbcScreenMode mode, int logicalColour)
    {
        ArgumentNullException.ThrowIfNull(mode);

        if (logicalColour < 0 || logicalColour >= mode.ColourCount)
            throw new ArgumentOutOfRangeException(
                nameof(logicalColour),
                $"Logical colour {logicalColour} out of range for MODE {mode.ModeNumber} (0–{mode.ColourCount - 1}).");

        return mode.DefaultLogicalToPhysical[logicalColour];
    }

    /// <summary>
    /// Returns the physical colour name for a logical colour in the given mode.
    /// </summary>
    public static string LogicalColourName(BbcScreenMode mode, int logicalColour) =>
        PhysicalColourNames[LogicalToPhysical(mode, logicalColour)];
}
