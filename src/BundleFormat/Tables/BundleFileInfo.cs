using BundleFormat.Bundle;
using BundleFormat.Primitives;

namespace BundleFormat.Tables;

/// <summary>Port of DlgFileInfo::SetFileInfo: CRC32 over COMPRESSED header/body bytes,
/// with zlib crc32 continuation semantics for the full-file checksum.</summary>
public sealed class BundleFileInfo
{
    public BundleFileInfo(BundleEntry entry, ReadOnlySpan<byte> compressedHeader, ReadOnlySpan<byte> compressedBody)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        FileType = entry.FileType;
        AbsoluteHeaderOffset = entry.AbsoluteHeaderOffset;
        AbsoluteBodyOffset = entry.AbsoluteBodyOffset;
        RawHeaderUncompressedSize = entry.Raw.HdrUncompSizeRaw;
        RawBodyUncompressedSize = entry.Raw.DataUncompSizeRaw;
        MaskedHeaderUncompressedSize = entry.MaskedHdrUncompSize;
        MaskedBodyUncompressedSize = entry.MaskedDataUncompSize;
        CompressedHeaderSize = entry.Raw.HdrCompSize;
        CompressedBodySize = entry.Raw.DataCompSize;

        HeaderCrc = Crc32.Compute(0, compressedHeader);
        if (CompressedBodySize > 0)
        {
            DataCrc = Crc32.Compute(0, compressedBody);
            FullCrc = Crc32.Compute(HeaderCrc, compressedBody);
        }
        else
        {
            DataCrc = 0;
            FullCrc = HeaderCrc;
        }
    }

    public uint FileType { get; }
    public long AbsoluteHeaderOffset { get; }
    public long AbsoluteBodyOffset { get; }
    public uint RawHeaderUncompressedSize { get; }
    public uint RawBodyUncompressedSize { get; }
    public uint MaskedHeaderUncompressedSize { get; }
    public uint MaskedBodyUncompressedSize { get; }
    public uint CompressedHeaderSize { get; }
    public uint CompressedBodySize { get; }

    public uint HeaderCrc { get; }
    public uint DataCrc { get; }
    public uint FullCrc { get; }

    public string HeaderCrcHex => HeaderCrc.ToString("X8");
    public string DataCrcHex => DataCrc.ToString("X8");
    public string FullCrcHex => FullCrc.ToString("X8");
}
