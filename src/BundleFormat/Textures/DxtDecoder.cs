using System.Buffers.Binary;
using BundleFormat.Primitives;

namespace BundleFormat.Textures;

/// <summary>
/// Managed port of DDS.cpp's software decoder (DDS_DecodeColorBlock,
/// DDS_DecodeDXT5AlphaBlock, DDS_DecodeCompressedImage). The 5/6/5 expansion uses the
/// original <<3 / <<2 / <<3 (max 248/252/248) - no rounding up to 255.
/// </summary>
public static class DxtDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> compressed, int width, int height, uint format)
    {
        if (width <= 0 || height <= 0)
            throw new BundleFormatException($"Invalid DXT image size {width}x{height}.");
        if (format != DdsCodec.FourCcDxt1 && format != DdsCodec.FourCcDxt5)
            throw new BundleFormatException($"Unsupported DXT FourCC 0x{format:X8}.");

        int components = format == DdsCodec.FourCcDxt1 ? 3 : 4;
        int blockBytes = format == DdsCodec.FourCcDxt5 ? 16 : 8;
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        long needed = (long)blocksX * blocksY * blockBytes;
        if (needed > int.MaxValue || compressed.Length < needed)
            throw new BundleFormatException(
                $"DXT payload is {compressed.Length} byte(s); {needed} required for {width}x{height}.");

        long pixels = (long)width * height;
        if (pixels * components > int.MaxValue)
            throw new BundleFormatException("Decoded DXT image is too large.");

        var destination = new byte[(int)(pixels * components)];
        int offset = 0;
        for (int blockY = 0; blockY < height; blockY += 4)
        {
            for (int blockX = 0; blockX < width; blockX += 4)
            {
                int sx = Math.Min(4, width - blockX);
                int sy = Math.Min(4, height - blockY);
                int dstBase = (blockY * width + blockX) * components;

                if (format == DdsCodec.FourCcDxt5)
                {
                    DecodeDxt5Alpha(compressed.Slice(offset, 8), destination, dstBase + 3,
                        sx, sy, components, width * components);
                    offset += 8;
                }
                // DXT3 alpha is intentionally not supported: the NFS format never uses it.
                DecodeColorBlock(compressed.Slice(offset, 8), destination, dstBase,
                    sx, sy, components, width * components, format);
                offset += 8;
            }
        }
        return destination;
    }

    private static void DecodeColorBlock(ReadOnlySpan<byte> src, byte[] dest, int dstBase,
        int w, int h, int components, int yOff, uint format)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(0, 2));
        ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(2, 2));

        var colors = new byte[4][];
        colors[0] = new[] { (byte)(((c0 >> 11) & 0x1F) << 3), (byte)(((c0 >> 5) & 0x3F) << 2), (byte)((c0 & 0x1F) << 3) };
        colors[1] = new[] { (byte)(((c1 >> 11) & 0x1F) << 3), (byte)(((c1 >> 5) & 0x3F) << 2), (byte)((c1 & 0x1F) << 3) };
        colors[2] = new byte[3];
        colors[3] = new byte[3];

        if (c0 > c1 || format == DdsCodec.FourCcDxt5)
        {
            for (int i = 0; i < 3; i++)
            {
                colors[2][i] = (byte)((2 * colors[0][i] + colors[1][i] + 1) / 3);
                colors[3][i] = (byte)((colors[0][i] + 2 * colors[1][i] + 1) / 3);
            }
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                colors[2][i] = (byte)((colors[0][i] + colors[1][i] + 1) >> 1);
                colors[3][i] = 0;
            }
        }

        for (int y = 0; y < h; y++)
        {
            int row = dstBase + yOff * y;
            uint indexes = src[4 + y];
            for (int x = 0; x < w; x++)
            {
                byte[] color = colors[indexes & 0x3];
                int at = row + x * components;
                dest[at] = color[0];
                dest[at + 1] = color[1];
                dest[at + 2] = color[2];
                indexes >>= 2;
            }
        }
    }

    private static void DecodeDxt5Alpha(ReadOnlySpan<byte> src, byte[] dest, int dstBase,
        int w, int h, int components, int yOff)
    {
        byte a0 = src[0];
        byte a1 = src[1];
        ulong alpha = BinaryPrimitives.ReadUInt64LittleEndian(src) >> 16;

        for (int y = 0; y < h; y++)
        {
            int row = dstBase + yOff * y;
            for (int x = 0; x < w; x++)
            {
                int k = (int)(alpha & 0x7);
                byte value;
                if (k == 0) value = a0;
                else if (k == 1) value = a1;
                else if (a0 > a1) value = (byte)(((8 - k) * a0 + (k - 1) * a1) / 7);
                else if (k >= 6) value = (byte)(k == 6 ? 0 : 255);
                else value = (byte)(((6 - k) * a0 + (k - 1) * a1) / 5);

                dest[row + x * components] = value;
                alpha >>= 3;
            }
            if (w < 4) alpha >>= 3 * (4 - w);
        }
    }
}
