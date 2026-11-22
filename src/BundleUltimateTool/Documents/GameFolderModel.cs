using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BundleFormat.IO;

namespace BundleUltimateTool.Documents;

public sealed class GameFileEntry
{
    public GameFileEntry(string name, string fullPath, bool isDirectory, long length, DateTime lastWriteTimeUtc)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Length = length;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long Length { get; }
    public DateTime LastWriteTimeUtc { get; }
    public string Extension => IsDirectory ? string.Empty : GamePathRules.GetExtensionLabel(FullPath);
    public bool IsBundle => !IsDirectory && GamePathRules.IsBundleFile(FullPath);

    public string DisplayText => IsDirectory
        ? "[DIR] " + Name
        : Name + " | " + GamePathRules.FormatSize(Length);
}

/// <summary>
/// Lazy, per-directory model of a game installation. Never walks the whole tree;
/// each enumeration touches exactly one directory and reports access errors as
/// diagnostics instead of throwing.
/// </summary>
public sealed class GameFolderModel
{
    public string RootPath { get; private set; }
    public string CurrentRelativePath { get; private set; } = string.Empty;
    public IReadOnlyList<GameFileEntry> Items { get; private set; } = Array.Empty<GameFileEntry>();
    public IReadOnlyList<string> Diagnostics { get; private set; } = Array.Empty<string>();

    public bool HasRoot => !string.IsNullOrEmpty(RootPath) && Directory.Exists(RootPath);

    public string CurrentFullPath => string.IsNullOrEmpty(CurrentRelativePath)
        ? RootPath
        : Path.Combine(RootPath ?? string.Empty, CurrentRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public void SetRoot(string path)
    {
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            RootPath = null;
            CurrentRelativePath = string.Empty;
            Items = Array.Empty<GameFileEntry>();
            Diagnostics = string.IsNullOrWhiteSpace(path)
                ? Array.Empty<string>()
                : new[] { "Game folder does not exist: " + path };
            return;
        }

        RootPath = Path.GetFullPath(path);
        CurrentRelativePath = string.Empty;
        Items = Enumerate(string.Empty, diagnostics);
        Diagnostics = diagnostics;
    }

    public void Navigate(string relativePath)
    {
        if (!HasRoot) return;
        string normalized = NormalizeRelative(relativePath);
        string full = string.IsNullOrEmpty(normalized)
            ? RootPath
            : Path.GetFullPath(Path.Combine(RootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinRoot(full))
        {
            Diagnostics = new[] { "Cannot navigate outside the game folder: " + relativePath };
            return;
        }
        if (!Directory.Exists(full))
        {
            Diagnostics = new[] { "Folder no longer exists: " + full };
            return;
        }
        CurrentRelativePath = normalized;
        Refresh();
    }

    public void NavigateUp()
    {
        if (!HasRoot) return;
        if (string.IsNullOrEmpty(CurrentRelativePath)) return;
        int lastSlash = CurrentRelativePath.LastIndexOf('/');
        Navigate(lastSlash < 0 ? string.Empty : CurrentRelativePath.Substring(0, lastSlash));
    }

    public void Refresh()
    {
        if (!HasRoot) return;
        var diagnostics = new List<string>();
        Items = Enumerate(CurrentRelativePath, diagnostics);
        Diagnostics = diagnostics;
    }

    /// <summary>Enumerates exactly one directory (no recursion).</summary>
    public IReadOnlyList<GameFileEntry> Enumerate(string relativePath, List<string> diagnostics = null)
    {
        var result = new List<GameFileEntry>();
        if (!HasRoot) return result;
        string full = string.IsNullOrEmpty(relativePath)
            ? RootPath
            : Path.GetFullPath(Path.Combine(RootPath, NormalizeRelative(relativePath).Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinRoot(full) || !Directory.Exists(full)) return result;

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(full))
                result.Add(MakeEntry(directory, true));
        }
        catch (UnauthorizedAccessException ex) { (diagnostics ?? new List<string>()).Add("Access denied: " + ex.Message); }
        catch (IOException ex) { (diagnostics ?? new List<string>()).Add("I/O error: " + ex.Message); }

        try
        {
            foreach (string file in Directory.EnumerateFiles(full))
                result.Add(MakeEntry(file, false));
        }
        catch (UnauthorizedAccessException ex) { (diagnostics ?? new List<string>()).Add("Access denied: " + ex.Message); }
        catch (IOException ex) { (diagnostics ?? new List<string>()).Add("I/O error: " + ex.Message); }

        return result
            .OrderBy(entry => entry.IsDirectory ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<GameFileEntry> ApplyFilter(string query, string extensionFilter)
    {
        string needle = (query ?? string.Empty).Trim();
        // Callers pass null for the localized "All types" option.
        bool hasExtension = !string.IsNullOrWhiteSpace(extensionFilter);
        return Items.Where(entry =>
        {
            if (hasExtension && !entry.IsDirectory &&
                !string.Equals(entry.Extension, extensionFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (needle.Length == 0) return true;
            return entry.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entry.DisplayText.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }).ToList();
    }

    public string GetRelativePath(GameFileEntry entry)
    {
        if (entry == null || !HasRoot) return string.Empty;
        return RelativeToRoot(entry.FullPath);
    }

    private GameFileEntry MakeEntry(string fullPath, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                var info = new DirectoryInfo(fullPath);
                return new GameFileEntry(info.Name, fullPath, true, 0, info.LastWriteTimeUtc);
            }
            var file = new FileInfo(fullPath);
            return new GameFileEntry(file.Name, fullPath, false, file.Length, file.LastWriteTimeUtc);
        }
        catch (Exception)
        {
            return new GameFileEntry(Path.GetFileName(fullPath), fullPath, isDirectory, 0, DateTime.MinValue);
        }
    }

    private string RelativeToRoot(string fullPath)
    {
        string relative = Path.GetRelativePath(RootPath, fullPath);
        return NormalizeRelative(relative);
    }

    private static string NormalizeRelative(string relative) =>
        (relative ?? string.Empty).Replace('\\', '/').Trim('/');

    private bool IsWithinRoot(string fullPath)
    {
        if (!HasRoot) return false;
        string rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return string.Equals(fullPath, RootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
