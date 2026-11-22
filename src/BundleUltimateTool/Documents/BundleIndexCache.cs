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
