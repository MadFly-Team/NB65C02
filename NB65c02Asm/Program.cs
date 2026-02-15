using NB65c02Asm.Assembler;
using NB65c02Asm.Bbc;
using NB65c02Asm.Disk;
using NB65c02Asm;
using NB65c02Asm.Ide;
using NB65c02Asm.Tools;

var argsList = args.ToList();

// IDE mode: --ide [optional initial file]
if (argsList.Contains("--ide", StringComparer.OrdinalIgnoreCase))
{
    var ideIdx = argsList.FindIndex(a => string.Equals(a, "--ide", StringComparison.OrdinalIgnoreCase));
    var ssdTemplate = GetOptionValue(argsList, "--ssd-template");
    var ideBeebEmPath = GetOptionValue(argsList, "--beebem");
    var ideOutDir = GetOptionValue(argsList, "--out") ?? "build";

    var buildService = new BuildService
    {
        SsdTemplatePath = ssdTemplate,
        BeebEmPath = ideBeebEmPath,
        OutputDirectory = ideOutDir,
    };

    // First non-option arg is the initial file to open.
    string? initialFile = null;
    for (var i = 0; i < argsList.Count; i++)
    {
        if (i == ideIdx) continue;
        var a = argsList[i];
        if (a.StartsWith('-')) { if (i + 1 < argsList.Count && !argsList[i + 1].StartsWith('-')) i++; continue; }
        initialFile = a;
        break;
    }

    var ide = new IdeApp(buildService);
    ide.Run(initialFile);
    return;
}

if (argsList.Count == 0 || argsList.Contains("--help", StringComparer.OrdinalIgnoreCase) || argsList.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("NB65c02Asm\n\nUsage:\n  nb65c02asm <input.asm> [--out <basePath>] [--obj] [--prg] [--d64 <diskName>] [--start $0801]\n                     [--ssd <diskTitle>] [--dsd <diskTitle>] [--dsd-order side|track]\n                     [--ssd-interleave <n>] [--ssd-template <path>]\n                     [--bbcfile <name>] [--bbcexec <addr>] [--bbcdir <D>]\n                     [--beebem <exe>]\n  nb65c02asm --ide [<file.asm>] [--ssd-template <path>] [--beebem <exe>] [--out <dir>]\n\nOutputs:\n  --obj        Write <basePath>.bin (assembled bytes)\n  --prg        Write <basePath>.prg (2-byte little endian load address + bytes; C64 style)\n  --d64        Write <basePath>.d64 (C64 1541 image with PRG file)\n  --ssd        Write <basePath>.ssd (Acorn DFS single-sided image with auto-boot !BOOT + binary)\n  --dsd        Write <basePath>.dsd (Acorn DFS double-sided image; mirrored files)\n\nIDE mode:\n  --ide              Launch the full-screen TUI editor (F5 build, F6 build+run)\n\nEmulator:\n  --beebem <exe>     Path to BeebEm executable; launches emulator with the .ssd/.dsd after build\n\nBBC/DFS options:\n  --bbcfile    DFS filename (default: OUTBASE)\n  --bbcdir     DFS directory letter (default: $. i.e. 'A'..'Z' supported; '$' treated as '$')\n  --bbcexec    Exec address (default: load/start address)\n  --ssd-template    Path to a BeebEm/DFS-created template .ssd; patches HELLO payload in-place\n  --ssd-interleave  Sector interleave per track (default: 2; ignored when --ssd-template is used)\n\nNotes:\n  If no output switches are provided, --prg is assumed.\n");
    return;
}

var inputPath = argsList[0];
var outBasePath = GetOptionValue(argsList, "--out") ?? Path.ChangeExtension(inputPath, null);
var diskName = GetOptionValue(argsList, "--d64");
var ssdTitle = GetOptionValue(argsList, "--ssd");
var dsdTitle = GetOptionValue(argsList, "--dsd");
var dumpSsdPath = GetOptionValue(argsList, "--dump-ssd");
var hexDumpPath = GetOptionValue(argsList, "--hexdump");
var diffAPath = GetOptionValue(argsList, "--diff-a");
var diffBPath = GetOptionValue(argsList, "--diff-b");
var patchTemplatePath = GetOptionValue(argsList, "--patch-template-ssd");
var patchOutPath = GetOptionValue(argsList, "--patch-out-ssd");
var ssdTemplatePath = GetOptionValue(argsList, "--ssd-template");
var beebEmPath = GetOptionValue(argsList, "--beebem");

if (dumpSsdPath is not null)
{
    var bytes = await File.ReadAllBytesAsync(dumpSsdPath);
    Console.WriteLine(SsdDump.DumpCatalog(bytes));
    return;
}

if (diffAPath is not null && diffBPath is not null)
{
    var a = await File.ReadAllBytesAsync(diffAPath);
    var b = await File.ReadAllBytesAsync(diffBPath);
    var len = Math.Min(512, Math.Min(a.Length, b.Length));
    Console.WriteLine(CatalogDiff.Diff(a, b, 0, len));
    return;
}

if (hexDumpPath is not null)
{
    var bytes = await File.ReadAllBytesAsync(hexDumpPath);
    var startOpt = GetOptionValue(argsList, "--hexdump-start");
    var lenOpt = GetOptionValue(argsList, "--hexdump-len");
    var start = startOpt is null ? 0 : int.Parse(startOpt);
    var len = lenOpt is null ? 512 : int.Parse(lenOpt);
    Console.WriteLine(HexDump.Dump(bytes.AsSpan(start, Math.Min(len, bytes.Length - start))));
    return;
}

var outDir = Path.GetDirectoryName(outBasePath);
if (!string.IsNullOrWhiteSpace(outDir))
{
    Directory.CreateDirectory(outDir);
}

var writeObj = argsList.Contains("--obj", StringComparer.OrdinalIgnoreCase);
var writePrg = argsList.Contains("--prg", StringComparer.OrdinalIgnoreCase);
if (patchTemplatePath is null && !writeObj && !writePrg && diskName is null)
{
    writePrg = true;
}

var startText = GetOptionValue(argsList, "--start");
ushort? forcedStart = startText is null ? null : NumberParser.ParseU16(startText);

var sourceText = await File.ReadAllTextAsync(inputPath);
var asm = new Assembler65C02();

AssemblyResult result;
try
{
    result = asm.Assemble(sourceText, inputPath);
}
catch (AssemblerException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
    return;
}

var startAddress = forcedStart ?? result.Origin ?? throw new InvalidOperationException("No origin (.org) specified and no --start provided.");
var objectBytes = result.GetContiguousBytes();

// Template SSD patch mode (runs without needing --ssd/--dsd).
if (patchTemplatePath is not null && patchOutPath is not null)
{
    var bbcExecOpt = GetOptionValue(argsList, "--bbcexec");
    var execAddr = bbcExecOpt is null ? (uint)startAddress : NumberParser.ParseU16(bbcExecOpt);
    var raw = new RawBinary(objectBytes, loadAddress: startAddress);
    var exe = BbcExecutable.Wrap(raw, execAddress: execAddr);

    var template = await File.ReadAllBytesAsync(patchTemplatePath);
    var patched = DfsTemplatePatcher.PatchHello(template, exe.Data, exe.LoadAddress, exe.ExecAddress);
    await File.WriteAllBytesAsync(patchOutPath, patched);
    Console.WriteLine($"Wrote {Path.GetFullPath(patchOutPath)}");
    return;
}



if (writeObj)
{
    var objPath = outBasePath + ".bin";
    await File.WriteAllBytesAsync(objPath, objectBytes);
    Console.WriteLine($"Wrote {Path.GetFullPath(objPath)}");
}

byte[] prgBytes = PrgWriter.Write(startAddress, objectBytes);
if (writePrg)
{
    var prgPath = outBasePath + ".prg";
    await File.WriteAllBytesAsync(prgPath, prgBytes);
    Console.WriteLine($"Wrote {Path.GetFullPath(prgPath)}");
}

if (diskName is not null)
{
    var d64Path = outBasePath + ".d64";
    var fileName = Path.GetFileName(outBasePath).ToUpperInvariant();
    var image = D64Image.CreateBlank(diskName);
    image.AddPrg(fileName, prgBytes);
    await File.WriteAllBytesAsync(d64Path, image.GetBytes());
    Console.WriteLine($"Wrote {Path.GetFullPath(d64Path)}");
}

if (ssdTitle is not null || dsdTitle is not null)
{
    var bbcFile = (GetOptionValue(argsList, "--bbcfile") ?? Path.GetFileName(outBasePath)).ToUpperInvariant();
    if (bbcFile.Length > 7)
    {
        bbcFile = bbcFile[..7];
    }

    var bbcDirOpt = GetOptionValue(argsList, "--bbcdir");
    var bbcDir = string.IsNullOrWhiteSpace(bbcDirOpt) ? "$" : bbcDirOpt.Trim().ToUpperInvariant();
    if (bbcDir != "$" && (bbcDir.Length != 1 || bbcDir[0] is < 'A' or > 'Z'))
    {
        throw new ArgumentException("--bbcdir must be '$' or a single letter A-Z.");
    }

    var bbcExecOpt = GetOptionValue(argsList, "--bbcexec");
    var execAddr = bbcExecOpt is null ? (uint)startAddress : NumberParser.ParseU16(bbcExecOpt);

    var interleaveOpt = GetOptionValue(argsList, "--ssd-interleave");
    var interleave = interleaveOpt is null ? 2 : int.Parse(interleaveOpt);

    var raw = new RawBinary(objectBytes, loadAddress: startAddress);
    var exe = BbcExecutable.Wrap(raw, execAddress: execAddr);
    var boot = BootFileGenerator.CreateBootScript(bbcDir, bbcFile);

    if (ssdTitle is not null)
    {
        // .output directive overrides the default SSD path.
        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Environment.CurrentDirectory;
        var ssdPath = result.OutputPath is not null
            ? Path.GetFullPath(Path.Combine(inputDir, result.OutputPath))
            : outBasePath + ".ssd";
        var ssdDir = Path.GetDirectoryName(ssdPath);
        if (!string.IsNullOrEmpty(ssdDir))
            Directory.CreateDirectory(ssdDir);

        if (ssdTemplatePath is not null)
        {
            var template = await File.ReadAllBytesAsync(ssdTemplatePath);
            var patched = DfsTemplatePatcher.PatchHello(template, exe.Data, exe.LoadAddress, exe.ExecAddress);
            await File.WriteAllBytesAsync(ssdPath, patched);
        }
        else
        {
            var ssd = DfsSsdImage.CreateBlank(ssdTitle, interleave);
            ssd.AddFile("$", "!BOOT", boot, loadAddress: 0, execAddress: 0, locked: true);
            ssd.AddFile(bbcDir, bbcFile, exe.Data, exe.LoadAddress, exe.ExecAddress);
            ssd.Validate();
            await File.WriteAllBytesAsync(ssdPath, ssd.GetBytes());
        }
        Console.WriteLine($"Wrote {Path.GetFullPath(ssdPath)}");
    }

    if (dsdTitle is not null)
    {
        var dsdPath = outBasePath + ".dsd";
        var orderOpt = GetOptionValue(argsList, "--dsd-order");
        var order = orderOpt?.ToLowerInvariant() switch
        {
            null or "side" => DfsDsdOrdering.Side0ThenSide1,
            "track" => DfsDsdOrdering.TrackInterleaved,
            _ => throw new ArgumentException("--dsd-order must be 'side' or 'track'.")
        };

        var bytes = DfsDsdBuilder.CreateAutobootDsd(
            dsdTitle,
            dsdTitle,
            bbcDir,
            bbcFile,
            boot,
            exe.Data,
            exe.LoadAddress,
            exe.ExecAddress,
            interleave,
            order);

        await File.WriteAllBytesAsync(dsdPath, bytes);
        Console.WriteLine($"Wrote {Path.GetFullPath(dsdPath)}");
    }

    // Launch BeebEm with the disk image if --beebem was specified.
    if (beebEmPath is not null)
    {
        var imagePath = ssdTitle is not null ? outBasePath + ".ssd"
                      : dsdTitle is not null ? outBasePath + ".dsd"
                      : null;

        if (imagePath is not null)
        {
            var fullImagePath = Path.GetFullPath(imagePath);
            Console.WriteLine($"Launching BeebEm with {fullImagePath}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = beebEmPath,
                Arguments = $"\"{fullImagePath}\"",
                UseShellExecute = false,
            });
        }
        else
        {
            Console.Error.WriteLine("Warning: --beebem specified but no --ssd or --dsd output was produced.");
        }
    }
}

static string? GetOptionValue(List<string> args, string name)
{
    var idx = args.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    if (idx < 0)
    {
        return null;
    }

    if (idx + 1 >= args.Count)
    {
        throw new ArgumentException($"Missing value for {name}.");
    }

    return args[idx + 1];
}
