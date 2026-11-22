using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BundleUltimateTool.UI;

/// <summary>Simple 16x16 icons generated in code — no emoji and no external assets.</summary>
internal static class TreeIcons
{
    internal const int Texture = 0;
    internal const int Model = 1;
    internal const int File = 2;
    internal const int Folder = 3;

    internal static ImageList Create()
    {
        var list = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };
        list.Images.Add("texture", TextureIcon());
        list.Images.Add("model", ModelIcon());
        list.Images.Add("file", FileIcon());
        list.Images.Add("folder", FolderIcon());
        return list;
    }

    internal static int ImageIndexForArchive(uint fileType) => fileType switch
    {
        1 => Texture,
        5 => Model,
        _ => File
    };

    private static Bitmap NewBitmap()
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        return bitmap;
    }

    private static Bitmap TextureIcon()
    {
        var bitmap = NewBitmap();
        using var g = Graphics.FromImage(bitmap);
        using var sky = new SolidBrush(Color.FromArgb(70, 150, 220));
        using var hill = new SolidBrush(Color.FromArgb(70, 160, 80));
        using var sun = new SolidBrush(Color.FromArgb(250, 220, 90));
        using var border = new Pen(Color.FromArgb(45, 55, 70));
        g.FillRectangle(sky, 1, 2, 14, 12);
        g.FillEllipse(sun, 10, 3, 4, 4);
        g.FillPolygon(hill, new[] { new Point(1, 13), new Point(6, 7), new Point(11, 13) });
        g.DrawRectangle(border, 1, 2, 14, 12);
        return bitmap;
    }

    private static Bitmap ModelIcon()
    {
        var bitmap = NewBitmap();
        using var g = Graphics.FromImage(bitmap);
        using var body = new SolidBrush(Color.FromArgb(210, 60, 60));
        using var glass = new SolidBrush(Color.FromArgb(150, 200, 235));
        using var tire = new SolidBrush(Color.FromArgb(40, 40, 45));
        using var border = new Pen(Color.FromArgb(90, 35, 35));
        g.FillRectangle(body, 1, 7, 14, 5);
        g.FillPolygon(body, new[] { new Point(4, 7), new Point(6, 4), new Point(11, 4), new Point(13, 7) });
        g.FillPolygon(glass, new[] { new Point(5, 7), new Point(7, 5), new Point(10, 5), new Point(11, 7) });
        g.FillEllipse(tire, 3, 11, 4, 4);
        g.FillEllipse(tire, 10, 11, 4, 4);
        g.DrawRectangle(border, 1, 7, 14, 5);
        return bitmap;
    }

    private static Bitmap FileIcon()
    {
        var bitmap = NewBitmap();
        using var g = Graphics.FromImage(bitmap);
        using var page = new SolidBrush(Color.FromArgb(238, 238, 240));
        using var fold = new SolidBrush(Color.FromArgb(160, 165, 175));
        using var border = new Pen(Color.FromArgb(90, 95, 105));
        using var line = new Pen(Color.FromArgb(140, 145, 155));
        g.FillPolygon(page, new[]
        {
            new Point(3, 1), new Point(11, 1), new Point(14, 4), new Point(14, 15),
            new Point(3, 15)
        });
        g.FillPolygon(fold, new[] { new Point(11, 1), new Point(14, 4), new Point(11, 4) });
        g.DrawLines(border, new[]
        {
            new Point(3, 1), new Point(11, 1), new Point(14, 4), new Point(14, 15),
            new Point(3, 15), new Point(3, 1)
        });
        g.DrawLine(line, 5, 7, 12, 7);
        g.DrawLine(line, 5, 10, 12, 10);
        g.DrawLine(line, 5, 13, 10, 13);
        return bitmap;
    }

    private static Bitmap FolderIcon()
    {
        var bitmap = NewBitmap();
        using var g = Graphics.FromImage(bitmap);
        using var back = new SolidBrush(Color.FromArgb(190, 150, 50));
        using var front = new SolidBrush(Color.FromArgb(245, 205, 90));
        using var border = new Pen(Color.FromArgb(120, 95, 30));
        g.FillPolygon(back, new[]
        {
            new Point(1, 4), new Point(6, 4), new Point(8, 6), new Point(15, 6),
            new Point(15, 13), new Point(1, 13)
        });
        g.FillPolygon(front, new[]
        {
            new Point(1, 7), new Point(15, 7), new Point(14, 14), new Point(2, 14)
        });
        g.DrawLines(border, new[]
        {
            new Point(1, 4), new Point(6, 4), new Point(8, 6), new Point(15, 6),
            new Point(15, 13), new Point(2, 14), new Point(1, 7)
        });
        return bitmap;
    }
}
