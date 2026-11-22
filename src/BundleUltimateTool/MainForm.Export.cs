using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private void ExportRaw(bool selectedOnly)
        {
            if (_document == null) return;

            List<BundleEntry> entries;
            if (selectedOnly)
            {
                entries = nodeTreeView.SelectedNodes
                    .OfType<TreeNode>()
                    .Select(node => node.Tag)
                    .OfType<int>()
                    .Where(index => index >= 0 && index < _document.Entries.Count)
                    .Select(index => _document.Entries[index])
                    .ToList();
                if (entries.Count == 0)
                {
                    MessageBox.Show(this,
                        UiLocalization.TranslateId("export.noSelection", "Select at least one entry in the tree.", _uiLanguage),
                        UiLocalization.TranslateId("export.title", "Export raw entries", _uiLanguage),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            else
            {
                entries = _document.Entries.ToList();
            }

            using var folderDialog = new FolderBrowserDialog
            {
                Description = UiLocalization.TranslateId("dialog.export.folder",
                    "Select output folder for .nfs files", _uiLanguage)
            };
            if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

            var extractor = new BundleEntryExtractor(_document.Reader.File);
            int exported = 0;
            int failed = 0;
            foreach (BundleEntry entry in entries)
            {
                string fileName = $"file_{entry.Index:D4}.nfs";
                try
                {
                    extractor.ExtractRawTo(entry, folderDialog.SelectedPath, fileName, out RawExtractResult metadata);
                    exported++;
                    ApplicationLog.Write("EXPORT_OK",
                        $"{fileName}: {metadata} from '{_document.Path}' (entry {entry.Index}).");
                }
                catch (Exception ex)
                {
                    failed++;
                    ApplicationLog.Write("EXPORT_FAILED", $"{fileName} (entry {entry.Index}): {ex.Message}", ex);
                }
            }

            string summary = string.Format(
                UiLocalization.TranslateId("export.summary.body", "Exported: {0}\nFailed: {1}", _uiLanguage),
                exported, failed);
            SetStatus(string.Format(
                UiLocalization.TranslateId("status.exportDone", "Exported {0}, failed {1}", _uiLanguage),
                exported, failed));
            MessageBox.Show(this, summary,
                UiLocalization.TranslateId("export.title", "Export raw entries", _uiLanguage),
                MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }
}
