using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleFormat.IO;
using BundleUltimateTool.Documents;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private const int AutoOpenBundleWarnLimit = 10000;
        private const int TextPreviewLimit = 256 * 1024;
        private const int HexPreviewLimit = 512;
        private readonly System.Windows.Forms.Timer _openBundleTimer = new System.Windows.Forms.Timer { Interval = 350 };

        private void InitializeGameFolderUi()
        {
            _openBundleTimer.Tick += (s, e) =>
            {
                _openBundleTimer.Stop();
                if (_openBundleTimer.Tag is string path && File.Exists(path))
                {
                    ApplicationLog.Write("SINGLE_OPEN", $"'{path}'");
                    SetFilesMode(false);
                    OpenBundle(path);
                }
            };
            // Same activation pattern as PrototypeUltimateTool:MainForm.cs lines 527-539.
            gameFileTreeView.AfterSelect += (s, e) =>
            {
                if (!gameFileTreeView.IsSelectionGestureInProgress) OnGameFileSelected();
            };
            gameFileTreeView.SelectedNodesChanged += (s, e) =>
            {
                if (_filesMode && !gameFileTreeView.IsSelectionGestureInProgress) OnGameFileSelected();
            };
            gameFileTreeView.NodeMouseClick += (s, e) =>
            {
                OnGameFileSelected();
                if (_filesMode && e.Node?.Tag is GameFileEntry clicked && clicked.IsBundle)
                    ScheduleBundleOpen(clicked.FullPath);
                else _openBundleTimer.Stop();
            };
            gameFileTreeView.NodeMouseDoubleClick += (s, e) => ActivateGameNode(e.Node);
            gameFileTreeView.NodeActivated += (s, e) => ActivateGameNode(e.Node);
            gameFileTreeView.BeforeExpand += (s, e) => ExpandGameDirectory(e.Node);
            gameFileTreeView.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Back && _filesMode && _gameFolderModel.CurrentRelativePath.Length > 0)
                {
                    NavigateGameUp();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.F5 && _filesMode)
                {
                    RefreshGameFolder();
                    e.Handled = true;
                }
            };
        }

        private void SelectGameFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = UiLocalization.TranslateId("folder.select.title",
                    "Select the game installation folder", _uiLanguage)
            };
            if (!string.IsNullOrWhiteSpace(_gameFolderPath) && Directory.Exists(_gameFolderPath))
                dialog.SelectedPath = _gameFolderPath;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            ApplyGameFolder(dialog.SelectedPath, switchToFileMode: true);
        }

        private void ApplyGameFolder(string path, bool switchToFileMode)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                ApplicationLog.Write("GAME_FOLDER_FAILED", "Invalid game folder: " + path);
                MessageBox.Show(this,
                    string.Format(UiLocalization.TranslateId("folder.invalid.body",
                        "The folder does not exist:\n{0}", _uiLanguage), path) + "\n\n" +
                    UiLocalization.TranslateId("error.nextStep", "Check the path or choose a different file.", _uiLanguage),
                    UiLocalization.TranslateId("folder.invalid.title", "Invalid game folder", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            try
            {
                _gameFolderModel.SetRoot(path);
                _gameFolderPath = path;
                SaveSettings();

                // Prototype-style indexing: read ONLY the 48-byte header per archive.
                // Tables are read lazily (and cached per archive) when a bundle is expanded.
                ClearDocuments();
                IReadOnlyList<string> bundles = _gameFolderModel.FindBundleFiles();
                ApplicationLog.Write("BUNDLE_DISCOVERED", $"root='{_gameFolderModel.RootPath}', count={bundles.Count}");
                ApplicationLog.Write("INDEX_MODE", "header-only, per-archive lazy table cache");
                int failures = 0;
                var histogramSources = new List<(string Path, BundleHeader Header)>(bundles.Count);
                var histogramIdentities = new List<ArchiveFileIdentity>(bundles.Count);
                foreach (string bundlePath in bundles)
                {
                    if (IsDisposed || Disposing) return;
                    try
                    {
                        BundleHeader header = BundleIndex.ReadHeader(bundlePath);
                        string relative = Path.GetRelativePath(_gameFolderModel.RootPath, bundlePath).Replace('\\', '/');
                        _openDocuments.Add(new BundleDocument(bundlePath, header, _readerCache, relative));
                        string fullPath = Path.GetFullPath(bundlePath);
                        histogramSources.Add((fullPath, header));
                        ArchiveFileIdentity identity = ArchiveFileIdentity.TryCapture(fullPath);
                        if (identity != null) histogramIdentities.Add(identity);
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        ApplicationLog.Write("AUTO_OPEN_FAILED", $"'{bundlePath}': {ex.Message}", ex);
                    }
                }
                if (IsDisposed || Disposing) return;
                ApplicationLog.Write("STARTUP_STEP", "index done, building type histogram");
                _archiveTypeHistogram = BuildOrLoadTypeHistogram(_gameFolderModel.RootPath, histogramSources, histogramIdentities);
                ApplicationLog.Write("TYPE_HISTOGRAM_READY",
                    $"root='{_gameFolderModel.RootPath}', types={_archiveTypeHistogram.Counts.Count}, entries={_archiveTypeHistogram.TotalEntries}");
                _document = _openDocuments.FirstOrDefault();
                _compositeMode = true;
                RefreshTypeFilterOptions();
                ApplicationLog.Write("STARTUP_STEP", "after type filter");
                SetFilesMode(false);
                ApplicationLog.Write("STARTUP_STEP", "after set files mode");
                UpdateGameStatus();
                _commands?.Refresh();

                int totalEntries = _openDocuments.Sum(doc => doc.EntryCount);
                ApplicationLog.Write("AUTO_OPEN_OK",
                    $"root='{_gameFolderModel.RootPath}', bundles={_openDocuments.Count}, entries={totalEntries}, failures={failures}");
                if (bundles.Count >= AutoOpenBundleWarnLimit)
                {
                    string warning = string.Format(UiLocalization.TranslateId("autoOpen.capWarning",
                        "A large number of bundles were discovered ({0}); loading may be slow.", _uiLanguage), bundles.Count);
                    SetStatus(warning);
                }
                else
                {
                    SetStatus(string.Format(UiLocalization.TranslateId("status.autoOpened",
                        "Bundles: {0}, entries: {1}", _uiLanguage), _openDocuments.Count, totalEntries));
                }
                ApplicationLog.Write("GAME_FOLDER_OK",
                    $"root='{_gameFolderModel.RootPath}', items={_gameFolderModel.Items.Count}");
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("GAME_FOLDER_FAILED", $"root='{path}': {ex.Message}", ex);
                MessageBox.Show(this,
                    string.Format(UiLocalization.TranslateId("folder.failed.body",
                        "Could not read the folder:\n{0}", _uiLanguage), ex.Message) + "\n\n" +
                    UiLocalization.TranslateId("error.nextStep", "Check the path or choose a different file.", _uiLanguage),
                    UiLocalization.TranslateId("folder.failed.title", "Folder error", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetBusy(false); }
        }

        private BundleTypeHistogram BuildOrLoadTypeHistogram(string rootPath,
            IReadOnlyList<(string Path, BundleHeader Header)> sources,
            IReadOnlyList<ArchiveFileIdentity> identities)
        {
            var token = System.Threading.CancellationToken.None;
            if (identities.Count == sources.Count && identities.Count > 0)
            {
                BundleTypeHistogram cached = BundleTypeHistogramCache.TryLoad(rootPath, identities, token);
                if (cached != null)
                {
                    ApplicationLog.Write("TYPE_HISTOGRAM_CACHE_HIT",
                        $"root='{rootPath}', files={identities.Count}, types={cached.Counts.Count}");
                    return cached;
                }
            }

            ApplicationLog.Write("TYPE_HISTOGRAM_SCAN_BEGIN", $"root='{rootPath}', files={sources.Count}");
            BundleTypeHistogram scanned = BundleTypeHistogramScanner.Scan(sources, token, int.MaxValue,
                (processed, total) =>
                {
                    if ((processed % 256) == 0 || (total > 0 && processed == total))
                        ApplicationLog.Write("TYPE_HISTOGRAM_SCAN_PROGRESS", $"processed={processed}, total={total}");
                });
            if (identities.Count == sources.Count && identities.Count > 0)
            {
                BundleTypeHistogramCache.Save(rootPath, identities, scanned, token);
                ApplicationLog.Write("TYPE_HISTOGRAM_CACHE_SAVED", $"root='{rootPath}', files={identities.Count}");
            }
            return scanned;
        }

        private void SetFilesMode(bool files)
        {
            _filesMode = files;
            if (archiveModeButton != null) archiveModeButton.Checked = !files;
            if (filesModeButton != null) filesModeButton.Checked = files;
            nodeTreeView.Visible = !files;
            gameFileTreeView.Visible = files;
            breadcrumbLabel.Visible = files;
            folderUpButton.Enabled = files && _gameFolderModel.CurrentRelativePath.Length > 0;

            if (files)
            {
                RefreshGameTypeFilter();
                RefreshGameTree();
                UpdateGameStatus();
            }
            else
            {
                if (noResultsPanel != null) noResultsPanel.Visible = false;
                RefreshTypeFilterOptions();
                RebuildTree();
                UpdateBundleStatus();
            }
            filePreviewTextBox.Visible = false;
            previewMessageLabel.Visible = false;
            if (channelToolStrip != null) channelToolStrip.Visible = false;
            previewScrollPanel.Visible = !files;
            previewBox.Visible = !files && _document != null;
            UpdateWorkspaceState();
            _commands?.Refresh();
        }

        private void RefreshGameTree()
        {
            if (gameFileTreeView == null || gameFileTreeView.IsDisposed) return;
            gameFileTreeView.BeginUpdate();
            try
            {
                gameFileTreeView.Nodes.Clear();
                propGrid.SelectedObject = null;
                if (!_gameFolderModel.HasRoot)
                {
                    if (noResultsPanel != null) noResultsPanel.Visible = false;
                    return;
                }

                string folderName = Path.GetFileName(_gameFolderModel.RootPath.TrimEnd(Path.DirectorySeparatorChar));
                var rootEntry = new GameFileEntry(folderName, _gameFolderModel.RootPath, true, 0, DateTime.MinValue);
                var root = new TreeNode(folderName)
                {
                    Tag = rootEntry, ToolTipText = _gameFolderModel.RootPath,
                    ImageIndex = TreeIcons.Folder
                };

                string extensionFilter = GetFileExtensionFilter();
                IReadOnlyList<GameFileEntry> filtered = _gameFolderModel.ApplyFilter(
                    searchTextBox?.Text, extensionFilter);
                AddGameNodes(root, filtered);
                gameFileTreeView.Nodes.Add(root);
                SortGameNodes(gameFileTreeView.Nodes, sortComboBox?.SelectedIndex ?? 0);
                root.Expand();
                if (noResultsPanel != null) noResultsPanel.Visible = filtered.Count == 0;
            }
            finally { gameFileTreeView.EndUpdate(); }
            UpdateWorkspaceState();
        }

        // Same port as MainForm.Bundle.cs: sort items with the original 8-mode switch,
        // then put directories first (stable OrderBy preserves the chosen order).
        private static void SortGameNodes(TreeNodeCollection nodes, int mode)
        {
            TreeNode[] children = nodes.Cast<TreeNode>().ToArray();
            foreach (TreeNode child in children) SortGameNodes(child.Nodes, mode);
            var list = children.Select(node =>
            {
                GameFileEntry entry = node.Tag as GameFileEntry;
                return new TreeSortItem
                {
                    Node = node,
                    SortName = node.Text ?? string.Empty,
                    Size = entry?.Length ?? 0,
                    PixelCount = 0,
                    Extension = entry?.Extension ?? string.Empty
                };
            }).ToList();

            switch (mode)
            {
                case 1: list = list.OrderBy(n => n.SortName, StringComparer.OrdinalIgnoreCase).ToList(); break;
                case 2: list = list.OrderByDescending(n => n.SortName, StringComparer.OrdinalIgnoreCase).ToList(); break;
                case 3: list = list.OrderByDescending(n => n.Size).ToList(); break;
                case 4: list = list.OrderBy(n => n.Size).ToList(); break;
                case 5: list = list.OrderByDescending(n => n.PixelCount).ToList(); break;
                case 6: list = list.OrderBy(n => n.PixelCount).ToList(); break;
                case 7: list = list.OrderBy(n => n.Extension, StringComparer.OrdinalIgnoreCase).ToList(); break;
            }

            var reordered = list
                .OrderBy(n => n.Node.Tag is GameFileEntry e && e.IsDirectory ? 0 : 1)
                .Select(n => n.Node).ToArray();
            nodes.Clear();
            nodes.AddRange(reordered);
        }

        private void AddGameNodes(TreeNode parent, IEnumerable<GameFileEntry> entries)
        {
            foreach (GameFileEntry entry in entries)
            {
                int imageIndex = entry.IsDirectory
                    ? TreeIcons.Folder
                    : GamePathRules.GetExtensionLabel(entry.FullPath) == "DDS"
                        ? TreeIcons.Texture
                        : TreeIcons.File;
                var node = new TreeNode(entry.DisplayText)
                {
                    Tag = entry,
                    ToolTipText = entry.FullPath,
                    ContextMenuStrip = gameFileContextMenu,
                    ImageIndex = imageIndex
                };
                if (entry.IsDirectory) node.Nodes.Add(new TreeNode(string.Empty)); // expand cue
                parent.Nodes.Add(node);
            }
        }

        private void ExpandGameDirectory(TreeNode node)
        {
            if (node?.Tag is not GameFileEntry entry || !entry.IsDirectory) return;
            if (node.Nodes.Count != 1 || node.Nodes[0].Tag != null) return;
            var diagnostics = new List<string>();
            IReadOnlyList<GameFileEntry> children = _gameFolderModel.Enumerate(
                _gameFolderModel.GetRelativePath(entry), diagnostics);
            node.Nodes.Clear();
            AddGameNodes(node, children);
        }

        private void ActivateGameNode(TreeNode node)
        {
            if (node?.Tag is not GameFileEntry entry) return;
            if (entry.IsDirectory)
            {
                SetBusy(true);
                try
                {
                    _gameFolderModel.Navigate(_gameFolderModel.GetRelativePath(entry));
                    RefreshGameTypeFilter();
                    RefreshGameTree();
                    UpdateGameStatus();
                    _commands?.Refresh();
                }
                finally { SetBusy(false); }
                return;
            }
            if (entry.IsBundle)
            {
                OpenBundle(entry.FullPath);
                SetFilesMode(false);
                return;
            }
            PreviewGameFile(entry);
        }

        private void OnGameFileSelected()
        {
            ClearDefsView();
            if (gameFileTreeView.SelectedNode?.Tag is not GameFileEntry entry)
            {
                propGrid.SelectedObject = null;
                filePreviewTextBox.Visible = false;
                UpdateWorkspaceState();
                return;
            }
            propGrid.SelectedObject = new GameFileProperties(entry);
            PreviewGameFile(entry);
            UpdateWorkspaceState();
            _commands?.Refresh();

            // Single click on an archive opens it; the short delay avoids opening bundles
            // while the user is just moving through the list with the keyboard.
            if (_filesMode && entry.IsBundle) ScheduleBundleOpen(entry.FullPath);
            else _openBundleTimer.Stop();
        }

        private void ScheduleBundleOpen(string path)
        {
            _openBundleTimer.Tag = path;
            _openBundleTimer.Stop();
            _openBundleTimer.Start();
        }

        private void PreviewGameFile(GameFileEntry entry)
        {
            if (entry == null) return;
            if (channelToolStrip != null) channelToolStrip.Visible = false;
            previewScrollPanel.Visible = false;
            previewBox.Visible = false;
            previewMessageLabel.Visible = false;
            filePreviewTextBox.Visible = true;
            filePreviewTextBox.Clear();

            if (entry.IsDirectory)
            {
                filePreviewTextBox.Text = UiLocalization.TranslateId("preview.directoryHint",
                    "Double-click to open this folder.", _uiLanguage);
                return;
            }
            if (entry.IsBundle)
            {
                filePreviewTextBox.Text = UiLocalization.TranslateId("preview.bundleHint",
                    "Double-click to open this bundle as an archive.", _uiLanguage);
                return;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(entry.FullPath);
                if (GamePathRules.IsTextFile(entry.FullPath))
                    filePreviewTextBox.Text = ReadTextPreview(bytes);
                else
                    filePreviewTextBox.Text = HexDump(bytes) +
                        (GamePathRules.GetExtensionLabel(entry.FullPath) == "DDS"
                            ? "\n\n" + UiLocalization.TranslateId("preview.ddsNote",
                                "DDS payload shown as hex; open a bundle to preview decoded textures.", _uiLanguage)
                            : string.Empty);
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("FILE_PREVIEW_FAILED", $"{entry.FullPath}: {ex.Message}", ex);
                filePreviewTextBox.Text = string.Format(
                    UiLocalization.TranslateId("preview.failed", "Cannot preview texture: {0}", _uiLanguage),
                    ex.Message);
            }
            filePreviewTextBox.SelectionStart = 0;
            filePreviewTextBox.ScrollToCaret();
        }

        private string ReadTextPreview(byte[] bytes)
        {
            bool truncated = bytes.Length > TextPreviewLimit;
            int count = truncated ? TextPreviewLimit : bytes.Length;
            var payload = new byte[count];
            Buffer.BlockCopy(bytes, 0, payload, 0, count);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(payload); }
            if (truncated)
                text += "\n\n" + UiLocalization.TranslateId("preview.textTruncated",
                    "Preview truncated to 256 KB.", _uiLanguage);
            return text;
        }

        private static string HexDump(byte[] bytes)
        {
            int count = Math.Min(HexPreviewLimit, bytes.Length);
            var text = new StringBuilder();
            for (int offset = 0; offset < count; offset += 16)
            {
                text.Append(offset.ToString("X8")).Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    int index = offset + i;
                    text.Append(index < count ? bytes[index].ToString("X2") + " " : "   ");
                }
                text.Append(" |");
                for (int i = 0; i < 16; i++)
                {
                    int index = offset + i;
                    char c = index < count && bytes[index] >= 32 && bytes[index] < 127
                        ? (char)bytes[index] : '.';
                    text.Append(c);
                }
                text.AppendLine("|");
            }
            return text.ToString();
        }

        private void NavigateGameUp()
        {
            SetBusy(true);
            try
            {
                _gameFolderModel.NavigateUp();
                RefreshGameTypeFilter();
                RefreshGameTree();
                UpdateGameStatus();
                _commands?.Refresh();
            }
            finally { SetBusy(false); }
        }

        private void RefreshGameFolder()
        {
            SetBusy(true);
            try
            {
                _gameFolderModel.Refresh();
                RefreshGameTypeFilter();
                RefreshGameTree();
                UpdateGameStatus();
            }
            finally { SetBusy(false); }
        }

        private void OpenFromFolder()
        {
            if (gameFileTreeView.SelectedNode?.Tag is not GameFileEntry entry) return;
            if (entry.IsBundle) OpenBundle(entry.FullPath);
            else PreviewGameFile(entry);
        }

        private void CopySelectedPaths()
        {
            var paths = gameFileTreeView.SelectedNodes
                .OfType<TreeNode>()
                .Select(node => node.Tag)
                .OfType<GameFileEntry>()
                .Select(entry => entry.FullPath)
                .ToList();
            if (paths.Count == 0) return;
            try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); }
            catch (Exception ex) { ApplicationLog.Write("COPY_PATH_FAILED", ex.Message, ex); }
        }

        private string GetFileExtensionFilter()
        {
            if (typeFilterComboBox == null || typeFilterComboBox.SelectedIndex <= 0) return null;
            return (typeFilterComboBox.SelectedItem as TypeFilterOption)?.Text;
        }

        private void RefreshGameTypeFilter()
        {
            if (typeFilterComboBox == null) return;
            int previous = typeFilterComboBox.SelectedIndex;
            var options = new List<TypeFilterOption>
            {
                new TypeFilterOption(null, UiLocalization.TranslateId("filter.allTypes", "All types", _uiLanguage))
            };
            if (_gameFolderModel.HasRoot)
            {
                foreach (string extension in _gameFolderModel.Items
                    .Where(entry => !entry.IsDirectory && GamePathRules.IsRelevantExtension(entry.Extension))
                    .Select(entry => entry.Extension)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase))
                    options.Add(new TypeFilterOption(null, extension));
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

        private void UpdateGameStatus()
        {
            if (!_gameFolderModel.HasRoot) return;
            string folderName = Path.GetFileName(_gameFolderModel.RootPath.TrimEnd(Path.DirectorySeparatorChar));
            string relative = string.IsNullOrEmpty(_gameFolderModel.CurrentRelativePath)
                ? "." : _gameFolderModel.CurrentRelativePath;
            SetStatus(string.Format(
                UiLocalization.TranslateId("status.gameFolder", "Game folder: {0} | {1} items in {2}", _uiLanguage),
                folderName, _gameFolderModel.Items.Count, relative));
            string full = string.Format(UiLocalization.TranslateId("status.file", "File: {0}", _uiLanguage),
                _gameFolderModel.CurrentFullPath);
            statusPathLabel.ToolTipText = _gameFolderModel.CurrentFullPath;
            int available = Math.Max(80, statusStrip.ClientSize.Width - statusStateLabel.Width -
                (statusProgress != null && statusProgress.Visible ? statusProgress.Width : 0) - 24);
            statusPathLabel.Text = FitStatusText(full, statusPathLabel.Font, available);

            string breadcrumbText = string.Format(
                UiLocalization.TranslateId("folder.breadcrumb", "Game: {0}", _uiLanguage), relative);
            breadcrumbLabel.Text = FitStatusText(breadcrumbText, breadcrumbLabel.Font,
                Math.Max(80, mainToolStrip.ClientSize.Width / 3));
            breadcrumbLabel.ToolTipText = _gameFolderModel.CurrentFullPath;
        }
    }
}
