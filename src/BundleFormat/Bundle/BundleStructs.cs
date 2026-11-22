using BundleFormat.Primitives;

namespace BundleFormat.Bundle;

/// <summary>Values from NFSStructuresDef.h (E_NFS_FILE_TYPE).</summary>
public enum NfsFileType : uint
{
    Unknown0 = 0,
    Texture = 1,
    Material = 2,
    Model = 5,
    Unknown7 = 7,
    DefsTable = 18,
    Unknown19 = 19,
    Unknown81 = 81,
    Unknown129 = 129,
    Unknown176 = 176,
    Unknown178 = 178,
    Collision = 262,
    StringsTable = 513,
    Unknown527 = 527,
    Unknown532 = 532,
    Deformation = 771
}

public static class NfsFileTypeInfo
{
    /// <summary>Short labels used by the original tool's entry list (GetFileTypeString).</summary>
    public static string FileTypeLabel(uint fileType) => fileType switch
    {
        1 => "texture",
        2 => "material",
        5 => "model",
        18 => "defs table",
        513 => "strings",
        _ => "unknown"
    };

    // Unknown numeric values stay representable; callers must never reject an entry for them.
    public static string Describe(uint fileType) => fileType switch
    {
        0 => "unknown (0)",
        1 => "texture (1)",
        2 => "material (2)",
        5 => "model (5)",
        7 => "unknown (7)",
        18 => "defs table (18)",
        19 => "unknown (19)",
        81 => "unknown (81)",
        129 => "unknown (129)",
        176 => "unknown (176)",
        178 => "unknown (178)",
        262 => "collision (262)",
        513 => "strings (513)",
        527 => "unknown (527)",
        532 => "unknown (532)",
        771 => "deformation (771)",
        _ => $"unknown ({fileType})"
    };

    // Pure classifiers used by context-menu enablement (covered by BundleChecks).
    public static bool SupportsTextureExport(uint fileType) => fileType == 1;
    public static bool SupportsModelExport(uint fileType) => fileType == 5;
    public static bool SupportsStringsSave(uint fileType) => fileType == 513;
    public static bool SupportsDefsView(uint fileType) => fileType == 18;
}

public static class BundleLayout
{
    public const uint KEntryUncompSizeMask = 0x00ffffff;
    public const uint KRegularAllocationAlign = 16;
    public const uint KMidAllocationAlign = 32;
    public const uint KBigAllocationAlign = 128;

    public static uint MaskSize(uint raw) => raw & KEntryUncompSizeMask;
}

/// <summary>48-byte bundle header, parsed field by field (never via struct marshalling).</summary>
public readonly record struct BundleHeader(
    string Magic,
    uint Version,
    uint U2,
    uint BundleSize,
    uint NumFiles,
    uint TableOffset,
    uint HeadersOffset,
    uint BodysOffset,
    uint DataEnd,
    uint BundleSize2,
    uint Flags,
    uint Zero)
{
    public const int Size = 48;

    public static BundleHeader Parse(ReadOnlySpan<byte> bytes)
    {
        return new BundleHeader(
            EndianReader.ReadAscii(bytes, 0, 4, "header.magic"),
            EndianReader.ReadUInt32(bytes, 4, "header.version"),
            EndianReader.ReadUInt32(bytes, 8, "header.u2"),
            EndianReader.ReadUInt32(bytes, 12, "header.bundleSize"),
            EndianReader.ReadUInt32(bytes, 16, "header.numFiles"),
            EndianReader.ReadUInt32(bytes, 20, "header.tableOffset"),
            EndianReader.ReadUInt32(bytes, 24, "header.headersOffset"),
            EndianReader.ReadUInt32(bytes, 28, "header.bodysOffset"),
            EndianReader.ReadUInt32(bytes, 32, "header.dataEnd"),
            EndianReader.ReadUInt32(bytes, 36, "header.bundleSize2"),
            EndianReader.ReadUInt32(bytes, 40, "header.flags"),
            EndianReader.ReadUInt32(bytes, 44, "header.zero"));
    }
}

/// <summary>80-byte file table entry. Fields mirror S_NFSTableEntry exactly.</summary>
public readonly record struct BundleTableEntry(
    uint Hash,
    uint Zero1,
    uint LinkId,
    uint U1,
    uint HdrUncompSizeRaw,
    uint DataUncompSizeRaw,
    uint Zero2A,
    uint Zero2B,
    uint HdrCompSize,
    uint DataCompSize,
    uint Zero3A,
    uint Zero3B,
    uint HdrOffset,
    uint BodyOffset,
    uint Zero4A,
    uint Zero4B,
    uint DataEnd,
    uint FileType,
    uint NumLinks,
    uint Zero5)
{
    public const int Size = 80;

    public uint MaskedHdrUncompSize => BundleLayout.MaskSize(HdrUncompSizeRaw);
    public uint MaskedDataUncompSize => BundleLayout.MaskSize(DataUncompSizeRaw);

    public static BundleTableEntry Parse(ReadOnlySpan<byte> bytes)
    {
        return new BundleTableEntry(
            EndianReader.ReadUInt32(bytes, 0, "entry.hash"),
            EndianReader.ReadUInt32(bytes, 4, "entry.zero1"),
            EndianReader.ReadUInt32(bytes, 8, "entry.linkID"),
            EndianReader.ReadUInt32(bytes, 12, "entry.u1"),
            EndianReader.ReadUInt32(bytes, 16, "entry.hdrUncompSize"),
            EndianReader.ReadUInt32(bytes, 20, "entry.dataUncompSize"),
            EndianReader.ReadUInt32(bytes, 24, "entry.zero2[0]"),
            EndianReader.ReadUInt32(bytes, 28, "entry.zero2[1]"),
            EndianReader.ReadUInt32(bytes, 32, "entry.hdrCompSize"),
            EndianReader.ReadUInt32(bytes, 36, "entry.dataCompSize"),
            EndianReader.ReadUInt32(bytes, 40, "entry.zero3[0]"),
            EndianReader.ReadUInt32(bytes, 44, "entry.zero3[1]"),
            EndianReader.ReadUInt32(bytes, 48, "entry.hdrOffset"),
            EndianReader.ReadUInt32(bytes, 52, "entry.bodyOffset"),
            EndianReader.ReadUInt32(bytes, 56, "entry.zero4[0]"),
            EndianReader.ReadUInt32(bytes, 60, "entry.zero4[1]"),
            EndianReader.ReadUInt32(bytes, 64, "entry.dataEnd"),
            EndianReader.ReadUInt32(bytes, 68, "entry.fileType"),
            EndianReader.ReadUInt32(bytes, 72, "entry.numLinks"),
            EndianReader.ReadUInt32(bytes, 76, "entry.zero5"));
    }
}
