using System.Runtime.CompilerServices;

namespace NB65c02Asm.Bbc;

/// <summary>
/// A named collection of <see cref="CharSprite"/> objects that share a common
/// target <see cref="BbcScreenMode"/>.
/// </summary>
internal sealed class CharSpriteSheet
{
    private readonly Dictionary<string, CharSprite> _sprites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The BBC screen mode all sprites in this sheet target.</summary>
    public int TargetMode { get; }

    /// <summary>The mode descriptor for <see cref="TargetMode"/>.</summary>
    public BbcScreenMode Mode => BbcScreenMode.Get(TargetMode);

    public CharSpriteSheet(int targetMode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetMode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetMode, 7);
        TargetMode = targetMode;
    }

    /// <summary>
    /// Adds a new empty sprite to the sheet.
    /// </summary>
    public CharSprite Add(string name, int width, int height,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_sprites.ContainsKey(name))
            throw new ArgumentException(
                $"Sprite '{name}' already exists in the sheet. " +
                $"(called from {Path.GetFileName(callerFile)}:{callerLine})", nameof(name));

        var sprite = new CharSprite(name, width, height, TargetMode);
        _sprites[name] = sprite;
        return sprite;
    }

    /// <summary>
    /// Adds an existing sprite to the sheet. The sprite's target mode must
    /// match the sheet's target mode.
    /// </summary>
    public void Add(CharSprite sprite,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        if (sprite.TargetMode != TargetMode)
            throw new ArgumentException(
                $"Sprite '{sprite.Name}' targets MODE {sprite.TargetMode} but sheet targets MODE {TargetMode}. " +
                $"(called from {Path.GetFileName(callerFile)}:{callerLine})", nameof(sprite));
        if (_sprites.ContainsKey(sprite.Name))
            throw new ArgumentException(
                $"Sprite '{sprite.Name}' already exists in the sheet. " +
                $"(called from {Path.GetFileName(callerFile)}:{callerLine})", nameof(sprite));

        _sprites[sprite.Name] = sprite;
    }

    /// <summary>
    /// Gets a sprite by name.
    /// </summary>
    public CharSprite Get(string name,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_sprites.TryGetValue(name, out var sprite))
            throw new KeyNotFoundException(
                $"Sprite '{name}' not found in sheet. " +
                $"(called from {Path.GetFileName(callerFile)}:{callerLine})");
        return sprite;
    }

    /// <summary>
    /// Tries to get a sprite by name.
    /// </summary>
    public bool TryGet(string name, out CharSprite? sprite) =>
        _sprites.TryGetValue(name, out sprite);

    /// <summary>
    /// All sprites in the sheet.
    /// </summary>
    public IEnumerable<CharSprite> Sprites => _sprites.Values;

    /// <summary>
    /// Number of sprites in the sheet.
    /// </summary>
    public int Count => _sprites.Count;

    /// <summary>
    /// Removes a sprite by name. Returns true if it was found and removed.
    /// </summary>
    public bool Remove(string name) => _sprites.Remove(name);
}
