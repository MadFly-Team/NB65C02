using System.Text;

namespace NB65c02Asm.Bbc;

/// <summary>
/// Generates 6502 assembly source code for <see cref="CharSprite"/> objects.
/// The generated code uses BBC MOS calls (OSWRCH / VDU) to set colours and
/// print characters at a given screen position.
/// </summary>
internal static class SpriteCodeGenerator
{
    /// <summary>
    /// Generates complete 6502 assembly source for a single sprite.
    /// The output includes:
    /// <list type="bullet">
    ///   <item>Sprite data tables (characters, foreground colours, background colours)</item>
    ///   <item>Dimension constants</item>
    ///   <item>A <c>draw_&lt;name&gt;</c> subroutine that renders the sprite at the
    ///         screen position stored in two zero-page locations</item>
    /// </list>
    /// </summary>
    /// <param name="sprite">The sprite to generate code for.</param>
    /// <param name="zpX">Zero-page address for the X coordinate.</param>
    /// <param name="zpY">Zero-page address for the Y coordinate.</param>
    public static string GenerateAssembly(CharSprite sprite, byte zpX = 0x70, byte zpY = 0x71)
    {
        ArgumentNullException.ThrowIfNull(sprite);

        var sb = new StringBuilder();
        var label = SanitiseLabel(sprite.Name);
        var (chars, fgs, bgs) = sprite.ToByteArrays();

        sb.AppendLine($"; ─── Sprite: {sprite.Name} ───");
        sb.AppendLine($"; Target: MODE {sprite.TargetMode}  Size: {sprite.Width}×{sprite.Height}");
        sb.AppendLine($"; Colours: {BbcScreenMode.Get(sprite.TargetMode).ColourCount} logical colours");
        sb.AppendLine();

        // Constants
        sb.AppendLine($"{label}_W = {sprite.Width}");
        sb.AppendLine($"{label}_H = {sprite.Height}");
        sb.AppendLine();

        // Character data table
        sb.AppendLine($"{label}_chars:");
        AppendDataRows(sb, chars, sprite.Width);
        sb.AppendLine();

        // Foreground colour table
        sb.AppendLine($"{label}_fg:");
        AppendDataRows(sb, fgs, sprite.Width);
        sb.AppendLine();

        // Background colour table
        sb.AppendLine($"{label}_bg:");
        AppendDataRows(sb, bgs, sprite.Width);
        sb.AppendLine();

        // Draw subroutine
        sb.AppendLine($"; Draw sprite at screen position (${zpX:X2}) = column, (${zpY:X2}) = row");
        sb.AppendLine($"draw_{label}:");
        sb.AppendLine($"    LDX #0              ; byte index into tables");
        sb.AppendLine($"    LDY #0              ; current row offset");
        sb.AppendLine($".draw_{label}_row:");
        sb.AppendLine();

        // VDU 31,x,y — TAB(x,y)
        sb.AppendLine($"    ; VDU 31,col,row — move text cursor");
        sb.AppendLine($"    LDA #31");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH");
        sb.AppendLine($"    CLC");
        sb.AppendLine($"    TYA                 ; row offset");
        sb.AppendLine($"    ADC ${zpY:X2}            ; + base row");
        sb.AppendLine($"    PHA                 ; save row for OSWRCH");
        sb.AppendLine($"    LDA ${zpX:X2}            ; base column");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH — X coord");
        sb.AppendLine($"    PLA");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH — Y coord");
        sb.AppendLine();

        // Print all characters in the row, setting colours per cell
        sb.AppendLine($"    STX ${zpX:X2}+2          ; save X (use {zpX + 2:X2} as temp)");
        sb.AppendLine($"    LDA #0");
        sb.AppendLine($"    STA ${zpX:X2}+3          ; column counter");
        sb.AppendLine($".draw_{label}_col:");

        // VDU 17,fg — set text foreground
        sb.AppendLine($"    LDA #17");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH — VDU 17");
        sb.AppendLine($"    LDA {label}_fg,X");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH — foreground colour");

        // VDU 17,bg+128 — set text background
        sb.AppendLine($"    LDA #17");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH — VDU 17");
        sb.AppendLine($"    LDA {label}_bg,X");
        sb.AppendLine($"    ORA #$80            ; bit 7 = background");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH — background colour");

        // Print the character
        sb.AppendLine($"    LDA {label}_chars,X");
        sb.AppendLine($"    JSR $FFEE           ; OSWRCH — print character");
        sb.AppendLine();

        sb.AppendLine($"    INX");
        sb.AppendLine($"    INC ${zpX:X2}+3          ; column++");
        sb.AppendLine($"    LDA ${zpX:X2}+3");
        sb.AppendLine($"    CMP #{sprite.Width}");
        sb.AppendLine($"    BNE .draw_{label}_col");
        sb.AppendLine();

        sb.AppendLine($"    INY                 ; next row");
        sb.AppendLine($"    CPY #{sprite.Height}");
        sb.AppendLine($"    BNE .draw_{label}_row");
        sb.AppendLine($"    RTS");

        return sb.ToString();
    }

    /// <summary>
    /// Generates assembly data tables for every sprite in a sheet, plus
    /// a lookup table of sprite addresses.
    /// </summary>
    public static string GenerateSheetAssembly(CharSpriteSheet sheet, byte zpX = 0x70, byte zpY = 0x71)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var sb = new StringBuilder();
        sb.AppendLine($"; ═══ Sprite Sheet — MODE {sheet.TargetMode} ═══");
        sb.AppendLine($"; {sheet.Count} sprite(s), {sheet.Mode.ColourCount} logical colours");
        sb.AppendLine();

        foreach (var sprite in sheet.Sprites)
        {
            sb.AppendLine(GenerateAssembly(sprite, zpX, zpY));
            sb.AppendLine();
        }

        // Address lookup table
        sb.AppendLine("; ─── Sprite address table ───");
        sb.AppendLine("sprite_table_lo:");
        foreach (var sprite in sheet.Sprites)
            sb.AppendLine($"    .byte <draw_{SanitiseLabel(sprite.Name)}");
        sb.AppendLine("sprite_table_hi:");
        foreach (var sprite in sheet.Sprites)
            sb.AppendLine($"    .byte >draw_{SanitiseLabel(sprite.Name)}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates just the <c>.byte</c> data tables (no draw routine) for a
    /// sprite, useful for including as inline data.
    /// </summary>
    public static string GenerateDataOnly(CharSprite sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);

        var sb = new StringBuilder();
        var label = SanitiseLabel(sprite.Name);
        var (chars, fgs, bgs) = sprite.ToByteArrays();

        sb.AppendLine($"{label}_chars:");
        AppendDataRows(sb, chars, sprite.Width);
        sb.AppendLine($"{label}_fg:");
        AppendDataRows(sb, fgs, sprite.Width);
        sb.AppendLine($"{label}_bg:");
        AppendDataRows(sb, bgs, sprite.Width);

        return sb.ToString();
    }

    private static void AppendDataRows(StringBuilder sb, byte[] data, int rowWidth)
    {
        for (var i = 0; i < data.Length; i += rowWidth)
        {
            sb.Append("    .byte ");
            var end = Math.Min(i + rowWidth, data.Length);
            for (var j = i; j < end; j++)
            {
                if (j > i) sb.Append(", ");
                sb.Append($"${data[j]:X2}");
            }
            sb.AppendLine();
        }
    }

    private static string SanitiseLabel(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
}
