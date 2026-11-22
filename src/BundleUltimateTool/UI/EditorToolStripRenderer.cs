using System;
using System.Drawing;
using System.Windows.Forms;

namespace BundleUltimateTool.UI
{
    internal sealed class EditorToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly Func<bool> _isDark;
        internal EditorToolStripRenderer(Func<bool> isDark) : base(new EditorColorTable(isDark))
        {
            _isDark = isDark;
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var palette = EditorPalette.ForTheme(_isDark());
            bool highlighted = e.Item.Selected || e.Item.Pressed ||
                e.Item is ToolStripButton button && button.Checked;
            e.TextColor = palette.ItemText(e.Item.Name, e.Item.Enabled, highlighted);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (!EditorCommandChrome.TryGetGlyph(e.Item.Name, out string glyph))
            {
                base.OnRenderItemImage(e);
                return;
            }
            var palette = EditorPalette.ForTheme(_isDark());
            bool highlighted = e.Item.Selected || e.Item.Pressed || e.Item is ToolStripButton button && button.Checked;
            Color color = palette.ItemText(null, e.Item.Enabled, highlighted);
            using var font = new Font(EditorCommandChrome.FontFamilyName,
                Math.Max(10, e.ImageRectangle.Height - 1), FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, glyph, font, e.ImageRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            var palette = EditorPalette.ForTheme(_isDark());
            e.ArrowColor = palette.ItemText(null, e.Item.Enabled, e.Item.Selected);
            base.OnRenderArrow(e);
        }

        private sealed class EditorColorTable : ProfessionalColorTable
        {
            private readonly Func<bool> _isDark;
            private EditorPalette Palette => EditorPalette.ForTheme(_isDark());
            internal EditorColorTable(Func<bool> isDark)
            {
                _isDark = isDark;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground => Palette.Surface;
            public override Color ImageMarginGradientBegin => Palette.Surface;
            public override Color ImageMarginGradientMiddle => Palette.Surface;
            public override Color ImageMarginGradientEnd => Palette.Surface;
            public override Color MenuBorder => Palette.Border;
            public override Color MenuItemBorder => Palette.Border;
            public override Color MenuItemSelected => Palette.Hover;
            public override Color MenuStripGradientBegin => Palette.Toolbar;
            public override Color MenuStripGradientEnd => Palette.Toolbar;
            public override Color MenuItemSelectedGradientBegin => Palette.Hover;
            public override Color MenuItemSelectedGradientEnd => Palette.Hover;
            public override Color MenuItemPressedGradientBegin => Palette.Pressed;
            public override Color MenuItemPressedGradientMiddle => Palette.Pressed;
            public override Color MenuItemPressedGradientEnd => Palette.Pressed;
            public override Color ToolStripBorder => Palette.Border;
            public override Color ToolStripGradientBegin => Palette.Toolbar;
            public override Color ToolStripGradientMiddle => Palette.Toolbar;
            public override Color ToolStripGradientEnd => Palette.Toolbar;
            public override Color SeparatorDark => Palette.Border;
            public override Color SeparatorLight => Palette.Toolbar;
            public override Color ButtonSelectedHighlight => Palette.Hover;
            public override Color ButtonSelectedBorder => Palette.Border;
            public override Color ButtonSelectedGradientBegin => Palette.Hover;
            public override Color ButtonSelectedGradientMiddle => Palette.Hover;
            public override Color ButtonSelectedGradientEnd => Palette.Hover;
            public override Color ButtonPressedHighlight => Palette.Pressed;
            public override Color ButtonPressedBorder => Palette.Border;
            public override Color ButtonPressedGradientBegin => Palette.Pressed;
            public override Color ButtonPressedGradientMiddle => Palette.Pressed;
            public override Color ButtonPressedGradientEnd => Palette.Pressed;
            public override Color CheckBackground => Palette.Selection;
            public override Color CheckSelectedBackground => Palette.Selection;
            public override Color CheckPressedBackground => Palette.Selection;
            public override Color ButtonCheckedHighlight => Palette.Selection;
            public override Color ButtonCheckedHighlightBorder => Palette.Accent;
            public override Color ButtonCheckedGradientBegin => Palette.Selection;
            public override Color ButtonCheckedGradientMiddle => Palette.Selection;
            public override Color ButtonCheckedGradientEnd => Palette.Selection;
        }
    }
}
