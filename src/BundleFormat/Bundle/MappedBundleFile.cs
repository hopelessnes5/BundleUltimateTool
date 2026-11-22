using System.IO.MemoryMappedFiles;
using BundleFormat.Primitives;

namespace BundleFormat.Bundle;

/// <summary>
/// Read-only random access to a bundle. The file is memory-mapped; only the explicitly
/// requested regions (header, table, one entry payload) are copied into managed memory.
/// </summary>
public sealed class MappedBundleFile : IDisposable
{
    private MemoryMappedFile _mapped;
    private MemoryMappedViewAccessor _accessor;
    private bool _disposed;

    public string Path { get; }
    public long Length { get; }

    public MappedBundleFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        Path = System.IO.Path.GetFullPath(path);

        using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Length = stream.Length;
        if (Length == 0) return; // BundleReader rejects an empty file before any read.

        _mapped = MemoryMappedFile.CreateFromFile(
            stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
        _accessor = _mapped.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public byte[] ReadBytes(long offset, int count)
    {
        EnsureReadable(offset, count);
        var buffer = new byte[count];
        if (count == 0) return buffer;
        long read = _accessor.ReadArray(offset, buffer, 0, count);
        if (read != count)
            throw new BundleFormatException(
                $"read: requested {count} byte(s) at offset {offset}, got {read}.");
        return buffer;
    }

    public void ReadBytes(long offset, Span<byte> destination) =>
        ReadBytes(offset, destination.Length).CopyTo(destination);

    /// <summary>Span over a freshly copied region. The backing array stays alive via the span.</summary>
    public ReadOnlySpan<byte> ReadSpan(long offset, int count) => ReadBytes(offset, count);

    public ReadOnlyMemory<byte> ReadMemory(long offset, int count) => ReadBytes(offset, count);

    private void EnsureReadable(long offset, int count)
    {
        if (_disposed || _accessor == null)
            throw new ObjectDisposedException(nameof(MappedBundleFile));
        if (offset < 0 || count < 0 || (ulong)offset + (ulong)count > (ulong)Length)
            throw new BundleFormatException(
                $"read: region [0x{offset:X}, 0x{offset + count:X}) is outside the file (length 0x{Length:X}).");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessor?.Dispose();
        _mapped?.Dispose();
    }
}
