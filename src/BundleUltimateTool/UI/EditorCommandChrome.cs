using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace BundleUltimateTool.UI
{
    internal static class EditorCommandChrome
    {
        // This transparent image only reserves the native ToolStrip image slot.
        // The renderer draws the scalable system-font outline at the actual image rectangle.
        private static readonly Image ImageSlot = new Bitmap(16, 16);
        internal static readonly string FontFamilyName = FindFont();
        private static readonly Dictionary<string, string> Glyphs = new(StringComparer.Ordinal)
        {
            ["menu/file/open"] = "\uE8E5",
            ["cmd.file.open"] = "\uE8E5",
            ["cmd.file.exportRawSelected"] = "\uE896", ["context.exportRawSelected"] = "\uE896",
            ["cmd.file.exportRawAll"] = "\uE8B7", ["context.exportRawAll"] = "\uE8B7",
            ["cmd.file.exportTextureSelected"] = "\uE8B9", ["context.exportTextureSelected"] = "\uE8B9",
            ["cmd.file.exportTextureAll"] = "\uE8B7", ["context.exportTextureAll"] = "\uE8B7",
            ["cmd.tools.ddsToNfs"] = "\uE7B8",
            ["cmd.file.exportObjSelected"] = "\uE7B8", ["context.exportObjSelected"] = "\uE7B8",
            ["cmd.file.exportObjAll"] = "\uE8B7", ["context.exportObjAll"] = "\uE8B7",
            ["cmd.context.saveStrings"] = "\uE74E", ["cmd.context.viewDefs"] = "\uE7B8",
            ["cmd.file.selectGameFolder"] = "\uE8B7", ["cmd.folder.up"] = "\uE74A",
            ["cmd.view.archive"] = "\uE7B8", ["cmd.view.files"] = "\uE8B7",
            ["cmd.file.openFromFolder"] = "\uE8E5", ["cmd.file.copyPath"] = "\uE8C8",
            ["menu/file/exit"] = "\uE7E8",
            ["cmd.view.theme"] = "\uE708",
            ["cmd.tools.extract"] = "\uE896",
            ["cmd.tools.assemble"] = "\uE7B8",
            ["cmd.help.about"] = "\uE897",
            ["label.search"] = "\uE721",
            ["view.mode2d"] = "\uE8B9", ["view.mode3d"] = "\uE7B8"
        };
        private static readonly string[] LegacyPrefixes = { "📂", "💾", "📤", "📦", "📁", "📥", "🖼️", "🧊", "🔍" };

        private static string FindFont()
        {
            using var fonts = new InstalledFontCollection();
            foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                if (fonts.Families.Any(family => family.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name;
            return null; // Text remains usable if neither system icon font is installed.
        }

        internal static bool TryGetGlyph(string id, out string glyph)
        {
            glyph = null;
            return FontFamilyName != null && id != null && Glyphs.TryGetValue(id, out glyph);
        }

        internal static void Apply(ToolStrip strip, bool spacious)
        {
            if (strip == null || strip.IsDisposed) return;
            int Scale(int value) => (int)Math.Round(value * strip.DeviceDpi / 96d);
            // Keep the already dense search/filter row at its existing size.
            if (spacious) strip.Padding = new Padding(Scale(4), 0, Scale(4), 0);
            foreach (ToolStripItem item in strip.Items)
            {
                if (TryGetGlyph(item.Name, out _))
                {
                    item.Image = ImageSlot;
                    item.DisplayStyle = item.Name == "cmd.search.clear" || item.Name == "label.search"
                        ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText;
                    if (spacious && item is ToolStripButton)
                    {
                        item.Padding = new Padding(Scale(4), Scale(1), Scale(4), Scale(1));
                        item.Margin = new Padding(Scale(1), 0, Scale(1), 0);
                    }
                }
                if (item is ToolStripDropDownItem dropdown) Apply(dropdown.DropDown, false);
            }
            RefreshLabels(strip.Items);
        }

        internal static void RefreshLabels(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                if (Glyphs.ContainsKey(item.Name ?? string.Empty))
                {
                    // Only known UI command IDs are normalized; never inspect resource names.
                    foreach (string prefix in LegacyPrefixes)
                        if (item.Text.StartsWith(prefix, StringComparison.Ordinal))
                        { item.Text = item.Text.Substring(prefix.Length).TrimStart(); break; }
                    if (item.DisplayStyle == ToolStripItemDisplayStyle.Image)
                        item.AccessibleName = item.Name == "cmd.search.clear" && !string.IsNullOrWhiteSpace(item.ToolTipText)
                            ? item.ToolTipText : item.Text;
                }
                if (item is ToolStripDropDownItem dropdown) RefreshLabels(dropdown.DropDownItems);
            }
        }
    }
}
