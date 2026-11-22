using BundleFormat.Primitives;

namespace BundleFormat.Textures;

/// <summary>NFS texture payload: decompressed 32-byte header plus image data.</summary>
public sealed class NfsTexture
{
    public const int MaxDecodedPixels = 16_000_000;

    private readonly byte[] _imageData;

    private NfsTexture(NfsTextureHeader header, byte[] imageData)
    {
        Header = header;
        _imageData = imageData;
    }

    public NfsTextureHeader Header { get; }
    public int GetWidth() => Header.Width;
    public int GetHeight() => Header.Height;
    public byte GetNumMipmaps() => Header.NumMipmaps;

    public static bool IsNfsTextureSignature(ReadOnlySpan<byte> headerData)
    {
        if (headerData.Length < NfsTextureHeader.Size) return false;
        uint type = EndianReader.ReadUInt32(headerData, 12, "texture.type");
        return type == DdsCodec.FourCcDxt1 || type == DdsCodec.FourCcDxt5 ||
               type == DdsCodec.NfsTypeRgba32;
    }

    public static NfsTexture InitFromData(ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> imageBytes)
    {
        NfsTextureHeader header = NfsTextureHeader.Parse(headerBytes);
        if (!IsNfsTextureSignature(headerBytes))
            throw new BundleFormatException($"Unsupported NFS texture type {header.Type}.");
        if (header.Width == 0 || header.Height == 0)
            throw new BundleFormatException($"Invalid NFS texture size {header.Width}x{header.Height}.");

        long pixels = (long)header.Width * header.Height;
        if (pixels > MaxDecodedPixels)
            throw new BundleFormatException(
                $"Texture {header.Width}x{header.Height} exceeds the {MaxDecodedPixels:N0}-pixel preview/alloc cap.");

        int required = GetImageSizeForType(header.Type, header.Width, header.Height,
            Math.Max(1, (int)header.NumMipmaps));
        if (imageBytes.Length < required)
            throw new BundleFormatException(
                $"Texture image data is {imageBytes.Length} byte(s); at least {required} required for mip chain.");

        return new NfsTexture(header, imageBytes.ToArray());
    }

    /// <summary>Faithful port of GetImageSizeForType, including the DXT5=16 / DXT1=8 block rule.</summary>
    public static int GetImageSizeForType(uint type, int width, int height, int mipmaps)
    {
        long size = 0;
        int w = width;
        int h = height;
        int remaining = mipmaps;
        while (remaining > 0)
        {
            if (type == DdsCodec.FourCcDxt5)
                size += (long)(((w + 3) >> 2) * ((h + 3) >> 2)) << 4;
            else if (type == DdsCodec.FourCcDxt1)
                size += (long)(((w + 3) >> 2) * ((h + 3) >> 2)) << 3;
            else
                size += (long)w * h * 4;

            if (w == 1 && h == 1) break;
            if (w > 1) w >>= 1;
            if (h > 1) h >>= 1;
            remaining--;
        }
        if (size > int.MaxValue)
            throw new BundleFormatException($"Texture mip chain is {size} byte(s), too large for a single array.");
        return (int)size;
    }

    public int GetNumComponents() => Header.Type == DdsCodec.FourCcDxt1 ? 3 : 4;

    public int GetDecompressedSize() => GetWidth() * GetHeight() * GetNumComponents();

    public byte[] Decompress()
    {
        if (Header.Type == DdsCodec.FourCcDxt1 || Header.Type == DdsCodec.FourCcDxt5)
            return DxtDecoder.Decode(_imageData, GetWidth(), GetHeight(), Header.Type);

        if (Header.Type == DdsCodec.NfsTypeRgba32)
        {
            // The NFS RGBA32 payload is BGRA in memory (DDS masks R=0x00ff0000,
            // B=0x000000ff). DDS_LoadUncompressed2D swaps s[2],s[1],s[0],s[3];
            // we do the same to produce RGBA. Note: CNFSTexture::Decompress in the
            // original tool uses a raw memcpy here and leaves the channels swapped.
            int count = GetWidth() * GetHeight();
            int needed = count * 4;
            if (_imageData.Length < needed)
                throw new BundleFormatException($"RGBA texture needs {needed} byte(s), got {_imageData.Length}.");
            var result = new byte[needed];
            for (int i = 0; i < count; i++)
            {
                result[i * 4] = _imageData[i * 4 + 2];
                result[i * 4 + 1] = _imageData[i * 4 + 1];
                result[i * 4 + 2] = _imageData[i * 4];
                result[i * 4 + 3] = _imageData[i * 4 + 3];
            }
            return result;
        }

        throw new BundleFormatException($"Unsupported NFS texture type {Header.Type}.");
    }

    public byte[] SaveAsDds() => DdsCodec.BuildDds(Header, _imageData);

    public void SaveAsDdsFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        File.WriteAllBytes(path, SaveAsDds());
    }
}
