using System.Text;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace NB65c02Asm.Bbc;

/// <summary>
/// A Terminal.Gui dialog that renders a <see cref="CharSprite"/> or an entire
/// <see cref="CharSpriteSheet"/> using mode-correct colours.
/// </summary>
internal sealed class SpritePreviewWindow : Dialog
{
    private readonly CharSpriteSheet _sheet;
    private readonly List<CharSprite> _spriteList;
    private int _currentIndex;

    private SpriteView _spriteView = null!;
    private Label _infoLabel = null!;
    private Label _modeLabel = null!;

    public SpritePreviewWindow(CharSpriteSheet sheet)
    {
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        _spriteList = [.. sheet.Sprites];
        _currentIndex = 0;

        Title = "Sprite Preview";
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);

        SetupUi();
    }

    public SpritePreviewWindow(CharSprite sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        _sheet = new CharSpriteSheet(sprite.TargetMode);
        _sheet.Add(sprite);
        _spriteList = [sprite];
        _currentIndex = 0;

        Title = "Sprite Preview";
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);

        SetupUi();
    }

    private void SetupUi()
    {
        var mode = _sheet.Mode;

        _modeLabel = new Label
        {
            Text = $"MODE {mode.ModeNumber}: {mode.ColourCount} colours | {mode.PixelWidth}×{mode.PixelHeight}",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
        };

        // Palette legend
        var paletteSb = new StringBuilder("Palette: ");
        for (var i = 0; i < mode.ColourCount; i++)
        {
            var physName = BbcPalette.PhysicalColourNames[mode.DefaultLogicalToPhysical[i]];
            paletteSb.Append($"{i}={physName}");
            if (i < mode.ColourCount - 1) paletteSb.Append(", ");
        }

        var paletteLabel = new Label
        {
            Text = paletteSb.ToString(),
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
        };

        _spriteView = new SpriteView(mode)
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(1),
            Height = Dim.Fill(4),
        };

        _infoLabel = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
        };

        var btnPrev = new Button { Text = "◄ Prev", X = 0, Y = Pos.AnchorEnd(2), };
        btnPrev.Accepting += (_, _) => Navigate(-1);

        var btnNext = new Button { Text = "Next ►", X = Pos.Right(btnPrev) + 2, Y = Pos.AnchorEnd(2), };
        btnNext.Accepting += (_, _) => Navigate(1);

        var btnClose = new Button { Text = "Close", X = Pos.Right(btnNext) + 2, Y = Pos.AnchorEnd(2), };
        btnClose.Accepting += (_, _) => Application.RequestStop(this);

        Add(_modeLabel, paletteLabel, _spriteView, _infoLabel, btnPrev, btnNext, btnClose);

        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case KeyCode.CursorLeft:
                    Navigate(-1);
                    e.Handled = true;
                    break;
                case KeyCode.CursorRight:
                    Navigate(1);
                    e.Handled = true;
                    break;
                case KeyCode.Esc:
                    Application.RequestStop(this);
                    e.Handled = true;
                    break;
            }
        };

        Ready += (_, _) => ShowSprite(_currentIndex);
    }

    private void Navigate(int delta)
    {
        if (_spriteList.Count == 0) return;
        _currentIndex = (_currentIndex + delta + _spriteList.Count) % _spriteList.Count;
        ShowSprite(_currentIndex);
    }

    private void ShowSprite(int index)
    {
        if (index < 0 || index >= _spriteList.Count) return;
        var sprite = _spriteList[index];
        _spriteView.Sprite = sprite;
        _infoLabel.Text = $"[{index + 1}/{_spriteList.Count}] \"{sprite.Name}\" — {sprite.Width}×{sprite.Height}";
        _spriteView.SetNeedsDraw();
    }

    /// <summary>
    /// Custom view that draws a <see cref="CharSprite"/> with BBC-mode-correct
    /// colours using Terminal.Gui cell-level colour attributes.
    /// </summary>
    private sealed class SpriteView : View
    {
        private readonly BbcScreenMode _mode;

        public CharSprite? Sprite { get; set; }

        public SpriteView(BbcScreenMode mode)
        {
            _mode = mode;
            CanFocus = false;
        }

        protected override bool OnDrawingContent()
        {
            if (Sprite is null) return true;

            for (var y = 0; y < Sprite.Height && y < Viewport.Height; y++)
            {
                for (var x = 0; x < Sprite.Width && x < Viewport.Width; x++)
                {
                    var cell = Sprite[x, y];
                    var fgPhysical = _mode.DefaultLogicalToPhysical[cell.ForegroundColour];
                    var bgPhysical = _mode.DefaultLogicalToPhysical[cell.BackgroundColour];

                    var fgColour = BbcPalette.ToColorName16(fgPhysical);
                    var bgColour = BbcPalette.ToColorName16(bgPhysical);

                    var attr = new Attribute(fgColour, bgColour);
                    Application.Driver?.SetAttribute(attr);
                    Move(x, y);
                    Application.Driver?.AddStr(cell.Character.ToString());
                }
            }

            return true;
        }
    }
}
