using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleFormat.Compression;
using BundleFormat.Models;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private NfsModel TryLoadModel(BundleEntry entry, out string error)
        {
            error = null;
            try
            {
                byte[] headerBytes = ZLibAdapter.Decompress(
                    _document.Reader.ReadBytes(entry.AbsoluteHeaderOffset, (int)entry.Raw.HdrCompSize),
                    (int)entry.MaskedHdrUncompSize);
                byte[] bodyBytes = ZLibAdapter.Decompress(
                    _document.Reader.ReadBytes(entry.AbsoluteBodyOffset, (int)entry.Raw.DataCompSize),
                    (int)entry.MaskedDataUncompSize);
                return NfsModel.InitFromData(headerBytes, bodyBytes);
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("MODEL_LOAD_FAILED", $"entry {entry.Index}: {ex.Message}", ex);
                error = ex.Message;
                return null;
            }
        }

        private void ExportObj(bool selectedOnly)
        {
            if (_document == null) return;
            var models = new List<BundleEntry>(_document.Entries
                .Where(entry => entry.FileType == (uint)NfsFileType.Model));
            if (selectedOnly)
            {
                var selectedIndices = nodeTreeView.SelectedNodes.OfType<TreeNode>()
                    .Select(node => node.Tag).OfType<int>().ToHashSet();
                models.RemoveAll(entry => !selectedIndices.Contains(entry.Index));
            }
            if (models.Count == 0)
            {
                MessageBox.Show(this,
                    UiLocalization.TranslateId("export.noModels", "No model entries selected.", _uiLanguage),
                    UiLocalization.TranslateId("export.objTitle", "Export OBJ models", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string singlePath = null;
            string folderPath = null;
            if (models.Count == 1)
            {
                using var saveDialog = new SaveFileDialog
                {
                    Title = UiLocalization.TranslateId("dialog.export.objFile", "Save OBJ model", _uiLanguage),
                    Filter = UiLocalization.TranslateId("dialog.export.objFilter",
                        "Wavefront OBJ (*.obj)|*.obj", _uiLanguage),
                    FileName = $"file_{models[0].Index:D4}.obj",
                    OverwritePrompt = true
                };
                if (saveDialog.ShowDialog(this) != DialogResult.OK) return;
                singlePath = saveDialog.FileName;
            }
            else
            {
                using var folderDialog = new FolderBrowserDialog
                {
                    Description = UiLocalization.TranslateId("dialog.export.objFolder",
                        "Select output folder for .obj files", _uiLanguage)
                };
                if (folderDialog.ShowDialog(this) != DialogResult.OK) return;
                folderPath = folderDialog.SelectedPath;
            }

            int exported = 0;
            int failed = 0;
            foreach (BundleEntry entry in models)
            {
                string target = singlePath ?? Path.Combine(folderPath, $"file_{entry.Index:D4}.obj");
                try
                {
                    NfsModel model = TryLoadModel(entry, out string error);
                    if (model == null)
                        throw new BundleFormat.Primitives.BundleFormatException(error);
                    ObjSummary summary = ObjExporter.ExportFile(target, model);
                    exported++;
                    ApplicationLog.Write("EXPORT_OBJ_OK",
                        $"{target}: meshes={summary.Meshes}, vertices={summary.Vertices}, faces={summary.Faces}.");
                }
                catch (Exception ex)
                {
                    failed++;
                    ApplicationLog.Write("EXPORT_OBJ_FAILED", $"{target} (entry {entry.Index}): {ex.Message}", ex);
                }
            }

            string message = string.Format(
                UiLocalization.TranslateId("export.summary.body", "Exported: {0}\nFailed: {1}", _uiLanguage),
                exported, failed);
            SetStatus(string.Format(
                UiLocalization.TranslateId("status.exportDone", "Exported {0}, failed {1}", _uiLanguage),
                exported, failed));
            MessageBox.Show(this, message,
                UiLocalization.TranslateId("export.objTitle", "Export OBJ models", _uiLanguage),
                MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }
}
