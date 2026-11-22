using BundleFormat.Compression;
using BundleFormat.Primitives;

namespace BundleFormat.Bundle;

public readonly record struct RawExtractResult(int HeaderLength, int BodyLength, int TotalLength)
{
    public override string ToString() => $"header={HeaderLength}, body={BodyLength}, total={TotalLength}";
}

/// <summary>
/// Port of MainWindow::SaveRAWFile: decompress the compressed header into the masked
/// uncompressed size, then (only when dataCompSize &gt; 0) the compressed body into its
/// masked uncompressed size, and concatenate header + body into one .nfs payload.
/// </summary>
public sealed class BundleEntryExtractor
{
    private readonly MappedBundleFile _file;

    public BundleEntryExtractor(MappedBundleFile file)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
    }

    public byte[] ExtractRaw(BundleEntry entry, out RawExtractResult metadata)
    {
        uint headerSize = entry.Raw.HdrUncompSizeRaw & BundleLayout.KEntryUncompSizeMask;
        uint bodySize = entry.MaskedDataUncompSize;

        byte[] header = ReadPayload(
            entry.AbsoluteHeaderOffset, entry.Raw.HdrCompSize, headerSize, "header", entry.Index);
        byte[] body = ReadPayload(
            entry.AbsoluteBodyOffset, entry.Raw.DataCompSize, bodySize, "body", entry.Index);

        long total = (long)header.Length + body.Length;
        if (total > int.MaxValue)
            throw new BundleFormatException($"entry[{entry.Index}]: raw payload is {total} byte(s), too large for a single array.");

        var result = new byte[(int)total];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(body, 0, result, header.Length, body.Length);
        metadata = new RawExtractResult(header.Length, body.Length, result.Length);
        return result;
    }

    /// <summary>
    /// Writes file_%04d.nfs-style output under <paramref name="outputFolder"/>. Rejects rooted
    /// or traversing file names and never overwrites the source bundle.
    /// </summary>
    public void ExtractRawTo(BundleEntry entry, string outputFolder, string fileName, out RawExtractResult metadata)
    {
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new ArgumentException("Output folder is required.", nameof(outputFolder));
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
            throw new BundleFormatException($"Unsafe output file name: '{fileName}'.");

        string[] segments = fileName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(segment => segment == ".."))
            throw new BundleFormatException($"Unsafe output file name (parent traversal): '{fileName}'.");

        string fullFolder = Path.GetFullPath(outputFolder);
        string target = Path.GetFullPath(Path.Combine(fullFolder, fileName));
        string folderPrefix = fullFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!target.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            throw new BundleFormatException($"Output path '{target}' escapes folder '{fullFolder}'.");
        if (string.Equals(target, _file.Path, StringComparison.OrdinalIgnoreCase))
            throw new BundleFormatException($"Refusing to overwrite the source bundle: '{target}'.");

        byte[] raw = ExtractRaw(entry, out metadata);
        string directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllBytes(target, raw);
    }

    private byte[] ReadPayload(long offset, uint compressedSize, uint uncompressedSize, string region, int index)
    {
        if (compressedSize == 0)
        {
            if (uncompressedSize != 0)
                throw new BundleFormatException(
                    $"entry[{index}]: {region} has no compressed data but declares {uncompressedSize} uncompressed byte(s).");
            return Array.Empty<byte>();
        }
        if (compressedSize > int.MaxValue || uncompressedSize > int.MaxValue)
            throw new BundleFormatException($"entry[{index}]: {region} size exceeds the supported range.");

        byte[] compressed = _file.ReadBytes(offset, (int)compressedSize);
        return ZLibAdapter.Decompress(compressed, (int)uncompressedSize);
    }
}
