using System.Collections.ObjectModel;
using System.Drawing;
using System.Text;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace NB65c02Asm.Bbc;

/// <summary>
/// A full-screen character-based sprite editor.  The user paints characters
/// onto a grid; each cell carries a foreground and background logical colour
/// index appropriate to the selected BBC screen mode.
/// </summary>
internal sealed class SpriteEditorWindow : Toplevel
{
    private CharSprite _sprite;
    private BbcScreenMode _mode;

    // Editing state
    private int _cursorX;
    private int _cursorY;
    private char _paintChar = '#';
    private int _fgColour = 1;
    private int _bgColour;
    private bool _dirty;

    // UI elements
    private CanvasView _canvas = null!;
    private Label _statusLabel = null!;
    private Label _cellInfoLabel = null!;
    private Label _modeInfoLabel = null!;
    private PaletteBar _fgPalette = null!;
    private PaletteBar _bgPalette = null!;
    private TextField _charField = null!;
    private TextField _nameField = null!;

    /// <summary>The edited sprite (may be replaced by New/Resize).</summary>
    public CharSprite Sprite => _sprite;

    /// <summary>Whether the sprite was modified since last save/creation.</summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Creates the editor with a new blank sprite.
    /// </summary>
    public SpriteEditorWindow(int modeNumber, string name, int width, int height)
    {
        _mode = BbcScreenMode.Get(modeNumber);
        _sprite = new CharSprite(name, width, height, modeNumber);
        _fgColour = Math.Min(1, _mode.ColourCount - 1);
        SetupUi();
    }

    /// <summary>
    /// Creates the editor pre-loaded with an existing sprite.
    /// </summary>
    public SpriteEditorWindow(CharSprite sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        _sprite = sprite;
        _mode = BbcScreenMode.Get(sprite.TargetMode);
        _fgColour = Math.Min(1, _mode.ColourCount - 1);
        SetupUi();
    }

    private void SetupUi()
    {
        var window = new Window
        {
            Title = $"Sprite Editor — \"{_sprite.Name}\"",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        // ── Top info bar ──
        _modeInfoLabel = new Label
        {
            Text = ModeInfoText(),
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
        };

        // ── Name field ──
        var nameLabel = new Label { Text = "Name:", X = 0, Y = 1 };
        _nameField = new TextField
        {
            Text = _sprite.Name,
            X = Pos.Right(nameLabel) + 1,
            Y = 1,
            Width = 20,
        };

        // ── Paint character field ──
        var charLabel = new Label { Text = "Char:", X = Pos.Right(_nameField) + 2, Y = 1 };
        _charField = new TextField
        {
            Text = _paintChar.ToString(),
            X = Pos.Right(charLabel) + 1,
            Y = 1,
            Width = 3,
        };
        _charField.HasFocusChanged += (_, _) =>
        {
            var t = _charField.Text ?? "";
            if (t.Length > 0)
                _paintChar = t[0];
        };

        // ── Foreground palette ──
        var fgLabel = new Label { Text = "FG:", X = 0, Y = 2 };
        _fgPalette = new PaletteBar(_mode, _fgColour)
        {
            X = Pos.Right(fgLabel) + 1,
            Y = 2,
            Width = Dim.Fill(),
            Height = 1,
        };
        _fgPalette.ColourSelected += c =>
        {
            _fgColour = c;
            RefreshCellInfo();
        };

        // ── Background palette ──
        var bgLabel = new Label { Text = "BG:", X = 0, Y = 3 };
        _bgPalette = new PaletteBar(_mode, _bgColour)
        {
            X = Pos.Right(bgLabel) + 1,
            Y = 3,
            Width = Dim.Fill(),
            Height = 1,
        };
        _bgPalette.ColourSelected += c =>
        {
            _bgColour = c;
            RefreshCellInfo();
        };

        // ── Canvas ──
        var canvasFrame = new FrameView
        {
            Title = "Canvas",
            X = 0,
            Y = 4,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
        };

        _canvas = new CanvasView(_sprite, _mode)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _canvas.CursorMoved += (cx, cy) =>
        {
            _cursorX = cx;
            _cursorY = cy;
            RefreshCellInfo();
        };
        _canvas.CellPainted += (cx, cy) =>
        {
            PaintCell(cx, cy);
        };
        _canvas.CellErased += (cx, cy) =>
        {
            EraseCell(cx, cy);
        };
        _canvas.CellPicked += (cx, cy) =>
        {
            PickCell(cx, cy);
        };
        _canvas.CharTyped += c =>
        {
            _paintChar = c;
            _charField.Text = c.ToString();
        };
        canvasFrame.Add(_canvas);

        // ── Cell info bar ──
        _cellInfoLabel = new Label
        {
            Text = CellInfoText(0, 0),
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
        };

        // ── Toolbar buttons ──
        var btnClear = new Button { Text = "Clear", X = 0, Y = Pos.AnchorEnd(2) };
        btnClear.Accepting += (_, _) => DoClear();

        var btnFill = new Button { Text = "Fill", X = Pos.Right(btnClear) + 1, Y = Pos.AnchorEnd(2) };
        btnFill.Accepting += (_, _) => DoFill();

        var btnResize = new Button { Text = "Resize", X = Pos.Right(btnFill) + 1, Y = Pos.AnchorEnd(2) };
        btnResize.Accepting += (_, _) => DoResize();

        var btnMode = new Button { Text = "Mode", X = Pos.Right(btnResize) + 1, Y = Pos.AnchorEnd(2) };
        btnMode.Accepting += (_, _) => DoChangeMode();

        var btnExport = new Button { Text = "Export ASM", X = Pos.Right(btnMode) + 1, Y = Pos.AnchorEnd(2) };
        btnExport.Accepting += (_, _) => DoExportAsm();

        var btnSave = new Button { Text = "Save", X = Pos.Right(btnExport) + 1, Y = Pos.AnchorEnd(2) };
        btnSave.Accepting += (_, _) => DoSave();

        var btnClose = new Button { Text = "Close", X = Pos.Right(btnSave) + 1, Y = Pos.AnchorEnd(2) };
        btnClose.Accepting += (_, _) => DoClose();

        window.Add(
            _modeInfoLabel, nameLabel, _nameField, charLabel, _charField,
            fgLabel, _fgPalette, bgLabel, _bgPalette,
            canvasFrame, _cellInfoLabel,
            btnClear, btnFill, btnResize, btnMode, btnExport, btnSave, btnClose);

        _statusLabel = new Label
        {
            Text = StatusText(),
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };

        Add(window, _statusLabel);

        // Keyboard shortcuts
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case KeyCode.Esc:
                    DoClose();
                    e.Handled = true;
                    break;
            }
        };

        // Theme
        var main = new ColorScheme(
            new Attribute(ColorName16.White, ColorName16.Black),
            new Attribute(ColorName16.White, ColorName16.DarkGray),
            new Attribute(ColorName16.Yellow, ColorName16.Black),
            new Attribute(ColorName16.DarkGray, ColorName16.Black),
            new Attribute(ColorName16.Yellow, ColorName16.DarkGray));

        var bar = new ColorScheme(
            new Attribute(ColorName16.Black, ColorName16.Gray),
            new Attribute(ColorName16.White, ColorName16.Blue),
            new Attribute(ColorName16.White, ColorName16.Gray),
            new Attribute(ColorName16.DarkGray, ColorName16.Gray),
            new Attribute(ColorName16.Yellow, ColorName16.Blue));

        window.ColorScheme = main;
        _statusLabel.ColorScheme = bar;
        _cellInfoLabel.ColorScheme = bar;

        Ready += (_, _) =>
        {
            _canvas.SetFocus();
            RefreshCellInfo();
        };
    }

    // ── Painting operations ──

    private void PaintCell(int x, int y)
    {
        if (x < 0 || x >= _sprite.Width || y < 0 || y >= _sprite.Height) return;
        _sprite[x, y] = new SpriteCell(_paintChar, _fgColour, _bgColour);
        _dirty = true;
        _canvas.SetNeedsDraw();
        RefreshCellInfo();
    }

    private void EraseCell(int x, int y)
    {
        if (x < 0 || x >= _sprite.Width || y < 0 || y >= _sprite.Height) return;
        _sprite[x, y] = SpriteCell.Empty;
        _dirty = true;
        _canvas.SetNeedsDraw();
        RefreshCellInfo();
    }

    private void PickCell(int x, int y)
    {
        if (x < 0 || x >= _sprite.Width || y < 0 || y >= _sprite.Height) return;
        var cell = _sprite[x, y];
        _paintChar = cell.Character;
        _fgColour = cell.ForegroundColour;
        _bgColour = cell.BackgroundColour;
        _charField.Text = _paintChar.ToString();
        _fgPalette.SelectedColour = _fgColour;
        _bgPalette.SelectedColour = _bgColour;
        _fgPalette.SetNeedsDraw();
        _bgPalette.SetNeedsDraw();
        RefreshCellInfo();
    }

    // ── Toolbar actions ──

    private void DoClear()
    {
        var r = MessageBox.Query("Clear Canvas", "Erase all cells?", "OK", "Cancel");
        if (r != 0) return;

        for (var y = 0; y < _sprite.Height; y++)
            for (var x = 0; x < _sprite.Width; x++)
                _sprite[x, y] = SpriteCell.Empty;

        _dirty = true;
        _canvas.SetNeedsDraw();
    }

    private void DoFill()
    {
        for (var y = 0; y < _sprite.Height; y++)
            for (var x = 0; x < _sprite.Width; x++)
                _sprite[x, y] = new SpriteCell(_paintChar, _fgColour, _bgColour);

        _dirty = true;
        _canvas.SetNeedsDraw();
    }

    private void DoResize()
    {
        var dlg = new Dialog { Title = "Resize Sprite", Width = 40, Height = 12 };

        var wLabel = new Label { Text = "Width:", X = 1, Y = 1 };
        var wField = new TextField { Text = _sprite.Width.ToString(), X = 12, Y = 1, Width = 8 };
        var hLabel = new Label { Text = "Height:", X = 1, Y = 3 };
        var hField = new TextField { Text = _sprite.Height.ToString(), X = 12, Y = 3, Width = 8 };

        var ok = new Button { Text = "OK", IsDefault = true };
        ok.Accepting += (_, _) =>
        {
            if (int.TryParse(wField.Text, out var nw) && int.TryParse(hField.Text, out var nh) &&
                nw is > 0 and <= 80 && nh is > 0 and <= 32)
            {
                ResizeSprite(nw, nh);
                Application.RequestStop(dlg);
            }
            else
            {
                MessageBox.ErrorQuery("Invalid Size", "Width: 1–80, Height: 1–32.", "OK");
            }
        };

        var cancel = new Button { Text = "Cancel" };
        cancel.Accepting += (_, _) => Application.RequestStop(dlg);

        dlg.Add(wLabel, wField, hLabel, hField);
        dlg.AddButton(ok);
        dlg.AddButton(cancel);
        Application.Run(dlg);
        dlg.Dispose();
    }

    private void ResizeSprite(int newWidth, int newHeight)
    {
        var name = (_nameField.Text ?? _sprite.Name).Trim();
        if (string.IsNullOrWhiteSpace(name)) name = _sprite.Name;

        var newSprite = new CharSprite(name, newWidth, newHeight, _sprite.TargetMode);

        // Copy existing cells that fit
        for (var y = 0; y < Math.Min(_sprite.Height, newHeight); y++)
            for (var x = 0; x < Math.Min(_sprite.Width, newWidth); x++)
                newSprite[x, y] = _sprite[x, y];

        _sprite = newSprite;
        _canvas.Sprite = _sprite;
        _cursorX = Math.Min(_cursorX, newWidth - 1);
        _cursorY = Math.Min(_cursorY, newHeight - 1);
        _dirty = true;
        _canvas.SetNeedsDraw();
        RefreshTitle();
        RefreshCellInfo();
    }

    private void DoChangeMode()
    {
        var dlg = new Dialog { Title = "Select BBC Screen Mode", Width = 50, Height = 16 };

        var items = new List<string>();
        foreach (var m in BbcScreenMode.AllModes)
            items.Add($"MODE {m.ModeNumber}: {m.ColourCount} colours ({m.PixelWidth}×{m.PixelHeight})");

        var list = new ListView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            Source = new ListWrapper<string>(new ObservableCollection<string>(items)),
            SelectedItem = _mode.ModeNumber,
        };

        var ok = new Button { Text = "OK", IsDefault = true };
        ok.Accepting += (_, _) =>
        {
            var sel = list.SelectedItem;
            if (sel >= 0 && sel <= 7)
            {
                ApplyModeChange(sel);
                Application.RequestStop(dlg);
            }
        };

        var cancel = new Button { Text = "Cancel" };
        cancel.Accepting += (_, _) => Application.RequestStop(dlg);

        dlg.Add(list);
        dlg.AddButton(ok);
        dlg.AddButton(cancel);
        Application.Run(dlg);
        dlg.Dispose();
    }

    private void ApplyModeChange(int newMode)
    {
        if (newMode == _mode.ModeNumber) return;

        var newModeObj = BbcScreenMode.Get(newMode);
        var name = (_nameField.Text ?? _sprite.Name).Trim();
        if (string.IsNullOrWhiteSpace(name)) name = _sprite.Name;

        var newSprite = new CharSprite(name, _sprite.Width, _sprite.Height, newMode);

        // Copy cells, clamping colour indices to the new mode's range
        for (var y = 0; y < _sprite.Height; y++)
        {
            for (var x = 0; x < _sprite.Width; x++)
            {
                var cell = _sprite[x, y];
                var fg = Math.Min(cell.ForegroundColour, newModeObj.ColourCount - 1);
                var bg = Math.Min(cell.BackgroundColour, newModeObj.ColourCount - 1);
                newSprite[x, y] = new SpriteCell(cell.Character, fg, bg);
            }
        }

        _mode = newModeObj;
        _sprite = newSprite;
        _fgColour = Math.Min(_fgColour, _mode.ColourCount - 1);
        _bgColour = Math.Min(_bgColour, _mode.ColourCount - 1);

        _canvas.Sprite = _sprite;
        _canvas.Mode = _mode;
        _fgPalette.UpdateMode(_mode, _fgColour);
        _bgPalette.UpdateMode(_mode, _bgColour);

        _dirty = true;
        _canvas.SetNeedsDraw();
        _modeInfoLabel.Text = ModeInfoText();
        RefreshTitle();
        RefreshCellInfo();
    }

    private void DoExportAsm()
    {
        ApplyName();
        var asm = SpriteCodeGenerator.GenerateAssembly(_sprite);

        var dlg = new Dialog
        {
            Title = "Generated 6502 Assembly",
            Width = Dim.Percent(85),
            Height = Dim.Percent(85),
        };

        var tv = new TextView
        {
            Text = asm,
            ReadOnly = true,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        var ok = new Button { Text = "Close", IsDefault = true };
        ok.Accepting += (_, _) => Application.RequestStop(dlg);

        dlg.Add(tv);
        dlg.AddButton(ok);
        Application.Run(dlg);
        dlg.Dispose();
    }

    private void DoSave()
    {
        ApplyName();
        var sheet = new CharSpriteSheet(_mode.ModeNumber);
        sheet.Add(_sprite);

        var dialog = new SaveDialog
        {
            Title = "Save Sprite",
            AllowedTypes =
            [
                new AllowedType("Sprite Files", ".json", ".sprite"),
                new AllowedTypeAny(),
            ],
        };

        Application.Run(dialog);

        try
        {
            if (dialog.Canceled) return;

            var savePath = dialog.Path;
            if (string.IsNullOrEmpty(savePath)) return;

            savePath = Path.GetFullPath(savePath);
            if (Directory.Exists(savePath)) return;

            var parentDir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            SpriteDefinitionFile.Save(sheet, savePath);
            _dirty = false;
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void DoClose()
    {
        if (_dirty)
        {
            var r = MessageBox.Query("Unsaved Changes", "Discard changes to sprite?", "Discard", "Cancel");
            if (r != 0) return;
        }

        Application.RequestStop(this);
    }

    private void ApplyName()
    {
        var name = (_nameField.Text ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, _sprite.Name, StringComparison.Ordinal))
        {
            var newSprite = new CharSprite(name, _sprite.Width, _sprite.Height, _sprite.TargetMode);
            for (var y = 0; y < _sprite.Height; y++)
                for (var x = 0; x < _sprite.Width; x++)
                    newSprite[x, y] = _sprite[x, y];
            _sprite = newSprite;
            _canvas.Sprite = _sprite;
            RefreshTitle();
        }
    }

    // ── Helpers ──

    private void RefreshCellInfo()
    {
        _cellInfoLabel.Text = CellInfoText(_cursorX, _cursorY);
    }

    private void RefreshTitle()
    {
        if (SuperView is Window w)
            w.Title = $"Sprite Editor — \"{_sprite.Name}\"";
    }

    private string ModeInfoText() =>
        $"MODE {_mode.ModeNumber}: {_mode.ColourCount} colours | " +
        $"{_mode.PixelWidth}×{_mode.PixelHeight} | Grid: {_mode.Columns}×{_mode.Rows}";

    private string CellInfoText(int x, int y)
    {
        if (x < 0 || x >= _sprite.Width || y < 0 || y >= _sprite.Height)
            return $" Pos: {x},{y} | Paint: '{_paintChar}' FG:{_fgColour} BG:{_bgColour}";

        var cell = _sprite[x, y];
        var fgName = BbcPalette.PhysicalColourNames[_mode.DefaultLogicalToPhysical[cell.ForegroundColour]];
        var bgName = BbcPalette.PhysicalColourNames[_mode.DefaultLogicalToPhysical[cell.BackgroundColour]];

        return $" Pos: {x},{y} | Cell: '{cell.Character}' FG:{cell.ForegroundColour}={fgName} BG:{cell.BackgroundColour}={bgName}" +
               $" | Paint: '{_paintChar}' FG:{_fgColour} BG:{_bgColour}";
    }

    private static string StatusText() =>
        " Arrows Move | Enter/Space Paint | Del Erase | Tab Pick | Esc Close";

    // ═══════════════════════════════════════════════════════════════
    //  CanvasView — the interactive sprite grid
    // ═══════════════════════════════════════════════════════════════

    internal sealed class CanvasView : View
    {
        private BbcScreenMode _mode;
        private int _cx, _cy;

        public CharSprite Sprite { get; set; }
        public BbcScreenMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public event Action<int, int>? CursorMoved;
        public event Action<int, int>? CellPainted;
        public event Action<int, int>? CellErased;
        public event Action<int, int>? CellPicked;
        public event Action<char>? CharTyped;

        public CanvasView(CharSprite sprite, BbcScreenMode mode)
        {
            Sprite = sprite;
            _mode = mode;
            CanFocus = true;
            CursorVisibility = CursorVisibility.Box;
        }

        protected override bool OnDrawingContent()
        {
            var gridAttr = new Attribute(ColorName16.DarkGray, ColorName16.Black);

            for (var y = 0; y < Viewport.Height; y++)
            {
                for (var x = 0; x < Viewport.Width; x++)
                {
                    if (x < Sprite.Width && y < Sprite.Height)
                    {
                        var cell = Sprite[x, y];
                        var fgPhysical = _mode.DefaultLogicalToPhysical[cell.ForegroundColour];
                        var bgPhysical = _mode.DefaultLogicalToPhysical[cell.BackgroundColour];
                        var fgColour = BbcPalette.ToColorName16(fgPhysical);
                        var bgColour = BbcPalette.ToColorName16(bgPhysical);

                        // Highlight cursor position
                        if (x == _cx && y == _cy)
                        {
                            // Invert colours for cursor
                            (fgColour, bgColour) = (bgColour, fgColour);
                        }

                        var attr = new Attribute(fgColour, bgColour);
                        Application.Driver?.SetAttribute(attr);
                        Move(x, y);
                        Application.Driver?.AddStr(cell.Character == ' ' && x == _cx && y == _cy
                            ? "█"
                            : cell.Character.ToString());
                    }
                    else
                    {
                        // Draw grid dots outside the sprite area
                        Application.Driver?.SetAttribute(gridAttr);
                        Move(x, y);
                        Application.Driver?.AddStr("·");
                    }
                }
            }

            return true;
        }

        protected override bool OnKeyDown(Key keyEvent)
        {
            var handled = true;
            switch (keyEvent.KeyCode)
            {
                case KeyCode.CursorUp:
                    MoveCursor(0, -1);
                    break;
                case KeyCode.CursorDown:
                    MoveCursor(0, 1);
                    break;
                case KeyCode.CursorLeft:
                    MoveCursor(-1, 0);
                    break;
                case KeyCode.CursorRight:
                    MoveCursor(1, 0);
                    break;
                case KeyCode.Home:
                    _cx = 0;
                    CursorMoved?.Invoke(_cx, _cy);
                    SetNeedsDraw();
                    break;
                case KeyCode.End:
                    _cx = Sprite.Width - 1;
                    CursorMoved?.Invoke(_cx, _cy);
                    SetNeedsDraw();
                    break;
                case KeyCode.Enter or KeyCode.Space:
                    CellPainted?.Invoke(_cx, _cy);
                    // Auto-advance cursor right after painting
                    if (_cx < Sprite.Width - 1)
                    {
                        _cx++;
                        CursorMoved?.Invoke(_cx, _cy);
                    }
                    SetNeedsDraw();
                    break;
                case KeyCode.Delete or KeyCode.Backspace:
                    CellErased?.Invoke(_cx, _cy);
                    SetNeedsDraw();
                    break;
                case KeyCode.Tab:
                    CellPicked?.Invoke(_cx, _cy);
                    break;
                default:
                    // Any printable character: set as paint char and paint the cell
                    var ch = KeyToChar(keyEvent.KeyCode);
                    if (ch is not null)
                    {
                        CharTyped?.Invoke(ch.Value);
                        CellPainted?.Invoke(_cx, _cy);
                        // Auto-advance
                        if (_cx < Sprite.Width - 1)
                        {
                            _cx++;
                            CursorMoved?.Invoke(_cx, _cy);
                        }
                        SetNeedsDraw();
                    }
                    else
                    {
                        handled = false;
                    }
                    break;
            }

            return handled || base.OnKeyDown(keyEvent);
        }

        protected override bool OnMouseEvent(MouseEventArgs mouseEvent)
        {
            if (mouseEvent.Position.X < 0 || mouseEvent.Position.Y < 0) return false;

            var mx = mouseEvent.Position.X;
            var my = mouseEvent.Position.Y;

            if (mx < Sprite.Width && my < Sprite.Height)
            {
                _cx = mx;
                _cy = my;
                CursorMoved?.Invoke(_cx, _cy);

                if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Pressed) ||
                    mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
                {
                    CellPainted?.Invoke(_cx, _cy);
                }
                else if (mouseEvent.Flags.HasFlag(MouseFlags.Button3Pressed) ||
                         mouseEvent.Flags.HasFlag(MouseFlags.Button3Clicked))
                {
                    CellErased?.Invoke(_cx, _cy);
                }
                else if (mouseEvent.Flags.HasFlag(MouseFlags.Button2Clicked))
                {
                    CellPicked?.Invoke(_cx, _cy);
                }

                SetNeedsDraw();
                return true;
            }

            return base.OnMouseEvent(mouseEvent);
        }

        public override Point? PositionCursor()
        {
            if (_cx >= 0 && _cx < Viewport.Width && _cy >= 0 && _cy < Viewport.Height)
            {
                Move(_cx, _cy);
                return new Point(_cx, _cy);
            }
            return base.PositionCursor();
        }

        private void MoveCursor(int dx, int dy)
        {
            var nx = Math.Clamp(_cx + dx, 0, Sprite.Width - 1);
            var ny = Math.Clamp(_cy + dy, 0, Sprite.Height - 1);
            if (nx != _cx || ny != _cy)
            {
                _cx = nx;
                _cy = ny;
                CursorMoved?.Invoke(_cx, _cy);
                SetNeedsDraw();
            }
        }

        private static char? KeyToChar(KeyCode code)
        {
            // Strip modifier masks — we only want raw printable keys
            var raw = code & ~KeyCode.CtrlMask & ~KeyCode.AltMask & ~KeyCode.ShiftMask;
            var value = (int)raw;
            if (value is >= 0x20 and < 0x7F)
                return (char)value;
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  PaletteBar — clickable colour swatch strip
    // ═══════════════════════════════════════════════════════════════

    internal sealed class PaletteBar : View
    {
        private BbcScreenMode _mode;
        public int SelectedColour { get; set; }

        public event Action<int>? ColourSelected;

        public PaletteBar(BbcScreenMode mode, int selected)
        {
            _mode = mode;
            SelectedColour = selected;
            CanFocus = false;
        }

        public void UpdateMode(BbcScreenMode mode, int selected)
        {
            _mode = mode;
            SelectedColour = Math.Min(selected, mode.ColourCount - 1);
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent()
        {
            // Each colour swatch is 4 chars wide: "[Xn]" where n is the index
            for (var i = 0; i < _mode.ColourCount && i * 4 < Viewport.Width; i++)
            {
                var physical = _mode.DefaultLogicalToPhysical[i];
                var colour = BbcPalette.ToColorName16(physical);
                var isSelected = i == SelectedColour;

                // Selected swatch has bright white brackets; others use dark grey
                var bracketAttr = isSelected
                    ? new Attribute(ColorName16.BrightCyan, ColorName16.Black)
                    : new Attribute(ColorName16.DarkGray, ColorName16.Black);

                var swatchAttr = new Attribute(colour, colour);

                Application.Driver?.SetAttribute(bracketAttr);
                Move(i * 4, 0);
                Application.Driver?.AddStr(isSelected ? "[" : " ");

                Application.Driver?.SetAttribute(swatchAttr);
                Application.Driver?.AddStr("██");

                Application.Driver?.SetAttribute(bracketAttr);
                Application.Driver?.AddStr(isSelected ? "]" : " ");
            }

            // Label after the swatches
            var labelX = _mode.ColourCount * 4 + 1;
            if (labelX < Viewport.Width)
            {
                var physIdx = _mode.DefaultLogicalToPhysical[SelectedColour];
                var name = BbcPalette.PhysicalColourNames[physIdx];
                var label = $"{SelectedColour}={name}";
                Application.Driver?.SetAttribute(new Attribute(ColorName16.White, ColorName16.Black));
                Move(labelX, 0);
                Application.Driver?.AddStr(label);
            }

            return true;
        }

        protected override bool OnMouseEvent(MouseEventArgs mouseEvent)
        {
            if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked) ||
                mouseEvent.Flags.HasFlag(MouseFlags.Button1Pressed))
            {
                var idx = mouseEvent.Position.X / 4;
                if (idx >= 0 && idx < _mode.ColourCount)
                {
                    SelectedColour = idx;
                    ColourSelected?.Invoke(idx);
                    SetNeedsDraw();
                    return true;
                }
            }

            return base.OnMouseEvent(mouseEvent);
        }
    }
}
