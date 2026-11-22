using System.ComponentModel;
using BundleFormat.Bundle;
using BundleFormat.Models;
using BundleFormat.Tables;
using BundleFormat.Textures;

namespace BundleUltimateTool.Documents;

/// <summary>Read-only PropertyGrid view of one bundle table entry.</summary>
public sealed class BundleEntryProperties
{
    private readonly BundleEntry _entry;
    private readonly string _filePath;
    private readonly NfsTexture _texture;
    private readonly NfsModel _model;
    private readonly BundleFileInfo _fileInfo;

    public BundleEntryProperties(BundleEntry entry, string filePath, NfsTexture texture = null,
        NfsModel model = null, BundleFileInfo fileInfo = null)
    {
        _entry = entry;
        _filePath = filePath;
        _texture = texture;
        _model = model;
        _fileInfo = fileInfo;
    }

    [DisplayName("Type")]
    public string Type => $"{_entry.FileType} ({_entry.TypeLabel})";

    [DisplayName("Type ID")]
    public uint TypeId => _entry.FileType;

    [DisplayName("Hash")]
    public string Hash => $"0x{_entry.Raw.Hash:X8}";

    [DisplayName("Link ID")]
    public uint LinkId => _entry.Raw.LinkId;

    [DisplayName("Num links")]
    public uint NumLinks => _entry.Raw.NumLinks;

    [Category("Sizes"), DisplayName("Header compressed")]
    public uint HeaderCompressedSize => _entry.Raw.HdrCompSize;

    [Category("Sizes"), DisplayName("Body compressed")]
    public uint BodyCompressedSize => _entry.Raw.DataCompSize;

    [Category("Sizes"), DisplayName("Header uncompressed raw")]
    public uint HeaderUncompressedRaw => _entry.Raw.HdrUncompSizeRaw;

    [Category("Sizes"), DisplayName("Header uncompressed masked")]
    public uint HeaderUncompressedMasked => _entry.MaskedHdrUncompSize;

    [Category("Sizes"), DisplayName("Body uncompressed raw")]
    public uint BodyUncompressedRaw => _entry.Raw.DataUncompSizeRaw;

    [Category("Sizes"), DisplayName("Body uncompressed masked")]
    public uint BodyUncompressedMasked => _entry.MaskedDataUncompSize;

    [Category("Offsets"), DisplayName("Header absolute")]
    public long AbsoluteHeaderOffset => _entry.AbsoluteHeaderOffset;

    [Category("Offsets"), DisplayName("Body absolute")]
    public long AbsoluteBodyOffset => _entry.AbsoluteBodyOffset;

    [DisplayName("File")]
    public string FilePath => _filePath;

    [Category("Texture"), DisplayName("Texture type")]
    public string TextureType => _texture == null ? "—" : _texture.Header.TypeLabel;

    [Category("Texture"), DisplayName("Width")]
    public string TextureWidth => _texture == null ? "—" : _texture.GetWidth().ToString();

    [Category("Texture"), DisplayName("Height")]
    public string TextureHeight => _texture == null ? "—" : _texture.GetHeight().ToString();

    [Category("Texture"), DisplayName("Mip count")]
    public string TextureMipCount => _texture == null ? "—" : _texture.GetNumMipmaps().ToString();

    [Category("Texture"), DisplayName("Components")]
    public string TextureComponents => _texture == null ? "—" : _texture.GetNumComponents().ToString();

    [Category("Model"), DisplayName("Mesh count")]
    public string ModelMeshes => _model == null ? "—" : _model.Meshes.Count.ToString();

    [Category("Model"), DisplayName("Total vertices")]
    public string ModelVertices => _model == null ? "—" : _model.TotalVertices.ToString();

    [Category("Model"), DisplayName("Total faces")]
    public string ModelFaces => _model == null ? "—" : _model.TotalFaces.ToString();

    [Category("Model"), DisplayName("Vertex size (first mesh)")]
    public string ModelVertexSize => _model == null || _model.Meshes.Count == 0
        ? "—" : _model.Meshes[0].VertexSize.ToString();

    [Category("File Info"), DisplayName("File type")]
    public string InfoFileType => _fileInfo == null ? "—" : NfsFileTypeInfo.Describe(_fileInfo.FileType);

    [Category("File Info"), DisplayName("Header packed")]
    public string InfoHeaderPacked => _fileInfo == null ? "—" : _fileInfo.CompressedHeaderSize.ToString();

    [Category("File Info"), DisplayName("Header uncompressed")]
    public string InfoHeaderUncompressed => _fileInfo == null ? "—" : _fileInfo.MaskedHeaderUncompressedSize.ToString();

    [Category("File Info"), DisplayName("Body packed")]
    public string InfoBodyPacked => _fileInfo == null ? "—" : _fileInfo.CompressedBodySize.ToString();

    [Category("File Info"), DisplayName("Body uncompressed")]
    public string InfoBodyUncompressed => _fileInfo == null ? "—" : _fileInfo.MaskedBodyUncompressedSize.ToString();

    [Category("File Info"), DisplayName("Header absolute")]
    public string InfoHeaderOffset => _fileInfo == null ? "—" : _fileInfo.AbsoluteHeaderOffset.ToString();

    [Category("File Info"), DisplayName("Body absolute")]
    public string InfoBodyOffset => _fileInfo == null ? "—" : _fileInfo.AbsoluteBodyOffset.ToString();

    [Category("File Info"), DisplayName("Header CRC32")]
    public string InfoHeaderCrc => _fileInfo == null ? "—" : _fileInfo.HeaderCrcHex;

    [Category("File Info"), DisplayName("Data CRC32")]
    public string InfoDataCrc => _fileInfo == null ? "—" : _fileInfo.DataCrcHex;

    [Category("File Info"), DisplayName("Full CRC32")]
    public string InfoFullCrc => _fileInfo == null ? "—" : _fileInfo.FullCrcHex;
}
