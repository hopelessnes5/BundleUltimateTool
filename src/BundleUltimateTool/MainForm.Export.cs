using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleUltimateTool.Documents;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private void ExportRaw(bool selectedOnly)
        {
            if (_openDocuments.Count == 0) return;

            List<BundleEntryView> views;
            if (selectedOnly)
            {
                views = GetSelectedViews();
                if (views.Count == 0)
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
                views = GetAllViews();
            }

            using var folderDialog = new FolderBrowserDialog
            {
                Description = UiLocalization.TranslateId("dialog.export.folder",
                    "Select output folder for .nfs files", _uiLanguage)
            };
            if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

            int exported = 0;
            int failed = 0;
            foreach (BundleEntryView view in views)
            {
                BundleEntry entry = view.Entry;
                string fileName = ExportFileName(view, "nfs");
                try
                {
                    var extractor = new BundleEntryExtractor(view.Document.Reader.File);
                    extractor.ExtractRawTo(entry, folderDialog.SelectedPath, fileName, out RawExtractResult metadata);
                    exported++;
                    ApplicationLog.Write("EXPORT_OK",
                        $"{fileName}: {metadata} from '{view.Document.Path}' (entry {entry.Index}).");
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
