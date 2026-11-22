using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BundleUltimateTool.UI;

/// <summary>
/// Faithful port of PrototypeUltimateTool:Modules/Formats/Prototype/DdsDecoder.cs
/// ProcessChannels (source lines 127-186). Channel math is identical to the source.
/// </summary>
internal static class TextureChannelProcessor
{
    public static Bitmap ProcessChannels(Bitmap source, bool showR, bool showG, bool showB, bool showA)
    {
        if (source == null) return null;

        int width = source.Width;
        int height = source.Height;

        Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        BitmapData srcData = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData dstData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(srcData.Stride) * height;
        byte[] srcBuffer = new byte[bytes];
        byte[] dstBuffer = new byte[bytes];

        Marshal.Copy(srcData.Scan0, srcBuffer, 0, bytes);
        source.UnlockBits(srcData);

        int activeColors = (showR ? 1 : 0) + (showG ? 1 : 0) + (showB ? 1 : 0);

        if (!showR && !showG && !showB && showA) // Только Alpha -> Градации серого
        {
            for (int i = 0; i < bytes; i += 4)
            {
                byte a = srcBuffer[i + 3];
                dstBuffer[i + 0] = a;
                dstBuffer[i + 1] = a;
                dstBuffer[i + 2] = a;
                dstBuffer[i + 3] = 255;
            }
        }
        else if (activeColors == 1 && !showA) // Ровно один цвет -> Градации серого
        {
            int offset = showB ? 0 : (showG ? 1 : 2);
            for (int i = 0; i < bytes; i += 4)
            {
                byte val = srcBuffer[i + offset];
                dstBuffer[i + 0] = val;
                dstBuffer[i + 1] = val;
                dstBuffer[i + 2] = val;
                dstBuffer[i + 3] = 255;
            }
        }
        else // Мультивыбор
        {
            for (int i = 0; i < bytes; i += 4)
            {
                dstBuffer[i + 0] = showB ? srcBuffer[i + 0] : (byte)0;
                dstBuffer[i + 1] = showG ? srcBuffer[i + 1] : (byte)0;
                dstBuffer[i + 2] = showR ? srcBuffer[i + 2] : (byte)0;
                dstBuffer[i + 3] = showA ? srcBuffer[i + 3] : (byte)255;
            }
        }

        Marshal.Copy(dstBuffer, 0, dstData.Scan0, bytes);
        result.UnlockBits(dstData);

        return result;
    }
}
