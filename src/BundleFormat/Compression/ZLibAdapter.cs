using System.IO.Compression;
using BundleFormat.Primitives;

namespace BundleFormat.Compression;

/// <summary>
/// Managed equivalent of zlib_adapter::DecompressMemory / CompressMemory.
/// The C++ code uses zlib's inflateInit/inflate(Z_SYNC_FLUSH) for decompression and
/// deflateInit(Z_DEFAULT_COMPRESSION)/deflate(Z_FINISH) for compression. ZLibStream is
/// the wire-compatible zlib wrapper (the 0x78 streams found in the bundle fixtures).
/// </summary>
public static class ZLibAdapter
{
    /// <summary>
    /// Decompress exactly one zlib stream. Throws if it yields fewer or more bytes than
    /// <paramref name="expectedUncompressedSize"/>, so a truncated or mis-sized entry
    /// can never be silently accepted.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedUncompressedSize)
    {
        if (expectedUncompressedSize < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedUncompressedSize),
                "Expected uncompressed size cannot be negative.");

        var output = new byte[expectedUncompressedSize];
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var stream = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);

        int total = 0;
        while (total < output.Length)
        {
            int read = stream.Read(output, total, output.Length - total);
            if (read == 0) break;
            total += read;
        }
        if (total != output.Length)
            throw new BundleFormatException(
                $"zlib decompress: expected {output.Length} byte(s), produced {total}. " +
                "The stream is truncated or the entry size field is wrong.");

        if (stream.ReadByte() != -1)
            throw new BundleFormatException(
                $"zlib decompress: stream produced more than the expected {output.Length} byte(s).");

        return output;
    }

    /// <summary>
    /// Compress with the zlib default level (Z_DEFAULT_COMPRESSION = 6, mapped to
    /// CompressionLevel.Optimal) and finish the stream (equivalent to Z_FINISH).
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            stream.Write(data);
        return output.ToArray();
    }
}
