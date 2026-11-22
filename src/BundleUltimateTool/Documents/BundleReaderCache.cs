using System;
using System.Collections.Generic;
using BundleFormat.Bundle;

namespace BundleUltimateTool.Documents;

/// <summary>Small LRU cache of open BundleReaders. Avoids keeping thousands of memory maps.</summary>
internal sealed class BundleReaderCache : IDisposable
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Path, BundleReader Reader)>> _map =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Path, BundleReader Reader)> _lru = new();

    internal BundleReaderCache(int capacity = 4)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    internal BundleReader GetOrOpen(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        string fullPath = System.IO.Path.GetFullPath(path);
        if (_map.TryGetValue(fullPath, out LinkedListNode<(string Path, BundleReader Reader)> node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Reader;
        }

        BundleReader reader = BundleReader.Open(fullPath);
        var newNode = new LinkedListNode<(string Path, BundleReader Reader)>((fullPath, reader));
        _lru.AddFirst(newNode);
        _map[fullPath] = newNode;
        while (_map.Count > _capacity)
        {
            LinkedListNode<(string Path, BundleReader Reader)> last = _lru.Last;
            if (last == null) break;
            _lru.RemoveLast();
            _map.Remove(last.Value.Path);
            last.Value.Reader.Dispose();
        }
        return reader;
    }

    internal void Clear()
    {
        foreach ((string _, BundleReader reader) in _lru) reader.Dispose();
        _lru.Clear();
        _map.Clear();
    }

    public void Dispose() => Clear();
}