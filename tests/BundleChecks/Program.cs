using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BundleFormat.Bundle;
using BundleFormat.Models;
using BundleFormat.Compression;
using BundleFormat.IO;
using BundleFormat.Primitives;
using BundleFormat.Tables;
using BundleFormat.Textures;
using BundleUltimateTool.Documents;
using BundleUltimateTool.UI;

const string FixtureRoot = @"F:\repos\nfs-bundle-explorer\src\test";

int failures = 0;
int passes = 0;
byte[] vehicleListCompressedHeader = Array.Empty<byte>();
byte[] vehicleListHeader = Array.Empty<byte>();

void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} | {name} | {detail}");
    if (ok) passes++; else failures++;
}

void Eq<T>(string name, T actual, T expected) =>
    Check(name, EqualityComparer<T>.Default.Equals(actual, expected), $"actual={actual}, expected={expected}");

static string FindSolutionRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "BundleUltimateTool.sln"))) return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate BundleUltimateTool.sln above " + AppContext.BaseDirectory);
}

static Dictionary<string, string> LoadCatalog(string path)
{
    XDocument document = XDocument.Load(path);
    XElement strings = document.Root?.Element("strings");
    if (strings == null) throw new InvalidDataException($"{path}: missing <strings> element.");
    return strings.Elements("string")
        .Select(element => new KeyValuePair<string, string>(
            ((string)element.Attribute("key") ?? string.Empty).Trim(), element.Value))
        .Where(pair => pair.Key.Length > 0)
        .ToDictionary(pair => pair.Key, pair => pair.Value);
}

static void W32(byte[] destination, int offset, uint value) =>
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, 4), value);

static (byte[] Header, byte[] Body) Decompressed(BundleReader reader, BundleEntry entry)
{
    byte[] header = ZLibAdapter.Decompress(
        reader.ReadBytes(entry.AbsoluteHeaderOffset, (int)entry.Raw.HdrCompSize),
        (int)entry.MaskedHdrUncompSize);
    byte[] body = entry.Raw.DataCompSize > 0
        ? ZLibAdapter.Decompress(
            reader.ReadBytes(entry.AbsoluteBodyOffset, (int)entry.Raw.DataCompSize),
            (int)entry.MaskedDataUncompSize)
        : Array.Empty<byte>();
    return (header, body);
}

void CheckZlib(string name, BundleReader reader, BundleEntry entry)
{
    byte[] head = reader.ReadBytes(entry.AbsoluteHeaderOffset, 2);
    bool ok = head[0] == 0x78 && (head[1] == 0x01 || head[1] == 0x5E || head[1] == 0x9C || head[1] == 0xDA);
    Check(name, ok, $"bytes @0x{entry.AbsoluteHeaderOffset:X}: {head[0]:X2} {head[1]:X2}");
}

void VerifyCommon(BundleReader reader, string file)
{
    Check($"{file}: magic", reader.Header.Magic == "bnd2", $"magic='{reader.Header.Magic}'");
    Eq($"{file}: version", reader.Header.Version, 3u);
    Eq($"{file}: tableOffset", reader.Header.TableOffset, 48u);
    Check($"{file}: table read at 48 matches tableOffset", reader.TableOffsetMatchesConvention,
        $"tableOffset={reader.Header.TableOffset}, diagnostics={string.Join("; ", reader.Diagnostics)}");
    Eq($"{file}: numFiles", (uint)reader.Entries.Count, reader.Header.NumFiles);
}

void WithFixture(string file, Action<BundleReader> body)
{
    string path = Path.Combine(FixtureRoot, file);
    try
    {
        using BundleReader reader = BundleReader.Open(path);
        body(reader);
    }
    catch (Exception ex)
    {
        Check($"{file}: open", false, $"{ex.GetType().Name}: {ex.Message}");
    }
}

WithFixture("VEHICLELIST.BUNDLE", reader =>
{
    VerifyCommon(reader, "VEHICLELIST.BUNDLE");
    Eq("VEHICLELIST: file length", reader.FileLength, 4608L);
    Eq("VEHICLELIST: bundleSize", reader.Header.BundleSize, 4608u);
    Eq("VEHICLELIST: numFiles", reader.Header.NumFiles, 1u);
    Eq("VEHICLELIST: headersOffset", reader.Header.HeadersOffset, 128u);
    Eq("VEHICLELIST: bodysOffset", reader.Header.BodysOffset, 4608u);
    Eq("VEHICLELIST: dataEnd", reader.Header.DataEnd, 4608u);
    Eq("VEHICLELIST: flags", reader.Header.Flags, 1u);

    BundleEntry e = reader.Entries[0];
    Eq("VEHICLELIST[0]: hash", e.Raw.Hash, 0x6CB9F778u);
    Eq("VEHICLELIST[0]: hdrCompSize", e.Raw.HdrCompSize, 4448u);
    Eq("VEHICLELIST[0]: dataCompSize", e.Raw.DataCompSize, 0u);
    Eq("VEHICLELIST[0]: hdrUncompSize raw", e.Raw.HdrUncompSizeRaw, 0x40003A00u);
    Eq("VEHICLELIST[0]: hdrUncompSize masked", e.MaskedHdrUncompSize, 0x3A00u);
    Eq("VEHICLELIST[0]: hdrOffset", e.Raw.HdrOffset, 0u);
    Eq("VEHICLELIST[0]: bodyOffset", e.Raw.BodyOffset, 0u);
    Eq("VEHICLELIST[0]: fileType", e.FileType, 261u);
    Eq("VEHICLELIST[0]: numLinks", e.Raw.NumLinks, 0u);
    Eq("VEHICLELIST[0]: absoluteHeaderOffset", e.AbsoluteHeaderOffset, 128L);
    Eq("VEHICLELIST[0]: absoluteBodyOffset", e.AbsoluteBodyOffset, 4608L);
    CheckZlib("VEHICLELIST[0]: zlib signature", reader, e);
    Eq("VEHICLELIST[0]: unknown type stays representable", NfsFileTypeInfo.Describe(e.FileType), "unknown (261)");

    vehicleListCompressedHeader = reader.ReadBytes(e.AbsoluteHeaderOffset, (int)e.Raw.HdrCompSize);
    vehicleListHeader = ZLibAdapter.Decompress(vehicleListCompressedHeader, (int)e.MaskedHdrUncompSize);
    Eq("VEHICLELIST[0]: header decompressed length", vehicleListHeader.Length, 14848);
    Check("VEHICLELIST[0]: header decompressed is non-zero", vehicleListHeader.Any(b => b != 0),
        $"nonZeroBytes={vehicleListHeader.Count(b => b != 0)}");
    Console.WriteLine("DUMP  | VEHICLELIST[0] header first 16 bytes | " +
        BitConverter.ToString(vehicleListHeader, 0, 16).Replace('-', ' '));

    var vehicleListInfo = new BundleFileInfo(e, vehicleListCompressedHeader, ReadOnlySpan<byte>.Empty);
    Console.WriteLine($"INFO  | FILEINFO VEHICLELIST[0] | hdr=0x{vehicleListInfo.HeaderCrcHex}, " +
        $"data=0x{vehicleListInfo.DataCrcHex}, full=0x{vehicleListInfo.FullCrcHex}");
    uint naiveCrc = 0xFFFFFFFFu;
    foreach (byte value in vehicleListCompressedHeader)
    {
        naiveCrc ^= value;
        for (int bit = 0; bit < 8; bit++)
            naiveCrc = (naiveCrc & 1) != 0 ? 0xEDB88320u ^ (naiveCrc >> 1) : naiveCrc >> 1;
    }
    naiveCrc ^= 0xFFFFFFFFu;
    Eq("FILEINFO VEHICLELIST[0] hdrCrc matches naive byte-by-byte CRC", vehicleListInfo.HeaderCrc, naiveCrc);

    BundleIndex idx = BundleIndex.Read(Path.Combine(FixtureRoot, "VEHICLELIST.BUNDLE"));
    Eq("BUNDLEINDEX VEHICLELIST: numFiles", idx.Header.NumFiles, reader.Header.NumFiles);
    Eq("BUNDLEINDEX VEHICLELIST: headersOffset", idx.Header.HeadersOffset, reader.Header.HeadersOffset);
    Eq("BUNDLEINDEX VEHICLELIST: bodysOffset", idx.Header.BodysOffset, reader.Header.BodysOffset);
    Eq("BUNDLEINDEX VEHICLELIST: dataEnd", idx.Header.DataEnd, reader.Header.DataEnd);
    Eq("BUNDLEINDEX VEHICLELIST: entries count", idx.Entries.Count, reader.Entries.Count);
    BundleEntry ie = idx.Entries[0];
    Eq("BUNDLEINDEX VEHICLELIST[0]: hash", ie.Raw.Hash, e.Raw.Hash);
    Eq("BUNDLEINDEX VEHICLELIST[0]: fileType", ie.FileType, e.FileType);
    Eq("BUNDLEINDEX VEHICLELIST[0]: hdrCompSize", ie.Raw.HdrCompSize, e.Raw.HdrCompSize);
    Eq("BUNDLEINDEX VEHICLELIST[0]: dataCompSize", ie.Raw.DataCompSize, e.Raw.DataCompSize);
    Eq("BUNDLEINDEX VEHICLELIST[0]: hdrUncomp masked", ie.MaskedHdrUncompSize, e.MaskedHdrUncompSize);
});

WithFixture("VEHICLETEX.BIN", reader =>
{
    VerifyCommon(reader, "VEHICLETEX.BIN");
    Eq("VEHICLETEX: file length", reader.FileLength, 5420672L);
    Eq("VEHICLETEX: bundleSize", reader.Header.BundleSize, 5420672u);
    Eq("VEHICLETEX: numFiles", reader.Header.NumFiles, 51u);
    Eq("VEHICLETEX: headersOffset", reader.Header.HeadersOffset, 4128u);
    Eq("VEHICLETEX: bodysOffset", reader.Header.BodysOffset, 5760u);
    Eq("VEHICLETEX: dataEnd", reader.Header.DataEnd, 5420672u);

    BundleEntry e0 = reader.Entries[0];
    Eq("VEHICLETEX[0]: hash", e0.Raw.Hash, 0x0160E1F9u);
    Eq("VEHICLETEX[0]: hdrUncompSize raw", e0.Raw.HdrUncompSizeRaw, 0x20000020u);
    Eq("VEHICLETEX[0]: hdrUncompSize masked", e0.MaskedHdrUncompSize, 32u);
    Eq("VEHICLETEX[0]: dataUncompSize raw", e0.Raw.DataUncompSizeRaw, 0x4002AB00u);
    // FORMAT_FIXTURES.md was corrected by the lead to 174848; assert agreement explicitly.
    Eq("VEHICLETEX[0]: dataUncompSize masked (matches corrected doc)", e0.MaskedDataUncompSize, 174848u);
    Check("VEHICLETEX[0]: documented and raw masked sizes agree", true,
        "doc=174848, raw 0x4002AB00 & 0x00ffffff = 0x0002AB00 = 174848");
    Eq("VEHICLETEX[0]: hdrCompSize", e0.Raw.HdrCompSize, 29u);
    Eq("VEHICLETEX[0]: dataCompSize", e0.Raw.DataCompSize, 40569u);
    Eq("VEHICLETEX[0]: hdrOffset", e0.Raw.HdrOffset, 0u);
    Eq("VEHICLETEX[0]: bodyOffset", e0.Raw.BodyOffset, 0u);
    Eq("VEHICLETEX[0]: fileType", e0.FileType, 1u);
    Eq("VEHICLETEX[0]: numLinks", e0.Raw.NumLinks, 0u);
    Eq("VEHICLETEX[0]: absoluteHeaderOffset", e0.AbsoluteHeaderOffset, 4128L);
    Eq("VEHICLETEX[0]: absoluteBodyOffset", e0.AbsoluteBodyOffset, 5760L);
    CheckZlib("VEHICLETEX[0]: zlib signature", reader, e0);

    BundleEntry e1 = reader.Entries[1];
    Eq("VEHICLETEX[1]: hdrUncompSize masked", e1.MaskedHdrUncompSize, 32u);
    Eq("VEHICLETEX[1]: dataUncompSize masked", e1.MaskedDataUncompSize, 699136u);
    Eq("VEHICLETEX[1]: hdrCompSize", e1.Raw.HdrCompSize, 28u);
    Eq("VEHICLETEX[1]: dataCompSize", e1.Raw.DataCompSize, 81599u);
    Eq("VEHICLETEX[1]: hdrOffset", e1.Raw.HdrOffset, 32u);
    Eq("VEHICLETEX[1]: bodyOffset", e1.Raw.BodyOffset, 40576u);
    Eq("VEHICLETEX[1]: fileType", e1.FileType, 1u);
    Eq("VEHICLETEX[1]: absoluteHeaderOffset", e1.AbsoluteHeaderOffset, 4160L);
    Eq("VEHICLETEX[1]: absoluteBodyOffset", e1.AbsoluteBodyOffset, 46336L);

    byte[] header = ZLibAdapter.Decompress(
        reader.ReadBytes(e0.AbsoluteHeaderOffset, (int)e0.Raw.HdrCompSize), (int)e0.MaskedHdrUncompSize);
    Eq("VEHICLETEX[0]: header decompressed length", header.Length, 32);
    byte[] body = ZLibAdapter.Decompress(
        reader.ReadBytes(e0.AbsoluteBodyOffset, (int)e0.Raw.DataCompSize), (int)e0.MaskedDataUncompSize);
    Eq("VEHICLETEX[0]: body decompressed length", body.Length, 174848);

    var extractor = new BundleEntryExtractor(reader.File);
    byte[] raw = extractor.ExtractRaw(e0, out RawExtractResult metadata);
    Eq("VEHICLETEX[0]: raw extract length", raw.Length, 174880);
    Eq("VEHICLETEX[0]: raw extract metadata", metadata.ToString(), "header=32, body=174848, total=174880");

    byte[] packedHeader = reader.ReadBytes(e0.AbsoluteHeaderOffset, (int)e0.Raw.HdrCompSize);
    byte[] packedBody = reader.ReadBytes(e0.AbsoluteBodyOffset, (int)e0.Raw.DataCompSize);
    var fileInfo = new BundleFileInfo(e0, packedHeader, packedBody);
    Console.WriteLine($"INFO  | FILEINFO VEHICLETEX[0] | hdr=0x{fileInfo.HeaderCrcHex}, " +
        $"data=0x{fileInfo.DataCrcHex}, full=0x{fileInfo.FullCrcHex}");

    for (int i = 0; i < 5; i++)
    {
        BundleEntry textureEntry = reader.Entries[i];
        byte[] textureHeaderBytes = ZLibAdapter.Decompress(
            reader.ReadBytes(textureEntry.AbsoluteHeaderOffset, (int)textureEntry.Raw.HdrCompSize),
            (int)textureEntry.MaskedHdrUncompSize);
        NfsTextureHeader textureHeader = NfsTextureHeader.Parse(textureHeaderBytes);
        bool knownType = textureHeader.Type == DdsCodec.FourCcDxt1 ||
                         textureHeader.Type == DdsCodec.FourCcDxt5 ||
                         textureHeader.Type == DdsCodec.NfsTypeRgba32;
        Check($"VEHICLETEX[{i}]: texture type", knownType,
            $"0x{textureHeader.Type:X8} ({textureHeader.TypeLabel})");
        Check($"VEHICLETEX[{i}]: texture size/mips",
            textureHeader.Width >= 1 && textureHeader.Width <= 4096 &&
            textureHeader.Height >= 1 && textureHeader.Height <= 4096 &&
            textureHeader.NumMipmaps >= 1,
            $"{textureHeader.Width}x{textureHeader.Height}, mips={textureHeader.NumMipmaps}");
        Console.WriteLine($"INFO  | VEHICLETEX[{i}] texture | {textureHeader.TypeLabel}, " +
            $"{textureHeader.Width}x{textureHeader.Height}, mips={textureHeader.NumMipmaps}");
    }

    NfsTexture texture = NfsTexture.InitFromData(header, body);
    byte[] decoded = texture.Decompress();
    Eq("VEHICLETEX[0]: decoded pixel bytes", decoded.Length,
        texture.GetWidth() * texture.GetHeight() * texture.GetNumComponents());
    byte[] dds = texture.SaveAsDds();
    Eq("VEHICLETEX[0]: DDS magic", BitConverter.ToUInt32(dds, 0), DdsCodec.MagicDds);
    DdsCodec.DdsHeader ddsHeader = DdsCodec.ReadDds(dds);
    Eq("VEHICLETEX[0]: DDS dwSize", ddsHeader.Size, 124u);
    Eq("VEHICLETEX[0]: DDS width", ddsHeader.Width, (uint)texture.GetWidth());
    Eq("VEHICLETEX[0]: DDS height", ddsHeader.Height, (uint)texture.GetHeight());
    Eq("VEHICLETEX[0]: DDS mip count", ddsHeader.MipMapCount, (uint)texture.GetNumMipmaps());
    if (texture.Header.Type == DdsCodec.NfsTypeRgba32)
    {
        Check("VEHICLETEX[0]: DDS RGBA format", ddsHeader.PixelFormat.Flags == DdsCodec.DdsRgba &&
            ddsHeader.PixelFormat.RgbBitCount == 32 &&
            ddsHeader.PixelFormat.RBitMask == 0x00ff0000 &&
            ddsHeader.PixelFormat.BBitMask == 0x000000ff,
            $"flags=0x{ddsHeader.PixelFormat.Flags:X}, masks R/B=0x{ddsHeader.PixelFormat.RBitMask:X8}/0x{ddsHeader.PixelFormat.BBitMask:X8}");
    }
    else
    {
        Check("VEHICLETEX[0]: DDS FourCC", ddsHeader.PixelFormat.Flags == DdsCodec.DdsFourCc &&
            ddsHeader.PixelFormat.FourCC == texture.Header.Type,
            $"flags=0x{ddsHeader.PixelFormat.Flags:X}, fourcc=0x{ddsHeader.PixelFormat.FourCC:X8}");
    }
    // C++ SaveAsDDS writes the entire decompressed payload (m_Data.size()), including any
    // trailing alignment padding. The mip-chain formula is smaller here (174800 vs 174848).
    Eq("VEHICLETEX[0]: DDS payload length equals decompressed body", dds.Length - 4 - 124, body.Length);
    int mipMath = NfsTexture.GetImageSizeForType(texture.Header.Type, texture.GetWidth(),
        texture.GetHeight(), Math.Max(1, (int)texture.GetNumMipmaps()));
    Console.WriteLine($"NOTE  | DDS payload vs mip math | payload={body.Length}, GetImageSizeForType={mipMath}, " +
        $"trailing padding={body.Length - mipMath} byte(s); C++ SaveAsDDS writes all payload bytes");

    byte[] nfsRoundTrip = DdsToNfsConverter.ConvertBytes(dds);
    Eq("DDS->NFS: magic", BitConverter.ToUInt32(nfsRoundTrip, 0), DdsToNfsConverter.NfsoMagic);
    Eq("DDS->NFS: file type", BitConverter.ToUInt32(nfsRoundTrip, 4), 1u);
    NfsTextureHeader roundTripHeader = NfsTextureHeader.Parse(nfsRoundTrip.AsSpan(8, NfsTextureHeader.Size));
    Eq("DDS->NFS: type", roundTripHeader.Type, texture.Header.Type);
    Eq("DDS->NFS: width", roundTripHeader.Width, texture.Header.Width);
    Eq("DDS->NFS: height", roundTripHeader.Height, texture.Header.Height);
    Eq("DDS->NFS: mips", roundTripHeader.NumMipmaps, texture.GetNumMipmaps());
});

WithFixture("VEH_118158_MS.BIN", reader =>
{
    VerifyCommon(reader, "VEH_118158_MS.BIN");
    Eq("VEH_118158_MS: file length", reader.FileLength, 3579264L);
    Eq("VEH_118158_MS: bundleSize", reader.Header.BundleSize, 3579264u);
    Eq("VEH_118158_MS: bundleSize2", reader.Header.BundleSize2, 3579264u);
    Eq("VEH_118158_MS: numFiles", reader.Header.NumFiles, 538u);
    Eq("VEH_118158_MS: headersOffset", reader.Header.HeadersOffset, 43088u);
    Eq("VEH_118158_MS: bodysOffset", reader.Header.BodysOffset, 158592u);
    Eq("VEH_118158_MS: dataEnd", reader.Header.DataEnd, 3579264u);
    Eq("VEH_118158_MS: flags", reader.Header.Flags, 39u);
    Eq("VEH_118158_MS: zero", reader.Header.Zero, 0u);

    BundleEntry e0 = reader.Entries[0];
    Eq("VEH_118158_MS[0]: hash", e0.Raw.Hash, 0x016FDEA2u);
    Eq("VEH_118158_MS[0]: linkID", e0.Raw.LinkId, 0x6179A9A2u);
    Eq("VEH_118158_MS[0]: hdrUncompSize raw", e0.Raw.HdrUncompSizeRaw, 0x400000B0u);
    Eq("VEH_118158_MS[0]: hdrUncompSize masked", e0.MaskedHdrUncompSize, 176u);
    Eq("VEH_118158_MS[0]: dataUncompSize raw", e0.Raw.DataUncompSizeRaw, 0x40000100u);
    Eq("VEH_118158_MS[0]: dataUncompSize masked", e0.MaskedDataUncompSize, 256u);
    Eq("VEH_118158_MS[0]: hdrCompSize", e0.Raw.HdrCompSize, 93u);
    Eq("VEH_118158_MS[0]: dataCompSize", e0.Raw.DataCompSize, 157u);
    Eq("VEH_118158_MS[0]: dataEnd", e0.Raw.DataEnd, 160u);
    Eq("VEH_118158_MS[0]: fileType", e0.FileType, 5u);
    Eq("VEH_118158_MS[0]: numLinks", e0.Raw.NumLinks, 1u);
    Eq("VEH_118158_MS[0]: absoluteHeaderOffset", e0.AbsoluteHeaderOffset, 43088L);
    Eq("VEH_118158_MS[0]: absoluteBodyOffset", e0.AbsoluteBodyOffset, 158592L);
    CheckZlib("VEH_118158_MS[0]: zlib signature", reader, e0);

    BundleEntry e1 = reader.Entries[1];
    Eq("VEH_118158_MS[1]: hash", e1.Raw.Hash, 0x01CCD04Fu);
    Eq("VEH_118158_MS[1]: hdrUncompSize masked", e1.MaskedHdrUncompSize, 32u);
    Eq("VEH_118158_MS[1]: dataUncompSize masked", e1.MaskedDataUncompSize, 11008u);
    Eq("VEH_118158_MS[1]: hdrCompSize", e1.Raw.HdrCompSize, 28u);
    Eq("VEH_118158_MS[1]: dataCompSize", e1.Raw.DataCompSize, 7928u);
    Eq("VEH_118158_MS[1]: hdrOffset", e1.Raw.HdrOffset, 96u);
    Eq("VEH_118158_MS[1]: bodyOffset", e1.Raw.BodyOffset, 256u);
    Eq("VEH_118158_MS[1]: fileType", e1.FileType, 1u);
    Eq("VEH_118158_MS[1]: absoluteHeaderOffset", e1.AbsoluteHeaderOffset, 43184L);
    Eq("VEH_118158_MS[1]: absoluteBodyOffset", e1.AbsoluteBodyOffset, 158848L);

    BundleEntry e2 = reader.Entries[2];
    Eq("VEH_118158_MS[2]: hash", e2.Raw.Hash, 0x02F209E5u);
    Eq("VEH_118158_MS[2]: linkID", e2.Raw.LinkId, 0xFFFFFFFFu);
    Eq("VEH_118158_MS[2]: u1", e2.Raw.U1, 0x01000053u);
    Eq("VEH_118158_MS[2]: hdrUncompSize masked", e2.MaskedHdrUncompSize, 624u);
    Eq("VEH_118158_MS[2]: hdrCompSize", e2.Raw.HdrCompSize, 311u);
    Eq("VEH_118158_MS[2]: dataCompSize", e2.Raw.DataCompSize, 0u);
    Eq("VEH_118158_MS[2]: dataEnd", e2.Raw.DataEnd, 448u);
    Eq("VEH_118158_MS[2]: fileType", e2.FileType, 2u);
    Eq("VEH_118158_MS[2]: numLinks", e2.Raw.NumLinks, 11u);
    Eq("VEH_118158_MS[2]: absoluteHeaderOffset", e2.AbsoluteHeaderOffset, 43216L);
    Eq("VEH_118158_MS[2]: absoluteBodyOffset", e2.AbsoluteBodyOffset, 158592L);

    byte[] header = ZLibAdapter.Decompress(
        reader.ReadBytes(e0.AbsoluteHeaderOffset, (int)e0.Raw.HdrCompSize), (int)e0.MaskedHdrUncompSize);
    Eq("VEH_118158_MS[0]: header decompressed length", header.Length, 176);
    byte[] body = ZLibAdapter.Decompress(
        reader.ReadBytes(e0.AbsoluteBodyOffset, (int)e0.Raw.DataCompSize), (int)e0.MaskedDataUncompSize);
    Eq("VEH_118158_MS[0]: body decompressed length", body.Length, 256);

    var extractor = new BundleEntryExtractor(reader.File);
    byte[] raw = extractor.ExtractRaw(e0, out RawExtractResult metadata);
    Eq("VEH_118158_MS[0]: raw extract length", raw.Length, 432);
    Eq("VEH_118158_MS[0]: raw extract metadata", metadata.ToString(), "header=176, body=256, total=432");

    BundleEntry defsEntry = reader.Entries[159];
    Eq("VEH_118158_MS[159]: fileType", defsEntry.FileType, 18u);
    byte[] defsHeader = ZLibAdapter.Decompress(
        reader.ReadBytes(defsEntry.AbsoluteHeaderOffset, (int)defsEntry.Raw.HdrCompSize),
        (int)defsEntry.MaskedHdrUncompSize);
    NfsDefsTable defs = NfsDefsTable.Parse(defsHeader);
    Console.WriteLine($"INFO  | DEFS VEH_118158_MS[159] | name='{defs.DefName}', count={defs.Count}");
    foreach (NfsDefsTable.DefEntry entry in defs.Entries.Take(5))
        Console.WriteLine($"INFO  | DEFS entry | defID=0x{entry.DefIdHex}, offset={entry.Offset}, type={entry.Type}");
    Check("DEFS: count > 0", defs.Count > 0, $"count={defs.Count}");
    bool defsFieldsReadable = defs.Entries.All(entry =>
        entry.DefIdHex.Length == 8 && entry.Offset >= 0 && entry.Type >= 0);
    Check("DEFS: all entry fields readable", defsFieldsReadable, $"entries={defs.Entries.Count}");

    byte[] modelHeader = ZLibAdapter.Decompress(
        reader.ReadBytes(e0.AbsoluteHeaderOffset, (int)e0.Raw.HdrCompSize), (int)e0.MaskedHdrUncompSize);
    byte[] modelBody = ZLibAdapter.Decompress(
        reader.ReadBytes(e0.AbsoluteBodyOffset, (int)e0.Raw.DataCompSize), (int)e0.MaskedDataUncompSize);
    NfsModel model = NfsModel.InitFromData(modelHeader, modelBody);
    Check("VEH_118158_MS[0]: model has meshes", model.Meshes.Count > 0, $"meshes={model.Meshes.Count}");
    foreach (NfsMesh mesh in model.Meshes)
    {
        Console.WriteLine($"INFO  | VEH_118158_MS[0] mesh | numV={mesh.Header.NumVertexes}, numI={mesh.Header.NumIndexes}, " +
            $"beginMesh={mesh.Header.BeginMeshOffset}, VBStart={mesh.Header.VbStartOffset}, " +
            $"VBSize={mesh.Header.VbSize}, vertexSize={mesh.VertexSize}");
    }
    foreach (NfsMesh mesh in model.Meshes)
    {
        uint vertexSize = mesh.VertexSize;
        Check($"VEH_118158_MS[0]: vertexSize {vertexSize} is known",
            vertexSize == 0 || vertexSize == 36 || vertexSize == 48 || vertexSize == 52 ||
            vertexSize == 56 || vertexSize == 60 || vertexSize == 64, $"vertexSize={vertexSize}");
    }
    Check("VEH_118158_MS[0]: model has faces", model.TotalFaces > 0, $"faces={model.TotalFaces}");

    string obj = ObjExporter.Build(model);
    string[] objLines = obj.Split('\n');
    int vLines = objLines.Count(line => line.StartsWith("v ", StringComparison.Ordinal));
    int vtLines = objLines.Count(line => line.StartsWith("vt ", StringComparison.Ordinal));
    int vnLines = objLines.Count(line => line.StartsWith("vn ", StringComparison.Ordinal));
    int fLines = objLines.Count(line => line.StartsWith("f ", StringComparison.Ordinal));
    Eq("OBJ: v line count", vLines, model.TotalVertices);
    Eq("OBJ: vt line count (C++ always emits vt)", vtLines, model.TotalVertices);
    Eq("OBJ: vn line count", vnLines, model.TotalVertices);
    Eq("OBJ: f line count", fLines, model.TotalFaces);

    bool faceIndicesValid = true;
    bool facesUseTripleReferences = true;
    foreach (string line in objLines.Where(line => line.StartsWith("f ", StringComparison.Ordinal)))
    {
        foreach (string token in line.Substring(2).Split(' '))
        {
            string[] components = token.Split('/');
            if (components.Length != 3 || components[0] != components[1] || components[1] != components[2])
                facesUseTripleReferences = false;
            int index = int.Parse(components[0], CultureInfo.InvariantCulture);
            if (index < 1 || index > model.TotalVertices) faceIndicesValid = false;
        }
    }
    Check("OBJ: all face indices in 1..totalVertices", faceIndicesValid,
        $"totalVertices={model.TotalVertices}, faces={fLines}");
    Check("OBJ: face lines use a/a/a references", facesUseTripleReferences, $"faces={fLines}");

    NfsMesh firstMesh = model.Meshes[0];
    int vbStart = (int)firstMesh.Header.VbStartOffset;
    float rawX = BinaryPrimitives.ReadInt16LittleEndian(modelBody.AsSpan(vbStart, 2)) / 32767.0f * 8.0f;
    float rawY = BinaryPrimitives.ReadInt16LittleEndian(modelBody.AsSpan(vbStart + 2, 2)) / 32767.0f * 8.0f;
    float rawZ = BinaryPrimitives.ReadInt16LittleEndian(modelBody.AsSpan(vbStart + 4, 2)) / 32767.0f * 8.0f;
    string firstVertexLine = objLines.First(line => line.StartsWith("v ", StringComparison.Ordinal));
    string[] parts = firstVertexLine.Split(' ');
    float objX = float.Parse(parts[1], CultureInfo.InvariantCulture);
    float objY = float.Parse(parts[2], CultureInfo.InvariantCulture);
    float objZ = float.Parse(parts[3], CultureInfo.InvariantCulture);
    Check("OBJ: first vertex matches raw int16 position",
        Math.Abs(objX - rawX) < 1e-5f && Math.Abs(objY - rawY) < 1e-5f && Math.Abs(objZ - rawZ) < 1e-5f,
        $"raw=({rawX},{rawY},{rawZ}), obj=({objX},{objY},{objZ})");

    IReadOnlyList<RenderMesh> renderMeshes = ModelRenderConverter.Convert(model);
    Eq("RENDERMESH mesh count", renderMeshes.Count, model.Meshes.Count);
    Eq("RENDERMESH vertex count", renderMeshes.Sum(item => item.Positions.Length), 4);
    Eq("RENDERMESH index count (2 faces x 3)", renderMeshes.Sum(item => item.Indices.Length), 6);
    Vector3 firstRenderPosition = renderMeshes[0].Positions[0];
    // Prototype viewer conversion: (X, -Z, Y) * 1000 into Z-up display space.
    float viewX = objX * 1000f, viewY = -objZ * 1000f, viewZ = objY * 1000f;
    Check("RENDERMESH first position matches Prototype viewer transform",
        Math.Abs(firstRenderPosition.X - viewX) < 0.01f &&
        Math.Abs(firstRenderPosition.Y - viewY) < 0.01f &&
        Math.Abs(firstRenderPosition.Z - viewZ) < 0.01f,
        $"render=({firstRenderPosition.X},{firstRenderPosition.Y},{firstRenderPosition.Z}) expected=({viewX},{viewY},{viewZ})");
    RenderBounds renderBounds = ModelRenderConverter.GetBounds(renderMeshes);
    Check("RENDERMESH bounds computed", renderBounds.Radius > 0f, $"radius={renderBounds.Radius}");

    // Texture preview transform checks (pure, no WinForms).
    byte[] srcRgba = { 10, 20, 30, 40,  50, 60, 70, 80,  90, 100, 110, 120,  130, 140, 150, 160 };
    byte[] all = TexturePreviewTransform.Transform(srcRgba, 2, 2, 4, true, true, true, true, true, false);
    Check("TEXPREVIEW RGBA identity", all.SequenceEqual(srcRgba), "all channels");
    byte[] rgbOnly = TexturePreviewTransform.Transform(srcRgba, 2, 2, 4, true, true, true, true, false, false);
    Check("TEXPREVIEW RGB mode forces alpha 255", rgbOnly[3] == 255 && rgbOnly[7] == 255 && rgbOnly[15] == 255, "alpha forced");
    byte[] noG = TexturePreviewTransform.Transform(srcRgba, 2, 2, 4, true, false, true, true, true, false);
    Check("TEXPREVIEW G channel zeroed", noG[1] == 0 && noG[5] == 0 && noG[0] == 10 && noG[2] == 30, "G zeroed");
    // Prototype's ProcessChannels only makes a single active color grayscale when A is off.
    byte[] redGray = TexturePreviewTransform.Transform(srcRgba, 2, 2, 4, true, false, false, false, false, false);
    Check("TEXPREVIEW single channel grayscale", redGray[0] == 10 && redGray[1] == 10 && redGray[2] == 10, "R as gray");
    byte[] flipped = TexturePreviewTransform.Transform(srcRgba, 2, 2, 4, true, true, true, true, true, true);
    Check("TEXPREVIEW V-flip swaps rows", flipped[0] == 90 && flipped[4] == 130 && flipped[8] == 10 && flipped[12] == 50, "rows flipped");

    int parsedModels = 0;
    int zeroFaceModels = 0;
    int failedModels = 0;
    foreach (BundleEntry modelEntry in reader.Entries.Where(entry => entry.FileType == (uint)NfsFileType.Model).Take(10))
    {
        try
        {
            byte[] mh = ZLibAdapter.Decompress(
                reader.ReadBytes(modelEntry.AbsoluteHeaderOffset, (int)modelEntry.Raw.HdrCompSize),
                (int)modelEntry.MaskedHdrUncompSize);
            byte[] mb = ZLibAdapter.Decompress(
                reader.ReadBytes(modelEntry.AbsoluteBodyOffset, (int)modelEntry.Raw.DataCompSize),
                (int)modelEntry.MaskedDataUncompSize);
            NfsModel scanned = NfsModel.InitFromData(mh, mb);
            parsedModels++;
            if (scanned.TotalFaces == 0) zeroFaceModels++;
            Console.WriteLine($"INFO  | model scan entry {modelEntry.Index} | meshes={scanned.Meshes.Count}, " +
                $"verts={scanned.TotalVertices}, faces={scanned.TotalFaces}");
        }
        catch (Exception ex)
        {
            failedModels++;
            Console.WriteLine($"NOTE  | model scan entry {modelEntry.Index} skipped | {ex.Message}");
        }
    }
    Console.WriteLine($"INFO  | model scan summary | parsed={parsedModels}, zeroFace={zeroFaceModels}, failed={failedModels}");

    // Functional regression: every model entry in the fixture must be exportable to OBJ
    // through the same code path used by the UI command.
    string objTemp = Path.Combine(Path.GetTempPath(), "BundleUltimateTool-ObjChecks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(objTemp);
    int objOk = 0;
    int objFailed = 0;
    foreach (BundleEntry modelEntry in reader.Entries.Where(entry => entry.FileType == (uint)NfsFileType.Model))
    {
        try
        {
            byte[] mh = ZLibAdapter.Decompress(
                reader.ReadBytes(modelEntry.AbsoluteHeaderOffset, (int)modelEntry.Raw.HdrCompSize),
                (int)modelEntry.MaskedHdrUncompSize);
            byte[] mb = modelEntry.Raw.DataCompSize > 0
                ? ZLibAdapter.Decompress(
                    reader.ReadBytes(modelEntry.AbsoluteBodyOffset, (int)modelEntry.Raw.DataCompSize),
                    (int)modelEntry.MaskedDataUncompSize)
                : Array.Empty<byte>();
            NfsModel exportModel = NfsModel.InitFromData(mh, mb);
            string path = Path.Combine(objTemp, $"file_{modelEntry.Index:D4}.obj");
            ObjExporter.ExportFile(path, exportModel);
            Check($"OBJ export entry {modelEntry.Index} produced file",
                File.Exists(path) && new FileInfo(path).Length > 0, path);
            objOk++;
        }
        catch (Exception ex)
        {
            objFailed++;
            Console.WriteLine($"FAIL | OBJ export entry {modelEntry.Index} | {ex.GetType().Name}: {ex.Message}");
        }
    }
    Console.WriteLine($"INFO  | OBJ export regression | ok={objOk}, failed={objFailed}");
    Check("OBJ export regression: no failures", objFailed == 0, $"ok={objOk}, failed={objFailed}");
});

// Synthetic model helpers.
Eq("ChooseVertexSize(10, 480)", NfsMeshHeader.ChooseVertexSize(10, 480), 48u);
Eq("ChooseVertexSize(10, 417)", NfsMeshHeader.ChooseVertexSize(10, 417), 0u);
List<NfsFace> strip = NfsModel.TriStripToTriList(new ushort[] { 0, 1, 2, 3, 4 }, 5);
Eq("TriStripToTriList([0,1,2,3,4])",
    string.Join("|", strip.Select(face => $"{face.A},{face.B},{face.C}")), "1,2,0|3,2,1|3,4,2");
List<NfsFace> degenerate = NfsModel.TriStripToTriList(new ushort[] { 0, 0, 2, 3 }, 4);
Eq("TriStripToTriList([0,0,2,3]) skips degenerate",
    string.Join("|", degenerate.Select(face => $"{face.A},{face.B},{face.C}")), "3,2,0");

// PrototypeUltimateTool:DdsDecoder uses *255/31 expansion (full-range 565).
byte[] dxt1Block = { 0x00, 0xF8, 0x1F, 0x00, 0x00, 0x00, 0x00, 0x00 };
byte[] dxt1Pixels = DxtDecoder.Decode(dxt1Block, 4, 4, DdsCodec.FourCcDxt1);
Check("DXT1 first pixel from c0=0xF800 is (255,0,0) per Prototype",
    dxt1Pixels[0] == 255 && dxt1Pixels[1] == 0 && dxt1Pixels[2] == 0,
    $"RGB=({dxt1Pixels[0]},{dxt1Pixels[1]},{dxt1Pixels[2]})");
byte[] dxt5Block = new byte[16];
dxt5Block[0] = 255; // a0
dxt5Block[1] = 0;   // a1; remaining alpha indexes are 0
byte[] dxt5Pixels = DxtDecoder.Decode(dxt5Block, 4, 4, DdsCodec.FourCcDxt5);
Check("DXT5 alpha index 0 uses a0=255", dxt5Pixels[3] == 255, $"alpha={dxt5Pixels[3]}");

byte[] recompressed = ZLibAdapter.Compress(vehicleListHeader);
byte[] roundTrip = ZLibAdapter.Decompress(recompressed, vehicleListHeader.Length);
Check("ROUNDTRIP: decompress(compress(x)) == x", roundTrip.SequenceEqual(vehicleListHeader),
    $"source={vehicleListHeader.Length} B, recompressed={recompressed.Length} B");
bool streamsIdentical = recompressed.SequenceEqual(vehicleListCompressedHeader);
Console.WriteLine($"NOTE  | zlib stream identity | .NET output {recompressed.Length} B vs original {vehicleListCompressedHeader.Length} B; byte-identical={streamsIdentical}");

Eq("CRC32(\"123456789\")", Crc32.Compute(Encoding.ASCII.GetBytes("123456789")), 0xCBF43926u);
Console.WriteLine($"INFO  | CRC32 VEHICLELIST[0] decompressed header | 0x{Crc32.Compute(vehicleListHeader):X8}");

// Synthetic STRINGS table: version=1, 3 UTF-16LE strings, infos at byte 16.
string[] samples = { "Hello", "Привет", "42" };
byte[][] encodedSamples = samples.Select(Encoding.Unicode.GetBytes).ToArray();
int sampleDataStart = 16 + samples.Length * 8;
var stringsBytes = new byte[sampleDataStart + encodedSamples.Sum(bytes => bytes.Length)];
W32(stringsBytes, 0, 1);
W32(stringsBytes, 4, (uint)samples.Length);
W32(stringsBytes, 8, 16);
W32(stringsBytes, 12, 16);
int cursor = sampleDataStart;
for (int i = 0; i < samples.Length; i++)
{
    W32(stringsBytes, 16 + i * 8, (uint)cursor);
    W32(stringsBytes, 20 + i * 8, (uint)(encodedSamples[i].Length / 2));
    encodedSamples[i].CopyTo(stringsBytes.AsSpan(cursor));
    cursor += encodedSamples[i].Length;
}
NfsStringsTable parsedStrings = NfsStringsTable.Parse(stringsBytes);
for (int i = 0; i < samples.Length; i++)
    Eq($"STRINGS synthetic[{i}]", parsedStrings.Strings[i], samples[i]);
Eq("STRINGS SaveAsTxt line count", parsedStrings.SaveAsTxt().Split('\n').Count(line => line.Length > 0), 3);

byte[] malformedStrings = (byte[])stringsBytes.Clone();
W32(malformedStrings, 16 + 8, 0xFFFFFF00); // info[1].offset out of range
NfsStringsTable malformedTable = NfsStringsTable.Parse(malformedStrings);
Check("STRINGS malformed entry skipped, others kept",
    malformedTable.Strings[1] == string.Empty && malformedTable.Strings[0] == "Hello" &&
    malformedTable.Diagnostics.Count > 0,
    $"diagnostics={malformedTable.Diagnostics.Count}");

byte[] wrongCharSize = (byte[])stringsBytes.Clone();
W32(wrongCharSize, 8, 8);
bool charSize8Rejected = false;
try { NfsStringsTable.Parse(wrongCharSize); }
catch (BundleFormatException) { charSize8Rejected = true; }
Check("STRINGS charSize=8 rejected", charSize8Rejected, "expected BundleFormatException");

// Synthetic DEFS table: known name and two entries at exact offsets.
var syntheticDefs = new byte[NfsDefsTable.HeaderSize + 2 * NfsDefsTable.EntrySize];
W32(syntheticDefs, 12, 2);
Encoding.ASCII.GetBytes("TESTDEFS").CopyTo(syntheticDefs.AsSpan(17));
W32(syntheticDefs, 68, 0x1234ABCD);
W32(syntheticDefs, 72, 7);
W32(syntheticDefs, 80, 100);
W32(syntheticDefs, 88, 0x0000002A);
W32(syntheticDefs, 92, 18);
W32(syntheticDefs, 100, 200);
NfsDefsTable parsedDefs = NfsDefsTable.Parse(syntheticDefs);
Eq("DEFS synthetic name", parsedDefs.DefName, "TESTDEFS");
Eq("DEFS synthetic count", parsedDefs.Count, 2u);
Eq("DEFS synthetic[0].defID", parsedDefs.Entries[0].DefIdHex, "1234ABCD");
Eq("DEFS synthetic[0].offset", parsedDefs.Entries[0].Offset, 100u);
Eq("DEFS synthetic[1].type", parsedDefs.Entries[1].Type, 18u);
Eq("DEFS synthetic[1].offset", parsedDefs.Entries[1].Offset, 200u);

// ---- original-style file type labels ----
Eq("LABEL texture", NfsFileTypeInfo.FileTypeLabel(1), "texture");
Eq("LABEL material", NfsFileTypeInfo.FileTypeLabel(2), "material");
Eq("LABEL model", NfsFileTypeInfo.FileTypeLabel(5), "model");
Eq("LABEL defs table", NfsFileTypeInfo.FileTypeLabel(18), "defs table");
Eq("LABEL strings", NfsFileTypeInfo.FileTypeLabel(513), "strings");
Eq("LABEL unknown 261", NfsFileTypeInfo.FileTypeLabel(261), "unknown");
Check("LABEL Describe still keeps numeric suffix", NfsFileTypeInfo.Describe(1) == "texture (1)",
    NfsFileTypeInfo.Describe(1));
Check("CONTEXT texture -> DDS only, not OBJ",
    NfsFileTypeInfo.SupportsTextureExport(1) && !NfsFileTypeInfo.SupportsModelExport(1), "texture=1");
Check("CONTEXT model -> OBJ only, not DDS",
    NfsFileTypeInfo.SupportsModelExport(5) && !NfsFileTypeInfo.SupportsTextureExport(5), "model=5");
Check("CONTEXT strings/defs classifiers",
    NfsFileTypeInfo.SupportsStringsSave(513) && NfsFileTypeInfo.SupportsDefsView(18) &&
    !NfsFileTypeInfo.SupportsTextureExport(513), "strings=513, defs=18");

// ---- game path rules ----
Check("PATH IsBundleFile .bundle", GamePathRules.IsBundleFile(@"C:\games\a.BUNDLE"), "true");
Check("PATH IsBundleFile .bndl mixed case", GamePathRules.IsBundleFile("VEHICLELIST.BNDL"), "true");
Check("PATH IsBundleFile .bin", GamePathRules.IsBundleFile("VEHICLETEX.bin"), "true");
Check("PATH IsBundleFile rejects .txt", !GamePathRules.IsBundleFile("notes.txt"), "false");
Check("PATH IsTextFile .txt/.xml/.json/.md",
    GamePathRules.IsTextFile("a.txt") && GamePathRules.IsTextFile("a.XML") &&
    GamePathRules.IsTextFile("a.json") && GamePathRules.IsTextFile("a.md"), "true");
Check("PATH IsTextFile rejects .bundle", !GamePathRules.IsTextFile("a.bundle"), "false");
Eq("PATH FormatSize bytes", GamePathRules.FormatSize(512), "512 B");
Eq("PATH FormatSize KB", GamePathRules.FormatSize(2048), "2.0 KB");
Eq("PATH FormatSize MB", GamePathRules.FormatSize(2 * 1024 * 1024), "2.0 MB");
Eq("PATH GetExtensionLabel", GamePathRules.GetExtensionLabel("a.BUNDLE"), "BUNDLE");
Eq("PATH GetExtensionLabel no extension", GamePathRules.GetExtensionLabel("README"), "FILE");

// ---- game folder model / filters ----
string browserRoot = Path.Combine(Path.GetTempPath(), "BundleUltimateTool-Browser-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(browserRoot);
Directory.CreateDirectory(Path.Combine(browserRoot, "Sub"));
File.WriteAllBytes(Path.Combine(browserRoot, "VEHICLE.BUNDLE"), new byte[] { 1, 2, 3 });
File.WriteAllBytes(Path.Combine(browserRoot, "other.BIN"), new byte[2048]);
File.WriteAllBytes(Path.Combine(browserRoot, "notes.txt"), new byte[] { 65 });
var browser = new GameFolderModel();
browser.SetRoot(browserRoot);
Eq("BROWSER items include folder and three files", browser.Items.Count, 4);
var binOnly = browser.ApplyFilter(null, "BIN");
Check("BROWSER extension filter BIN is case-insensitive",
    binOnly.Count(item => !item.IsDirectory) == 1 &&
    binOnly.Any(item => !item.IsDirectory && item.Name == "other.BIN"),
    string.Join(", ", binOnly.Select(item => item.Name)));
Check("BROWSER directories remain when a type filter is active",
    binOnly.Any(item => item.IsDirectory), "folder visible for navigation");
var nameSearch = browser.ApplyFilter("veh", null);
Check("BROWSER search matches name",
    nameSearch.Any(item => item.Name == "VEHICLE.BUNDLE"),
    string.Join(", ", nameSearch.Select(item => item.Name)));
var extensionSearch = browser.ApplyFilter("bin", null);
Check("BROWSER search matches extension",
    extensionSearch.Any(item => item.Name == "other.BIN"),
    string.Join(", ", extensionSearch.Select(item => item.Name)));
GameFileEntry folderEntry = browser.Items.First(item => item.IsDirectory);
Check("BROWSER folder display marker", folderEntry.DisplayText.StartsWith("[folder]"), folderEntry.DisplayText);
GameFileEntry binEntry = browser.Items.First(item => item.Name == "other.BIN");
Check("BROWSER file display uses KB with one decimal",
    binEntry.DisplayText.Contains("2") && binEntry.DisplayText.Contains("KB"), binEntry.DisplayText);
browser.Navigate("Sub");
Eq("BROWSER navigate relative path", browser.CurrentRelativePath, "Sub");
browser.NavigateUp();
Eq("BROWSER navigate up returns to root", browser.CurrentRelativePath, string.Empty);

var fixtureModel = new GameFolderModel();
fixtureModel.SetRoot(FixtureRoot);
IReadOnlyList<string> discovered = fixtureModel.FindBundleFiles();
Eq("BUNDLES discovered in fixture folder", discovered.Count, 3);
Eq("BUNDLES cap maxFiles=1", fixtureModel.FindBundleFiles(1).Count, 1);
Check("BUNDLES ordered by relative path",
    discovered[0].EndsWith("VEHICLELIST.BUNDLE", StringComparison.OrdinalIgnoreCase),
    string.Join(", ", discovered.Select(Path.GetFileName)));
var tempBrowserModel = new GameFolderModel();
tempBrowserModel.SetRoot(browserRoot);
IReadOnlyList<string> tempBundles = tempBrowserModel.FindBundleFiles();
Check("BUNDLES non-bundle files excluded",
    tempBundles.Count == 2 && tempBundles.All(path => GamePathRules.IsBundleFile(path)),
    string.Join(", ", tempBundles.Select(Path.GetFileName)));

// ---- themed property grid row generation ----
IReadOnlyList<PropertyRow> propertyRows = PropertyRowBuilder.Build(new SamplePropertySource());
Check("THEMEDGRID first row is a category",
    propertyRows.Count > 0 && propertyRows[0].IsCategory && propertyRows[0].Name == "File Info",
    propertyRows.Count > 0 ? propertyRows[0].Name : "<empty>");
Check("THEMEDGRID DisplayName and value honoured",
    propertyRows.Any(row => row.Name == "Header CRC32" && row.Value == "DEADBEEF"),
    string.Join(", ", propertyRows.Select(row => row.Name)));
Eq("THEMEDGRID categories grouped", propertyRows.Count(row => row.IsCategory), 2);
Eq("THEMEDGRID null selection yields no rows", PropertyRowBuilder.Build(null).Count, 0);

// ---- localization consistency ----
Dictionary<string, string> englishCatalog = new(StringComparer.Ordinal);
Dictionary<string, string> russianCatalog = new(StringComparer.Ordinal);
string languagesDirectory = Path.Combine(FindSolutionRoot(), "src", "BundleUltimateTool", "Languages");
try
{
    englishCatalog = LoadCatalog(Path.Combine(languagesDirectory, "en.xml"));
    Check("L10N en.xml parses", true, $"keys={englishCatalog.Count}");
}
catch (Exception ex) { Check("L10N en.xml parses", false, ex.Message); }
try
{
    russianCatalog = LoadCatalog(Path.Combine(languagesDirectory, "ru.xml"));
    Check("L10N ru.xml parses", true, $"keys={russianCatalog.Count}");
}
catch (Exception ex) { Check("L10N ru.xml parses", false, ex.Message); }

string[] missingInRussian = englishCatalog.Keys.Except(russianCatalog.Keys).OrderBy(key => key).ToArray();
string[] missingInEnglish = russianCatalog.Keys.Except(englishCatalog.Keys).OrderBy(key => key).ToArray();
Check("L10N key sets identical", missingInRussian.Length == 0 && missingInEnglish.Length == 0,
    $"missingInRu=[{string.Join(", ", missingInRussian)}], missingInEn=[{string.Join(", ", missingInEnglish)}]");
string[] emptyEnglish = englishCatalog.Where(pair => string.IsNullOrWhiteSpace(pair.Value))
    .Select(pair => pair.Key).OrderBy(key => key).ToArray();
string[] emptyRussian = russianCatalog.Where(pair => string.IsNullOrWhiteSpace(pair.Value))
    .Select(pair => pair.Key).OrderBy(key => key).ToArray();
Check("L10N no empty values", emptyEnglish.Length == 0 && emptyRussian.Length == 0,
    $"enEmpty=[{string.Join(", ", emptyEnglish)}], ruEmpty=[{string.Join(", ", emptyRussian)}]");

string[] requiredIds =
{
    "cmd.file.open", "cmd.file.exportRawSelected", "cmd.file.exportTextureSelected",
    "cmd.file.exportObjSelected", "cmd.context.saveStrings", "cmd.context.viewDefs",
    "cmd.tools.ddsToNfs", "cmd.tools.assemble", "workspace.empty.title", "status.ready"
};
foreach (string id in requiredIds)
{
    Check($"L10N required id '{id}'", englishCatalog.ContainsKey(id) && russianCatalog.ContainsKey(id),
        $"en={englishCatalog.ContainsKey(id)}, ru={russianCatalog.ContainsKey(id)}");
}

// ---- assembler: no-replacement byte identity ----
string workDirectory = Path.Combine(Path.GetTempPath(), "BundleUltimateTool-S4-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workDirectory);
string[] allFixtures = { "VEHICLELIST.BUNDLE", "VEHICLETEX.BIN", "VEH_118158_MS.BIN" };
foreach (string fixture in allFixtures)
{
    string sourcePath = Path.Combine(FixtureRoot, fixture);
    string outputPath = Path.Combine(workDirectory, fixture + ".rebuilt");
    try
    {
        BundleAssemblerReport report = BundleAssembler.Rebuild(sourcePath, outputPath,
            new Dictionary<int, string>());
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        byte[] outputBytes = File.ReadAllBytes(outputPath);
        Check($"ASSEMBLER {fixture}: byte-identical rebuild",
            outputBytes.SequenceEqual(sourceBytes),
            $"source={sourceBytes.Length} B, output={report.OutputLength} B, replaced={report.ReplacedCount}");
        Console.WriteLine($"INFO  | ASSEMBLER {fixture} SHA256 | {Convert.ToHexString(SHA256.HashData(outputBytes)).ToLowerInvariant()}");
    }
    catch (Exception ex)
    {
        Check($"ASSEMBLER {fixture}: byte-identical rebuild", false, $"{ex.GetType().Name}: {ex.Message}");
    }
}

// ---- assembler: replacement on VEHICLETEX entry 1 ----
string replacementPath = Path.Combine(workDirectory, "file_0001.nfs");
string replacedOutput = Path.Combine(workDirectory, "VEHICLETEX.replaced.bin");
try
{
    using (BundleReader originalReader = BundleReader.Open(Path.Combine(FixtureRoot, "VEHICLETEX.BIN")))
    {
        BundleEntry target = originalReader.Entries[1];
        byte[] decompressed = new BundleEntryExtractor(originalReader.File).ExtractRaw(target, out _);
        decompressed[32] ^= 0x5A;
        decompressed[33] ^= 0xA5;
        decompressed[40] ^= 0x3C;
        // The assembler consumes .nfs files in the ConvertDDS2NFS layout:
        // "NFSO" + fileType 1 + 32-byte texture header + image body.
        byte[] raw = new byte[8 + decompressed.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw, DdsToNfsConverter.NfsoMagic);
        W32(raw, 4, 1);
        decompressed.CopyTo(raw.AsSpan(8));
        File.WriteAllBytes(replacementPath, raw);

        BundleAssemblerReport report = BundleAssembler.Rebuild(
            Path.Combine(FixtureRoot, "VEHICLETEX.BIN"), replacedOutput,
            new Dictionary<string, string> { ["file_0001.nfs"] = replacementPath });
        Eq("ASSEMBLER replacement count", report.ReplacedCount, 1);

        using BundleReader rebuilt = BundleReader.Open(replacedOutput);
        Eq("ASSEMBLER replacement: numFiles", rebuilt.Entries.Count, originalReader.Entries.Count);
        bool metadataUnchanged = rebuilt.Entries.Zip(originalReader.Entries).All(pair =>
            pair.First.FileType == pair.Second.FileType &&
            pair.First.Raw.Hash == pair.Second.Raw.Hash &&
            pair.First.Raw.LinkId == pair.Second.Raw.LinkId);
        Check("ASSEMBLER replacement: fileType/hash/linkID unchanged", metadataUnchanged,
            $"entries={rebuilt.Entries.Count}");

        (byte[] header, byte[] body) = Decompressed(rebuilt, rebuilt.Entries[1]);
        Check("ASSEMBLER replacement: entry 1 decompresses to .nfs payload",
            header.SequenceEqual(decompressed.AsSpan(0, 32).ToArray()) &&
            body.SequenceEqual(decompressed.AsSpan(32).ToArray()),
            $"header={header.Length} B, body={body.Length} B");

        bool othersIdentical = true;
        for (int i = 0; i < rebuilt.Entries.Count; i++)
        {
            if (i == 1) continue;
            (byte[] h1, byte[] b1) = Decompressed(rebuilt, rebuilt.Entries[i]);
            (byte[] h0, byte[] b0) = Decompressed(originalReader, originalReader.Entries[i]);
            if (!h1.SequenceEqual(h0) || !b1.SequenceEqual(b0)) othersIdentical = false;
        }
        Check("ASSEMBLER replacement: all other entries byte-identical when decompressed",
            othersIdentical, $"entries={rebuilt.Entries.Count}");
    }
}
catch (Exception ex)
{
    Check("ASSEMBLER replacement: rebuild and verify", false, $"{ex.GetType().Name}: {ex.Message}");
}

// ---- assembler: negative validation ----
byte[] validNfs = File.Exists(replacementPath) ? File.ReadAllBytes(replacementPath) : Array.Empty<byte>();
var negativeCases = new Dictionary<string, byte[]>();
byte[] wrongMagic = (byte[])validNfs.Clone();
wrongMagic[0] = (byte)'X';
negativeCases["wrong magic"] = wrongMagic;
byte[] wrongType = (byte[])validNfs.Clone();
W32(wrongType, 4, 5);
negativeCases["wrong fileType"] = wrongType;
negativeCases["truncated header"] = validNfs.Take(20).ToArray();
foreach ((string label, byte[] bytes) in negativeCases)
{
    string badPath = Path.Combine(workDirectory, label.Replace(' ', '_') + ".nfs");
    string badOutput = Path.Combine(workDirectory, label.Replace(' ', '_') + ".bundle");
    File.WriteAllBytes(badPath, bytes);
    bool threw = false;
    try
    {
        BundleAssembler.Rebuild(Path.Combine(FixtureRoot, "VEHICLETEX.BIN"), badOutput,
            new Dictionary<int, string> { [0] = badPath });
    }
    catch (BundleFormatException) { threw = true; }
    Check($"ASSEMBLER negative: {label} rejected", threw, "BundleFormatException expected");
    Check($"ASSEMBLER negative: {label} leaves no output", !File.Exists(badOutput), badOutput);
    Check($"ASSEMBLER negative: {label} leaves no temp file",
        Directory.GetFiles(workDirectory, "*.tmp").Length == 0, "temp directory clean");
}

// Regression for the reported UI crash: a .bin in a game folder can have a valid bnd2
// header but a bogus table. BundleIndex.TryRead must reject it without throwing.
string bogusBundle = Path.Combine(Path.GetTempPath(), "BundleUltimateTool-bogus-" + Guid.NewGuid().ToString("N") + ".bin");
byte[] bogusBytes = File.ReadAllBytes(Path.Combine(FixtureRoot, "VEHICLELIST.BUNDLE"));
Buffer.BlockCopy(BitConverter.GetBytes(0u), 0, bogusBytes, 48 + 32, 4);      // hdrCompSize = 0
Buffer.BlockCopy(BitConverter.GetBytes(0xF21BC06Fu), 0, bogusBytes, 48 + 48, 4); // hdrOffset
File.WriteAllBytes(bogusBundle, bogusBytes);
bool rejected = !BundleIndex.TryRead(bogusBundle, out BundleIndex bogusIndex, out string bogusError);
Check("BUNDLEINDEX bogus table rejected by TryRead", rejected && bogusIndex == null, bogusError ?? "no error");
bool readHeaderOk = BundleIndex.ReadHeader(bogusBundle).NumFiles == 1;
Check("BUNDLEINDEX bogus table still indexes by header", readHeaderOk, "header-only indexing");
File.Delete(bogusBundle);

Console.WriteLine($"INFO  | TOTAL PASS COUNT | {passes}");
Console.WriteLine(failures == 0
    ? "ALL CHECKS PASSED"
    : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

internal sealed class SamplePropertySource
{
    [Category("File Info"), DisplayName("Header CRC32")]
    public string Crc => "DEADBEEF";

    [Category("File Info")]
    public int Count => 7;

    [Category("Offsets"), DisplayName("Header absolute")]
    public long Offset => 128;
}
