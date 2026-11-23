using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleFormat.Models;
using BundleFormat.Primitives;
using BundleFormat.Tables;
using BundleFormat.Textures;
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

        internal void ExportAuto(bool selectedOnly, BundleDocument onlyDocument = null)
        {
            if (_openDocuments.Count == 0) return;

            List<BundleEntryView> views = onlyDocument != null
                ? GetAllViews().Where(view => ReferenceEquals(view.Document, onlyDocument)).ToList()
                : selectedOnly ? GetSelectedViews() : GetAllViews();
            if (views.Count == 0)
            {
                MessageBox.Show(this,
                    UiLocalization.TranslateId("export.noSelection", "Select at least one entry in the tree.", _uiLanguage),
                    UiLocalization.TranslateId("export.autoTitle", "Export archive (auto)", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var folderDialog = new FolderBrowserDialog
            {
                Description = UiLocalization.TranslateId("dialog.export.autoFolder",
                    "Select output folder for auto export", _uiLanguage)
            };
            if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

            var ok = new Dictionary<string, int>(StringComparer.Ordinal);
            var bad = new Dictionary<string, int>(StringComparer.Ordinal);
            void Count(Dictionary<string, int> map, string category)
            {
                map.TryGetValue(category, out int current);
                map[category] = current + 1;
            }

            foreach (BundleEntryView view in views)
            {
                BundleEntry entry = view.Entry;
                uint fileType = entry.FileType;
                string category;
                string target = null;
                try
                {
                    if (fileType == (uint)NfsFileType.Texture)
                    {
                        category = "texture";
                        target = Path.Combine(folderDialog.SelectedPath, ExportFileName(view, "dds"));
                        NfsTexture texture = TryLoadTexture(view.Document, entry, out string error);
                        if (texture == null)
                            throw new BundleFormatException(error ?? "Unsupported texture.");
                        texture.SaveAsDdsFile(target);
                        Count(ok, category);
                        ApplicationLog.Write("EXPORT_AUTO_OK",
                            $"[texture] {Path.GetFileName(target)} from entry {entry.Index} in '{view.Document.Path}'.");
                    }
                    else if (fileType == (uint)NfsFileType.Model)
                    {
                        category = "model";
                        target = Path.Combine(folderDialog.SelectedPath, ExportFileName(view, "usd"));
                        NfsModel model = TryLoadModel(view.Document, entry, out string error);
                        if (model == null)
                            throw new BundleFormatException(error ?? "Cannot load model.");
                        UsdSummary usdSummary = UsdExporter.ExportFile(target, model);
                        Count(ok, category);
                        ApplicationLog.Write("EXPORT_AUTO_OK",
                            $"[model] {Path.GetFileName(target)}: meshes={usdSummary.Meshes}, vertices={usdSummary.Vertices}, faces={usdSummary.Faces}, entry {entry.Index}.");
                    }
                    else if (fileType == (uint)NfsFileType.StringsTable)
                    {
                        category = "strings";
                        target = Path.Combine(folderDialog.SelectedPath, ExportFileName(view, "txt"));
                        try
                        {
                            NfsStringsTable table = NfsStringsTable.Parse(GetDecompressedHeader(view.Document, entry));
                            table.SaveAsTxtFile(target);
                        }
                        catch
                        {
                            // Reusable SaveStrings helper is UI-dialog-based; fallback to raw on parse failure.
                            target = Path.Combine(folderDialog.SelectedPath, ExportFileName(view, "nfs"));
                            var extractor = new BundleEntryExtractor(view.Document.Reader.File);
                            extractor.ExtractRawTo(entry, folderDialog.SelectedPath, Path.GetFileName(target), out _);
                            category = "strings-raw-fallback";
                        }
                        Count(ok, category);
                        ApplicationLog.Write("EXPORT_AUTO_OK",
                            $"[{category}] {Path.GetFileName(target)} from entry {entry.Index}.");
                    }
                    else if (fileType == (uint)NfsFileType.DefsTable)
                    {
                        category = "defs";
                        target = Path.Combine(folderDialog.SelectedPath, ExportFileName(view, "txt"));
                        try
                        {
                            NfsDefsTable table = NfsDefsTable.Parse(GetDecompressedHeader(view.Document, entry));
                            var sb = new StringBuilder();
                            sb.AppendLine(table.DefName);
                            sb.AppendLine(table.Count.ToString());
                            foreach (NfsDefsTable.DefEntry def in table.Entries)
                            {
                                sb.Append(def.DefIdHex);
                                sb.Append('\t');
                                sb.Append(def.Type);
                                sb.Append('\t');
                                sb.Append(def.Data2);
                                sb.Append('\t');
                                sb.Append(def.Offset);
                                sb.Append('\t');
                                sb.Append(def.Data4);
                                sb.AppendLine();
                            }
                            File.WriteAllText(target, sb.ToString(), Encoding.UTF8);
                        }
                        catch
                        {
                            target = Path.Combine(folderDialog.SelectedPath, ExportFileName(view, "nfs"));
                            var extractor = new BundleEntryExtractor(view.Document.Reader.File);
                            extractor.ExtractRawTo(entry, folderDialog.SelectedPath, Path.GetFileName(target), out _);
                            category = "defs-raw-fallback";
                        }
                        Count(ok, category);
                        ApplicationLog.Write("EXPORT_AUTO_OK",
                            $"[{category}] {Path.GetFileName(target)} from entry {entry.Index}.");
                    }
                    else
                    {
                        category = "raw";
                        target = Path.Combine(folderDialog.SelectedPath, ExportFileName(view, "nfs"));
                        var extractor = new BundleEntryExtractor(view.Document.Reader.File);
                        extractor.ExtractRawTo(entry, folderDialog.SelectedPath, Path.GetFileName(target), out RawExtractResult metadata);
                        Count(ok, category);
                        ApplicationLog.Write("EXPORT_AUTO_OK",
                            $"[raw] {Path.GetFileName(target)}: {metadata}, entry {entry.Index}.");
                    }
                }
                catch (Exception ex)
                {
                    string failedCategory = fileType == (uint)NfsFileType.Texture ? "texture"
                        : fileType == (uint)NfsFileType.Model ? "model"
                        : fileType == (uint)NfsFileType.StringsTable ? "strings"
                        : fileType == (uint)NfsFileType.DefsTable ? "defs"
                        : "raw";
                    Count(bad, failedCategory);
                    string name = target != null ? Path.GetFileName(target) : ExportFileName(view, "bin");
                    ApplicationLog.Write("EXPORT_AUTO_FAILED",
                        $"[{failedCategory}] {name} (entry {entry.Index}): {ex.Message}", ex);
                }
            }

            int exported = ok.Values.Sum();
            int failed = bad.Values.Sum();
            string detail = string.Join(", ",
                ok.Keys.Concat(bad.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal)
                    .Select(k =>
                    {
                        ok.TryGetValue(k, out int o);
                        bad.TryGetValue(k, out int b);
                        return b > 0 ? $"{k} {o}/{o + b}" : $"{k} {o}";
                    }));

            string summary = string.Format(
                UiLocalization.TranslateId("export.autoSummary.body",
                    "Exported: {0}\nFailed: {1}\nBy type: {2}", _uiLanguage),
                exported, failed, string.IsNullOrEmpty(detail) ? "-" : detail);
            SetStatus(string.Format(
                UiLocalization.TranslateId("status.exportDone", "Exported {0}, failed {1}", _uiLanguage),
                exported, failed));
            MessageBox.Show(this, summary,
                UiLocalization.TranslateId("export.autoTitle", "Export archive (auto)", _uiLanguage),
                MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }
}
