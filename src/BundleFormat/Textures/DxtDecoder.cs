using System;
using System.Buffers.Binary;
using BundleFormat.Primitives;

namespace BundleFormat.Textures;

/// <summary>
/// Faithful port of PrototypeUltimateTool:Modules/Formats/Prototype/DdsDecoder.cs
/// (DecodeDds/DecodeDxt1/DecodeDxt3/DecodeDxt5/DecodeRgb565). NFS textures provide
/// decompressed RGB/RGBA bytes via <see cref="NfsTexture.Decompress"/>; the input is
/// raw compressed blocks rather than a DDS file, but the DXT decode/channel math is
/// identical to the source.
/// </summary>
public static class DxtDecoder
{
    /// <summary>
    /// Decodes compressed DXT blocks to RGB (DXT1) or RGBA (DXT5). Preserves the public
    /// API used by <see cref="NfsTexture.Decompress"/> while using the source's decode math.
    /// </summary>
    public static byte[] Decode(ReadOnlySpan<byte> compressed, int width, int height, uint format)
    {
        if (width <= 0 || height <= 0)
            throw new BundleFormatException($"Invalid DXT image size {width}x{height}.");

        int components = format == DdsCodec.FourCcDxt1 ? 3 : 4;
        byte[] bgra = DecodeToBgra(compressed, width, height, format);

        var result = new byte[width * height * components];
        for (int i = 0, p = 0; i < bgra.Length; i += 4, p += components)
        {
            result[p + 0] = bgra[i + 2];
            result[p + 1] = bgra[i + 1];
            result[p + 2] = bgra[i + 0];
            if (components == 4)
                result[p + 3] = bgra[i + 3];
        }
        return result;
    }

    /// <summary>Decodes compressed DXT blocks to BGRA, matching the source DdsDecoder output layout.</summary>
    public static byte[] DecodeToBgra(ReadOnlySpan<byte> compressed, int width, int height, uint format)
    {
        if (width <= 0 || height <= 0)
            throw new BundleFormatException($"Invalid DXT image size {width}x{height}.");
        if (format != DdsCodec.FourCcDxt1 && format != DdsCodec.FourCcDxt3 && format != DdsCodec.FourCcDxt5)
            throw new BundleFormatException($"Unsupported DXT FourCC 0x{format:X8}.");

        int blockBytes = format == DdsCodec.FourCcDxt1 ? 8 : 16;
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        long needed = (long)blocksX * blocksY * blockBytes;
        if (needed > int.MaxValue || compressed.Length < needed)
            throw new BundleFormatException(
                $"DXT payload is {compressed.Length} byte(s); {needed} required for {width}x{height}.");

        var bgra = new byte[width * height * 4];
        byte[] data = compressed.Length == (int)needed ? compressed.ToArray() : compressed.Slice(0, (int)needed).ToArray();

        if (format == DdsCodec.FourCcDxt1)
            DecodeDxt1(data, 0, width, height, bgra);
        else if (format == DdsCodec.FourCcDxt3)
            DecodeDxt3(data, 0, width, height, bgra);
        else
            DecodeDxt5(data, 0, width, height, bgra);

        return bgra;
    }

    private static void DecodeRgb565(ushort c, out byte r, out byte g, out byte b)
    {
        r = (byte)(((c >> 11) & 0x1F) * 255 / 31);
        g = (byte)(((c >> 5) & 0x3F) * 255 / 63);
        b = (byte)((c & 0x1F) * 255 / 31);
    }

    private static void DecodeDxt1(byte[] data, int offset, int width, int height, byte[] dst)
    {
        int blockCountX = (width + 3) / 4;
        int blockCountY = (height + 3) / 4;
        int blockOffset = offset;
        byte[] r = new byte[4], g = new byte[4], b = new byte[4];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                if (blockOffset + 8 > data.Length) return;
                ushort c0 = BitConverter.ToUInt16(data, blockOffset);
                ushort c1 = BitConverter.ToUInt16(data, blockOffset + 2);
                uint code = BitConverter.ToUInt32(data, blockOffset + 4);

                DecodeRgb565(c0, out r[0], out g[0], out b[0]);
                DecodeRgb565(c1, out r[1], out g[1], out b[1]);

                if (c0 > c1)
                {
                    r[2] = (byte)((2 * r[0] + r[1]) / 3); g[2] = (byte)((2 * g[0] + g[1]) / 3); b[2] = (byte)((2 * b[0] + b[1]) / 3);
                    r[3] = (byte)((r[0] + 2 * r[1]) / 3); g[3] = (byte)((g[0] + 2 * g[1]) / 3); b[3] = (byte)((b[0] + 2 * b[1]) / 3);
                }
                else
                {
                    r[2] = (byte)((r[0] + r[1]) / 2); g[2] = (byte)((g[0] + g[1]) / 2); b[2] = (byte)((b[0] + b[1]) / 2);
                    r[3] = 0; g[3] = 0; b[3] = 0;
                }

                for (int p = 0; p < 16; p++)
                {
                    int px = bx * 4 + (p % 4), py = by * 4 + (p / 4);
                    if (px < width && py < height)
                    {
                        int idx = (int)((code >> (p * 2)) & 0x03);
                        int dstIdx = (py * width + px) * 4;
                        dst[dstIdx + 0] = b[idx];
                        dst[dstIdx + 1] = g[idx];
                        dst[dstIdx + 2] = r[idx];
                        dst[dstIdx + 3] = (c0 <= c1 && idx == 3) ? (byte)0 : (byte)255;
                    }
                }
                blockOffset += 8;
            }
        }
    }

    private static void DecodeDxt3(byte[] data, int offset, int width, int height, byte[] dst)
    {
        int blockCountX = (width + 3) / 4;
        int blockCountY = (height + 3) / 4;
        int blockOffset = offset;
        byte[] r = new byte[4], g = new byte[4], b = new byte[4];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                if (blockOffset + 16 > data.Length) return;
                ushort c0 = BitConverter.ToUInt16(data, blockOffset + 8);
                ushort c1 = BitConverter.ToUInt16(data, blockOffset + 10);
                uint code = BitConverter.ToUInt32(data, blockOffset + 12);

                DecodeRgb565(c0, out r[0], out g[0], out b[0]);
                DecodeRgb565(c1, out r[1], out g[1], out b[1]);
                r[2] = (byte)((2 * r[0] + r[1]) / 3); g[2] = (byte)((2 * g[0] + g[1]) / 3); b[2] = (byte)((2 * b[0] + b[1]) / 3);
                r[3] = (byte)((r[0] + 2 * r[1]) / 3); g[3] = (byte)((g[0] + 2 * g[1]) / 3); b[3] = (byte)((b[0] + 2 * b[1]) / 3);

                for (int p = 0; p < 16; p++)
                {
                    int px = bx * 4 + (p % 4), py = by * 4 + (p / 4);
                    if (px < width && py < height)
                    {
                        int alphaByte = data[blockOffset + (p / 2)];
                        byte alpha = (byte)(((p % 2 == 0) ? (alphaByte & 0x0F) : ((alphaByte >> 4) & 0x0F)) * 255 / 15);
                        int idx = (int)((code >> (p * 2)) & 0x03);
                        int dstIdx = (py * width + px) * 4;
                        dst[dstIdx + 0] = b[idx];
                        dst[dstIdx + 1] = g[idx];
                        dst[dstIdx + 2] = r[idx];
                        dst[dstIdx + 3] = alpha;
                    }
                }
                blockOffset += 16;
            }
        }
    }

    private static void DecodeDxt5(byte[] data, int offset, int width, int height, byte[] dst)
    {
        int blockCountX = (width + 3) / 4;
        int blockCountY = (height + 3) / 4;
        int blockOffset = offset;
        byte[] alphas = new byte[8];
        byte[] r = new byte[4], g = new byte[4], b = new byte[4];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                if (blockOffset + 16 > data.Length) return;

                byte a0 = data[blockOffset + 0], a1 = data[blockOffset + 1];
                alphas[0] = a0; alphas[1] = a1;

                if (a0 > a1)
                {
                    alphas[2] = (byte)((6 * a0 + 1 * a1) / 7); alphas[3] = (byte)((5 * a0 + 2 * a1) / 7);
                    alphas[4] = (byte)((4 * a0 + 3 * a1) / 7); alphas[5] = (byte)((3 * a0 + 4 * a1) / 7);
                    alphas[6] = (byte)((2 * a0 + 5 * a1) / 7); alphas[7] = (byte)((1 * a0 + 6 * a1) / 7);
                }
                else
                {
                    alphas[2] = (byte)((4 * a0 + 1 * a1) / 5); alphas[3] = (byte)((3 * a0 + 2 * a1) / 5);
                    alphas[4] = (byte)((2 * a0 + 3 * a1) / 5); alphas[5] = (byte)((1 * a0 + 4 * a1) / 5);
                    alphas[6] = 0; alphas[7] = 255;
                }

                ulong alphaBits = (ulong)data[blockOffset + 2] |
                                 ((ulong)data[blockOffset + 3] << 8) |
                                 ((ulong)data[blockOffset + 4] << 16) |
                                 ((ulong)data[blockOffset + 5] << 24) |
                                 ((ulong)data[blockOffset + 6] << 32) |
                                 ((ulong)data[blockOffset + 7] << 40);

                ushort c0 = BitConverter.ToUInt16(data, blockOffset + 8);
                ushort c1 = BitConverter.ToUInt16(data, blockOffset + 10);
                uint code = BitConverter.ToUInt32(data, blockOffset + 12);

                DecodeRgb565(c0, out r[0], out g[0], out b[0]);
                DecodeRgb565(c1, out r[1], out g[1], out b[1]);
                r[2] = (byte)((2 * r[0] + r[1]) / 3); g[2] = (byte)((2 * g[0] + g[1]) / 3); b[2] = (byte)((2 * b[0] + b[1]) / 3);
                r[3] = (byte)((r[0] + 2 * r[1]) / 3); g[3] = (byte)((g[0] + 2 * g[1]) / 3); b[3] = (byte)((b[0] + 2 * b[1]) / 3);

                for (int p = 0; p < 16; p++)
                {
                    int px = bx * 4 + (p % 4), py = by * 4 + (p / 4);
                    if (px < width && py < height)
                    {
                        int alphaIdx = (int)((alphaBits >> (p * 3)) & 0x07);
                        int colorIdx = (int)((code >> (p * 2)) & 0x03);
                        int dstIdx = (py * width + px) * 4;

                        dst[dstIdx + 0] = b[colorIdx];
                        dst[dstIdx + 1] = g[colorIdx];
                        dst[dstIdx + 2] = r[colorIdx];
                        dst[dstIdx + 3] = alphas[alphaIdx];
                    }
                }
                blockOffset += 16;
            }
        }
    }
}
