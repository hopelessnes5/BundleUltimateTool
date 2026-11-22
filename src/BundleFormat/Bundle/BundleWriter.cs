using System.Buffers.Binary;
using System.Text;
using BundleFormat.Compression;
using BundleFormat.Primitives;
using BundleFormat.Textures;

namespace BundleFormat.Bundle;

public readonly record struct BundleAssemblerReport(
    int ReplacedCount, long OutputLength, IReadOnlyList<string> Warnings);

/// <summary>
/// Port of DlgBundleAssembler::AssembleBundle + PrepareTextureFile. Reuses compressed
/// header/body bytes verbatim for non-replaced entries and writes to a unique temporary
/// file before atomically replacing the destination (fixing the original .temp bug).
/// </summary>
public static class BundleAssembler
{
    private static readonly byte[] NullPadding = new byte[128];

    public static BundleAssemblerReport Rebuild(string sourceBundlePath, string outputPath,
        IReadOnlyDictionary<int, string> replacementsByEntryIndex) =>
        RebuildCore(sourceBundlePath, outputPath,
            entry => replacementsByEntryIndex.TryGetValue(entry.Index, out string path) ? path : null);

    public static BundleAssemblerReport Rebuild(string sourceBundlePath, string outputPath,
        IReadOnlyDictionary<string, string> replacementsByName) =>
        RebuildCore(sourceBundlePath, outputPath,
            entry => replacementsByName.TryGetValue($"file_{entry.Index:D4}.nfs", out string path) ? path : null);

    private static BundleAssemblerReport RebuildCore(string sourceBundlePath, string outputPath,
        Func<BundleEntry, string> getReplacementPath)
    {
        if (string.IsNullOrWhiteSpace(sourceBundlePath)) throw new ArgumentException("Source path is required.", nameof(sourceBundlePath));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        string fullSource = Path.GetFullPath(sourceBundlePath);
        string fullOutput = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(fullOutput);
        if (string.IsNullOrEmpty(outputDirectory) || !Directory.Exists(outputDirectory))
            throw new BundleFormatException($"Output directory does not exist: '{outputDirectory}'.");

        PreparedEntry[] prepared;
        BundleHeader originalHeader;
        int replacedCount = 0;
        var warnings = new List<string>();

        // Read everything while the mapping is alive, then dispose it BEFORE the atomic
        // move so Windows does not keep the source file locked for in-place rebuilds.
        using (var source = new MappedBundleFile(fullSource))
        {
            BundleReader reader = BundleReader.Open(fullSource);
            try
            {
                originalHeader = reader.Header;
                prepared = new PreparedEntry[reader.Entries.Count];
                for (int i = 0; i < reader.Entries.Count; i++)
                {
                    BundleEntry entry = reader.Entries[i];
                    string replacementPath = getReplacementPath(entry);
                    if (replacementPath == null)
                    {
                        prepared[i] = PreparedEntry.FromSource(
                            entry,
                            source.ReadBytes(entry.AbsoluteHeaderOffset, (int)entry.Raw.HdrCompSize),
                            entry.Raw.DataCompSize > 0
                                ? source.ReadBytes(entry.AbsoluteBodyOffset, (int)entry.Raw.DataCompSize)
                                : Array.Empty<byte>());
                    }
                    else
                    {
                        prepared[i] = PrepareReplacement(entry, replacementPath);
                        replacedCount++;
                    }
                }
                warnings.AddRange(reader.Diagnostics);
            }
            finally { reader.Dispose(); }
        }

        string temp = Path.Combine(outputDirectory,
            $".{Path.GetFileName(fullOutput)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteOutput(temp, originalHeader, prepared);
            // Atomic replacement; the source mapping is already disposed.
            if (File.Exists(fullOutput)) File.Replace(temp, fullOutput, null);
            else File.Move(temp, fullOutput);
            return new BundleAssemblerReport(replacedCount, new FileInfo(fullOutput).Length, warnings);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    private sealed class PreparedEntry
    {
        public required BundleEntry Entry { get; init; }
        public required byte[] PackedHeader { get; init; }
        public required byte[] PackedBody { get; init; }
        public bool IsReplacement { get; private init; }
        public uint UncompressedBodySize { get; private init; }

        public uint HdrCompSize => (uint)PackedHeader.Length;
        public uint DataCompSize => (uint)PackedBody.Length;
        public uint HdrUncompSize => IsReplacement ? (uint)NfsTextureHeader.Size : Entry.MaskedHdrUncompSize;
        public uint DataUncompSize => IsReplacement ? UncompressedBodySize : Entry.MaskedDataUncompSize;

        public static PreparedEntry FromSource(BundleEntry entry, byte[] packedHeader, byte[] packedBody) =>
            new() { Entry = entry, PackedHeader = packedHeader, PackedBody = packedBody, IsReplacement = false };

        public static PreparedEntry FromReplacement(BundleEntry entry, byte[] packedHeader,
            byte[] packedBody, uint uncompressedBodySize) =>
            new()
            {
                Entry = entry, PackedHeader = packedHeader, PackedBody = packedBody,
                IsReplacement = true, UncompressedBodySize = uncompressedBodySize
            };
    }

    private static PreparedEntry PrepareReplacement(BundleEntry entry, string path)
    {
        byte[] file;
        try { file = File.ReadAllBytes(path); }
        catch (Exception ex) { throw new BundleFormatException($"Cannot read replacement '{path}': {ex.Message}", ex); }

        if (file.Length < 8)
            throw new BundleFormatException($"Replacement '{path}' is too short: expected NFSO magic and file type.");
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0, 4));
        uint fileType = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4, 4));
        if (magic != DdsToNfsConverter.NfsoMagic)
            throw new BundleFormatException($"Replacement '{path}' has bad magic 0x{magic:X8}; expected 'NFSO'.");
        if (fileType != DdsToNfsConverter.NfsFileTypeTexture)
            throw new BundleFormatException($"Replacement '{path}' has fileType {fileType}; only texture (1) is supported.");
        if (file.Length < 8 + NfsTextureHeader.Size)
            throw new BundleFormatException(
                $"Replacement '{path}' is truncated: need {8 + NfsTextureHeader.Size} bytes, got {file.Length}.");

        byte[] textureHeader = file.AsSpan(8, NfsTextureHeader.Size).ToArray();
        byte[] body = file.AsSpan(8 + NfsTextureHeader.Size).ToArray();
        byte[] packedHeader = ZLibAdapter.Compress(textureHeader);
        byte[] packedBody = body.Length > 0 ? ZLibAdapter.Compress(body) : Array.Empty<byte>();
        return PreparedEntry.FromReplacement(entry, packedHeader, packedBody, (uint)body.Length);
    }

    private static void WriteOutput(string path, BundleHeader header, PreparedEntry[] entries)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        WriteHeader(writer, header);

        uint relativeHeaderOffset = 0;
        uint relativeBodyOffset = 0;
        foreach (PreparedEntry item in entries)
        {
            BundleTableEntry raw = item.Entry.Raw;
            uint hdrComp = item.HdrCompSize;
            uint dataComp = item.DataCompSize;
            uint hdrUncomp = (raw.HdrUncompSizeRaw & ~BundleLayout.KEntryUncompSizeMask) |
                             (item.HdrUncompSize & BundleLayout.KEntryUncompSizeMask);
            uint dataUncomp = (raw.DataUncompSizeRaw & ~BundleLayout.KEntryUncompSizeMask) |
                              (item.DataUncompSize & BundleLayout.KEntryUncompSizeMask);

            var patched = raw with
            {
                HdrCompSize = hdrComp,
                DataCompSize = dataComp,
                HdrUncompSizeRaw = hdrUncomp,
                DataUncompSizeRaw = dataUncomp,
                HdrOffset = relativeHeaderOffset,
                BodyOffset = dataComp > 0 ? relativeBodyOffset : 0
            };
            WriteEntry(writer, patched);

            relativeHeaderOffset = Align(relativeHeaderOffset + hdrComp, BundleLayout.KRegularAllocationAlign);
            relativeBodyOffset = Align(relativeBodyOffset + dataComp, BundleLayout.KBigAllocationAlign);
        }

        uint baseHeaderOffset = header.HeadersOffset;
        foreach (PreparedEntry item in entries)
        {
            writer.Write(item.PackedHeader);
            uint aligned = Align(item.HdrCompSize, BundleLayout.KRegularAllocationAlign);
            WriteZeros(writer, aligned - item.HdrCompSize);
            baseHeaderOffset = Align(baseHeaderOffset + item.HdrCompSize, BundleLayout.KRegularAllocationAlign);
        }
        uint alignedHeaderOffset = Align(baseHeaderOffset, BundleLayout.KBigAllocationAlign);
        WriteZeros(writer, alignedHeaderOffset - baseHeaderOffset);

        uint baseBodiesOffset = alignedHeaderOffset;
        foreach (PreparedEntry item in entries)
        {
            if (item.DataCompSize == 0) continue;
            writer.Write(item.PackedBody);
            uint aligned = Align(item.DataCompSize, BundleLayout.KBigAllocationAlign);
            WriteZeros(writer, aligned - item.DataCompSize);
            baseBodiesOffset = Align(baseBodiesOffset + item.DataCompSize, BundleLayout.KBigAllocationAlign);
        }

        var patchedHeader = header with
        {
            BodysOffset = alignedHeaderOffset,
            BundleSize = baseBodiesOffset,
            BundleSize2 = baseBodiesOffset,
            DataEnd = baseBodiesOffset
        };
        stream.Position = 0;
        WriteHeader(writer, patchedHeader);
        writer.Flush();
    }

    private static void WriteHeader(BinaryWriter writer, BundleHeader header)
    {
        byte[] magic = Encoding.ASCII.GetBytes(string.IsNullOrEmpty(header.Magic) ? "bnd2" : header.Magic);
        writer.Write(magic.Length == 4 ? magic : Encoding.ASCII.GetBytes("bnd2"));
        writer.Write(header.Version);
        writer.Write(header.U2);
        writer.Write(header.BundleSize);
        writer.Write(header.NumFiles);
        writer.Write(header.TableOffset);
        writer.Write(header.HeadersOffset);
        writer.Write(header.BodysOffset);
        writer.Write(header.DataEnd);
        writer.Write(header.BundleSize2);
        writer.Write(header.Flags);
        writer.Write(header.Zero);
    }

    private static void WriteEntry(BinaryWriter writer, BundleTableEntry e)
    {
        writer.Write(e.Hash);
        writer.Write(e.Zero1);
        writer.Write(e.LinkId);
        writer.Write(e.U1);
        writer.Write(e.HdrUncompSizeRaw);
        writer.Write(e.DataUncompSizeRaw);
        writer.Write(e.Zero2A);
        writer.Write(e.Zero2B);
        writer.Write(e.HdrCompSize);
        writer.Write(e.DataCompSize);
        writer.Write(e.Zero3A);
        writer.Write(e.Zero3B);
        writer.Write(e.HdrOffset);
        writer.Write(e.BodyOffset);
        writer.Write(e.Zero4A);
        writer.Write(e.Zero4B);
        writer.Write(e.DataEnd);
        writer.Write(e.FileType);
        writer.Write(e.NumLinks);
        writer.Write(e.Zero5);
    }

    private static uint Align(uint value, uint alignment)
    {
        ulong result = ((ulong)value + alignment - 1) & ~(ulong)(alignment - 1);
        if (result > uint.MaxValue) throw new BundleFormatException($"Aligned offset {result} exceeds 32-bit range.");
        return (uint)result;
    }

    private static void WriteZeros(BinaryWriter writer, uint count)
    {
        while (count > 0)
        {
            int chunk = (int)Math.Min(count, (uint)NullPadding.Length);
            writer.Write(NullPadding, 0, chunk);
            count -= (uint)chunk;
        }
    }
}
