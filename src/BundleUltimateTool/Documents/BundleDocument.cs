using System;
using System.Collections.Generic;
using System.IO;
using BundleFormat.Bundle;

namespace BundleUltimateTool.Documents;

/// <summary>
/// UI-side document holder. A game-folder document starts as a lightweight stub (48-byte
/// header only); its table is read/cached lazily when entries are actually requested.
/// </summary>
public sealed class BundleDocument : IDisposable
{
    private BundleIndex _index;
    private readonly BundleReaderCache _cache;

    internal BundleDocument(BundleIndex index, BundleReaderCache cache, string relativePath = null)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        Path = index.Path;
        RelativePath = relativePath ?? System.IO.Path.GetFileName(index.Path);
        Header = index.Header;
    }

    internal BundleDocument(string path, BundleHeader header, BundleReaderCache cache, string relativePath = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        Header = header;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        RelativePath = relativePath ?? System.IO.Path.GetFileName(Path);
    }

    public string Path { get; }
    public BundleHeader Header { get; }
    public string RelativePath { get; }
    public BundleReader Reader => _cache.GetOrOpen(Path);
    public string DisplayName => System.IO.Path.GetFileName(Path);
    public bool HasEntries => EntryCount > 0;
    public int EntryCount => checked((int)Header.NumFiles);

    public BundleIndex Index
    {
        get
        {
            if (_index == null)
            {
                _index = BundleIndexCache.TryLoad(Path);
                if (_index == null)
                {
                    _index = BundleIndex.Read(Path);
                    BundleIndexCache.Save(_index);
                }
            }
            return _index;
        }
    }

    private IReadOnlyList<BundleEntry> _entries;
    private bool _entriesLoadFailed;

    /// <summary>
    /// UI-safe enumeration: a game folder may contain files that look like bundles by
    /// extension but have a corrupt table. Such documents contribute no entries instead
    /// of throwing in GetAllViews/export commands.
    /// </summary>
    public IReadOnlyList<BundleEntry> Entries
    {
        get
        {
            if (_entries != null) return _entries;
            if (_entriesLoadFailed) return Array.Empty<BundleEntry>();
            if (_index != null)
            {
                _entries = _index.Entries;
                return _entries;
            }
            BundleIndex cached = BundleIndexCache.TryLoad(Path);
            if (cached != null)
            {
                _index = cached;
                _entries = cached.Entries;
                return _entries;
            }
            if (!BundleIndex.TryRead(Path, out BundleIndex loaded, out string error))
            {
                _entriesLoadFailed = true;
                _entries = Array.Empty<BundleEntry>();
                ApplicationLog.Write("ENTRIES_LOAD_FAILED", $"'{Path}': {error}");
                return _entries;
            }
            _index = loaded;
            BundleIndexCache.Save(loaded);
            _entries = loaded.Entries;
            return _entries;
        }
    }

    public byte[] ReadBytes(long offset, int count) => Reader.ReadBytes(offset, count);

    public void Dispose() { /* readers owned by cache */ }
}
