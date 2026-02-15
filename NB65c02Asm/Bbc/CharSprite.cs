using System.Text;

namespace NB65c02Asm.Bbc;

/// <summary>
/// A single cell within a <see cref="CharSprite"/>.
/// </summary>
internal readonly record struct SpriteCell(char Character, int ForegroundColour, int BackgroundColour)
{
    /// <summary>An empty/transparent cell.</summary>
    public static SpriteCell Empty => new(' ', 0, 0);

    /// <summary>Whether this cell is considered transparent (space character).</summary>
    public bool IsTransparent => Character == ' ';
}

/// <summary>
/// A character-based sprite defined as a rectangular grid of <see cref="SpriteCell"/>
/// values. Colours are expressed as logical colour indices for a specific
/// <see cref="BbcScreenMode"/>.
/// </summary>
internal sealed class CharSprite
{
    private readonly SpriteCell[,] _cells;

    /// <summary>Sprite name used for code generation labels.</summary>
    public string Name { get; }

    /// <summary>Width in character cells.</summary>
    public int Width { get; }

    /// <summary>Height in character cells.</summary>
    public int Height { get; }

    /// <summary>The screen mode this sprite's colours target.</summary>
    public int TargetMode { get; }

    public CharSprite(string name, int width, int height, int targetMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(targetMode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetMode, 7);

        Name = name;
        Width = width;
        Height = height;
        TargetMode = targetMode;
        _cells = new SpriteCell[height, width];

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                _cells[y, x] = SpriteCell.Empty;
    }

    /// <summary>Gets or sets the cell at the given position.</summary>
    public SpriteCell this[int x, int y]
    {
        get
        {
            ValidateCoords(x, y);
            return _cells[y, x];
        }
        set
        {
            ValidateCoords(x, y);
            ValidateColour(value.ForegroundColour);
            ValidateColour(value.BackgroundColour);
            _cells[y, x] = value;
        }
    }

    /// <summary>
    /// Sets a row of the sprite from a pattern string. Each character in the
    /// string becomes the cell character; all cells in the row share the
    /// given foreground and background colours.
    /// </summary>
    public void SetRow(int y, string pattern, int foreground, int background = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        ArgumentNullException.ThrowIfNull(pattern);

        var len = Math.Min(pattern.Length, Width);
        for (var x = 0; x < len; x++)
            _cells[y, x] = new SpriteCell(pattern[x], foreground, background);
    }

    /// <summary>
    /// Sets a row with per-character colours. <paramref name="colours"/> contains
    /// one foreground colour index per character position.
    /// </summary>
    public void SetRow(int y, string pattern, int[] colours, int background = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(colours);

        var len = Math.Min(pattern.Length, Width);
        for (var x = 0; x < len; x++)
        {
            var fg = x < colours.Length ? colours[x] : 0;
            _cells[y, x] = new SpriteCell(pattern[x], fg, background);
        }
    }

    /// <summary>
    /// Returns the sprite data as flat arrays suitable for 6502 code generation.
    /// Characters, foreground colours and background colours are returned as
    /// separate byte arrays in row-major order.
    /// </summary>
    public (byte[] Characters, byte[] Foregrounds, byte[] Backgrounds) ToByteArrays()
    {
        var size = Width * Height;
        var chars = new byte[size];
        var fgs = new byte[size];
        var bgs = new byte[size];

        var i = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = _cells[y, x];
                chars[i] = (byte)cell.Character;
                fgs[i] = (byte)cell.ForegroundColour;
                bgs[i] = (byte)cell.BackgroundColour;
                i++;
            }
        }

        return (chars, fgs, bgs);
    }

    /// <summary>
    /// Returns a human-readable text representation of the sprite (characters only).
    /// </summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
                sb.Append(_cells[y, x].Character);
            if (y < Height - 1)
                sb.AppendLine();
        }
        return sb.ToString();
    }

    private void ValidateCoords(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
    }

    private void ValidateColour(int colour)
    {
        var mode = BbcScreenMode.Get(TargetMode);
        if (colour < 0 || colour >= mode.ColourCount)
            throw new ArgumentOutOfRangeException(
                nameof(colour),
                $"Colour {colour} out of range for MODE {TargetMode} (0–{mode.ColourCount - 1}).");
    }
}
