using System.Buffers.Binary;
using BundleFormat.Primitives;

namespace BundleFormat.Textures;

/// <summary>Port of CNFSTexture::ConvertDDS2NFS: writes NFSO magic, file type 1, a
/// synthesized S_NFSTextureHeader and the raw DDS image payload.</summary>
public static class DdsToNfsConverter
{
    public const uint NfsoMagic = 0x4F53464E; // MAKEFOURCC('N','F','S','O')
    public const uint NfsFileTypeTexture = 1;

    public static byte[] ConvertBytes(ReadOnlySpan<byte> ddsBytes)
    {
        using var input = new MemoryStream(ddsBytes.ToArray(), writable: false);
        using var output = new MemoryStream();
        Convert(input, output);
        return output.ToArray();
    }

    public static void ConvertFile(string inputPath, string outputPath)
    {
        using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        Convert(input, output);
    }

    public static void Convert(Stream dds, Stream output)
    {
        if (dds == null) throw new ArgumentNullException(nameof(dds));
        if (output == null) throw new ArgumentNullException(nameof(output));

        var prefix = new byte[4 + DdsCodec.DdsHeader.SizeBytes];
        ReadExactly(dds, prefix, "DDS header");
        DdsCodec.DdsHeader ddsHeader = DdsCodec.ReadDds(prefix);

        uint type;
        if ((ddsHeader.PixelFormat.Flags & DdsCodec.DdsFourCc) != 0)
        {
            uint fourCc = ddsHeader.PixelFormat.FourCC;
            if (fourCc != DdsCodec.FourCcDxt1 && fourCc != DdsCodec.FourCcDxt5)
                throw new BundleFormatException(
                    $"Unsupported DDS FourCC 0x{fourCc:X8}; only DXT1 and DXT5 can be converted.");
            type = fourCc;
        }
        else if ((ddsHeader.PixelFormat.Flags & (DdsCodec.DdsRgba | DdsCodec.DdsRgb)) != 0 &&
                 ddsHeader.PixelFormat.RgbBitCount == 32)
        {
            type = DdsCodec.NfsTypeRgba32;
        }
        else
        {
            throw new BundleFormatException("Unsupported DDS pixel format; expected DXT1, DXT5 or 32-bit RGBA.");
        }

        if (ddsHeader.Width == 0 || ddsHeader.Height == 0 ||
            ddsHeader.Width > ushort.MaxValue || ddsHeader.Height > ushort.MaxValue)
            throw new BundleFormatException($"Invalid DDS size {ddsHeader.Width}x{ddsHeader.Height}.");

        int width = (int)ddsHeader.Width;
        int height = (int)ddsHeader.Height;
        int mipmaps = Math.Max(1, (int)ddsHeader.MipMapCount);
        if (mipmaps > byte.MaxValue) mipmaps = byte.MaxValue;

        int imageSize = NfsTexture.GetImageSizeForType(type, width, height, mipmaps);
        var image = new byte[imageSize];
        ReadExactly(dds, image, "DDS image data");

        var nfsHeader = new NfsTextureHeader(
            Stuff0: 0, Stuff1: 0, Stuff2: 1, Type: type,
            Width: (ushort)width, Height: (ushort)height, Stuff3: 1,
            Unknown: 1, NumMipmaps: (byte)mipmaps, Stuff4: 0x3000, Stuff5: 0);
        var headerBytes = new byte[NfsTextureHeader.Size];
        nfsHeader.WriteTo(headerBytes);

        var magicBytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(magicBytes.AsSpan(0, 4), NfsoMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(magicBytes.AsSpan(4, 4), NfsFileTypeTexture);
        output.Write(magicBytes);
        output.Write(headerBytes);
        output.Write(image);
    }

    private static void ReadExactly(Stream stream, byte[] destination, string region)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = stream.Read(destination, total, destination.Length - total);
            if (read == 0)
                throw new BundleFormatException(
                    $"{region}: expected {destination.Length} byte(s), got {total}.");
            total += read;
        }
    }
}
