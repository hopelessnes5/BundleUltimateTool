using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    /// <summary>Compact modal assembler dialog. The only extra window in the product.</summary>
    internal sealed class BundleAssemblerForm : Form
    {
        private readonly ListBox _bundleList;
        private readonly CheckedListBox _nfsList;
        private readonly Dictionary<string, string> _bundlePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _nfsPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Button _addBundleButton;
        private readonly Button _addNfsButton;
        private readonly Button _clearButton;
        private readonly Button _assembleButton;
        private readonly Button _cancelButton;
        private readonly Label _statusLabel;
        private readonly bool _dark;
        private readonly string _language;
        private readonly Action<string> _openBundle;

        internal BundleAssemblerForm(bool dark, string language, Action<string> openBundle)
        {
            _dark = dark;
            _language = language;
            _openBundle = openBundle;

            Text = T("assembler.title", "Bundle assembler");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(620, 360);
            MinimumSize = ClientSize;

            var palette = EditorPalette.ForTheme(_dark);
            BackColor = palette.Surface;
            ForeColor = palette.Text;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4,
                Padding = new Padding(6), Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var bundleHeader = new Label { Text = T("assembler.bundles", "Source bundles"), Dock = DockStyle.Fill, Height = 18 };
            var nfsHeader = new Label { Text = T("assembler.replacements", "Replacement .nfs files"), Dock = DockStyle.Fill, Height = 18 };
            layout.Controls.Add(bundleHeader, 0, 0);
            layout.Controls.Add(nfsHeader, 1, 0);

            _bundleList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _nfsList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            layout.Controls.Add(_bundleList, 0, 1);
            layout.Controls.Add(_nfsList, 1, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4)
            };
            _addBundleButton = MakeButton("assembler.addBundle", "Add bundle", OnAddBundle);
            _addNfsButton = MakeButton("assembler.addNfs", "Add .nfs files", OnAddNfs);
            _clearButton = MakeButton("assembler.clear", "Clear", OnClear);
            _assembleButton = MakeButton("assembler.assemble", "Assemble...", OnAssemble);
            _cancelButton = MakeButton("assembler.cancel", "Cancel", (s, e) => Close());
            buttons.Controls.AddRange(new Control[]
            {
                _addBundleButton, _addNfsButton, _clearButton, _assembleButton, _cancelButton
            });
            layout.Controls.Add(buttons, 0, 2);
            layout.SetColumnSpan(buttons, 2);

            _statusLabel = new Label { Dock = DockStyle.Fill, Height = 20, AutoEllipsis = true };
            layout.Controls.Add(_statusLabel, 0, 3);
            layout.SetColumnSpan(_statusLabel, 2);
            Controls.Add(layout);

            ApplyPalette(palette);
            _bundleList.SelectedIndexChanged += (s, e) => UpdateState();
            _nfsList.ItemCheck += (s, e) => BeginInvoke((Action)UpdateState);
            UpdateState();
        }

        private Button MakeButton(string key, string fallback, EventHandler handler)
        {
            var button = new Button
            {
                Text = T(key, fallback), AutoSize = true,
                Padding = new Padding(6, 2, 6, 2), Margin = new Padding(2, 0, 2, 0),
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false
            };
            button.Click += handler;
            return button;
        }

        private string T(string key, string fallback) => UiLocalization.TranslateId(key, fallback, _language);

        private void ApplyPalette(EditorPalette palette)
        {
            _bundleList.BackColor = palette.Input;
            _bundleList.ForeColor = palette.Text;
            _nfsList.BackColor = palette.Input;
            _nfsList.ForeColor = palette.Text;
            _statusLabel.ForeColor = palette.SecondaryText;
            foreach (Control control in Controls)
            {
                if (control is Button button)
                {
                    button.BackColor = palette.Toolbar;
                    button.ForeColor = palette.Text;
                    button.FlatAppearance.BorderColor = palette.Border;
                }
            }
        }

        private void OnAddBundle(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = T("assembler.openBundle", "Select source bundle"),
                Filter = T("assembler.bundleFilter", "Bundle files (*.bundle;*.bndl;*.bin)|*.bundle;*.bndl;*.bin"),
                Multiselect = false,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string name = Path.GetFileName(dialog.FileName);
            if (_bundlePaths.ContainsKey(name)) return;
            _bundlePaths.Add(name, dialog.FileName);
            _bundleList.Items.Add(name);
            if (_bundleList.SelectedIndex < 0) _bundleList.SelectedIndex = 0;
            UpdateState();
        }

        private void OnAddNfs(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = T("assembler.openNfs", "Select replacement .nfs files"),
                Filter = T("assembler.nfsFilter", "NFS files (*.nfs)|*.nfs"),
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (string path in dialog.FileNames)
            {
                string name = Path.GetFileName(path);
                if (_nfsPaths.ContainsKey(name)) continue;
                _nfsPaths.Add(name, path);
                _nfsList.Items.Add(name, false);
            }
            UpdateState();
        }

        private void OnClear(object sender, EventArgs e)
        {
            _bundlePaths.Clear();
            _nfsPaths.Clear();
            _bundleList.Items.Clear();
            _nfsList.Items.Clear();
            UpdateState();
        }

        private void UpdateState()
        {
            bool hasBundle = _bundleList.SelectedIndex >= 0;
            bool hasCheckedNfs = false;
            foreach (object item in _nfsList.CheckedItems) if (item is string) hasCheckedNfs = true;
            _assembleButton.Enabled = hasBundle && hasCheckedNfs;
            _statusLabel.Text = hasBundle
                ? string.Format(T("assembler.ready", "{0} bundle(s), {1} replacement(s) selected"),
                    _bundlePaths.Count, _nfsList.CheckedItems.Count)
                : T("assembler.noBundle", "Add a source bundle first.");
        }

        private void OnAssemble(object sender, EventArgs e)
        {
            if (_bundleList.SelectedItem is not string bundleName ||
                !_bundlePaths.TryGetValue(bundleName, out string source)) return;

            var replacements = new Dictionary<int, string>();
            foreach (string name in _nfsList.CheckedItems.OfType<string>())
            {
                if (!TryParseEntryIndex(name, out int index))
                {
                    MessageBox.Show(this,
                        string.Format(T("assembler.badName", "Invalid replacement name: {0}\nExpected file_0000.nfs"),
                            name),
                        T("assembler.title", "Bundle assembler"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                replacements[index] = _nfsPaths[name];
            }
            if (replacements.Count == 0) return;

            string extension = Path.GetExtension(source);
            string stem = Path.GetFileNameWithoutExtension(source);
            string defaultName = stem + ".rebuilt" + extension;
            using var saveDialog = new SaveFileDialog
            {
                Title = T("assembler.output", "Save rebuilt bundle"),
                Filter = T("assembler.bundleFilter", "Bundle files (*.bundle;*.bndl;*.bin)|*.bundle;*.bndl;*.bin"),
                FileName = defaultName,
                InitialDirectory = Path.GetDirectoryName(source) ?? Environment.CurrentDirectory,
                OverwritePrompt = true
            };
            if (saveDialog.ShowDialog(this) != DialogResult.OK) return;
            string output = saveDialog.FileName;

            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            {
                var confirm = MessageBox.Show(this,
                    T("assembler.inPlace", "The output is the source bundle. Replace it in place?\nA temporary file will be used."),
                    T("assembler.title", "Bundle assembler"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                BundleAssemblerReport report = BundleAssembler.Rebuild(source, output, replacements);
                ApplicationLog.Write("ASSEMBLE_OK",
                    $"source='{source}', output='{output}', replaced={report.ReplacedCount}, " +
                    $"length={report.OutputLength}, warnings={report.Warnings.Count}");
                string message = string.Format(
                    T("assembler.success", "Rebuilt: {0} bytes\nReplacements: {1}"),
                    report.OutputLength, report.ReplacedCount);
                var open = MessageBox.Show(this,
                    message + "\n\n" + T("assembler.openQuestion", "Open the rebuilt bundle?"),
                    T("assembler.title", "Bundle assembler"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes) _openBundle?.Invoke(output);
                Close();
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("ASSEMBLE_FAILED", $"source='{source}': {ex.Message}", ex);
                MessageBox.Show(this,
                    string.Format(T("assembler.failed", "Assembly failed:\n{0}"), ex.Message),
                    T("assembler.title", "Bundle assembler"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                if (!IsDisposed) Enabled = true;
            }
        }

        private static bool TryParseEntryIndex(string name, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(name) || name.Length != 14) return false;
            if (!name.StartsWith("file_", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".nfs", StringComparison.OrdinalIgnoreCase)) return false;
            return int.TryParse(name.Substring(5, 4), out index);
        }
    }
}
