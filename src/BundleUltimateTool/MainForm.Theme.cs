using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private const int DwmwaImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Native theme names and DWM attributes only work once handles exist.
            var palette = EditorPalette.ForTheme(_isDarkTheme);
            ApplyTreeTheme(nodeTreeView, palette);
            ApplyTreeTheme(gameFileTreeView, palette);
            ApplyDarkTitleBar(palette);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyDarkTitleBar(EditorPalette.ForTheme(_isDarkTheme));
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ApplyDarkTitleBar(EditorPalette.ForTheme(_isDarkTheme));
        }

        private void PaintSplitter(SplitContainer split, Graphics g)
        {
            if (split == null) return;
            var palette = EditorPalette.ForTheme(_isDarkTheme);
            using var brush = new SolidBrush(palette.Toolbar);
            g.FillRectangle(brush, split.SplitterRectangle);
        }

        private void ToggleTheme()
        {
            _isDarkTheme = !_isDarkTheme;
            themeMenuItem.Checked = _isDarkTheme;
            ApplyTheme();
            SaveSettings();
        }

        private void ApplyTheme()
        {
            if (menuStrip == null || IsDisposed) return;
            var palette = EditorPalette.ForTheme(_isDarkTheme);
            BackColor = palette.Canvas;
            ForeColor = palette.Text;

            ApplyPaletteToTree(CollectControls(this), palette);

            // Tree colors must not depend on IsHandleCreated: a fresh form has no handle
            // yet, and leaving this to the handle event was the white-tree bug.
            ApplyTreeTheme(nodeTreeView, palette);
            ApplyTreeTheme(gameFileTreeView, palette);

            // Every strip gets explicit palette colors, not just the filter strip.
            ApplyStripTheme(menuStrip, palette, palette.Toolbar);
            ApplyStripTheme(mainToolStrip, palette, palette.Toolbar);
            ApplyStripTheme(filterToolStrip, palette, palette.Toolbar);
            ApplyStripTheme(viewModeToolStrip, palette, palette.Toolbar);
            ApplyStripTheme(statusStrip, palette, palette.Toolbar);
            ApplyStripTheme(treeContextMenu, palette, palette.Surface);
            ApplyStripTheme(gameFileContextMenu, palette, palette.Surface);
            if (statusStateLabel != null) statusStateLabel.ForeColor = palette.Text;
            if (statusPathLabel != null) statusPathLabel.ForeColor = palette.SecondaryText;
            if (breadcrumbLabel != null) breadcrumbLabel.ForeColor = palette.SecondaryText;
            if (statusProgress != null) statusProgress.BackColor = palette.Toolbar;

            if (filePreviewTextBox != null)
            {
                filePreviewTextBox.BackColor = palette.Input;
                filePreviewTextBox.ForeColor = palette.Text;
            }
            if (noResultsPanel != null)
            {
                noResultsPanel.BackColor = palette.Surface;
                noResultsContent.BackColor = palette.Surface;
                noResultsLabel.ForeColor = palette.SecondaryText;
                clearFilterButton.BackColor = palette.Input;
                clearFilterButton.ForeColor = palette.Text;
                clearFilterButton.FlatAppearance.BorderColor = palette.Accent;
            }
            if (propGrid != null)
            {
                propGrid.BackColor = palette.Surface;
                propGrid.ForeColor = palette.Text;
            }
            if (defsGrid != null)
            {
                defsGrid.BackgroundColor = palette.Surface;
                defsGrid.GridColor = palette.Border;
                defsGrid.ForeColor = palette.Text;
                defsGrid.DefaultCellStyle.BackColor = palette.Surface;
                defsGrid.DefaultCellStyle.ForeColor = palette.Text;
                defsGrid.ColumnHeadersDefaultCellStyle.BackColor = palette.Toolbar;
                defsGrid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
                defsGrid.EnableHeadersVisualStyles = false;
            }
            if (defsPanel != null) defsPanel.BackColor = palette.Surface;
            if (defsNameLabel != null)
            {
                defsNameLabel.BackColor = palette.Surface;
                defsNameLabel.ForeColor = palette.SecondaryText;
            }
            if (previewBox != null) previewBox.BackColor = palette.Preview;
            if (previewMessageLabel != null)
            {
                previewMessageLabel.BackColor = palette.Preview;
                previewMessageLabel.ForeColor = palette.SecondaryText;
            }
            ApplyWorkspacePalette(palette);
            ApplyDarkTitleBar(palette);

            if (themeMenuItem != null)
            {
                themeMenuItem.Checked = _isDarkTheme;
                themeMenuItem.Text = UiLocalization.TranslateId(
                    _isDarkTheme ? "cmd.view.theme.light" : "cmd.view.theme",
                    _isDarkTheme ? "Light theme" : "Dark theme", _uiLanguage);
            }
            Invalidate(true);
        }

        private void ApplyTreeTheme(MultiSelectTreeView tree, EditorPalette palette)
        {
            if (tree == null || tree.IsDisposed) return;
            tree.BackColor = palette.Surface;
            tree.ForeColor = palette.Text;
            tree.LineColor = palette.Border;
            tree.FocusIndicatorColor = palette.Accent;
            if (!tree.IsHandleCreated) return;
            try { SetWindowTheme(tree.Handle, _isDarkTheme ? "DarkMode_Explorer" : "Explorer", null); }
            catch { /* Native theme names vary across Windows versions. */ }
        }

        private static void ApplyStripTheme(ToolStrip strip, EditorPalette palette, Color background)
        {
            if (strip == null || strip.IsDisposed) return;
            strip.BackColor = background;
            strip.ForeColor = palette.Text;
            foreach (ToolStripItem item in strip.Items)
            {
                item.BackColor = background;
                item.ForeColor = item.Enabled ? palette.Text : palette.DisabledText;
                if (item is ToolStripDropDownItem dropDown)
                    ApplyDropDownItems(dropDown.DropDownItems, palette, palette.Surface);
            }
            strip.Invalidate();
        }

        private static void ApplyDropDownItems(ToolStripItemCollection items, EditorPalette palette, Color background)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = background;
                item.ForeColor = item.Enabled ? palette.Text : palette.DisabledText;
                if (item is ToolStripDropDownItem dropDown)
                    ApplyDropDownItems(dropDown.DropDownItems, palette, background);
            }
        }

        private static List<Control> CollectControls(Control root)
        {
            var result = new List<Control>();
            Collect(root, result);
            return result;
        }

        private static void Collect(Control parent, List<Control> result)
        {
            if (parent == null || parent.IsDisposed) return;
            result.Add(parent);
            foreach (Control child in parent.Controls) Collect(child, result);
        }

        private static void ApplyPaletteToTree(List<Control> controls, EditorPalette palette)
        {
            foreach (Control control in controls)
            {
                // Strips are themed explicitly with their own surface color; neutral
                // containers use the canvas color so no system-white area remains.
                if (control is ToolStrip) continue;
                if (control is Panel || control is SplitContainer || control is TableLayoutPanel)
                    control.BackColor = palette.Canvas;
            }
        }

        private void ApplyDarkTitleBar(EditorPalette palette)
        {
            if (!IsHandleCreated) return;
            try
            {
                int dark = _isDarkTheme ? 1 : 0;
                DwmSetWindowAttribute(Handle, DwmwaImmersiveDarkMode, ref dark, sizeof(int));
                DwmSetWindowAttribute(Handle, DwmwaImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
                int border = ColorToColorRef(palette.Border);
                int caption = ColorToColorRef(palette.Toolbar);
                int text = ColorToColorRef(palette.Text);
                DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref border, sizeof(int));
                DwmSetWindowAttribute(Handle, DwmwaCaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(Handle, DwmwaTextColor, ref text, sizeof(int));
            }
            catch { /* DWM attributes are best-effort. */ }
        }

        // Win32 COLORREF is 0x00BBGGRR, unlike System.Drawing.Color.ToArgb().
        private static int ColorToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);
    }
}
