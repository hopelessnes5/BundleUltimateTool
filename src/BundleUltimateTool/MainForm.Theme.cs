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
            var palette = EditorPalette.ForTheme(_isDarkTheme);
            BackColor = palette.Canvas;
            ForeColor = palette.Text;

            ApplyPaletteToTree(CollectControls(this), _isDarkTheme);

            if (nodeTreeView != null && nodeTreeView.IsHandleCreated)
            {
                try { SetWindowTheme(nodeTreeView.Handle, _isDarkTheme ? "DarkMode_Explorer" : "Explorer", null); }
                catch { /* Native theme names vary across Windows versions. */ }
                nodeTreeView.BackColor = palette.Surface;
                nodeTreeView.ForeColor = palette.Text;
                nodeTreeView.LineColor = palette.Border;
            }
            if (nodeTreeView != null) nodeTreeView.FocusIndicatorColor = palette.Accent;
            if (gameFileTreeView != null)
            {
                gameFileTreeView.BackColor = palette.Surface;
                gameFileTreeView.ForeColor = palette.Text;
                gameFileTreeView.LineColor = palette.Border;
                gameFileTreeView.FocusIndicatorColor = palette.Accent;
            }
            if (filePreviewTextBox != null)
            {
                filePreviewTextBox.BackColor = palette.Input;
                filePreviewTextBox.ForeColor = palette.Text;
            }
            if (breadcrumbLabel != null) breadcrumbLabel.ForeColor = palette.SecondaryText;
            if (noResultsPanel != null)
            {
                noResultsPanel.BackColor = palette.Surface;
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
            if (filterToolStrip != null) filterToolStrip.BackColor = palette.Toolbar;
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

        private static void ApplyPaletteToTree(List<Control> controls, bool dark)
        {
            Color canvas = EditorPalette.ForTheme(dark).Canvas;
            foreach (Control control in controls)
            {
                if (control is ToolStrip) continue;
                // Only neutral containers get a palette override here; specialized controls
                // (tree, property grid, empty state, picture box) are handled explicitly.
                if (control is Panel || control is SplitContainer) control.BackColor = canvas;
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
