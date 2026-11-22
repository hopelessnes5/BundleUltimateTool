using System.Buffers.Binary;
using BundleFormat.Primitives;

namespace BundleFormat.Textures;

/// <summary>DDS constants and header read/write, matching DDS.h exactly.</summary>
public static class DdsCodec
{
    public const uint MagicDds = 0x20534444;      // MAKEFOURCC('D','D','S',' ')
    public const uint FourCcDxt1 = 0x31545844;    // "DXT1"
    public const uint FourCcDxt3 = 0x33545844;    // "DXT3"
    public const uint FourCcDxt5 = 0x35545844;    // "DXT5"
    public const uint NfsTypeRgba32 = 21;

    public const uint DdsCaps = 0x00000001;
    public const uint DdsHeight = 0x00000002;
    public const uint DdsWidth = 0x00000004;
    public const uint DdsPixelFormatFlag = 0x00001000;
    public const uint DdsMipMapCount = 0x00020000;

    public const uint DdsAlphaPixels = 0x00000001;
    public const uint DdsAlpha = 0x00000002;
    public const uint DdsFourCc = 0x00000004;
    public const uint DdsRgb = 0x00000040;
    public const uint DdsRgba = 0x00000041;

    public const uint DdsComplex = 0x00000008;
    public const uint DdsTexture = 0x00001000;
    public const uint DdsMipMap = 0x00400000;

    public readonly record struct DdsPixelFormat(
        uint Size, uint Flags, uint FourCC, uint RgbBitCount,
        uint RBitMask, uint GBitMask, uint BBitMask, uint ABitMask)
    {
        public const int SizeBytes = 32;

        public static DdsPixelFormat Parse(ReadOnlySpan<byte> bytes) => new(
            EndianReader.ReadUInt32(bytes, 0, "ddspf.dwSize"),
            EndianReader.ReadUInt32(bytes, 4, "ddspf.dwFlags"),
            EndianReader.ReadUInt32(bytes, 8, "ddspf.dwFourCC"),
            EndianReader.ReadUInt32(bytes, 12, "ddspf.dwRGBBitCount"),
            EndianReader.ReadUInt32(bytes, 16, "ddspf.dwRBitMask"),
            EndianReader.ReadUInt32(bytes, 20, "ddspf.dwGBitMask"),
            EndianReader.ReadUInt32(bytes, 24, "ddspf.dwBBitMask"),
            EndianReader.ReadUInt32(bytes, 28, "ddspf.dwABitMask"));

        public void WriteTo(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), Size);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), FourCC);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), RgbBitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), RBitMask);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), GBitMask);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), BBitMask);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), ABitMask);
        }
    }

    public readonly record struct DdsHeader(
        uint Size, uint Flags, uint Height, uint Width,
        uint PitchOrLinearSize, uint Depth, uint MipMapCount,
        DdsPixelFormat PixelFormat, uint Caps1, uint Caps2)
    {
        public const int SizeBytes = 124;

        public static DdsHeader Parse(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < SizeBytes)
                throw new BundleFormatException($"DDS header needs {SizeBytes} bytes, got {bytes.Length}.");
            return new DdsHeader(
                EndianReader.ReadUInt32(bytes, 0, "dds.dwSize"),
                EndianReader.ReadUInt32(bytes, 4, "dds.dwFlags"),
                EndianReader.ReadUInt32(bytes, 8, "dds.dwHeight"),
                EndianReader.ReadUInt32(bytes, 12, "dds.dwWidth"),
                EndianReader.ReadUInt32(bytes, 16, "dds.dwPitchOrLinearSize"),
                EndianReader.ReadUInt32(bytes, 20, "dds.dwDepth"),
                EndianReader.ReadUInt32(bytes, 24, "dds.dwMipMapCount"),
                DdsPixelFormat.Parse(bytes.Slice(72, DdsPixelFormat.SizeBytes)),
                EndianReader.ReadUInt32(bytes, 104, "dds.dwCaps1"),
                EndianReader.ReadUInt32(bytes, 108, "dds.dwCaps2"));
        }

        public void WriteTo(Span<byte> destination)
        {
            if (destination.Length < SizeBytes)
                throw new BundleFormatException($"DDS header needs {SizeBytes} bytes, got {destination.Length}.");
            destination.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), Size);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), Height);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), Width);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), PitchOrLinearSize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), Depth);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), MipMapCount);
            PixelFormat.WriteTo(destination.Slice(72, DdsPixelFormat.SizeBytes));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(104, 4), Caps1);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(108, 4), Caps2);
        }
    }

    /// <summary>
    /// Builds the DDS header exactly as CNFSTexture::ConstructHeader does. Note that it
    /// intentionally omits DDS_PIXELFORMAT/DDS_LINEARSIZE in dwFlags, and that the RGBA32
    /// masks (R=0x00ff0000, B=0x000000ff) describe BGRA byte order in the pixel payload.
    /// </summary>
    public static DdsHeader BuildHeader(NfsTextureHeader texture)
    {
        DdsPixelFormat format;
        if (texture.Type == FourCcDxt1 || texture.Type == FourCcDxt5)
        {
            format = new DdsPixelFormat(DdsPixelFormat.SizeBytes, DdsFourCc,
                texture.Type, 0, 0, 0, 0, 0);
        }
        else if (texture.Type == NfsTypeRgba32)
        {
            format = new DdsPixelFormat(DdsPixelFormat.SizeBytes, DdsRgba, 0, 32,
                0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000);
        }
        else
        {
            throw new BundleFormatException($"Unsupported NFS texture type {texture.Type}.");
        }

        return new DdsHeader(
            DdsHeader.SizeBytes,
            DdsCaps | DdsWidth | DdsHeight | DdsMipMapCount,
            texture.Height, texture.Width,
            0, 0, texture.NumMipmaps,
            format, DdsTexture, 0);
    }

    public static byte[] BuildDds(NfsTextureHeader texture, ReadOnlySpan<byte> imageData)
    {
        DdsHeader header = BuildHeader(texture);
        var result = new byte[4 + DdsHeader.SizeBytes + imageData.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), MagicDds);
        header.WriteTo(result.AsSpan(4, DdsHeader.SizeBytes));
        imageData.CopyTo(result.AsSpan(4 + DdsHeader.SizeBytes));
        return result;
    }

    public static DdsHeader ReadDds(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 + DdsHeader.SizeBytes)
            throw new BundleFormatException("DDS file is too short.");
        uint magic = EndianReader.ReadUInt32(bytes, 0, "dds.magic");
        if (magic != MagicDds)
            throw new BundleFormatException($"Bad DDS magic 0x{magic:X8}; expected 0x{MagicDds:X8}.");
        DdsHeader header = DdsHeader.Parse(bytes.Slice(4, DdsHeader.SizeBytes));
        if (header.Size != DdsHeader.SizeBytes)
            throw new BundleFormatException($"Bad DDS header size {header.Size}; expected {DdsHeader.SizeBytes}.");
        return header;
    }
}
