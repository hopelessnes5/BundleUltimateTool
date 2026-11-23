using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleFormat.Models;
using BundleFormat.Tables;
using BundleFormat.Textures;
using BundleUltimateTool.Documents;
using BundleUltimateTool.UI;

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

        // Ported from PrototypeUltimateTool:MainForm.cs (NodeCacheItem + sort switch,
        // lines 4683-4692). PixelCount exists for parity with the original dropdown;
        // our index carries no resolution, so those two modes fall back to Size.
        private sealed class TreeSortItem
        {
            internal TreeNode Node;
            internal string SortName;
            internal long Size;
            internal long PixelCount;
            internal string Extension;
        }

        private void InitializeBundleUi()
        {
            nodeTreeView.AfterSelect += (s, e) => OnNodeSelected();
            nodeTreeView.BeforeExpand += OnBundleBeforeExpand;
            searchTextBox.TextChanged += (s, e) => { if (_filesMode) RefreshGameTree(); else RebuildTree(); };
            typeFilterComboBox.SelectedIndexChanged += (s, e) => { if (_filesMode) RefreshGameTree(); else RebuildTree(); };
            InitializeGameFolderUi();
        }

        // Entry nodes are created lazily: with thousands of archives an eager tree means
        // hundreds of thousands of TreeNodes and a UI freeze on every filter keystroke.
        private void OnBundleBeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node?.Tag is BundleDocument document &&
                e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null)
            {
                PopulateBundleEntries(e.Node, document);
            }
        }

        private void PopulateBundleEntries(TreeNode bundleNode, BundleDocument document)
        {
            bundleNode.Nodes.Clear();
            string query = GetArchiveQuery();
            uint? typeFilter = GetArchiveTypeFilter();
            try
            {
                var views = new List<BundleEntryView>();
                foreach (BundleEntry entry in document.Entries)
                    if (MatchesArchiveFilter(entry, query, typeFilter))
                        views.Add(new BundleEntryView(document, entry));

                // Apply the selected sort to lazily created entry nodes too, otherwise
                // changing the combo only re-sorts already expanded bundles by chance.
                switch (sortComboBox?.SelectedIndex ?? 0)
                {
                    case 1: views = views.OrderBy(v => EntryDisplayText(v.Entry), StringComparer.OrdinalIgnoreCase).ToList(); break;
                    case 2: views = views.OrderByDescending(v => EntryDisplayText(v.Entry), StringComparer.OrdinalIgnoreCase).ToList(); break;
                    case 3: views = views.OrderByDescending(v => (long)v.Entry.MaskedHdrUncompSize + v.Entry.MaskedDataUncompSize).ToList(); break;
                    case 4: views = views.OrderBy(v => (long)v.Entry.MaskedHdrUncompSize + v.Entry.MaskedDataUncompSize).ToList(); break;
                    case 5: views = views.OrderByDescending(v => v.Entry.MaskedDataUncompSize).ToList(); break;
                    case 6: views = views.OrderBy(v => v.Entry.MaskedDataUncompSize).ToList(); break;
                    case 7: views = views.OrderBy(v => NfsFileTypeInfo.FileTypeLabel(v.Entry.FileType), StringComparer.OrdinalIgnoreCase).ToList(); break;
                }

                bundleNode.Nodes.AddRange(views.Select(CreateEntryNode).ToArray());
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("ENTRY_LOAD_FAILED", $"'{document.Path}': {ex.Message}", ex);
                bundleNode.Nodes.Add(new TreeNode(UiLocalization.TranslateId("tree.entryLoadFailed",
                    "Cannot read entries", _uiLanguage)));
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ApplicationLog.Write("SHUTDOWN_BEGIN", "OnFormClosed");
            try
            {
                if (nodeTreeView != null && nodeTreeView.IsHandleCreated)
                {
                    nodeTreeView.BeginUpdate();
                    nodeTreeView.Nodes.Clear();
                    nodeTreeView.EndUpdate();
                }
                if (gameFileTreeView != null && gameFileTreeView.IsHandleCreated)
                {
                    gameFileTreeView.BeginUpdate();
                    gameFileTreeView.Nodes.Clear();
                    gameFileTreeView.EndUpdate();
                }
            }
            catch { }
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            ClearDocuments();
            _readerCache.Dispose();
            ApplicationLog.Write("SHUTDOWN_END", "OnFormClosed complete");
            base.OnFormClosed(e);
        }

        private BundleTypeHistogram _archiveTypeHistogram;

        private void ClearDocuments()
        {
            _openDocuments.Clear();
            _document = null;
            _compositeMode = false;
            _archiveTypeHistogram = null;
            _readerCache.Clear();
        }

        private void HandleStartupOnce()
        {
            if (_startupHandled) return;
            _startupHandled = true;
            ApplicationLog.Write("STARTUP_STEP", "begin");
            if (!string.IsNullOrWhiteSpace(_startupGameFolder))
            {
                // Only explicit --game-folder selection triggers indexing at startup.
                // A remembered settings path is kept in _gameFolderPath for the dialog
                // but must never be auto-scanned/restored.
                bool explicitGameFolder = _startupSwitchToFiles ||
                    !string.Equals(_startupGameFolder, _gameFolderPath, StringComparison.OrdinalIgnoreCase);
                if (explicitGameFolder)
                    ApplyGameFolder(_startupGameFolder, switchToFileMode: _startupSwitchToFiles);
                else
                    ApplicationLog.Write("STARTUP_LAZY", "skipped saved game folder restore: " + _startupGameFolder);
            }
            ApplicationLog.Write("STARTUP_STEP", "after game folder");
            if (!string.IsNullOrWhiteSpace(_startupFile))
                OpenBundle(_startupFile);
            ApplicationLog.Write("STARTUP_STEP", "after file");
            if (string.IsNullOrWhiteSpace(_startupFile) &&
                (string.IsNullOrWhiteSpace(_startupGameFolder) || !_startupSwitchToFiles) &&
                !_filesMode && _openDocuments.Count == 0)
                SetStatus(UiLocalization.TranslateId("status.ready", "Ready", _uiLanguage));
            if (_startupSelectedEntry >= 0) SelectEntryByIndex(_startupSelectedEntry);
            ApplicationLog.Write("STARTUP_STEP", "done");
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
                BundleIndex index = BundleIndex.Read(path);
                ClearDocuments();
                var document = new BundleDocument(index, _readerCache, Path.GetFileName(path));
                _document = document;
                _openDocuments.Add(document);

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
                    $"tableOffset={document.Header.TableOffset}, diagnostics={document.Index.Diagnostics.Count}");
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
            // PrototypeUltimateTool tree format: <Type> (<name/hash>), no size suffix.
            // NFS entries have no text names, so the stable 32-bit hash is used as the name.
            string typeName = NfsFileTypeInfo.FileTypeLabel(entry.FileType) switch
            {
                "texture" => "Texture",
                "material" => "Material",
                "model" => "Model",
                "defs table" => "DefsTable",
                "strings" => "Strings",
                _ => "Unknown"
            };
            string name = "0x" + entry.Raw.Hash.ToString("X8", CultureInfo.InvariantCulture);
            return string.Format(UiLocalization.TranslateId("tree.entry",
                "{0} ({1})", _uiLanguage), typeName, name);
        }

        private TreeNode CreateEntryNode(BundleEntryView view)
        {
            string text = EntryDisplayText(view.Entry);
            return new TreeNode(text)
            {
                Tag = view,
                ToolTipText = text,
                ImageIndex = TreeIcons.ImageIndexForArchive(view.Entry.FileType)
            };
        }

        private bool MatchesArchiveFilter(BundleEntry entry, string query, uint? typeFilter)
        {
            if (typeFilter.HasValue && entry.FileType != typeFilter.Value) return false;
            if (query.Length == 0) return true;
            return EntryDisplayText(entry).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   NfsFileTypeInfo.FileTypeLabel(entry.FileType).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetArchiveQuery() => (searchTextBox?.Text ?? string.Empty).Trim();

        private uint? GetArchiveTypeFilter() =>
            (typeFilterComboBox?.SelectedItem as TypeFilterOption)?.FileType;

        private void RebuildTree()
        {
            if (nodeTreeView == null || nodeTreeView.IsDisposed) return;
            if (_compositeMode)
            {
                RebuildCompositeTree();
                return;
            }

            object[] previouslySelectedTags = CaptureSelectedTreeTags();
            object previouslyActiveTag = CaptureActiveTreeTag();
            nodeTreeView.BeginUpdate();
            try
            {
                nodeTreeView.Nodes.Clear();
                propGrid.SelectedObject = null;
                int shown = 0;
                if (_document != null)
                {
                    string query = GetArchiveQuery();
                    uint? typeFilter = GetArchiveTypeFilter();
                    foreach (BundleEntry entry in _document.Entries)
                    {
                        if (!MatchesArchiveFilter(entry, query, typeFilter)) continue;
                        nodeTreeView.Nodes.Add(CreateEntryNode(new BundleEntryView(_document, entry)));
                        shown++;
                    }
                }
                SortTreeNodes(nodeTreeView.Nodes, sortComboBox?.SelectedIndex ?? 0);
                if (noResultsPanel != null) noResultsPanel.Visible = shown == 0;
                RestoreTreeSelection(previouslySelectedTags, previouslyActiveTag, null);
            }
            finally { nodeTreeView.EndUpdate(); }
            UpdateWorkspaceState();
        }

        private void RebuildCompositeTree()
        {
            object[] previouslySelectedTags = CaptureSelectedTreeTags();
            object previouslyActiveTag = CaptureActiveTreeTag();
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CaptureExpandedNodes(nodeTreeView.Nodes, expanded);
            ApplicationLog.Write("TREE_BUILD", "begin");
            nodeTreeView.BeginUpdate();
            try
            {
                nodeTreeView.Nodes.Clear();
                propGrid.SelectedObject = null;
                string query = GetArchiveQuery();
                uint? typeFilter = GetArchiveTypeFilter();
                int shown = 0;
                var dirNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
                foreach (BundleDocument document in _openDocuments)
                {
                    TreeNode parent = GetOrCreateDirectoryNodes(document.RelativePath, dirNodes, expanded);
                    string name = Path.GetFileName(document.Path);
                    var bundleNode = new TreeNode(
                        string.Format(UiLocalization.TranslateId("tree.bundleRoot",
                            "{0}", _uiLanguage), name))
                    {
                        Tag = document,
                        ToolTipText = document.Path,
                        ImageIndex = TreeIcons.File
                    };
                    // Do NOT touch document.Entries here: that would load and cache every
                    // archive just to build the tree. Filtering happens on expansion.
                    bool anyMatch = document.EntryCount > 0;
                    if (anyMatch)
                    {
                        // Placeholder is replaced with real entry nodes on first expansion.
                        bundleNode.Nodes.Add(new TreeNode(string.Empty));
                        (parent?.Nodes ?? nodeTreeView.Nodes).Add(bundleNode);
                        shown++;
                        if (expanded.Contains("B:" + document.Path)) bundleNode.Expand();
                    }
                }
                ApplicationLog.Write("TREE_BUILD", $"nodes built shown={shown}");
                SortTreeNodes(nodeTreeView.Nodes, sortComboBox?.SelectedIndex ?? 0);
                RemoveEmptyDirectoryNodes(nodeTreeView.Nodes);
                if (noResultsPanel != null) noResultsPanel.Visible = shown == 0 && _openDocuments.Count > 0;
                RestoreTreeSelection(previouslySelectedTags, previouslyActiveTag, null);
            }
            finally { nodeTreeView.EndUpdate(); }
            ApplicationLog.Write("TREE_BUILD", "end");
            UpdateWorkspaceState();
        }

        private static void CaptureExpandedNodes(TreeNodeCollection nodes, HashSet<string> expanded)
        {
            foreach (TreeNode node in nodes)
            {
                if (!node.IsExpanded) continue;
                if (node.Tag is BundleDocument doc) expanded.Add("B:" + doc.Path);
                else if (node.Tag is string dir) expanded.Add("D:" + dir);
                if (node.Nodes.Count > 0) CaptureExpandedNodes(node.Nodes, expanded);
            }
        }

        private TreeNode GetOrCreateDirectoryNodes(string relativePath, Dictionary<string, TreeNode> dirNodes, HashSet<string> expanded)
        {
            string normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
            int slash = normalized.LastIndexOf('/');
            if (slash < 0) return null;
            string dirPath = normalized.Substring(0, slash);
            if (dirNodes.TryGetValue(dirPath, out TreeNode existing)) return existing;

            string[] parts = dirPath.Split('/');
            string current = string.Empty;
            TreeNode parent = null;
            foreach (string part in parts)
            {
                current = current.Length == 0 ? part : current + "/" + part;
                if (!dirNodes.TryGetValue(current, out TreeNode node))
                {
                    node = new TreeNode(part)
                    {
                        Tag = current,
                        ToolTipText = current,
                        ImageIndex = TreeIcons.Folder
                    };
                    (parent?.Nodes ?? nodeTreeView.Nodes).Add(node);
                    if (expanded.Contains("D:" + current)) node.Expand();
                    dirNodes[current] = node;
                }
                parent = node;
            }
            return parent;
        }

        // Direct port of the original sorting switch; rank keeps folders above files.
        private static void SortTreeNodes(TreeNodeCollection nodes, int mode)
        {
            TreeNode[] children = nodes.Cast<TreeNode>().ToArray();
            foreach (TreeNode child in children) SortTreeNodes(child.Nodes, mode);
            var list = children.Select(node => new TreeSortItem
            {
                Node = node,
                SortName = node.Text ?? string.Empty,
                Size = node.Tag switch
                {
                    BundleDocument doc => doc.EntryCount,
                    BundleEntryView view => (long)view.Entry.MaskedHdrUncompSize + view.Entry.MaskedDataUncompSize,
                    _ => 0
                },
                PixelCount = node.Tag is BundleEntryView ? 0 : 0,
                Extension = node.Tag switch
                {
                    BundleDocument => ".bundle",
                    BundleEntryView view => NfsFileTypeInfo.FileTypeLabel(view.Entry.FileType),
                    string => ".folder",
                    _ => string.Empty
                }
            }).ToList();

            // Copied verbatim from PrototypeUltimateTool:MainForm.cs, lines 4683-4692.
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

            var reordered = list.OrderBy(n => NodeRank(n.Node)).Select(n => n.Node).ToArray();
            nodes.Clear();
            nodes.AddRange(reordered);
        }

        private static int NodeRank(TreeNode node) => node.Tag switch
        {
            string => 0,
            BundleDocument => 1,
            BundleEntryView => 2,
            _ => 3
        };

        private static bool RemoveEmptyDirectoryNodes(TreeNodeCollection nodes)
        {
            bool any = false;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                TreeNode node = nodes[i];
                if (node.Tag is string)
                {
                    RemoveEmptyDirectoryNodes(node.Nodes);
                    bool hasBundle = HasBundleDescendant(node);
                    if (!hasBundle) { nodes.RemoveAt(i); continue; }
                }
                any = true;
            }
            return any;
        }

        private static bool HasBundleDescendant(TreeNode node)
        {
            foreach (TreeNode child in node.Nodes)
            {
                if (child.Tag is BundleDocument) return true;
                if (child.Tag is string && HasBundleDescendant(child)) return true;
            }
            return false;
        }

        private void RestoreSelectedEntry(BundleEntryView previousView)
        {
            if (previousView == null) return;
            TreeNode restore = FindEntryNode(nodeTreeView.Nodes, previousView.Document, previousView.Entry.Index);
            if (restore != null) nodeTreeView.SelectedNode = restore;
        }

        private static TreeNode FindEntryNode(TreeNodeCollection nodes, BundleDocument document, int entryIndex)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is BundleEntryView view && ReferenceEquals(view.Document, document) && view.Entry.Index == entryIndex)
                    return node;
                TreeNode found = FindEntryNode(node.Nodes, document, entryIndex);
                if (found != null) return found;
            }
            return null;
        }

        // --- Selection capture/restore ported from PrototypeUltimateTool MainForm.cs
        // (CaptureSelectedTreeTags, CaptureActiveTreeTag, RestoreTreeSelection,
        //  CollectTreeNodes, TreeTagsMatch). Adapted for BundleDocument/BundleEntryView tags.

        private object[] CaptureSelectedTreeTags()
        {
            if (nodeTreeView == null) return Array.Empty<object>();
            return nodeTreeView.SelectedNodes
                .Where(node => node?.Tag != null)
                .Select(node => node.Tag)
                .ToArray();
        }

        private object CaptureActiveTreeTag() =>
            nodeTreeView?.SelectedNode?.Tag;

        private void RestoreTreeSelection(object[] selectedTags, object activeTag, TreeNode preferredActiveNode)
        {
            if (nodeTreeView == null) return;
            var allNodes = new List<TreeNode>();
            foreach (TreeNode root in nodeTreeView.Nodes)
                CollectTreeNodes(root, allNodes);
            RestoreTreeSelectionFromNodeList(allNodes, selectedTags, activeTag, preferredActiveNode);
        }

        private void RestoreTreeSelectionFromNodeList(IList<TreeNode> allNodes, object[] selectedTags,
            object activeTag, TreeNode preferredActiveNode)
        {
            if (nodeTreeView == null || allNodes == null) return;
            var restoredSelection = new List<TreeNode>();
            if (selectedTags != null && selectedTags.Length > 0)
            {
                foreach (TreeNode node in allNodes)
                {
                    if (node.Tag == null) continue;
                    foreach (object tag in selectedTags)
                    {
                        if (TreeTagsMatch(tag, node.Tag))
                        {
                            restoredSelection.Add(node);
                            break;
                        }
                    }
                }
            }

            TreeNode activeNode = preferredActiveNode != null && preferredActiveNode.TreeView == nodeTreeView
                ? preferredActiveNode
                : allNodes.FirstOrDefault(node => node.Tag != null && TreeTagsMatch(activeTag, node.Tag));

            if (preferredActiveNode != null && preferredActiveNode.TreeView == nodeTreeView &&
                !restoredSelection.Contains(preferredActiveNode))
                restoredSelection.Add(preferredActiveNode);

            nodeTreeView.SetSelectedNodes(restoredSelection, activeNode);
        }

        private static void CollectTreeNodes(TreeNode node, List<TreeNode> result)
        {
            if (node == null) return;
            result.Add(node);
            foreach (TreeNode child in node.Nodes) CollectTreeNodes(child, result);
        }

        private static bool TreeTagsMatch(object first, object second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;

            if (first is string firstDir && second is string secondDir)
                return string.Equals(firstDir, secondDir, StringComparison.OrdinalIgnoreCase);

            if (first is BundleDocument firstDoc && second is BundleDocument secondDoc)
                return string.Equals(firstDoc.Path, secondDoc.Path, StringComparison.OrdinalIgnoreCase);

            if (first is BundleEntryView firstView && second is BundleEntryView secondView)
                return ReferenceEquals(firstView.Document, secondView.Document)
                    || string.Equals(firstView.Document?.Path, secondView.Document?.Path, StringComparison.OrdinalIgnoreCase)
                    ? firstView.Entry.Index == secondView.Entry.Index
                    : false;

            return false;
        }

        internal List<BundleEntryView> GetSelectedViews() => nodeTreeView.SelectedNodes
            .OfType<TreeNode>()
            .Select(node => node.Tag)
            .OfType<BundleEntryView>()
            .ToList();

        internal List<BundleEntryView> GetAllViews() => _openDocuments
            .SelectMany(document => document.Entries.Select(entry => new BundleEntryView(document, entry)))
            .ToList();

        internal void SelectEntryByIndex(int index)
        {
            if (nodeTreeView == null) return;
            // The wanted entry may live inside a bundle that is still collapsed and
            // therefore has no entry nodes yet.
            BundleDocument owner = _openDocuments.FirstOrDefault(d => d.Entries.Any(e => e.Index == index));
            if (owner != null)
            {
                TreeNode bundleNode = FindBundleNodeByPath(nodeTreeView.Nodes, owner.Path);
                if (bundleNode != null && bundleNode.Nodes.Count == 1 && bundleNode.Nodes[0].Tag == null)
                    PopulateBundleEntries(bundleNode, owner);
            }
            TreeNode found = FindEntryNodeByIndex(nodeTreeView.Nodes, index);
            if (found == null) return;
            nodeTreeView.SelectedNode = found;
            found.EnsureVisible();
            OnNodeSelected();
        }

        private static TreeNode FindBundleNodeByPath(TreeNodeCollection nodes, string path)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is BundleDocument doc && string.Equals(doc.Path, path, StringComparison.OrdinalIgnoreCase))
                    return node;
                TreeNode found = FindBundleNodeByPath(node.Nodes, path);
                if (found != null) return found;
            }
            return null;
        }

        private static TreeNode FindEntryNodeByIndex(TreeNodeCollection nodes, int index)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is BundleEntryView view && view.Entry.Index == index) return node;
                TreeNode found = FindEntryNodeByIndex(node.Nodes, index);
                if (found != null) return found;
            }
            return null;
        }

        internal string ExportFileName(BundleEntryView view, string extension)
        {
            // The archive hash is the stable identity; index/document prefix only adds noise.
            string hash = view.Entry.Raw.Hash.ToString("X8", CultureInfo.InvariantCulture);
            return $"0x{hash}.{extension}";
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
            BundleEntryView view = nodeTreeView.SelectedNode?.Tag as BundleEntryView;
            BundleEntry selected = view?.Entry;
            BundleDocument owner = view?.Document;
            NfsTexture texture = null;
            NfsModel model = null;
            BundleFileInfo fileInfo = null;
            string error = null;
            if (view != null)
            {
                if (selected.FileType == (uint)NfsFileType.Texture)
                    texture = TryLoadTexture(owner, selected, out error);
                else if (selected.FileType == (uint)NfsFileType.Model)
                    model = TryLoadModel(owner, selected, out _);
                fileInfo = TryGetFileInfo(owner, selected, out _);
            }

            propGrid.SelectedObject = view == null
                ? null
                : new BundleEntryProperties(selected, owner.Path, texture, model, fileInfo);
            UpdateWorkspaceState();
            if (model != null)
            {
                SetPreviewMode(true);
                model3DView.SetModel(model);
                SetModel3DStatus();
            }
            else
            {
                SetPreviewMode(false);
                UpdatePreview(texture, error, selected != null);
            }
        }

        private void RefreshTypeFilterOptions()
        {
            if (typeFilterComboBox == null) return;
            uint? previousType = (typeFilterComboBox.SelectedItem as TypeFilterOption)?.FileType;
            var options = new List<TypeFilterOption>
            {
                new TypeFilterOption(null, UiLocalization.TranslateId("filter.allTypes", "All types", _uiLanguage))
            };

            // Ported from PrototypeUltimateTool texture-index/filter flow: populate type
            // options from actual scanned/indexed content, not a fixed hardcoded list.
            IReadOnlyDictionary<uint, int> histogram = GetArchiveTypeHistogram();
            foreach (KeyValuePair<uint, int> pair in histogram.OrderBy(p => p.Key))
            {
                string label = NfsFileTypeInfo.FileTypeLabel(pair.Key);
                options.Add(new TypeFilterOption(pair.Key, $"{label} ({pair.Value})"));
            }

            typeFilterComboBox.BeginUpdate();
            try
            {
                typeFilterComboBox.Items.Clear();
                foreach (TypeFilterOption option in options) typeFilterComboBox.Items.Add(option);
                int selected = 0;
                for (int i = 0; i < options.Count; i++)
                {
                    if (options[i].FileType == previousType) { selected = i; break; }
                }
                typeFilterComboBox.SelectedIndex = options.Count > 0 ? selected : -1;
            }
            finally { typeFilterComboBox.EndUpdate(); }
        }

        private IReadOnlyDictionary<uint, int> GetArchiveTypeHistogram()
        {
            if (_compositeMode)
                return _archiveTypeHistogram?.Counts ?? new Dictionary<uint, int>();

            if (_document != null)
                return BuildDocumentTypeHistogram(_document);

            return new Dictionary<uint, int>();
        }

        private static IReadOnlyDictionary<uint, int> BuildDocumentTypeHistogram(BundleDocument document)
        {
            var counts = new Dictionary<uint, int>();
            if (document == null) return counts;
            try
            {
                foreach (BundleEntry entry in document.Entries)
                {
                    counts.TryGetValue(entry.FileType, out int existing);
                    counts[entry.FileType] = existing + 1;
                }
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("TYPE_HISTOGRAM_DOC_FAILED", $"'{document.Path}': {ex.Message}", ex);
            }
            return counts;
        }

        private void UpdateBundleStatus()
        {
            if (_openDocuments.Count == 0)
            {
                SetStatus(UiLocalization.TranslateId("status.ready", "Ready", _uiLanguage));
                statusPathLabel.Text = string.Empty;
                statusPathLabel.ToolTipText = string.Empty;
                return;
            }

            string text;
            string fullPath;
            string tooltipPath;
            if (_compositeMode)
            {
                int totalEntries = _openDocuments.Sum(doc => doc.EntryCount);
                text = string.Format(
                    UiLocalization.TranslateId("status.autoOpened", "Bundles: {0}, entries: {1}", _uiLanguage),
                    _openDocuments.Count, totalEntries);
                fullPath = string.Format(UiLocalization.TranslateId("status.file", "File: {0}", _uiLanguage),
                    _gameFolderModel.RootPath ?? string.Empty);
                tooltipPath = _gameFolderModel.RootPath ?? string.Empty;
            }
            else
            {
                text = string.Format(
                    UiLocalization.TranslateId("status.bundleLoaded", "Bundle v{0}, {1} entries", _uiLanguage),
                    _document.Header.Version, _document.EntryCount);
                if (_document.Reader.Diagnostics.Count > 0)
                    text += " | " + UiLocalization.TranslateId("status.tableOffsetNote",
                        "tableOffset differs; using 48", _uiLanguage);
                fullPath = string.Format(
                    UiLocalization.TranslateId("status.file", "File: {0}", _uiLanguage), _document.Path);
                tooltipPath = _document.Path;
            }
            SetStatus(text);
            statusPathLabel.ToolTipText = tooltipPath;
            int available = Math.Max(80, statusStrip.ClientSize.Width - statusStateLabel.Width -
                (statusProgress != null && statusProgress.Visible ? statusProgress.Width : 0) - 24);
            statusPathLabel.Text = FitStatusText(fullPath, statusPathLabel.Font, available);
        }
    }
}
