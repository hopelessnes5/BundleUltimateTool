using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleFormat.Models;
using BundleFormat.Tables;
using BundleFormat.Textures;
using BundleUltimateTool.Documents;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private sealed class TypeFilterOption
        {
            internal TypeFilterOption(uint? fileType, string text)
            {
                FileType = fileType;
                Text = text;
            }

            internal uint? FileType { get; }
            internal string Text { get; }
            public override string ToString() => Text;
        }

        private void InitializeBundleUi()
        {
            nodeTreeView.AfterSelect += (s, e) => OnNodeSelected();
            searchTextBox.TextChanged += (s, e) => { if (_filesMode) RefreshGameTree(); else RebuildTree(); };
            typeFilterComboBox.SelectedIndexChanged += (s, e) => { if (_filesMode) RefreshGameTree(); else RebuildTree(); };
            InitializeGameFolderUi();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            _document?.Dispose();
            _document = null;
            base.OnFormClosed(e);
        }

        private void SetBusy(bool busy)
        {
            if (statusProgress != null && !statusProgress.IsDisposed) statusProgress.Visible = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (busy)
            {
                statusStrip?.Refresh();
                Application.DoEvents(); // allow one repaint before synchronous parsing
            }
        }

        private void OpenBundle(string path)
        {
            bool retry = false;
            SetBusy(true);
            try
            {
                BundleReader reader = BundleReader.Open(path);
                var document = new BundleDocument(reader, path);
                BundleDocument previous = _document;
                _document = document;
                previous?.Dispose();

                _currentFilePath = path;
                if (_recentFiles.Record(path)) SaveSettings();
                searchTextBox.Text = string.Empty;
                RefreshTypeFilterOptions();
                RebuildTree();
                UpdateBundleStatus();
                UpdateWorkspaceState();
                _commands?.Refresh();
                ApplicationLog.Write("OPEN_OK",
                    $"Opened '{path}': version={document.Header.Version}, files={document.Entries.Count}, " +
                    $"tableOffset={document.Header.TableOffset}, diagnostics={document.Reader.Diagnostics.Count}");
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("OPEN_FAILED", $"Could not open bundle '{path}'.", ex);
                string message = string.Format(
                    UiLocalization.TranslateId("error.openFailed.body", "Could not open bundle:\n{0}", _uiLanguage),
                    ex.Message);
                message += "\n\n" + UiLocalization.TranslateId("error.nextStep",
                    "Check the path or choose a different file.", _uiLanguage);
                DialogResult answer = MessageBox.Show(this, message,
                    UiLocalization.TranslateId("error.openFailed.title", "Cannot open bundle", _uiLanguage),
                    MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                retry = answer == DialogResult.Retry;
            }
            finally { SetBusy(false); }

            if (retry)
            {
                if (IsHandleCreated) BeginInvoke((Action)OnOpenDialog);
                else OnOpenDialog();
            }
        }

        private string EntryDisplayText(BundleEntry entry)
        {
            ulong packed = (ulong)entry.Raw.HdrCompSize + entry.Raw.DataCompSize;
            return string.Format(
                UiLocalization.TranslateId("tree.entry", "file_{0:D4} | {1} | {2} bytes", _uiLanguage),
                entry.Index, entry.TypeLabel, packed);
        }

        private void RebuildTree()
        {
            if (nodeTreeView == null || nodeTreeView.IsDisposed) return;
            int previousIndex = nodeTreeView.SelectedNode?.Tag is int old && old >= 0 ? old : -1;

            nodeTreeView.BeginUpdate();
            try
            {
                nodeTreeView.Nodes.Clear();
                propGrid.SelectedObject = null;
                if (_document == null) return;

                string query = (searchTextBox?.Text ?? string.Empty).Trim();
                uint? typeFilter = (typeFilterComboBox?.SelectedItem as TypeFilterOption)?.FileType;

                int shown = 0;
                foreach (BundleEntry entry in _document.Entries)
                {
                    if (typeFilter.HasValue && entry.FileType != typeFilter.Value) continue;
                    string text = EntryDisplayText(entry);
                    if (query.Length > 0 && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    nodeTreeView.Nodes.Add(new TreeNode(text) { Tag = entry.Index, ToolTipText = text });
                    shown++;
                }

                if (noResultsPanel != null) noResultsPanel.Visible = shown == 0;

                if (previousIndex >= 0)
                {
                    TreeNode restore = nodeTreeView.Nodes.Cast<TreeNode>()
                        .FirstOrDefault(node => node.Tag is int index && index == previousIndex);
                    if (restore != null) nodeTreeView.SelectedNode = restore;
                }
            }
            finally { nodeTreeView.EndUpdate(); }
            UpdateWorkspaceState();
        }

        private void ClearFilters()
        {
            if (searchTextBox != null) searchTextBox.Text = string.Empty;
            RefreshTypeFilterOptions();
            if (typeFilterComboBox != null && typeFilterComboBox.Items.Count > 0)
                typeFilterComboBox.SelectedIndex = 0;
            RebuildTree();
        }

        private static string FitStatusText(string text, Font font, int availableWidth)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 8) return text ?? string.Empty;
            if (TextRenderer.MeasureText(text, font).Width <= availableWidth) return text;
            const string ellipsis = "…";
            for (int length = text.Length - 1; length > 0; length--)
            {
                string candidate = text.Substring(0, length) + ellipsis;
                if (TextRenderer.MeasureText(candidate, font).Width <= availableWidth) return candidate;
            }
            return ellipsis;
        }

        private void OnNodeSelected()
        {
            ClearDefsView();
            BundleEntry selected = null;
            NfsTexture texture = null;
            NfsModel model = null;
            BundleFileInfo fileInfo = null;
            string error = null;
            if (_document != null && nodeTreeView.SelectedNode?.Tag is int index &&
                index >= 0 && index < _document.Entries.Count)
            {
                selected = _document.Entries[index];
                if (selected.FileType == (uint)NfsFileType.Texture)
                    texture = TryLoadTexture(selected, out error);
                else if (selected.FileType == (uint)NfsFileType.Model)
                    model = TryLoadModel(selected, out _);
                fileInfo = TryGetFileInfo(selected, out _);
            }

            propGrid.SelectedObject = selected == null
                ? null
                : new BundleEntryProperties(selected, _document.Path, texture, model, fileInfo);
            UpdatePreview(texture, error, selected != null);
            UpdateWorkspaceState();
        }

        private void RefreshTypeFilterOptions()
        {
            if (typeFilterComboBox == null) return;
            int previous = typeFilterComboBox.SelectedIndex;
            var options = new List<TypeFilterOption>
            {
                new TypeFilterOption(null, UiLocalization.TranslateId("filter.allTypes", "All types", _uiLanguage))
            };
            if (_document != null)
            {
                foreach (uint fileType in _document.Entries.Select(e => e.FileType).Distinct().OrderBy(t => t))
                    options.Add(new TypeFilterOption(fileType, NfsFileTypeInfo.Describe(fileType)));
            }

            typeFilterComboBox.BeginUpdate();
            try
            {
                typeFilterComboBox.Items.Clear();
                foreach (TypeFilterOption option in options) typeFilterComboBox.Items.Add(option);
                typeFilterComboBox.SelectedIndex = previous >= 0 && previous < options.Count ? previous : 0;
            }
            finally { typeFilterComboBox.EndUpdate(); }
        }

        private void UpdateBundleStatus()
        {
            if (_document == null)
            {
                SetStatus(UiLocalization.TranslateId("status.ready", "Ready", _uiLanguage));
                statusPathLabel.Text = string.Empty;
                statusPathLabel.ToolTipText = string.Empty;
                return;
            }

            string text = string.Format(
                UiLocalization.TranslateId("status.bundleLoaded", "Bundle v{0}, {1} entries", _uiLanguage),
                _document.Header.Version, _document.Entries.Count);
            if (_document.Reader.Diagnostics.Count > 0)
                text += " | " + UiLocalization.TranslateId("status.tableOffsetNote",
                    "tableOffset differs; using 48", _uiLanguage);
            SetStatus(text);
            string fullPath = string.Format(
                UiLocalization.TranslateId("status.file", "File: {0}", _uiLanguage), _document.Path);
            statusPathLabel.ToolTipText = _document.Path;
            int available = Math.Max(80, statusStrip.ClientSize.Width - statusStateLabel.Width -
                (statusProgress != null && statusProgress.Visible ? statusProgress.Width : 0) - 24);
            statusPathLabel.Text = FitStatusText(fullPath, statusPathLabel.Font, available);
        }
    }
}
