using System.Globalization;

namespace BundleFormat.IO;

/// <summary>Pure, UI-free rules for classifying files in a game installation folder.</summary>
public static class GamePathRules
{
    private static readonly HashSet<string> BundleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bundle", ".bndl", ".bin"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".xml", ".ini", ".json", ".log", ".cfg", ".md", ".csv"
    };

    public static bool IsBundleFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string extension = Path.GetExtension(path);
        return extension.Length > 0 && BundleExtensions.Contains(extension);
    }

    private static readonly HashSet<string> RelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bundle", ".bndl", ".bin", ".nfs", ".dds", ".obj", ".mtl", ".txt", ".xml", ".ini"
    };

    public static uint? ParseVehId(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        string name = Path.GetFileName(fileName);
        if (!name.StartsWith("VEH_", StringComparison.OrdinalIgnoreCase)) return null;
        string digits = new string(name.Skip(4).TakeWhile(char.IsDigit).ToArray());
        return digits.Length > 0 && uint.TryParse(digits, out uint id) ? id : null;
    }

    public static bool IsRelevantExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        string normalized = extension.StartsWith('.') ? extension : "." + extension;
        return RelevantExtensions.Contains(normalized);
    }

    public static bool IsTextFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string extension = Path.GetExtension(path);
        return extension.Length > 0 && TextExtensions.Contains(extension);
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024L * 1024)
            return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }

    public static string GetExtensionLabel(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "FILE";
        string extension = Path.GetExtension(path);
        return extension.Length > 1
            ? extension.Substring(1).ToUpperInvariant()
            : "FILE";
    }
}
