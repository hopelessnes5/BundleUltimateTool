using System;
using System.IO;

namespace BundleUltimateTool.Settings
{
    internal static class FilePathIdentity
    {
        internal static bool TryNormalize(string path, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(path)) return false;
            try { fullPath = Path.GetFullPath(path); return true; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        // Preserve the editor's Windows comparison, including fallback for invalid paths.
        // This is lexical identity: it does not resolve symbolic links or access the disk.
        internal static bool AreSame(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            if (TryNormalize(first, out string firstPath) && TryNormalize(second, out string secondPath))
                return string.Equals(firstPath.TrimEnd(Path.DirectorySeparatorChar),
                    secondPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }
}
