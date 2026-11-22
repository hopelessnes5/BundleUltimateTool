using BundleFormat.Primitives;

namespace BundleFormat.Bundle;

/// <summary>
/// Lightweight bundle table of contents read with a short-lived FileStream.
/// No MemoryMappedFile is kept open; payload reads go through a lazy reader cache.
/// </summary>
public sealed class BundleIndex
{
    public BundleIndex(string path, BundleHeader header, IReadOnlyList<BundleEntry> entries, IReadOnlyList<string> diagnostics)
    {
        Path = path;
        Header = header;
        Entries = entries;
        Diagnostics = diagnostics;
    }

    public string Path { get; }
    public BundleHeader Header { get; }
    public IReadOnlyList<BundleEntry> Entries { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Reads only the 48-byte header — enough to index a bundle without its table.</summary>
    public static BundleHeader ReadHeader(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        string fullPath = System.IO.Path.GetFullPath(path);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length < BundleHeader.Size)
            throw new BundleFormatException($"Truncated bundle: {fullPath}.");
        BundleHeader header = BundleHeader.Parse(ReadExactly(stream, BundleHeader.Size, "header"));
        if (!string.Equals(header.Magic, "bnd2", StringComparison.Ordinal))
            throw new BundleFormatException($"Not a bnd2 bundle: {fullPath}.");
        if (header.Version != 2 && header.Version != 3)
            throw new BundleFormatException($"Unsupported version {header.Version}: {fullPath}.");
        if (header.NumFiles == 0)
            throw new BundleFormatException($"Empty bundle: {fullPath}.");
        long tableEnd = BundleHeader.Size + (long)header.NumFiles * BundleTableEntry.Size;
        if (tableEnd > stream.Length)
            throw new BundleFormatException($"Truncated table in {fullPath}: need {tableEnd}, have {stream.Length}.");
        return header;
    }

    public static bool TryRead(string path, out BundleIndex index, out string error)
    {
        try
        {
            index = Read(path);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            index = null;
            error = ex.Message;
            return false;
        }
    }

    public static BundleIndex Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        string fullPath = System.IO.Path.GetFullPath(path);

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long length = stream.Length;
        if (length < BundleHeader.Size)
            throw new BundleFormatException(
                $"Truncated bundle: {fullPath} is {length} byte(s); the header needs {BundleHeader.Size}.");

        byte[] headerBytes = ReadExactly(stream, BundleHeader.Size, "header");
        BundleHeader header = BundleHeader.Parse(headerBytes);
        if (!string.Equals(header.Magic, "bnd2", StringComparison.Ordinal))
            throw new BundleFormatException(
                $"Bad magic: expected 'bnd2', got '{Escape(header.Magic)}' at offset 0.");
        if (header.Version != 2 && header.Version != 3)
            throw new BundleFormatException($"Unsupported bundle version {header.Version}; expected 2 or 3.");
        if (header.NumFiles == 0)
            throw new BundleFormatException("Bundle declares numFiles = 0.");

        long tableBytes = (long)header.NumFiles * BundleTableEntry.Size;
        long tableEnd = BundleHeader.Size + tableBytes;
        if (tableEnd > length)
            throw new BundleFormatException(
                $"Truncated table: {header.NumFiles} entries need {tableBytes} byte(s) at offset {BundleHeader.Size}; " +
                $"file length is {length}.");
        if (tableBytes > int.MaxValue)
            throw new BundleFormatException($"File table is {tableBytes} byte(s); this reader cannot map it in one pass.");

        var diagnostics = new List<string>();
        if (header.TableOffset != BundleHeader.Size)
            diagnostics.Add(
                $"tableOffset field is {header.TableOffset} (0x{header.TableOffset:X}), but the C++ reader ignores it " +
                $"and reads the table at {BundleHeader.Size}; keeping {BundleHeader.Size}.");

        stream.Position = BundleHeader.Size;
        byte[] table = ReadExactly(stream, (int)tableBytes, "table");
        var entries = new List<BundleEntry>((int)header.NumFiles);
        for (int i = 0; i < (int)header.NumFiles; i++)
        {
            BundleTableEntry raw = BundleTableEntry.Parse(table.AsSpan(i * BundleTableEntry.Size, BundleTableEntry.Size));
            string label = $"entry[{i}] (hash 0x{raw.Hash:X8})";
            long absHeader = (long)raw.HdrOffset + header.HeadersOffset;
            long absBody = (long)raw.BodyOffset + header.BodysOffset;
            EnsureRegion(absHeader, raw.HdrCompSize, length, label, "header");
            EnsureRegion(absBody, raw.DataCompSize, length, label, "body");
            entries.Add(new BundleEntry(i, raw, absHeader, absBody));
        }

        return new BundleIndex(fullPath, header, entries, diagnostics);
    }

    private static byte[] ReadExactly(Stream stream, int count, string label)
    {
        var buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
                throw new BundleFormatException($"Truncated {label}: expected {count} byte(s), got {offset}.");
            offset += read;
        }
        return buffer;
    }

    private static void EnsureRegion(long start, uint size, long length, string label, string region)
    {
        if ((ulong)start + size > (ulong)length)
            throw new BundleFormatException(
                $"{label}: {region} region [0x{start:X}, 0x{(ulong)start + size:X}) " +
                $"exceeds file length 0x{length:X} (compressed size {size}).");
    }

    private static string Escape(string value) =>
        string.IsNullOrEmpty(value) ? "<empty>" : value.Replace("\0", "\\0");
}