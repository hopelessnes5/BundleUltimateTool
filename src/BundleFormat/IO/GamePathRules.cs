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
