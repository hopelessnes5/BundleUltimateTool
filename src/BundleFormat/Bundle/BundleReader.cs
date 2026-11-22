using BundleFormat.Primitives;

namespace BundleFormat.Bundle;

public sealed class BundleEntry
{
    public BundleEntry(int index, BundleTableEntry raw, long absoluteHeaderOffset, long absoluteBodyOffset)
    {
        Index = index;
        Raw = raw;
        AbsoluteHeaderOffset = absoluteHeaderOffset;
        AbsoluteBodyOffset = absoluteBodyOffset;
    }

    public int Index { get; }
    public BundleTableEntry Raw { get; }
    public long AbsoluteHeaderOffset { get; }
    public long AbsoluteBodyOffset { get; }

    public uint MaskedHdrUncompSize => Raw.MaskedHdrUncompSize;
    public uint MaskedDataUncompSize => Raw.MaskedDataUncompSize;
    public uint FileType => Raw.FileType;
    public string TypeLabel => NfsFileTypeInfo.Describe(Raw.FileType);
}

/// <summary>
/// Read-only bundle table of contents. Reads the table immediately after the 48-byte
/// header, exactly like CNFSBundle::InitFromFile. The tableOffset field is recorded for
/// diagnostics only and never used to relocate the table.
/// </summary>
public sealed class BundleReader : IDisposable
{
    private readonly MappedBundleFile _file;

    private BundleReader(MappedBundleFile file, BundleHeader header,
        IReadOnlyList<BundleEntry> entries, IReadOnlyList<string> diagnostics)
    {
        _file = file;
        Header = header;
        Entries = entries;
        Diagnostics = diagnostics;
    }

    public string Path => _file.Path;
    public long FileLength => _file.Length;
    public MappedBundleFile File => _file;
    public BundleHeader Header { get; }
    public IReadOnlyList<BundleEntry> Entries { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public bool TableOffsetMatchesConvention => Header.TableOffset == BundleHeader.Size;

    public static BundleReader Open(string path)
    {
        var file = new MappedBundleFile(path);
        try
        {
            if (file.Length < BundleHeader.Size)
                throw new BundleFormatException(
                    $"Truncated bundle: {file.Path} is {file.Length} byte(s); the header needs {BundleHeader.Size}.");

            BundleHeader header = BundleHeader.Parse(file.ReadBytes(0, BundleHeader.Size));
            if (!string.Equals(header.Magic, "bnd2", StringComparison.Ordinal))
                throw new BundleFormatException(
                    $"Bad magic: expected 'bnd2', got '{Escape(header.Magic)}' at offset 0.");
            if (header.Version != 2 && header.Version != 3)
                throw new BundleFormatException($"Unsupported bundle version {header.Version}; expected 2 or 3.");
            if (header.NumFiles == 0)
                throw new BundleFormatException("Bundle declares numFiles = 0.");

            long tableBytes = (long)header.NumFiles * BundleTableEntry.Size;
            long tableEnd = BundleHeader.Size + tableBytes;
            if (tableEnd > file.Length)
                throw new BundleFormatException(
                    $"Truncated table: {header.NumFiles} entries need {tableBytes} byte(s) at offset {BundleHeader.Size}; " +
                    $"file length is {file.Length}.");
            if (tableBytes > int.MaxValue)
                throw new BundleFormatException($"File table is {tableBytes} byte(s); this reader cannot map it in one pass.");

            var diagnostics = new List<string>();
            if (header.TableOffset != BundleHeader.Size)
                diagnostics.Add(
                    $"tableOffset field is {header.TableOffset} (0x{header.TableOffset:X}), but the C++ reader ignores it " +
                    $"and reads the table at {BundleHeader.Size}; keeping {BundleHeader.Size}.");

            byte[] table = file.ReadBytes(BundleHeader.Size, (int)tableBytes);
            var entries = new List<BundleEntry>((int)header.NumFiles);
            for (int i = 0; i < (int)header.NumFiles; i++)
            {
                BundleTableEntry raw = BundleTableEntry.Parse(table.AsSpan(i * BundleTableEntry.Size, BundleTableEntry.Size));
                string label = $"entry[{i}] (hash 0x{raw.Hash:X8})";

                long absHeader = (long)raw.HdrOffset + header.HeadersOffset;
                long absBody = (long)raw.BodyOffset + header.BodysOffset;
                EnsureRegion(absHeader, raw.HdrCompSize, file.Length, label, "header");
                EnsureRegion(absBody, raw.DataCompSize, file.Length, label, "body");
                if (absBody > file.Length)
                    throw new BundleFormatException(
                        $"{label}: bodyOffset resolves to 0x{absBody:X}, beyond file length 0x{file.Length:X}.");

                entries.Add(new BundleEntry(i, raw, absHeader, absBody));
            }

            return new BundleReader(file, header, entries, diagnostics);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public byte[] ReadBytes(long offset, int count) => _file.ReadBytes(offset, count);

    private static void EnsureRegion(long start, uint size, long length, string label, string region)
    {
        if ((ulong)start + size > (ulong)length)
            throw new BundleFormatException(
                $"{label}: {region} region [0x{start:X}, 0x{(ulong)start + size:X}) " +
                $"exceeds file length 0x{length:X} (compressed size {size}).");
    }

    private static string Escape(string value) =>
        string.IsNullOrEmpty(value) ? "<empty>" : value.Replace("\0", "\\0");

    public void Dispose() => _file.Dispose();
}
