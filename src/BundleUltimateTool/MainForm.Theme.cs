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

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        private const int DwmwaImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ERASE = 0x0004;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_FRAME = 0x0400;
        private const uint RDW_UPDATENOW = 0x0100;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const int WM_SETREDRAW = 0x000B;
        private const int WM_NCACTIVATE = 0x0086;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Native theme names and DWM attributes only work once handles exist.
            var palette = EditorPalette.ForTheme(_isDarkTheme);
            ApplyTreeTheme(nodeTreeView, palette);
            ApplyTreeTheme(gameFileTreeView, palette);
            ApplyNativeThemeAll();
            ApplyDarkTitleBar(palette);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyNativeThemeAll();
            ApplyDarkTitleBar(EditorPalette.ForTheme(_isDarkTheme));
            HandleStartupOnce();
            ApplicationLog.Write("STARTUP_COMPLETE", "OnShown done");
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

        private void ApplyTheme(bool isDark)
        {
            _isDarkTheme = isDark;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (menuStrip == null || IsDisposed) return;
            var palette = EditorPalette.ForTheme(_isDarkTheme);
            if (themeMenuItem != null)
            {
                themeMenuItem.Checked = _isDarkTheme;
                themeMenuItem.Text = UiLocalization.TranslateId(
                    _isDarkTheme ? "cmd.view.theme.light" : "cmd.view.theme",
                    _isDarkTheme ? "Light theme" : "Dark theme", _uiLanguage);
            }

            // Suspend native redraw across child controls while changing their palettes.
            // Do not suspend redraw on the top-level Form; WM_SETREDRAW on a top-level
            // window can leave the DWM non-client caption stale until activation changes.
            var controls = new List<Control>();
            foreach (Control child in Controls)
                CollectControlsForTheme(child, controls);
            var redrawSuspended = new List<Control>(controls.Count);
            foreach (Control control in controls)
            {
                if (control == null || control.IsDisposed || !control.IsHandleCreated) continue;
                if (IsViewerThemeRelatedControl(control)) continue;
                try
                {
                    SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                    redrawSuspended.Add(control);
                }
                catch { /* A child can be destroyed while the theme is being switched. */ }
            }

            try
            {
                BackColor = palette.Canvas;
                ForeColor = palette.Text;

                ApplyPaletteToTree(CollectControls(this), palette);

                ApplyTreeTheme(nodeTreeView, palette);
                ApplyTreeTheme(gameFileTreeView, palette);

                ApplyStripTheme(menuStrip, palette, palette.Toolbar);
                ApplyStripTheme(mainToolStrip, palette, palette.Toolbar);
                ApplyStripTheme(filterToolStrip, palette, palette.Toolbar);
                ApplyStripTheme(viewModeToolStrip, palette, palette.Toolbar);
                ApplyStripTheme(channelToolStrip, palette, palette.Toolbar);
                if (channelRButton != null) channelRButton.ForeColor = palette.RedChannel;
                if (channelGButton != null) channelGButton.ForeColor = palette.GreenChannel;
                if (channelBButton != null) channelBButton.ForeColor = palette.BlueChannel;
                if (channelAButton != null) channelAButton.ForeColor = palette.Text;
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
                if (propGrid != null) propGrid.ApplyPalette(palette);
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
                if (model3DView != null) model3DView.BackColor = palette.Preview;
                if (previewMessageLabel != null)
                {
                    previewMessageLabel.BackColor = palette.Preview;
                    previewMessageLabel.ForeColor = palette.SecondaryText;
                }
                ApplyWorkspacePalette(palette);
                ApplyNativeThemeAll();
                ApplyDarkTitleBar(palette);
            }
            finally
            {
                for (int i = redrawSuspended.Count - 1; i >= 0; i--)
                {
                    Control control = redrawSuspended[i];
                    if (control == null || control.IsDisposed || !control.IsHandleCreated) continue;
                    try { SendMessage(control.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero); }
                    catch { }
                }
                ScheduleNonClientFrameRefresh();
            }

            Invalidate(true);
        }

        private bool IsViewerThemeRelatedControl(Control control)
        {
            if (control == null || model3DView == null || model3DView.IsDisposed) return false;
            for (Control current = model3DView; current != null; current = current.Parent)
                if (ReferenceEquals(current, control)) return true;
            for (Control current = control; current != null; current = current.Parent)
                if (ReferenceEquals(current, model3DView)) return true;
            return false;
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
                if (item is ToolStripComboBox combo)
                {
                    combo.BackColor = palette.Input;
                    combo.ForeColor = palette.Text;
                    combo.ComboBox.BackColor = palette.Input;
                    combo.ComboBox.ForeColor = palette.Text;
                }
                else if (item is ToolStripTextBox text)
                {
                    text.BackColor = palette.Input;
                    text.ForeColor = palette.Text;
                    text.TextBox.BackColor = palette.Input;
                    text.TextBox.ForeColor = palette.Text;
                }
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

        private static void CollectControlsForTheme(Control parent, List<Control> result)
        {
            if (parent == null || parent.IsDisposed) return;
            result.Add(parent);
            foreach (Control child in parent.Controls)
                CollectControlsForTheme(child, result);
        }

        private static void ApplyPaletteToTree(List<Control> controls, EditorPalette palette)
        {
            foreach (Control control in controls)
            {
                if (control is ToolStrip) continue;
                if (control is Panel || control is SplitContainer || control is TableLayoutPanel)
                    control.BackColor = palette.Canvas;
            }
        }

        private void WireNativeTheme(Control root)
        {
            if (root == null) return;
            foreach (Control child in root.Controls)
            {
                child.HandleCreated += (s, e) => ApplyNativeHandle(child);
                child.ControlAdded += (s, e) =>
                {
                    ApplyNativeThemeToControl(e.Control);
                    WireNativeTheme(e.Control);
                };
                if (child is ToolStrip strip) WireToolStripItems(strip);
                WireNativeTheme(child);
            }
        }

        private void ApplyNativeThemeAll()
        {
            foreach (Control control in CollectControls(this)) ApplyNativeThemeToControl(control);
        }

        private void ApplyNativeThemeToControl(Control control)
        {
            switch (control)
            {
                case TreeView _:
                case TextBox _:
                case ComboBox _:
                case DataGridView _:
                    ApplyNativeHandle(control);
                    break;
                case ToolStrip strip:
                    foreach (ToolStripItem item in strip.Items) ApplyToolStripItemNative(item);
                    break;
            }
        }

        private void WireToolStripItems(ToolStrip strip)
        {
            foreach (ToolStripItem item in strip.Items)
            {
                if (item is ToolStripComboBox combo)
                    combo.ComboBox.HandleCreated += (s, e) => ApplyNativeHandle(combo.ComboBox);
                else if (item is ToolStripTextBox text)
                    text.TextBox.HandleCreated += (s, e) => ApplyNativeHandle(text.TextBox);
                if (item is ToolStripDropDownItem dropDown) WireToolStripItems(dropDown.DropDown);
            }
        }

        private void ApplyToolStripItemNative(ToolStripItem item)
        {
            if (item is ToolStripComboBox combo) ApplyNativeHandle(combo.ComboBox);
            else if (item is ToolStripTextBox text) ApplyNativeHandle(text.TextBox);
            if (item is ToolStripDropDownItem dropDown)
                foreach (ToolStripItem child in dropDown.DropDownItems) ApplyToolStripItemNative(child);
        }

        private void ApplyNativeHandle(Control control)
        {
            if (control == null || control.IsDisposed || !control.IsHandleCreated) return;
            try
            {
                SetWindowTheme(control.Handle, _isDarkTheme ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch { /* Theme names vary across Windows builds. */ }
        }

        private void ApplyDarkTitleBar(EditorPalette palette)
        {
            if (!IsHandleCreated) return;
            try
            {
                int dark = _isDarkTheme && !SystemInformation.HighContrast ? 1 : 0;
                if (DwmSetWindowAttribute(Handle, DwmwaImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, DwmwaImmersiveDarkModeBefore20H1, ref dark, sizeof(int));

                int border = ToColorRef(palette.Border);
                int caption = ToColorRef(palette.Toolbar);
                int text = ToColorRef(palette.Text);
                DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref border, sizeof(int));
                DwmSetWindowAttribute(Handle, DwmwaCaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(Handle, DwmwaTextColor, ref text, sizeof(int));
            }
            catch { /* DWM attributes are best-effort. */ }
        }

        private void ScheduleNonClientFrameRefresh()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try
                    {
                        IntPtr hwnd = Handle;
                        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

                        bool active = ContainsFocus || Form.ActiveForm == this;
                        SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(active ? 0 : 1), IntPtr.Zero);
                        SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(active ? 1 : 0), IntPtr.Zero);
                        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                            RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME | RDW_UPDATENOW);
                        DwmFlush();
                    }
                    catch (ExternalException) { /* DWM is unavailable on some Windows versions. */ }
                    catch (InvalidOperationException) { /* Window is being disposed. */ }
                });
            }
            catch (InvalidOperationException) { /* Handle can disappear during shutdown. */ }
        }

        // Win32 COLORREF is 0x00BBGGRR, unlike System.Drawing.Color.ToArgb().
        private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

        private void DrawThemedComboBoxItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            EditorPalette palette = EditorPalette.ForTheme(_isDarkTheme);
            Color background = selected ? palette.Selection : palette.Input;
            Color foreground = !combo.Enabled ? palette.DisabledText
                : selected ? palette.SelectionText : palette.Text;

            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = e.Index >= 0 && e.Index < combo.Items.Count
                ? combo.GetItemText(combo.Items[e.Index])
                : combo.Text;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
                ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, foreground, background);
        }
    }
}
