using System;

namespace BundleUltimateTool.UI;

/// <summary>
/// Byte-level equivalent of PrototypeUltimateTool:DdsDecoder.ProcessChannels for the
/// raw RGBA/RGB buffers produced by NfsTexture.Decompress. Kept free of System.Drawing
/// so BundleChecks can verify the channel semantics without a WinForms reference.
/// </summary>
internal static class TexturePreviewTransform
{
    internal static byte[] Transform(byte[] pixels, int width, int height, int components,
        bool showR, bool showG, bool showB, bool showA, bool rgbaMode, bool vFlip)
    {
        if (pixels == null || width <= 0 || height <= 0) return Array.Empty<byte>();
        int sourceComponents = components >= 4 ? 4 : 3;
        if (pixels.Length < width * height * sourceComponents)
            throw new ArgumentException("Texture buffer is too small.", nameof(pixels));

        var result = new byte[width * height * 4];
        int activeColors = (showR ? 1 : 0) + (showG ? 1 : 0) + (showB ? 1 : 0);
        bool showAlpha = showA && rgbaMode;

        for (int y = 0; y < height; y++)
        {
            int sourceY = vFlip ? height - 1 - y : y;
            for (int x = 0; x < width; x++)
            {
                int src = (sourceY * width + x) * sourceComponents;
                int dst = (y * width + x) * 4;
                byte r = pixels[src + 0];
                byte g = pixels[src + 1];
                byte b = pixels[src + 2];
                byte a = sourceComponents == 4 ? pixels[src + 3] : (byte)255;

                if (!showR && !showG && !showB && showAlpha)
                {
                    result[dst] = result[dst + 1] = result[dst + 2] = a;
                    result[dst + 3] = 255;
                }
                else if (activeColors == 1 && !showAlpha)
                {
                    byte value = showR ? r : (showG ? g : b);
                    result[dst] = result[dst + 1] = result[dst + 2] = value;
                    result[dst + 3] = 255;
                }
                else
                {
                    result[dst] = showR ? r : (byte)0;
                    result[dst + 1] = showG ? g : (byte)0;
                    result[dst + 2] = showB ? b : (byte)0;
                    result[dst + 3] = showAlpha ? a : (byte)255;
                }
            }
        }
        return result;
    }
}
