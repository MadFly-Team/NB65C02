using NB65c02Asm.Assembler;
using NB65c02Asm.Bbc;
using NB65c02Asm.Disk;

namespace NB65c02Asm.Ide;

internal sealed class BuildService
{
    public string? SsdTemplatePath { get; set; }
    public string? BeebEmPath { get; set; }
    public string OutputDirectory { get; set; } = "build";
    public string BbcFileName { get; set; } = "HELLO";
    public string BbcDir { get; set; } = "$";
    public uint? ExecAddressOverride { get; set; }

    public BuildResult Build(string sourceText, string sourceFileName, SourceMap? sourceMap = null)
    {
        var asm = new Assembler65C02();

        AssemblyResult result;
        try
        {
            result = asm.Assemble(sourceText, sourceFileName, sourceMap);
        }
        catch (AssemblerException ex)
        {
            return new BuildResult(false, ex.Message, null);
        }

        var startAddress = result.Origin
            ?? throw new InvalidOperationException("No .org directive found in source.");
        var objectBytes = result.GetContiguousBytes();
        var execAddr = ExecAddressOverride ?? (uint)startAddress;

        var raw = new RawBinary(objectBytes, loadAddress: startAddress);
        var exe = BbcExecutable.Wrap(raw, execAddress: execAddr);

        Directory.CreateDirectory(OutputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(sourceFileName);
        var binPath = Path.Combine(OutputDirectory, baseName + ".bin");
        File.WriteAllBytes(binPath, objectBytes);

        // Resolve SSD output path: .output directive overrides the default.
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourceFileName)) ?? Environment.CurrentDirectory;
        var defaultSsdPath = Path.Combine(OutputDirectory, baseName + ".ssd");
        var ssdOutputPath = result.OutputPath is not null
            ? Path.GetFullPath(Path.Combine(sourceDir, result.OutputPath))
            : defaultSsdPath;

        // Ensure parent directory exists for the SSD.
        var ssdDir = Path.GetDirectoryName(ssdOutputPath);
        if (!string.IsNullOrEmpty(ssdDir))
            Directory.CreateDirectory(ssdDir);

        string? ssdPath = null;
        if (SsdTemplatePath is not null && File.Exists(SsdTemplatePath))
        {
            var template = File.ReadAllBytes(SsdTemplatePath);
            var patched = DfsTemplatePatcher.PatchHello(template, exe.Data, exe.LoadAddress, exe.ExecAddress);
            ssdPath = ssdOutputPath;
            File.WriteAllBytes(ssdPath, patched);
        }
        else
        {
            var boot = BootFileGenerator.CreateBootScript(BbcDir, BbcFileName);
            var ssd = DfsSsdImage.CreateBlank(BbcFileName, interleave: 2);
            ssd.AddFile("$", "!BOOT", boot, loadAddress: 0, execAddress: 0, locked: true);
            ssd.AddFile(BbcDir, BbcFileName, exe.Data, exe.LoadAddress, exe.ExecAddress);
            ssd.Validate();
            ssdPath = ssdOutputPath;
            File.WriteAllBytes(ssdPath, ssd.GetBytes());
        }

        var msg = $"OK — {objectBytes.Length} bytes at ${startAddress:X4}";
        if (ssdPath is not null)
        {
            msg += $"\n{Path.GetFullPath(ssdPath)}";
        }

        return new BuildResult(true, msg, ssdPath, objectBytes, (ushort)startAddress);
    }

    public BuildResult BuildProject(IReadOnlyList<string> sourceFilePaths)
    {
        if (sourceFilePaths.Count == 0)
            return new BuildResult(false, "No source files in project.", null);

        // Expand .include directives per-file so each file's includes
        // resolve relative to that file's own directory.  Build a source
        // map so error messages report the original file and line.
        var map = new SourceMap();
        var parts = new List<string>(sourceFilePaths.Count);
        foreach (var path in sourceFilePaths)
        {
            var source = File.ReadAllText(path);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;
            parts.Add(Assembler65C02.ExpandIncludes(source, dir, [], map, path));
        }

        var combined = string.Concat(parts);
        return Build(combined, sourceFilePaths[0], map);
    }
}

internal sealed record BuildResult(bool Success, string Message, string? SsdPath, byte[]? ObjectBytes = null, ushort LoadAddress = 0);
