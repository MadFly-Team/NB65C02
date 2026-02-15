using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NB65c02Asm.Bbc;
using NB65c02Asm.Debugger;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace NB65c02Asm.Ide;

internal sealed class IdeApp
{
    private readonly BuildService _buildService;
    private string? _filePath;
    private bool _dirty;
    private EditorView _editor = null!;
    private LineNumberGutter _gutter = null!;
    private TextView _output = null!;
    private Window _window = null!;
    private FrameView _outputFrame = null!;
    private MenuBar _menu = null!;
    private Label _statusLabel = null!;
    private string _themeName = "Dark";
    private FrameView _projectFrame = null!;
    private ListView _projectList = null!;
    private readonly List<string> _projectFiles = [];
    private string? _projectPath;

    public IdeApp(BuildService buildService)
    {
        _buildService = buildService;

        if (_buildService.BeebEmPath is null)
        {
            _buildService.BeebEmPath = DiscoverBeebEm();
        }
    }

    private static string? DiscoverBeebEm()
    {
        string[] candidates =
        [
            @"C:\Program Files\BeebEm\BeebEm.exe",
            @"C:\Program Files (x86)\BeebEm\BeebEm.exe",
            @"C:\BeebEm\BeebEm.exe",
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "BeebEm.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// On Windows the console intercepts Ctrl+S (XOFF) when ENABLE_LINE_INPUT
    /// is set, swallowing it before any application code can see it.
    /// Clearing this flag lets the key through to Terminal.Gui.
    /// </summary>
    private static void DisableLineInput()
    {
        if (!OperatingSystem.IsWindows()) return;

        var handle = GetStdHandle(STD_INPUT_HANDLE);
        if (GetConsoleMode(handle, out int mode))
        {
            mode &= ~ENABLE_LINE_INPUT;
            SetConsoleMode(handle, mode);
        }
    }

    private const int STD_INPUT_HANDLE = -10;
    private const int ENABLE_LINE_INPUT = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

    public void Run(string? initialFile)
    {
        Application.Init();

        try
        {
            var top = new Toplevel();
            SetupUi(top);

            // Give the editor focus once the layout is ready so the
            // block cursor is visible immediately at startup.
            // Also disable ENABLE_LINE_INPUT on Windows — the console
            // intercepts Ctrl+S (XOFF flow control) before TGv2 can
            // see it. Clearing this flag lets the key through.
            top.Ready += (_, _) =>
            {
                DisableLineInput();
                _editor.SetFocus();
            };

            if (initialFile is not null)
            {
                var fullPath = Path.GetFullPath(initialFile);
                if (Path.GetExtension(fullPath).Equals(".nbproj", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                {
                    LoadProject(fullPath);
                }
                else if (File.Exists(fullPath))
                {
                    LoadFile(fullPath);
                }
                else
                {
                    _filePath = fullPath;
                    UpdateTitle();
                }
            }

            Application.Run(top);
        }
        finally
        {
            Application.Shutdown();
        }
    }

    private void SetupUi(Toplevel top)
    {
        const int GutterWidth = 5;
        const int ProjectPaneWidth = 24;

        _menu = new MenuBar
        {
            Menus =
            [
                new MenuBarItem("_File",
                [
                    new MenuItem("_New", "Ctrl+N", NewFile),
                    new MenuItem("_Open...", "Ctrl+O", DoOpen),
                    new MenuItem("_Save", "Ctrl+S", DoSave),
                    new MenuItem("Save _As...", "", DoSaveAs),
                    null!,
                    new MenuItem("_Quit", "Ctrl+Q", DoQuit),
                ]),
                new MenuBarItem("_Project",
                [
                    new MenuItem("_New Project", "", NewProject),
                    new MenuItem("_Open Project...", "", DoOpenProject),
                    new MenuItem("_Save Project", "", DoSaveProject),
                    new MenuItem("Save Project _As...", "", DoSaveProjectAs),
                    null!,
                    new MenuItem("_Add File...", "", DoAddFileToProject),
                    new MenuItem("_Remove File", "", DoRemoveFileFromProject),
                ]),
                new MenuBarItem("_Build",
                [
                    new MenuItem("_Build", "", DoBuild, shortcutKey: Key.F5),
                    new MenuItem("Build & _Run", "", DoBuildAndRun, shortcutKey: Key.F6),
                    new MenuItem("_Debug", "", DoDebug, shortcutKey: Key.F7),
                    null!,
                    new MenuItem("Set _Emulator...", "", DoSetEmulatorPath),
                ]),
                new MenuBarItem("_Sprites",
                [
                    new MenuItem("_New Sprite...", "", DoNewSprite, shortcutKey: Key.F8),
                    new MenuItem("_Edit Sprite...", "", DoEditSprite),
                    null!,
                    new MenuItem("_Demo Sprites...", "", DoSpriteDemo),
                    new MenuItem("_Open Sprite File...", "", DoOpenSpriteFile),
                    new MenuItem("_Export ASM...", "", DoExportSpriteAsm),
                ]),
                new MenuBarItem("_View",
                [
                    new MenuItem("Theme: _Dark", "", () => ApplyTheme("Dark")),
                    new MenuItem("Theme: _Blue", "", () => ApplyTheme("Blue")),
                    new MenuItem("Theme: _Green", "", () => ApplyTheme("Green")),
                    new MenuItem("Theme: _Light", "", () => ApplyTheme("Light")),
                ]),
                new MenuBarItem("_Help",
                [
                    new MenuItem("_Help", "", ShowHelp, shortcutKey: Key.F1),
                    new MenuItem("_About", "", ShowAbout),
                ]),
            ]
        };

        _window = new Window
        {
            Title = "NB65c02Asm — [untitled]",
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        _projectFrame = new FrameView
        {
            Title = "Project",
            X = 0,
            Y = 0,
            Width = ProjectPaneWidth,
            Height = Dim.Fill(9),
        };

        _projectList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        _projectList.OpenSelectedItem += (_, args) =>
        {
            if (args.Item >= 0 && args.Item < _projectFiles.Count)
            {
                OpenProjectFile(args.Item);
            }
        };

        _projectFrame.Add(_projectList);

        _editor = new EditorView(key =>
        {
            switch (key)
            {
                case KeyCode.N | KeyCode.CtrlMask: NewFile(); break;
                case KeyCode.O | KeyCode.CtrlMask: DoOpen(); break;
                case KeyCode.S | KeyCode.CtrlMask: DoSave(); break;
                case KeyCode.Q | KeyCode.CtrlMask: DoQuit(); break;
            }
        })
        {
            X = ProjectPaneWidth + GutterWidth,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(9),
        };

        _editor.VerticalScrollBar.AutoShow = true;
        _editor.HorizontalScrollBar.AutoShow = true;

        _gutter = new LineNumberGutter(_editor)
        {
            X = ProjectPaneWidth,
            Y = 0,
            Width = GutterWidth,
            Height = Dim.Fill(9),
        };

        _outputFrame = new FrameView
        {
            Title = "Output",
            X = 0,
            Y = Pos.Bottom(_editor),
            Width = Dim.Fill(),
            Height = 9,
        };

        _output = new TextView
        {
            ReadOnly = true,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        _outputFrame.Add(_output);
        _window.Add(_projectFrame, _gutter, _editor, _outputFrame);

        _statusLabel = new Label
        {
            Text = " F1 Help | F5 Build | F6 Build+Run | F7 Debug | F8 Sprite | Ctrl+S Save | Ctrl+Q Quit",
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };

        top.Add(_menu, _window, _statusLabel);

        _editor.ContentsChanged += (_, _) =>
        {
            if (!_dirty)
            {
                _dirty = true;
                UpdateTitle();
            }
        };

        _editor.ContentsChanged += (_, _) => _gutter.SetNeedsDraw();
        _editor.ViewportChanged += (_, _) => _gutter.SetNeedsDraw();

        ApplyTheme(_themeName);
    }

    private static void ShowHelp()
    {
        const string help = """
            ╔═════════════════════════════════════════════════════════╗
            ║               NB65c02Asm — Quick Reference              ║
            ╠═════════════════════════════════════════════════════════╣
            ║  FILE                                                   ║
            ║    Ctrl+N       New file                                ║
            ║    Ctrl+O       Open file                               ║
            ║    Ctrl+S       Save file                               ║
            ║    Ctrl+Q       Quit (auto-saves project)               ║
            ║                                                         ║
            ║  BUILD                                                  ║
            ║    F5            Build                                  ║
            ║    F6            Build & Run in BeebEm                  ║
            ║    F7            Build & Debug (65C02 debugger)         ║
            ║                                                         ║
            ║  EDITOR                                                 ║
            ║    Shift+Arrows  Select text                            ║
            ║    Ctrl+C        Copy                                   ║
            ║    Ctrl+X        Cut                                    ║
            ║    Ctrl+V        Paste                                  ║
            ║    Ctrl+Z        Undo                                   ║
            ║    Ctrl+Y        Redo                                   ║
            ║    Ctrl+A        Select all                             ║
            ║    Home/End      Start / end of line                    ║
            ║    Ctrl+Home     Start of file                          ║
            ║    Ctrl+End      End of file                            ║
            ║    Page Up/Down  Scroll page                            ║
            ║                                                         ║
            ║  PROJECT                                                ║
            ║    Project > New Project      Create new .nbproj        ║
            ║    Project > Open Project     Open existing project     ║
            ║    Project > Add File         Add .asm file to project  ║
            ║    Project > Remove File      Remove selected file      ║
            ║    Enter (project list)       Open file in editor       ║
            ║                                                         ║
            ║  VIEW                                                   ║
            ║    View > Theme: Dark/Blue/Green/Light                  ║
            ║                                                         ║
            ║  SPRITE EDITOR (F8 or Sprites menu)                     ║
            ║    Arrows        Move cursor on canvas                  ║
            ║    Type char     Set paint char and paint cell          ║
            ║    Enter/Space   Paint cell with current char/colours   ║
            ║    Del/Bksp      Erase cell (set to space)              ║
            ║    Tab           Pick cell (copy char+colours)          ║
            ║    Home/End      Jump to start/end of row               ║
            ║    Left-click    Paint cell at mouse position           ║
            ║    Right-click   Erase cell at mouse position           ║
            ║    Middle-click  Pick cell at mouse position            ║
            ║    FG/BG bars    Click swatch to select colour          ║
            ║                                                         ║
            ║  DEBUGGER (when open)                                   ║
            ║    F10           Step one instruction                   ║
            ║    F5            Run (continuous execution)             ║
            ║    F6            Stop                                   ║
            ║    F8            Reset CPU                              ║
            ║    Esc           Close debugger                         ║
            ║    OS calls show named MOS vectors, e.g. [OSWRCH]       ║
            ║                                                         ║
            ║  ASSEMBLY DIRECTIVES                                    ║
            ║    .org $ADDR    Set origin / load address              ║
            ║    .byte N,N,..  Emit raw bytes or char literals        ║
            ║    .word N,N,..  Emit 16-bit words (little-endian)      ║
            ║    .text "STR"   Emit ASCII string                      ║
            ║    .include "F"  Include another source file            ║
            ║    .output "F"   Set SSD output path                    ║
            ║    SYM = EXPR    Define a constant symbol               ║
            ║                                                         ║
            ║  LABELS                                                 ║
            ║    loop:         Define a label (plain)                 ║
            ║    .loop:        Define a label (dot-prefixed)          ║
            ║    BNE loop      Reference a label                      ║
            ║    BNE .loop     Reference with dot prefix              ║
            ║    Forward references are fully supported.              ║
            ║                                                         ║
            ║  65C02 INSTRUCTION SET (all modes supported)            ║
            ║    Load:  LDA LDX LDY  Store: STA STX STY STZ           ║
            ║    Math:  ADC SBC INC DEC INX DEX INY DEY               ║
            ║    Logic: AND ORA EOR BIT ASL LSR ROL ROR               ║
            ║    CMP:   CMP CPX CPY                                   ║
            ║    Jump:  JMP JSR RTS RTI BRK NOP                       ║
            ║    Branch: BCC BCS BEQ BMI BNE BPL BVC BVS BRA          ║
            ║    Stack: PHA PLA PHP PLP PHX PHY PLX PLY               ║
            ║    Xfer:  TAX TAY TXA TYA TSX TXS                       ║
            ║    Flag:  CLC SEC CLD SED CLI SEI CLV                   ║
            ║    Test:  TRB TSB                                       ║
            ║                                                         ║
            ║  HELP                                                   ║
            ║    F1             Show this help                        ║
            ╚═════════════════════════════════════════════════════════╝
            """;

        var dialog = new Dialog
        {
            Title = "Help — NB65c02Asm",
            Width = Dim.Percent(75),
            Height = Dim.Percent(85),
        };

        var tv = new TextView
        {
            Text = help,
            ReadOnly = true,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        var ok = new Button { Text = "OK", IsDefault = true };
        ok.Accepting += (_, _) => Application.RequestStop();

        dialog.Add(tv);
        dialog.AddButton(ok);
        Application.Run(dialog);
        dialog.Dispose();
    }

    private static void ShowAbout()
    {
        MessageBox.Query("About NB65c02Asm",
            "NB65c02Asm\n\n" +
            "A 65C02 macro assembler, IDE and debugger\n" +
            "for BBC Micro / Acorn DFS development.\n\n" +
            "Built with .NET 10 and Terminal.Gui v2.",
            "OK");
    }

    private void ApplyTheme(string name)
    {
        _themeName = name;
        var (main, menu, gutter, output, project) = GetTheme(name);

        // Editor-specific scheme: Normal/HotFocus use a distinct selection background
        // so the block highlight is visible regardless of which attribute TGv2 uses.
        var editor = name switch
        {
            "Blue" => new ColorScheme(
                new Attribute(ColorName16.White, ColorName16.DarkGray),
                new Attribute(ColorName16.White, ColorName16.Blue),
                new Attribute(ColorName16.Yellow, ColorName16.DarkGray),
                new Attribute(ColorName16.Gray, ColorName16.Blue),
                new Attribute(ColorName16.White, ColorName16.DarkGray)),
            "Green" => new ColorScheme(
                new Attribute(ColorName16.Green, ColorName16.DarkGray),
                new Attribute(ColorName16.Green, ColorName16.Black),
                new Attribute(ColorName16.BrightGreen, ColorName16.DarkGray),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.Green, ColorName16.DarkGray)),
            "Light" => new ColorScheme(
                new Attribute(ColorName16.Black, ColorName16.BrightCyan),
                new Attribute(ColorName16.Black, ColorName16.White),
                new Attribute(ColorName16.Red, ColorName16.BrightCyan),
                new Attribute(ColorName16.DarkGray, ColorName16.White),
                new Attribute(ColorName16.Black, ColorName16.BrightCyan)),
            _ => new ColorScheme(
                new Attribute(ColorName16.White, ColorName16.Blue),
                new Attribute(ColorName16.White, ColorName16.Black),
                new Attribute(ColorName16.Yellow, ColorName16.Blue),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.White, ColorName16.Blue)),
        };

        _window.ColorScheme = main;
        _editor.ColorScheme = editor;
        _outputFrame.ColorScheme = output;
        _output.ColorScheme = output;
        _gutter.ColorScheme = gutter;
        _menu.ColorScheme = menu;
        _statusLabel.ColorScheme = menu;
        _projectFrame.ColorScheme = project;
        _projectList.ColorScheme = project;

        Application.Top?.SetNeedsDraw();
    }

    private static (ColorScheme Main, ColorScheme Menu, ColorScheme Gutter, ColorScheme Output, ColorScheme Project) GetTheme(string name) => name switch
    {
        "Blue" => (
            new ColorScheme(
                new Attribute(ColorName16.White, ColorName16.Blue),
                new Attribute(ColorName16.Black, ColorName16.Cyan),
                new Attribute(ColorName16.Yellow, ColorName16.Blue),
                new Attribute(ColorName16.Gray, ColorName16.Blue),
                new Attribute(ColorName16.Yellow, ColorName16.Cyan)),
            new ColorScheme(
                new Attribute(ColorName16.Black, ColorName16.Cyan),
                new Attribute(ColorName16.White, ColorName16.Blue),
                new Attribute(ColorName16.Red, ColorName16.Cyan),
                new Attribute(ColorName16.DarkGray, ColorName16.Cyan),
                new Attribute(ColorName16.Red, ColorName16.Blue)),
            new ColorScheme(
                new Attribute(ColorName16.Cyan, ColorName16.Blue),
                new Attribute(ColorName16.Cyan, ColorName16.Blue),
                new Attribute(ColorName16.Cyan, ColorName16.Blue),
                new Attribute(ColorName16.DarkGray, ColorName16.Blue),
                new Attribute(ColorName16.Cyan, ColorName16.Blue)),
            MakeUniformScheme(ColorName16.BrightCyan, ColorName16.Blue),
            new ColorScheme(
                new Attribute(ColorName16.White, ColorName16.DarkGray),
                new Attribute(ColorName16.Black, ColorName16.Cyan),
                new Attribute(ColorName16.Yellow, ColorName16.DarkGray),
                new Attribute(ColorName16.Gray, ColorName16.DarkGray),
                new Attribute(ColorName16.Yellow, ColorName16.Cyan))
        ),
        "Green" => (
            new ColorScheme(
                new Attribute(ColorName16.Green, ColorName16.Black),
                new Attribute(ColorName16.BrightGreen, ColorName16.DarkGray),
                new Attribute(ColorName16.BrightGreen, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.BrightGreen, ColorName16.DarkGray)),
            new ColorScheme(
                new Attribute(ColorName16.Black, ColorName16.Green),
                new Attribute(ColorName16.White, ColorName16.DarkGray),
                new Attribute(ColorName16.BrightGreen, ColorName16.Green),
                new Attribute(ColorName16.DarkGray, ColorName16.Green),
                new Attribute(ColorName16.White, ColorName16.DarkGray)),
            new ColorScheme(
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black)),
            MakeUniformScheme(ColorName16.Green, ColorName16.Black),
            new ColorScheme(
                new Attribute(ColorName16.Green, ColorName16.Black),
                new Attribute(ColorName16.Black, ColorName16.Green),
                new Attribute(ColorName16.BrightGreen, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.BrightGreen, ColorName16.Green))
        ),
        "Light" => (
            new ColorScheme(
                new Attribute(ColorName16.Black, ColorName16.White),
                new Attribute(ColorName16.Black, ColorName16.Gray),
                new Attribute(ColorName16.Red, ColorName16.White),
                new Attribute(ColorName16.DarkGray, ColorName16.White),
                new Attribute(ColorName16.Black, ColorName16.Gray)),
            new ColorScheme(
                new Attribute(ColorName16.Black, ColorName16.Gray),
                new Attribute(ColorName16.Black, ColorName16.White),
                new Attribute(ColorName16.DarkGray, ColorName16.Gray),
                new Attribute(ColorName16.DarkGray, ColorName16.Gray),
                new Attribute(ColorName16.DarkGray, ColorName16.White)),
            new ColorScheme(
                new Attribute(ColorName16.Gray, ColorName16.White),
                new Attribute(ColorName16.Gray, ColorName16.White),
                new Attribute(ColorName16.Gray, ColorName16.White),
                new Attribute(ColorName16.Gray, ColorName16.White),
                new Attribute(ColorName16.Gray, ColorName16.White)),
            MakeUniformScheme(ColorName16.Black, ColorName16.White),
            new ColorScheme(
                new Attribute(ColorName16.Black, ColorName16.Gray),
                new Attribute(ColorName16.White, ColorName16.DarkGray),
                new Attribute(ColorName16.DarkGray, ColorName16.Gray),
                new Attribute(ColorName16.DarkGray, ColorName16.Gray),
                new Attribute(ColorName16.Black, ColorName16.DarkGray))
        ),
        _ => (
            new ColorScheme(
                new Attribute(ColorName16.White, ColorName16.Black),
                new Attribute(ColorName16.White, ColorName16.DarkGray),
                new Attribute(ColorName16.Yellow, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.Yellow, ColorName16.DarkGray)),
            new ColorScheme(
                new Attribute(ColorName16.Black, ColorName16.Gray),
                new Attribute(ColorName16.White, ColorName16.Blue),
                new Attribute(ColorName16.White, ColorName16.Gray),
                new Attribute(ColorName16.DarkGray, ColorName16.Gray),
                new Attribute(ColorName16.Yellow, ColorName16.Blue)),
            new ColorScheme(
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black)),
            MakeUniformScheme(ColorName16.Gray, ColorName16.Black),
            new ColorScheme(
                new Attribute(ColorName16.Gray, ColorName16.Black),
                new Attribute(ColorName16.White, ColorName16.DarkGray),
                new Attribute(ColorName16.Cyan, ColorName16.Black),
                new Attribute(ColorName16.DarkGray, ColorName16.Black),
                new Attribute(ColorName16.Cyan, ColorName16.DarkGray))
        ),
    };

    private static ColorScheme MakeUniformScheme(ColorName16 fg, ColorName16 bg)
    {
        var a = new Attribute(fg, bg);
        return new ColorScheme(a, a, a, a, a);
    }

    private void UpdateTitle()
    {
        var projName = _projectPath is not null ? Path.GetFileNameWithoutExtension(_projectPath) : null;
        var name = _filePath is not null ? Path.GetFileName(_filePath) : "[untitled]";
        var mod = _dirty ? " •" : "";
        _window.Title = projName is not null
            ? $"NB65c02Asm — {projName} — {name}{mod}"
            : $"NB65c02Asm — {name}{mod}";
    }

    private void SetOutput(string text)
    {
        _output.Text = text;
    }

    private void LoadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _editor.Text = File.ReadAllText(fullPath, Encoding.UTF8);
        _filePath = fullPath;
        _dirty = false;
        UpdateTitle();
        SetOutput($"Loaded {fullPath}");

        if (!_projectFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            _projectFiles.Add(fullPath);
            RefreshProjectList();
        }
    }

    private bool SaveToCurrentPath()
    {
        if (_filePath is null)
        {
            DoSaveAs();
            return _filePath is not null && !_dirty;
        }

        try
        {
            File.WriteAllText(_filePath, _editor.Text, Encoding.UTF8);
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR saving file: {ex.Message}");
            return false;
        }
    }

    private void NewFile()
    {
        if (_dirty && !ConfirmDiscard()) return;
        _filePath = null;
        _editor.Text = "";
        _dirty = false;
        UpdateTitle();
        SetOutput("");
    }

    private void DoOpen()
    {
        if (_dirty && !ConfirmDiscard()) return;

        var dialog = new OpenDialog
        {
            Title = "Open Assembly File",
            AllowsMultipleSelection = false,
            MustExist = true,
            AllowedTypes =
            [
                new AllowedType("Assembly Files", ".asm", ".s"),
                new AllowedTypeAny(),
            ],
        };

        if (_filePath is not null)
        {
            dialog.Path = Path.GetDirectoryName(Path.GetFullPath(_filePath)) ?? Environment.CurrentDirectory;
        }

        Application.Run(dialog);

        try
        {
            if (dialog.Canceled) return;

            // In TG v2, FilePaths wraps Path into a list when not cancelled.
            // Fall back to Path directly for robustness.
            var selected = dialog.FilePaths.Count > 0
                ? dialog.FilePaths[0]
                : dialog.Path;

            if (string.IsNullOrEmpty(selected)) return;

            if (!File.Exists(selected))
            {
                SetOutput($"File not found: {selected}");
                return;
            }

            LoadFile(selected);
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR opening file: {ex.Message}");
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void DoSave()
    {
        if (SaveToCurrentPath())
        {
            SetOutput($"Saved {_filePath}");
        }
    }

    private void DoSaveAs()
    {
        var dialog = new SaveDialog
        {
            Title = "Save Assembly File",
            AllowedTypes =
            [
                new AllowedType("Assembly Files", ".asm", ".s"),
                new AllowedTypeAny(),
            ],
        };

        if (_filePath is not null)
        {
            dialog.Path = Path.GetFullPath(_filePath);
        }

        Application.Run(dialog);

        try
        {
            if (dialog.Canceled) return;

            // In TG v2, SaveDialog.Path is the full file path the user chose.
            // SaveDialog.FileName also returns the full path (not just the name).
            var savePath = dialog.Path;
            if (string.IsNullOrEmpty(savePath)) return;

            savePath = Path.GetFullPath(savePath);

            // Guard against saving to a directory instead of a file.
            if (Directory.Exists(savePath))
            {
                SetOutput("ERROR: Selected path is a directory. Please specify a filename.");
                return;
            }

            // Ensure parent directory exists.
            var parentDir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            File.WriteAllText(savePath, _editor.Text, Encoding.UTF8);
            _filePath = savePath;
            _dirty = false;
            UpdateTitle();
            SetOutput($"Saved {savePath}");
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR saving file: {ex.Message}");
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void DoQuit()
    {
        if (_dirty && !ConfirmDiscard()) return;
        AutoSaveProject();
        Application.RequestStop();
    }

    private void AutoSaveProject()
    {
        if (_projectFiles.Count == 0 || _projectPath is null) return;
        SaveProjectToPath(_projectPath);
    }

    private bool ConfirmDiscard()
    {
        var result = MessageBox.Query("Unsaved Changes",
            "You have unsaved changes. Discard them?",
            "Discard", "Cancel");
        return result == 0;
    }

    private BuildResult? PerformBuild()
    {
        if (_filePath is not null && _dirty)
        {
            if (!SaveToCurrentPath()) { SetOutput("ERROR: Save cancelled."); return null; }
        }

        try
        {
            BuildResult result;
            if (_projectFiles.Count > 0)
            {
                result = _buildService.BuildProject(_projectFiles);
            }
            else if (_filePath is not null)
            {
                var source = File.ReadAllText(_filePath, Encoding.UTF8);
                result = _buildService.Build(source, _filePath);
            }
            else
            {
                SetOutput("ERROR: No files to build. Open or add files first.");
                return null;
            }

            SetOutput(result.Message);
            return result;
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR: {ex.Message}");
            return null;
        }
    }

    private void DoBuild()
    {
        PerformBuild();
    }

    private void DoBuildAndRun()
    {
        if (_buildService.BeebEmPath is null)
        {
            PromptForEmulatorPath();
            if (_buildService.BeebEmPath is null)
            {
                SetOutput("Build & Run cancelled — no emulator configured.");
                return;
            }
        }

        var result = PerformBuild();
        if (result is null || !result.Success) return;

        if (result.SsdPath is null)
        {
            SetOutput(result.Message + "\n\nNo .ssd produced; cannot launch emulator.");
            return;
        }

        try
        {
            var ssdFull = Path.GetFullPath(result.SsdPath);
            var psi = new ProcessStartInfo
            {
                FileName = _buildService.BeebEmPath,
                Arguments = $"\"{ssdFull}\"",
                UseShellExecute = false,
            };
            Process.Start(psi);
            SetOutput(result.Message + $"\n\nLaunched BeebEm with {ssdFull}");
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR: {ex.Message}");
        }
    }

    private void DoDebug()
    {
        var result = PerformBuild();
        if (result is null || !result.Success) return;

        if (result.ObjectBytes is null || result.ObjectBytes.Length == 0)
        {
            SetOutput(result.Message + "\n\nNo object code to debug.");
            return;
        }

        var debugger = new DebuggerWindow(result.ObjectBytes, result.LoadAddress);
        Application.Run(debugger);
        debugger.Dispose();
        SetOutput("Debugger closed.");
    }

    private void DoSetEmulatorPath()
    {
        PromptForEmulatorPath();
        if (_buildService.BeebEmPath is not null)
        {
            SetOutput($"Emulator set to: {_buildService.BeebEmPath}");
        }
    }

    private void PromptForEmulatorPath()
    {
        var dialog = new OpenDialog
        {
            Title = "Locate BeebEm Executable",
            AllowsMultipleSelection = false,
            MustExist = true,
            AllowedTypes =
            [
                new AllowedType("Executables", ".exe"),
                new AllowedTypeAny(),
            ],
        };

        Application.Run(dialog);

        try
        {
            if (dialog.Canceled) return;

            var selected = dialog.FilePaths.Count > 0
                ? dialog.FilePaths[0]
                : dialog.Path;

            if (!string.IsNullOrEmpty(selected) && File.Exists(selected))
            {
                _buildService.BeebEmPath = Path.GetFullPath(selected);
            }
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private CharSpriteSheet? _lastSpriteSheet;

    private void DoNewSprite()
    {
        var dlg = new Dialog { Title = "New Sprite", Width = 50, Height = 18 };

        var modeLabel = new Label { Text = "BBC Mode:", X = 1, Y = 1 };
        var modeItems = new List<string>();
        foreach (var m in BbcScreenMode.AllModes)
            modeItems.Add($"MODE {m.ModeNumber}: {m.ColourCount} colours");
        var modeList = new ListView
        {
            X = 12,
            Y = 1,
            Width = 30,
            Height = 4,
            Source = new ListWrapper<string>(new ObservableCollection<string>(modeItems)),
            SelectedItem = 2,
        };

        var nameLabel = new Label { Text = "Name:", X = 1, Y = 6 };
        var nameField = new TextField { Text = "Sprite1", X = 12, Y = 6, Width = 20 };

        var wLabel = new Label { Text = "Width:", X = 1, Y = 8 };
        var wField = new TextField { Text = "8", X = 12, Y = 8, Width = 8 };
        var hLabel = new Label { Text = "Height:", X = 1, Y = 10 };
        var hField = new TextField { Text = "8", X = 12, Y = 10, Width = 8 };

        // Capture the user's choices so the editor can be launched
        // after the dialog's run-loop has fully exited.
        int? chosenMode = null;
        string? chosenName = null;
        int chosenW = 0, chosenH = 0;

        var ok = new Button { Text = "Create", IsDefault = true };
        ok.Accepting += (_, _) =>
        {
            var name = (nameField.Text ?? "Sprite1").Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Sprite1";

            if (int.TryParse(wField.Text, out var w) && int.TryParse(hField.Text, out var h) &&
                w is > 0 and <= 80 && h is > 0 and <= 32)
            {
                var mode = modeList.SelectedItem;
                if (mode < 0 || mode > 7) mode = 2;

                chosenMode = mode;
                chosenName = name;
                chosenW = w;
                chosenH = h;
                Application.RequestStop(dlg);
            }
            else
            {
                MessageBox.ErrorQuery("Invalid Size", "Width: 1–80, Height: 1–32.", "OK");
            }
        };

        var cancel = new Button { Text = "Cancel" };
        cancel.Accepting += (_, _) => Application.RequestStop(dlg);

        dlg.Add(modeLabel, modeList, nameLabel, nameField, wLabel, wField, hLabel, hField);
        dlg.AddButton(ok);
        dlg.AddButton(cancel);
        Application.Run(dlg);
        dlg.Dispose();

        // Launch the sprite editor only after the dialog has fully closed.
        if (chosenMode is not null)
        {
            var editor = new SpriteEditorWindow(chosenMode.Value, chosenName!, chosenW, chosenH);
            Application.Run(editor);
            if (editor.IsDirty || editor.Sprite is not null)
            {
                // Use the sprite's current TargetMode — the user may have
                // changed the mode inside the editor.
                var actualMode = editor.Sprite.TargetMode;
                var sheet = new CharSpriteSheet(actualMode);
                sheet.Add(editor.Sprite);
                _lastSpriteSheet = sheet;
                SetOutput($"Sprite \"{editor.Sprite.Name}\" created ({editor.Sprite.Width}×{editor.Sprite.Height}, MODE {actualMode}).");
            }
            editor.Dispose();
        }
    }

    private void DoEditSprite()
    {
        if (_lastSpriteSheet is null || _lastSpriteSheet.Count == 0)
        {
            SetOutput("No sprites loaded. Create a new sprite or open a sprite file first.");
            return;
        }

        // If multiple sprites, let the user pick one
        var sprites = _lastSpriteSheet.Sprites.ToList();
        CharSprite spriteToEdit;

        if (sprites.Count == 1)
        {
            spriteToEdit = sprites[0];
        }
        else
        {
            var dlg = new Dialog { Title = "Select Sprite to Edit", Width = 50, Height = 16 };
            var items = sprites.Select(s => $"{s.Name} ({s.Width}×{s.Height})").ToList();
            var list = new ListView
            {
                X = 1, Y = 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(1),
                Source = new ListWrapper<string>(new ObservableCollection<string>(items)),
            };

            CharSprite? selected = null;
            var ok = new Button { Text = "Edit", IsDefault = true };
            ok.Accepting += (_, _) =>
            {
                var idx = list.SelectedItem;
                if (idx >= 0 && idx < sprites.Count)
                {
                    selected = sprites[idx];
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

            if (selected is null) return;
            spriteToEdit = selected;
        }

        var editor = new SpriteEditorWindow(spriteToEdit);
        Application.Run(editor);

        if (editor.IsDirty)
        {
            // Replace the sheet with the edited sprite
            var sheet = new CharSpriteSheet(editor.Sprite.TargetMode);
            foreach (var s in sprites)
            {
                if (string.Equals(s.Name, spriteToEdit.Name, StringComparison.OrdinalIgnoreCase))
                    sheet.Add(editor.Sprite);
                else
                    sheet.Add(s);
            }
            _lastSpriteSheet = sheet;
            SetOutput($"Sprite \"{editor.Sprite.Name}\" updated.");
        }

        editor.Dispose();
    }

    private void DoSpriteDemo()
    {
        // Build a demo sprite sheet in MODE 2 (16 colours) showcasing the system.
        var sheet = new CharSpriteSheet(2);

        // --- Player sprite (MODE 2 — 16 logical colours) ---
        var player = sheet.Add("Player", 5, 5);
        player.SetRow(0, "  ^  ", [0, 0, 3, 0, 0], 0);  // yellow hat
        player.SetRow(1, " /O\\ ", [0, 7, 1, 7, 0], 0); // white body, red face
        player.SetRow(2, " |X| ", [0, 6, 3, 6, 0], 0);  // cyan arms, yellow body
        player.SetRow(3, " / \\ ", [0, 2, 0, 2, 0], 0);  // green legs
        player.SetRow(4, " d b ", [0, 7, 0, 7, 0], 0);  // white feet

        // --- Enemy sprite ---
        var enemy = sheet.Add("Enemy", 5, 4);
        enemy.SetRow(0, "/vvv\\", [1, 1, 1, 1, 1], 0);  // red top
        enemy.SetRow(1, "|oXo|", [5, 3, 1, 3, 5], 0);   // magenta/yellow/red face
        enemy.SetRow(2, " |Y| ", [0, 5, 3, 5, 0], 0);   // body
        enemy.SetRow(3, " M M ", [0, 1, 0, 1, 0], 0);   // red feet

        // --- Heart pickup ---
        var heart = sheet.Add("Heart", 5, 3);
        heart.SetRow(0, " * * ", [0, 1, 0, 1, 0], 0);
        heart.SetRow(1, " *** ", [0, 1, 1, 1, 0], 0);
        heart.SetRow(2, "  *  ", [0, 0, 1, 0, 0], 0);

        // --- Star sprite (MODE 1 — 4 colours) ---
        var mode1Sheet = new CharSpriteSheet(1);
        var star = mode1Sheet.Add("Star", 3, 3);
        star.SetRow(0, " * ", [0, 3, 0], 0); // colour 3 = white in MODE 1
        star.SetRow(1, "***", [3, 3, 3], 0);
        star.SetRow(2, " * ", [0, 3, 0], 0);

        _lastSpriteSheet = sheet;

        // Show the MODE 2 sprite sheet preview
        var preview = new SpritePreviewWindow(sheet);
        Application.Run(preview);
        preview.Dispose();
        SetOutput("Sprite demo closed. Use Sprites > Export ASM to generate 6502 code.");
    }

    private void DoOpenSpriteFile()
    {
        var dialog = new OpenDialog
        {
            Title = "Open Sprite Definition",
            AllowsMultipleSelection = false,
            MustExist = true,
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

            var selected = dialog.FilePaths.Count > 0
                ? dialog.FilePaths[0]
                : dialog.Path;

            if (string.IsNullOrEmpty(selected) || !File.Exists(selected)) return;

            var sheet = SpriteDefinitionFile.Load(selected);
            _lastSpriteSheet = sheet;

            var preview = new SpritePreviewWindow(sheet);
            Application.Run(preview);
            preview.Dispose();
            SetOutput($"Loaded sprite file: {selected} ({sheet.Count} sprites, MODE {sheet.TargetMode})");
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR loading sprites: {ex.Message}");
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void DoExportSpriteAsm()
    {
        if (_lastSpriteSheet is null || _lastSpriteSheet.Count == 0)
        {
            SetOutput("No sprites loaded. Open a sprite file or run the demo first.");
            return;
        }

        var asm = SpriteCodeGenerator.GenerateSheetAssembly(_lastSpriteSheet);

        var dialog = new SaveDialog
        {
            Title = "Export Sprite Assembly",
            AllowedTypes =
            [
                new AllowedType("Assembly Files", ".asm", ".s"),
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

            File.WriteAllText(savePath, asm, Encoding.UTF8);
            SetOutput($"Exported sprite assembly to {savePath}");
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR exporting sprites: {ex.Message}");
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void NewProject()
    {
        if (_projectFiles.Count > 0)
        {
            var result = MessageBox.Query("New Project",
                "Close the current project?",
                "OK", "Cancel");
            if (result != 0) return;
        }

        AutoSaveProject();
        _projectFiles.Clear();
        _projectPath = null;
        _filePath = null;
        _editor.Text = "";
        _dirty = false;
        UpdateTitle();
        RefreshProjectList();
        SetOutput("New project created.");
    }

    private void DoOpenProject()
    {
        AutoSaveProject();

        var dialog = new OpenDialog
        {
            Title = "Open Project",
            AllowsMultipleSelection = false,
            MustExist = true,
            AllowedTypes =
            [
                new AllowedType("NB65 Projects", ".nbproj"),
                new AllowedTypeAny(),
            ],
        };

        if (_projectPath is not null)
        {
            dialog.Path = Path.GetDirectoryName(Path.GetFullPath(_projectPath)) ?? Environment.CurrentDirectory;
        }

        Application.Run(dialog);

        try
        {
            if (dialog.Canceled) return;

            var selected = dialog.FilePaths.Count > 0
                ? dialog.FilePaths[0]
                : dialog.Path;

            if (string.IsNullOrEmpty(selected) || !File.Exists(selected)) return;

            LoadProject(Path.GetFullPath(selected));
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void DoSaveProject()
    {
        if (_projectPath is null)
        {
            DoSaveProjectAs();
            return;
        }

        SaveProjectToPath(_projectPath);
        SetOutput($"Project saved to {_projectPath}");
    }

    private void DoSaveProjectAs()
    {
        var dialog = new SaveDialog
        {
            Title = "Save Project",
            AllowedTypes =
            [
                new AllowedType("NB65 Projects", ".nbproj"),
                new AllowedTypeAny(),
            ],
        };

        if (_projectPath is not null)
        {
            dialog.Path = Path.GetFullPath(_projectPath);
        }
        else if (_filePath is not null)
        {
            dialog.Path = Path.GetDirectoryName(Path.GetFullPath(_filePath)) ?? Environment.CurrentDirectory;
        }

        Application.Run(dialog);

        try
        {
            if (dialog.Canceled) return;

            var savePath = dialog.Path;
            if (string.IsNullOrEmpty(savePath)) return;

            savePath = Path.GetFullPath(savePath);
            if (Directory.Exists(savePath)) return;

            SaveProjectToPath(savePath);
            SetOutput($"Project saved to {savePath}");
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void LoadProject(string path)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<ProjectFileData>(json);
            if (data?.Files is null) return;

            var projectDir = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;

            _projectFiles.Clear();
            foreach (var relative in data.Files)
            {
                var full = Path.GetFullPath(Path.Combine(projectDir, relative));
                if (File.Exists(full))
                {
                    _projectFiles.Add(full);
                }
            }

            _projectPath = path;
            _dirty = false;
            RefreshProjectList();

            if (_projectFiles.Count > 0)
            {
                LoadFile(_projectFiles[0]);
            }

            UpdateTitle();
            SetOutput($"Project loaded: {path} ({_projectFiles.Count} file(s))");
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR loading project: {ex.Message}");
        }
    }

    private void SaveProjectToPath(string path)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
            var relativePaths = _projectFiles
                .Select(f => Path.GetRelativePath(projectDir, f))
                .ToList();

            var data = new ProjectFileData { Files = relativePaths };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            File.WriteAllText(path, json, Encoding.UTF8);
            _projectPath = path;
        }
        catch (Exception ex)
        {
            SetOutput($"ERROR saving project: {ex.Message}");
        }
    }

    private void DoAddFileToProject()
    {
        var dialog = new OpenDialog
        {
            Title = "Add File to Project",
            AllowsMultipleSelection = true,
            MustExist = true,
            AllowedTypes =
            [
                new AllowedType("Assembly Files", ".asm", ".s"),
                new AllowedTypeAny(),
            ],
        };

        if (_filePath is not null)
        {
            dialog.Path = Path.GetDirectoryName(Path.GetFullPath(_filePath)) ?? Environment.CurrentDirectory;
        }

        Application.Run(dialog);

        try
        {
            if (dialog.Canceled) return;

            foreach (var file in dialog.FilePaths)
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (!_projectFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                    {
                        _projectFiles.Add(fullPath);
                    }
                }
            }

            RefreshProjectList();
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private void DoRemoveFileFromProject()
    {
        var index = _projectList.SelectedItem;
        if (index < 0 || index >= _projectFiles.Count) return;

        _projectFiles.RemoveAt(index);
        RefreshProjectList();
    }

    private void OpenProjectFile(int index)
    {
        if (index < 0 || index >= _projectFiles.Count) return;

        var path = _projectFiles[index];
        if (!File.Exists(path))
        {
            SetOutput($"File not found: {path}");
            return;
        }

        if (_dirty && !ConfirmDiscard()) return;

        LoadFile(path);
    }

    private void RefreshProjectList()
    {
        var names = _projectFiles.Select(f => Path.GetFileName(f)!).ToList();
        _projectList.SetSource(new ObservableCollection<string>(names));
        _projectList.SetNeedsDraw();
    }

    private sealed class LineNumberGutter : View
    {
        private readonly EditorView _editor;

        public LineNumberGutter(EditorView editor)
        {
            _editor = editor;
            CanFocus = false;
        }

        protected override bool OnDrawingContent()
        {
            var topRow = _editor.TopRow;
            var totalLines = Math.Max(1, _editor.Lines);
            var height = Viewport.Height;
            var width = Viewport.Width;
            var attr = ColorScheme?.Normal ?? new Attribute(ColorName16.DarkGray, ColorName16.Black);

            Application.Driver?.SetAttribute(attr);

            for (var i = 0; i < height; i++)
            {
                Move(0, i);
                var lineNum = topRow + i + 1;
                if (lineNum <= totalLines)
                {
                    Application.Driver?.AddStr(lineNum.ToString().PadLeft(width - 1) + " ");
                }
                else
                {
                    Application.Driver?.AddStr(new string(' ', width));
                }
            }

            return true;
        }
    }

    private sealed class ProjectFileData
    {
        public List<string> Files { get; set; } = [];
    }

    private sealed class EditorView : TextView
    {
        private bool _prevSelecting;
        private Point _prevCursorPos;
        private readonly Action<KeyCode>? _onShortcut;

        public EditorView(Action<KeyCode>? onShortcut = null)
        {
            _onShortcut = onShortcut;
        }

        protected override bool OnKeyDown(Key keyEvent)
        {
            // Intercept Ctrl+letter shortcuts before the base TextView
            // consumes them — TGv2 TextView swallows all Ctrl+letter keys.
            var code = keyEvent.KeyCode;
            if (code is (KeyCode.N | KeyCode.CtrlMask)
                     or (KeyCode.O | KeyCode.CtrlMask)
                     or (KeyCode.S | KeyCode.CtrlMask)
                     or (KeyCode.Q | KeyCode.CtrlMask))
            {
                _onShortcut?.Invoke(code);
                return true;
            }

            return base.OnKeyDown(keyEvent);
        }

        protected override void OnDrawSelectionColor(List<Cell> line, int idxCol, int idxRow)
        {
            // Base fires DrawSelectionColor event then overrides the attribute,
            // so the event-based approach cannot work.  Override instead and
            // use ColorScheme.Normal which ApplyTheme sets to the selection colour.
            SetAttribute(ColorScheme.Normal);
        }

        public override Point? PositionCursor()
        {
            // TGv2 MoveRight/Left/Up/Down only call SetNeedsDraw when scrolling.
            // For in-viewport cursor moves we must force a content redraw so that
            // OnDrawSelectionColor is invoked for the updated selection range.
            var selecting = IsSelecting;
            var cur = CursorPosition;
            if (selecting != _prevSelecting || (selecting && cur != _prevCursorPos))
            {
                SetNeedsDraw();
            }
            _prevSelecting = selecting;
            _prevCursorPos = cur;

            var pos = base.PositionCursor();
            Application.Driver?.SetCursorVisibility(CursorVisibility.Box);
            return pos ?? new Point(0, 0);
        }
    }
}
