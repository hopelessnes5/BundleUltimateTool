#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BundleFormat.Bundle;
using BundleFormat.Primitives;
using BundleUltimateTool.Settings;

namespace BundleUltimateTool.Documents;

/// <summary>
/// Compact per-archive binary cache (same idea as PrototypeUltimateTool's PTX2 sidecars):
/// strict magic/version/identity checks, hard byte budgets and atomic writes.
/// Pattern ported from PrototypeUltimateTool Caching/TextureIndexCacheStore.cs,
/// TextureIndexCacheCodec.cs and ArchiveFileIdentity.cs.
/// </summary>
internal static class BundleIndexCache
{
    private const int Magic = 0x58444242; // "BBDI"
    private const int Version = 1;
    private const int MaxEntriesPerArchive = 2_000_000;
    private const long MaxCacheBytes = 512L * 1024 * 1024;
    private const int MaxStringBytes = 1024;
    private const int MaxDiagnostics = 4096;

    internal readonly record struct ReadLimits(long Bytes, int StringBytes, int MaxEntries, int MaxDiagnostics)
    {
        internal static ReadLimits Default => new(MaxCacheBytes, MaxStringBytes, MaxEntriesPerArchive, BundleIndexCache.MaxDiagnostics);
    }

    private static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BundleUltimateTool", "IndexCache");

    private static string CachePath(string fullPath)
    {
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant())));
        return Path.Combine(CacheDirectory, key + ".idx");
    }

    internal static BundleIndex? TryLoad(string archivePath, CancellationToken token = default)
        => TryLoad(archivePath, token, ReadLimits.Default);

    internal static BundleIndex? TryLoad(string archivePath, CancellationToken token, ReadLimits limits)
    {
        token.ThrowIfCancellationRequested();
        ArchiveFileIdentity? identity = ArchiveFileIdentity.TryCapture(archivePath);
        if (identity == null) return null;
        string cachePath = CachePath(identity.FullPath);
        if (!File.Exists(cachePath)) return null;
        try
        {
            using var input = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long remaining = input.Length - input.Position;
            if (remaining < 0 || remaining > limits.Bytes) return null;
            var result = TryRead(input, identity, token, limits);
            return identity.StillMatches() ? result : null;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Sidecars are optional: missing/old/invalid caches fall back to scanning.
            return null;
        }
    }

    internal static bool Save(BundleIndex index, CancellationToken token = default)
        => Save(index, token, ReadLimits.Default);

    internal static bool Save(BundleIndex index, CancellationToken token, ReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(index);
        token.ThrowIfCancellationRequested();
        ArchiveFileIdentity? identity = ArchiveFileIdentity.TryCapture(index.Path);
        if (identity == null) return false;
        string cachePath = CachePath(identity.FullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        try
        {
            AtomicFileWriter.Write(cachePath, output =>
            {
                Write(output, identity, index, token, limits);
                token.ThrowIfCancellationRequested();
                if (!identity.StillMatches())
                    throw new IOException("Source archive changed while writing its bundle index cache.");
            });
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Cache is an optimization only.
            return false;
        }
    }

    private static BundleIndex? TryRead(Stream input, ArchiveFileIdentity identity, CancellationToken token, ReadLimits limits)
    {
        if (!input.CanRead || !input.CanSeek) return null;
        try
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return null;
            if (!IdentityMatches(reader, identity, limits)) return null;

            BundleHeader header = ReadHeader(reader, limits);
            int count = reader.ReadInt32();
            if (count < 0 || count > limits.MaxEntries) return null;
            var entries = new BundleEntry[count];
            for (int i = 0; i < count; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                int index = reader.ReadInt32();
                var raw = new BundleTableEntry(
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
                long absoluteHeader = reader.ReadInt64();
                long absoluteBody = reader.ReadInt64();
                if (index != i) return null;
                entries[i] = new BundleEntry(index, raw, absoluteHeader, absoluteBody);
            }

            int diagnosticsCount = reader.ReadInt32();
            if (diagnosticsCount < 0 || diagnosticsCount > limits.MaxDiagnostics) return null;
            var diagnostics = new string[diagnosticsCount];
            for (int i = 0; i < diagnosticsCount; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                diagnostics[i] = ReadBudgetedString(reader, limits.StringBytes);
            }

            if (input.Position != input.Length) return null;
            token.ThrowIfCancellationRequested();
            return new BundleIndex(identity.FullPath, header, entries, diagnostics);
        }
        catch (OperationCanceledException) { throw; }
        catch (EndOfStreamException) { return null; }
        catch (IOException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (FormatException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static void Write(Stream output, ArchiveFileIdentity identity, BundleIndex index, CancellationToken token, ReadLimits limits)
    {
        IReadOnlyList<BundleEntry> entries = index.Entries;
        IReadOnlyList<string> diagnostics = index.Diagnostics;
        if (entries.Count > limits.MaxEntries) throw new InvalidDataException("Bundle index cache exceeds entry budget.");
        if (diagnostics.Count > limits.MaxDiagnostics) throw new InvalidDataException("Bundle index cache exceeds diagnostic budget.");

        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(identity.FullPath);
        writer.Write(identity.Length);
        writer.Write(identity.LastWriteTimeUtcTicks);
        WriteHeader(writer, index.Header);
        writer.Write(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            BundleEntry entry = entries[i];
            writer.Write(entry.Index);
            BundleTableEntry raw = entry.Raw;
            writer.Write(raw.Hash); writer.Write(raw.Zero1); writer.Write(raw.LinkId); writer.Write(raw.U1);
            writer.Write(raw.HdrUncompSizeRaw); writer.Write(raw.DataUncompSizeRaw);
            writer.Write(raw.Zero2A); writer.Write(raw.Zero2B);
            writer.Write(raw.HdrCompSize); writer.Write(raw.DataCompSize);
            writer.Write(raw.Zero3A); writer.Write(raw.Zero3B);
            writer.Write(raw.HdrOffset); writer.Write(raw.BodyOffset);
            writer.Write(raw.Zero4A); writer.Write(raw.Zero4B);
            writer.Write(raw.DataEnd); writer.Write(raw.FileType);
            writer.Write(raw.NumLinks); writer.Write(raw.Zero5);
            writer.Write(entry.AbsoluteHeaderOffset);
            writer.Write(entry.AbsoluteBodyOffset);
        }

        writer.Write(diagnostics.Count);
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            WriteBudgetedString(writer, diagnostics[i] ?? string.Empty, limits.StringBytes);
        }
    }

    private static bool IdentityMatches(BinaryReader reader, ArchiveFileIdentity identity, ReadLimits limits)
    {
        string path = ReadBudgetedString(reader, limits.StringBytes);
        long length = reader.ReadInt64();
        long ticks = reader.ReadInt64();
        return string.Equals(path, identity.FullPath, StringComparison.OrdinalIgnoreCase)
            && length == identity.Length
            && ticks == identity.LastWriteTimeUtcTicks;
    }

    private static BundleHeader ReadHeader(BinaryReader reader, ReadLimits limits) => new(
        ReadBudgetedString(reader, limits.StringBytes),
        reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
        reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
        reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
        reader.ReadUInt32(), reader.ReadUInt32());

    private static void WriteHeader(BinaryWriter writer, BundleHeader header)
    {
        writer.Write(header.Magic ?? string.Empty);
        writer.Write(header.Version); writer.Write(header.U2); writer.Write(header.BundleSize);
        writer.Write(header.NumFiles); writer.Write(header.TableOffset); writer.Write(header.HeadersOffset);
        writer.Write(header.BodysOffset); writer.Write(header.DataEnd); writer.Write(header.BundleSize2);
        writer.Write(header.Flags); writer.Write(header.Zero);
    }

    private static string ReadBudgetedString(BinaryReader reader, int maximumBytes)
    {
        int count = reader.Read7BitEncodedInt();
        if (count < 0 || count > maximumBytes || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Bundle index cache string exceeds budget.");
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteBudgetedString(BinaryWriter writer, string value, int maximumBytes)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > maximumBytes) throw new InvalidDataException("Bundle index cache string exceeds budget.");
        writer.Write(value);
    }
}

/// <summary>
/// Preserve the existing cache identity contract: full path, size and UTC mtime.
/// Ported from PrototypeUltimateTool Caching/ArchiveFileIdentity.cs.
/// </summary>
internal sealed class ArchiveFileIdentity
{
    internal string FullPath { get; }
    internal long Length { get; }
    internal long LastWriteTimeUtcTicks { get; }

    private ArchiveFileIdentity(string fullPath, long length, long ticks)
    {
        FullPath = fullPath;
        Length = length;
        LastWriteTimeUtcTicks = ticks;
    }

    internal static ArchiveFileIdentity? TryCapture(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) return null;
            var info = new FileInfo(fullPath);
            return new ArchiveFileIdentity(fullPath, info.Length, info.LastWriteTimeUtc.Ticks);
        }
        catch { return null; }
    }

    internal bool StillMatches()
    {
        ArchiveFileIdentity? current = TryCapture(FullPath);
        return current != null
            && string.Equals(current.FullPath, FullPath, StringComparison.OrdinalIgnoreCase)
            && current.Length == Length
            && current.LastWriteTimeUtcTicks == LastWriteTimeUtcTicks;
    }
}

/// <summary>
/// Compact file-type histogram for a set of discovered bundles. Used to populate the
/// archive type filter without loading full per-entry indices for every bundle.
/// </summary>
internal sealed class BundleTypeHistogram
{
    internal IReadOnlyDictionary<uint, int> Counts { get; }
    internal int TotalEntries { get; }

    internal BundleTypeHistogram(IReadOnlyDictionary<uint, int> counts)
    {
        Counts = counts ?? throw new ArgumentNullException(nameof(counts));
        long total = 0;
        foreach (int value in counts.Values) total += value;
        TotalEntries = total > int.MaxValue ? int.MaxValue : (int)total;
    }
}

/// <summary>
/// Sidecar cache for game-folder type histograms.
/// Pattern ported from PrototypeUltimateTool Caching/TextureIndexCacheStore.cs,
/// TextureIndexCacheCodec.cs and ArchiveFileIdentity.cs: magic/version, strict identity,
/// byte budgets, cancellation, atomic write, safe fallback on invalid cache.
/// </summary>
internal static class BundleTypeHistogramCache
{
    private const int Magic = 0x48444242; // "BBDH"
    private const int Version = 1;
    private const int MaxFiles = 32_000;
    private const int MaxHistogramEntries = 4096;
    private const long MaxCacheBytes = 16L * 1024 * 1024;
    private const int MaxStringBytes = 2048;

    internal readonly record struct HistogramSource(string RootPath, IReadOnlyList<ArchiveFileIdentity> Files);

    private static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BundleUltimateTool", "TypeHistogramCache");

    private static string CachePath(string rootPath)
    {
        string full = Path.GetFullPath(rootPath);
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(full.ToLowerInvariant())));
        return Path.Combine(CacheDirectory, key + ".thc");
    }

    internal static BundleTypeHistogram? TryLoad(string rootPath, IReadOnlyList<ArchiveFileIdentity> files,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(rootPath) || files == null) return null;
        string cachePath = CachePath(rootPath);
        if (!File.Exists(cachePath)) return null;
        try
        {
            using var input = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length < 0 || input.Length > MaxCacheBytes) return null;
            var result = TryRead(input, Path.GetFullPath(rootPath), files, token);
            return AllStillMatch(files) ? result : null;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    internal static bool Save(string rootPath, IReadOnlyList<ArchiveFileIdentity> files,
        BundleTypeHistogram histogram, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(histogram);
        token.ThrowIfCancellationRequested();
        if (files.Count > MaxFiles) return false;
        if (histogram.Counts.Count > MaxHistogramEntries) return false;
        if (!AllStillMatch(files)) return false;

        string cachePath = CachePath(rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        try
        {
            AtomicFileWriter.Write(cachePath, output =>
            {
                Write(output, Path.GetFullPath(rootPath), files, histogram, token);
                token.ThrowIfCancellationRequested();
                if (!AllStillMatch(files))
                    throw new IOException("Source bundle set changed while writing type histogram cache.");
            });
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    private static BundleTypeHistogram? TryRead(Stream input, string rootPath,
        IReadOnlyList<ArchiveFileIdentity> expectedFiles, CancellationToken token)
    {
        if (!input.CanRead || !input.CanSeek) return null;
        try
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return null;
            string cachedRoot = ReadBudgetedString(reader, MaxStringBytes);
            if (!string.Equals(cachedRoot, rootPath, StringComparison.OrdinalIgnoreCase)) return null;

            int fileCount = reader.ReadInt32();
            if (fileCount < 0 || fileCount > MaxFiles) return null;
            if (fileCount != expectedFiles.Count) return null;
            for (int i = 0; i < fileCount; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                string path = ReadBudgetedString(reader, MaxStringBytes);
                long length = reader.ReadInt64();
                long ticks = reader.ReadInt64();
                ArchiveFileIdentity expected = expectedFiles[i];
                if (!string.Equals(path, expected.FullPath, StringComparison.OrdinalIgnoreCase) ||
                    length != expected.Length || ticks != expected.LastWriteTimeUtcTicks)
                    return null;
            }

            int histogramCount = reader.ReadInt32();
            if (histogramCount < 0 || histogramCount > MaxHistogramEntries) return null;
            var counts = new Dictionary<uint, int>(histogramCount);
            for (int i = 0; i < histogramCount; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                uint fileType = reader.ReadUInt32();
                int count = reader.ReadInt32();
                if (count < 0) return null;
                counts[fileType] = count;
            }

            if (input.Position != input.Length) return null;
            token.ThrowIfCancellationRequested();
            return new BundleTypeHistogram(counts);
        }
        catch (OperationCanceledException) { throw; }
        catch (EndOfStreamException) { return null; }
        catch (IOException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (FormatException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static void Write(Stream output, string rootPath, IReadOnlyList<ArchiveFileIdentity> files,
        BundleTypeHistogram histogram, CancellationToken token)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        WriteBudgetedString(writer, rootPath, MaxStringBytes);
        writer.Write(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            ArchiveFileIdentity file = files[i];
            WriteBudgetedString(writer, file.FullPath, MaxStringBytes);
            writer.Write(file.Length);
            writer.Write(file.LastWriteTimeUtcTicks);
        }

        var ordered = new List<KeyValuePair<uint, int>>(histogram.Counts);
        ordered.Sort((a, b) => a.Key.CompareTo(b.Key));
        writer.Write(ordered.Count);
        foreach (KeyValuePair<uint, int> pair in ordered)
        {
            token.ThrowIfCancellationRequested();
            writer.Write(pair.Key);
            writer.Write(pair.Value);
        }
    }

    private static bool AllStillMatch(IReadOnlyList<ArchiveFileIdentity> files)
    {
        for (int i = 0; i < files.Count; i++)
            if (!files[i].StillMatches()) return false;
        return true;
    }

    private static string ReadBudgetedString(BinaryReader reader, int maximumBytes)
    {
        int count = reader.Read7BitEncodedInt();
        if (count < 0 || count > maximumBytes || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Bundle type histogram cache string exceeds budget.");
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteBudgetedString(BinaryWriter writer, string value, int maximumBytes)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > maximumBytes) throw new InvalidDataException("Bundle type histogram cache string exceeds budget.");
        writer.Write(value);
    }
}

/// <summary>
/// Scans bundle table entries to build a compact type histogram without materializing
/// BundleEntry objects. Pattern ported from PrototypeUltimateTool's texture-index scan
/// flow: bounded/cancellable scan, compact result, no full object graph retained.
/// </summary>
internal static class BundleTypeHistogramScanner
{
    private const int TableEntrySize = 80;
    private const int FileTypeOffset = 68; // 17th uint32 in the 20-field table entry

    internal static BundleTypeHistogram Scan(IEnumerable<(string Path, BundleHeader Header)> bundles,
        CancellationToken token = default, int maxBundles = int.MaxValue, Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        var counts = new Dictionary<uint, int>();
        int processed = 0;
        int total = 0;
        if (bundles is ICollection<(string Path, BundleHeader Header)> collection) total = collection.Count;

        foreach ((string path, BundleHeader header) in bundles)
        {
            token.ThrowIfCancellationRequested();
            if (processed >= maxBundles) break;
            try
            {
                ScanBundle(path, header, counts, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ApplicationLog.Write("TYPE_HISTOGRAM_SCAN_FAILED", $"'{path}': {ex.Message}", ex);
            }
            processed++;
            if ((processed % 64) == 0) progress?.Invoke(processed, total);
        }
        progress?.Invoke(processed, total);
        return new BundleTypeHistogram(counts);
    }

    internal static BundleTypeHistogram ScanFiles(IEnumerable<string> bundlePaths,
        CancellationToken token = default, int maxBundles = int.MaxValue, Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(bundlePaths);
        var withHeaders = new List<(string Path, BundleHeader Header)>();
        foreach (string path in bundlePaths)
        {
            token.ThrowIfCancellationRequested();
            try { withHeaders.Add((Path.GetFullPath(path), BundleIndex.ReadHeader(path))); }
            catch (Exception ex) { ApplicationLog.Write("TYPE_HISTOGRAM_HEADER_FAILED", $"'{path}': {ex.Message}", ex); }
        }
        return Scan(withHeaders, token, maxBundles, progress);
    }

    private static void ScanBundle(string path, BundleHeader header, Dictionary<uint, int> counts, CancellationToken token)
    {
        int numFiles = checked((int)header.NumFiles);
        if (numFiles <= 0) return;
        long tableOffset = header.TableOffset;
        long needed = (long)numFiles * TableEntrySize;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        if (tableOffset < 0 || tableOffset + needed > stream.Length)
            throw new InvalidDataException($"Table range out of bounds: offset={tableOffset}, entries={numFiles}, length={stream.Length}.");
        stream.Position = tableOffset;

        byte[] buffer = new byte[64 * 1024];
        int bufferPos = 0;
        int bufferLen = 0;
        for (int i = 0; i < numFiles; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            if (bufferPos + TableEntrySize > bufferLen)
            {
                int remaining = bufferLen - bufferPos;
                if (remaining > 0) Buffer.BlockCopy(buffer, bufferPos, buffer, 0, remaining);
                bufferPos = 0;
                bufferLen = remaining;
                while (bufferLen < TableEntrySize)
                {
                    int read = stream.Read(buffer, bufferLen, buffer.Length - bufferLen);
                    if (read <= 0) throw new EndOfStreamException("Unexpected end of bundle table.");
                    bufferLen += read;
                }
            }
            uint fileType = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                buffer.AsSpan(bufferPos + FileTypeOffset, 4));
            counts.TryGetValue(fileType, out int existing);
            counts[fileType] = existing + 1;
            bufferPos += TableEntrySize;
        }
    }
}
