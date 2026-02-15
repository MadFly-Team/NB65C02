using System.Text.Json;
using System.Text.Json.Serialization;

namespace NB65c02Asm.Bbc;

/// <summary>
/// Serialisable sprite definition for loading/saving sprite sheets as JSON.
/// </summary>
internal sealed class SpriteDefinitionFile
{
    [JsonPropertyName("mode")]
    public int Mode { get; set; }

    [JsonPropertyName("sprites")]
    public List<SpriteDefinition> Sprites { get; set; } = [];

    /// <summary>
    /// Loads a <see cref="CharSpriteSheet"/> from a JSON file.
    /// </summary>
    public static CharSpriteSheet Load(string path)
    {
        var json = File.ReadAllText(path);
        var def = JsonSerializer.Deserialize<SpriteDefinitionFile>(json)
            ?? throw new InvalidOperationException("Failed to parse sprite definition file.");

        var sheet = new CharSpriteSheet(def.Mode);
        foreach (var sd in def.Sprites)
        {
            if (sd.Rows is null || sd.Rows.Count == 0) continue;

            var height = sd.Rows.Count;
            var width = sd.Rows.Max(r => r.Pattern?.Length ?? 0);
            if (width == 0) continue;

            var sprite = sheet.Add(sd.Name ?? "unnamed", width, height);
            for (var y = 0; y < height; y++)
            {
                var row = sd.Rows[y];
                var pattern = row.Pattern ?? new string(' ', width);
                var fg = row.Foreground;
                var bg = row.Background;

                for (var x = 0; x < Math.Min(pattern.Length, width); x++)
                {
                    var fgColour = fg is not null && x < fg.Count ? fg[x] : 0;
                    var bgColour = bg is not null && x < bg.Count ? bg[x] : 0;
                    sprite[x, y] = new SpriteCell(pattern[x], fgColour, bgColour);
                }
            }
        }

        return sheet;
    }

    /// <summary>
    /// Saves a <see cref="CharSpriteSheet"/> to a JSON file.
    /// </summary>
    public static void Save(CharSpriteSheet sheet, string path)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var def = new SpriteDefinitionFile { Mode = sheet.TargetMode };

        foreach (var sprite in sheet.Sprites)
        {
            var sd = new SpriteDefinition
            {
                Name = sprite.Name,
                Width = sprite.Width,
                Height = sprite.Height,
                Rows = [],
            };

            for (var y = 0; y < sprite.Height; y++)
            {
                var chars = new char[sprite.Width];
                var fgs = new List<int>(sprite.Width);
                var bgs = new List<int>(sprite.Width);

                for (var x = 0; x < sprite.Width; x++)
                {
                    var cell = sprite[x, y];
                    chars[x] = cell.Character;
                    fgs.Add(cell.ForegroundColour);
                    bgs.Add(cell.BackgroundColour);
                }

                sd.Rows.Add(new SpriteRow
                {
                    Pattern = new string(chars),
                    Foreground = fgs,
                    Background = bgs,
                });
            }

            def.Sprites.Add(sd);
        }

        var json = JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}

internal sealed class SpriteDefinition
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("rows")]
    public List<SpriteRow> Rows { get; set; } = [];
}

internal sealed class SpriteRow
{
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("fg")]
    public List<int>? Foreground { get; set; }

    [JsonPropertyName("bg")]
    public List<int>? Background { get; set; }
}
