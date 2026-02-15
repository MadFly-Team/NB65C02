using System.Text;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace NB65c02Asm.Debugger;

internal sealed class DebuggerWindow : Toplevel
{
    private readonly Cpu65C02 _cpu = new();
    private readonly ushort _loadAddress;
    private readonly byte[] _objectBytes;

    private Label _regLabel = null!;
    private TextView _disasmView = null!;
    private TextView _memLeftView = null!;
    private TextView _memRightView = null!;
    private TextField _memLeftAddrField = null!;
    private TextField _memRightAddrField = null!;
    private Label _statusLabel = null!;

    private bool _running;
    private object? _timerId;
    private ushort _memLeftAddress;
    private ushort _memRightAddress;

    // Previous register state for change highlighting
    private ushort _prevPC;
    private byte _prevA, _prevX, _prevY, _prevSP, _prevP;
    private bool _firstRefresh = true;

    public DebuggerWindow(byte[] objectBytes, ushort loadAddress)
    {
        _objectBytes = objectBytes;
        _loadAddress = loadAddress;
        _memLeftAddress = loadAddress;
        _memRightAddress = 0x0000;

        _cpu.Load(objectBytes, loadAddress);
        _cpu.Reset(loadAddress);

        SetupUi();
    }

    private void SetupUi()
    {
        var window = new Window
        {
            Title = "65C02 Debugger",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        // --- Registers pane (left top) ---
        var regFrame = new FrameView
        {
            Title = "Registers",
            X = 0,
            Y = 0,
            Width = Dim.Percent(35),
            Height = Dim.Percent(55),
        };

        _regLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        regFrame.Add(_regLabel);

        // --- Disassembly pane (right top) ---
        var disasmFrame = new FrameView
        {
            Title = "Disassembly",
            X = Pos.Right(regFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(55),
        };

        _disasmView = new TextView
        {
            ReadOnly = true,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        disasmFrame.Add(_disasmView);

        // --- Memory pane left (Code) ---
        var memLeftFrame = new FrameView
        {
            Title = "Memory (Code)",
            X = 0,
            Y = Pos.Bottom(regFrame),
            Width = Dim.Percent(50),
            Height = Dim.Fill(1),
        };
        BuildMemoryPane(memLeftFrame, _memLeftAddress,
            out _memLeftAddrField, out _memLeftView,
            addr => { _memLeftAddress = addr; RefreshMemoryLeft(); },
            delta => MoveMemoryLeft(delta));

        // --- Memory pane right (Zero Page) ---
        var memRightFrame = new FrameView
        {
            Title = "Memory (Zero Page)",
            X = Pos.Right(memLeftFrame),
            Y = Pos.Bottom(regFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        BuildMemoryPane(memRightFrame, _memRightAddress,
            out _memRightAddrField, out _memRightView,
            addr => { _memRightAddress = addr; RefreshMemoryRight(); },
            delta => MoveMemoryRight(delta));

        // --- Control buttons ---
        var btnStep = new Button { Text = "Step", X = 0, Y = Pos.AnchorEnd(1), };
        btnStep.Accepting += (_, _) => DoStep();

        var btnRun = new Button { Text = "Run", X = Pos.Right(btnStep) + 1, Y = Pos.AnchorEnd(1), };
        btnRun.Accepting += (_, _) => DoRun();

        var btnStop = new Button { Text = "Stop", X = Pos.Right(btnRun) + 1, Y = Pos.AnchorEnd(1), };
        btnStop.Accepting += (_, _) => DoStop();

        var btnReset = new Button { Text = "Reset", X = Pos.Right(btnStop) + 1, Y = Pos.AnchorEnd(1), };
        btnReset.Accepting += (_, _) => DoReset();

        var btnClose = new Button { Text = "Close", X = Pos.Right(btnReset) + 1, Y = Pos.AnchorEnd(1), };
        btnClose.Accepting += (_, _) => DoClose();

        window.Add(regFrame, disasmFrame, memLeftFrame, memRightFrame,
            btnStep, btnRun, btnStop, btnReset, btnClose);

        _statusLabel = new Label
        {
            Text = " F10 Step | F5 Run | F6 Stop | F8 Reset | Esc Close",
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };

        Add(window, _statusLabel);

        // Defer initial refresh until layout is complete
        Ready += (_, _) => RefreshAll();

        // Re-render when panes are resized
        _disasmView.ViewportChanged += (_, _) => RefreshDisassembly();
        _memLeftView.ViewportChanged += (_, _) => RefreshMemoryLeft();
        _memRightView.ViewportChanged += (_, _) => RefreshMemoryRight();

        // --- Keyboard shortcuts ---
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case KeyCode.F10:
                    DoStep();
                    e.Handled = true;
                    break;
                case KeyCode.F5:
                    DoRun();
                    e.Handled = true;
                    break;
                case KeyCode.F6:
                    DoStop();
                    e.Handled = true;
                    break;
                case KeyCode.F8:
                    DoReset();
                    e.Handled = true;
                    break;
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

        var accent = new ColorScheme(
            new Attribute(ColorName16.BrightCyan, ColorName16.Black),
            new Attribute(ColorName16.White, ColorName16.DarkGray),
            new Attribute(ColorName16.Yellow, ColorName16.Black),
            new Attribute(ColorName16.DarkGray, ColorName16.Black),
            new Attribute(ColorName16.Yellow, ColorName16.DarkGray));

        var code = new ColorScheme(
            new Attribute(ColorName16.BrightGreen, ColorName16.Black),
            new Attribute(ColorName16.BrightGreen, ColorName16.Black),
            new Attribute(ColorName16.Yellow, ColorName16.Black),
            new Attribute(ColorName16.DarkGray, ColorName16.Black),
            new Attribute(ColorName16.Yellow, ColorName16.Black));

        var bar = new ColorScheme(
            new Attribute(ColorName16.Black, ColorName16.Gray),
            new Attribute(ColorName16.White, ColorName16.Blue),
            new Attribute(ColorName16.White, ColorName16.Gray),
            new Attribute(ColorName16.DarkGray, ColorName16.Gray),
            new Attribute(ColorName16.Yellow, ColorName16.Blue));

        window.ColorScheme = main;
        regFrame.ColorScheme = accent;
        _regLabel.ColorScheme = accent;
        disasmFrame.ColorScheme = main;
        _disasmView.ColorScheme = code;
        memLeftFrame.ColorScheme = main;
        memRightFrame.ColorScheme = main;
        _memLeftView.ColorScheme = code;
        _memRightView.ColorScheme = code;
        _statusLabel.ColorScheme = bar;
    }

    private static void BuildMemoryPane(
        FrameView frame,
        ushort initialAddress,
        out TextField addrField,
        out TextView memView,
        Action<ushort> onApplyAddress,
        Action<int> onMoveMemory)
    {
        var addrLabel = new Label { Text = "Addr:", X = 0, Y = 0, };

        var field = new TextField
        {
            Text = $"{initialAddress:X4}",
            X = Pos.Right(addrLabel) + 1,
            Y = 0,
            Width = 7,
        };
        field.Accepting += (_, _) =>
        {
            var text = field.Text?.Trim() ?? "";
            if (text.StartsWith('$')) text = text[1..];
            if (ushort.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var addr))
                onApplyAddress(addr);
        };

        var btnPB = new Button { Text = "◄◄", X = Pos.Right(field) + 1, Y = 0, NoDecorations = true, NoPadding = true, };
        btnPB.Accepting += (_, _) => onMoveMemory(-128);

        var btnB = new Button { Text = " ◄", X = Pos.Right(btnPB) + 1, Y = 0, NoDecorations = true, NoPadding = true, };
        btnB.Accepting += (_, _) => onMoveMemory(-16);

        var btnF = new Button { Text = "► ", X = Pos.Right(btnB) + 1, Y = 0, NoDecorations = true, NoPadding = true, };
        btnF.Accepting += (_, _) => onMoveMemory(16);

        var btnPF = new Button { Text = "►►", X = Pos.Right(btnF) + 1, Y = 0, NoDecorations = true, NoPadding = true, };
        btnPF.Accepting += (_, _) => onMoveMemory(128);

        var view = new TextView { ReadOnly = true, X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), };

        frame.Add(addrLabel, field, btnPB, btnB, btnF, btnPF, view);
        addrField = field;
        memView = view;
    }

    private void DoStep()
    {
        if (_cpu.Halted)
        {
            SetStatus("CPU halted (BRK)");
            return;
        }

        SnapshotRegisters();
        _cpu.Step();
        RefreshAll();
        SetStatus(_running ? "Running..." : "Stepped");
    }

    private void DoRun()
    {
        if (_running || _cpu.Halted) return;

        _running = true;
        SetStatus("Running...");
        SnapshotRegisters();
        _timerId = Application.AddTimeout(TimeSpan.FromMilliseconds(50), RunBatch);
    }

    private bool RunBatch()
    {
        if (!_running || _cpu.Halted)
        {
            _running = false;
            RefreshAll();
            SetStatus(_cpu.Halted ? "CPU halted (BRK)" : "Stopped");
            return false;
        }

        SnapshotRegisters();
        for (var i = 0; i < 1000; i++)
        {
            _cpu.Step();
            if (_cpu.Halted) break;
        }

        RefreshAll();
        return _running && !_cpu.Halted;
    }

    private void DoStop()
    {
        _running = false;
        SetStatus("Stopped");
        RefreshAll();
    }

    private void DoReset()
    {
        _running = false;
        _cpu.Load(_objectBytes, _loadAddress);
        _cpu.Reset(_loadAddress);
        _firstRefresh = true;
        RefreshAll();
        SetStatus("Reset");
    }

    private void DoClose()
    {
        _running = false;
        Application.RequestStop(this);
    }

    private void SetStatus(string msg) =>
        _statusLabel.Text = $" {msg}  |  F10 Step | F5 Run | F6 Stop | F8 Reset | Esc Close";

    // --- Register snapshot for change highlighting ---

    private void SnapshotRegisters()
    {
        _prevPC = _cpu.PC;
        _prevA = _cpu.A;
        _prevX = _cpu.X;
        _prevY = _cpu.Y;
        _prevSP = _cpu.SP;
        _prevP = _cpu.GetP();
    }

    // --- Refresh helpers ---

    private void RefreshAll()
    {
        RefreshRegisters();
        RefreshDisassembly();
        RefreshMemoryLeft();
        RefreshMemoryRight();
    }

    private void RefreshRegisters()
    {
        var p = _cpu.GetP();
        var flags = $"{(_cpu.N ? 'N' : '-')}{(_cpu.V ? 'V' : '-')}-{(_cpu.D ? 'D' : '-')}{(_cpu.I ? 'I' : '-')}{(_cpu.Z ? 'Z' : '-')}{(_cpu.C ? 'C' : '-')}";

        string M(bool changed) => _firstRefresh ? " " : changed ? "►" : " ";

        var sb = new StringBuilder();
        sb.AppendLine($"{M(_cpu.PC != _prevPC)} PC:  ${_cpu.PC:X4}");
        sb.AppendLine($"{M(_cpu.A != _prevA)}  A:  ${_cpu.A:X2}  ({_cpu.A,3})");
        sb.AppendLine($"{M(_cpu.X != _prevX)}  X:  ${_cpu.X:X2}  ({_cpu.X,3})");
        sb.AppendLine($"{M(_cpu.Y != _prevY)}  Y:  ${_cpu.Y:X2}  ({_cpu.Y,3})");
        sb.AppendLine($"{M(_cpu.SP != _prevSP)} SP:  ${_cpu.SP:X2}");
        sb.AppendLine($"{M(p != _prevP)}  P:  ${p:X2}  {flags}");
        sb.AppendLine();
        sb.AppendLine($" Cycles: {_cpu.Cycles}");
        if (_cpu.Halted) sb.AppendLine(" ** HALTED **");
        _regLabel.Text = sb.ToString();

        if (_firstRefresh)
        {
            SnapshotRegisters();
            _firstRefresh = false;
        }
    }

    private void RefreshDisassembly()
    {
        var lines = Math.Max(_disasmView.Viewport.Height, 16);
        var sb = new StringBuilder();
        var addr = _cpu.PC;
        for (var i = 0; i < lines; i++)
        {
            var marker = addr == _cpu.PC ? ">" : " ";
            var (text, size) = Cpu65C02.Disassemble(_cpu.Memory, addr);

            var bytesStr = new StringBuilder();
            for (var b = 0; b < size; b++)
                bytesStr.Append($"{_cpu.Memory[(ushort)(addr + b)]:X2} ");

            sb.AppendLine($"{marker} ${addr:X4}  {bytesStr,-10} {text}");
            addr += (ushort)size;
        }
        _disasmView.Text = sb.ToString();
    }

    private void RefreshMemoryPane(TextView view, ushort baseAddress)
    {
        var rows = Math.Max(view.Viewport.Height, 4);
        var sb = new StringBuilder();
        var addr = baseAddress;
        for (var row = 0; row < rows; row++)
        {
            sb.Append($"${addr:X4}: ");
            var ascii = new StringBuilder();
            for (var col = 0; col < 8; col++)
            {
                var b = _cpu.Memory[(ushort)(addr + col)];
                sb.Append($"{b:X2} ");
                ascii.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.Append(' ');
            sb.AppendLine(ascii.ToString());
            addr += 8;
        }
        view.Text = sb.ToString();
    }

    private void RefreshMemoryLeft() => RefreshMemoryPane(_memLeftView, _memLeftAddress);
    private void RefreshMemoryRight() => RefreshMemoryPane(_memRightView, _memRightAddress);

    private void MoveMemoryLeft(int delta)
    {
        _memLeftAddress = (ushort)Math.Clamp(_memLeftAddress + delta, 0, 0xFFFF);
        _memLeftAddrField.Text = $"{_memLeftAddress:X4}";
        RefreshMemoryLeft();
    }

    private void MoveMemoryRight(int delta)
    {
        _memRightAddress = (ushort)Math.Clamp(_memRightAddress + delta, 0, 0xFFFF);
        _memRightAddrField.Text = $"{_memRightAddress:X4}";
        RefreshMemoryRight();
    }
}
