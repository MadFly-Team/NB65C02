using System.Text;

namespace NB65c02Asm.Bbc;

internal static class BootFileGenerator
{
    public static byte[] CreateBootScript(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Directory is required.", nameof(directory));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        directory = directory.Trim().ToUpperInvariant();
        fileName = fileName.Trim().ToUpperInvariant();

        // DFS boot file is plain text; *RUN uses the file's exec address.
        // Use a fully-qualified DFS name to avoid relying on current directory state.
        var qualified = directory == "$" ? $"$.{fileName}" : $"{directory}.{fileName}";
        var script = $"*RUN {qualified}\r";
        return Encoding.ASCII.GetBytes(script);
    }
}
