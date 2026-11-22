using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleFormat.Compression;
using BundleFormat.Textures;
using BundleUltimateTool.Documents;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private Bitmap _previewBitmap;
        private Bitmap _currentSourceBmp;
        private NfsTexture _previewTextureSource;
        private ToolStripLabel resolutionLabel;

        private NfsTexture TryLoadTexture(BundleDocument document, BundleEntry entry, out string error)
        {
            error = null;
            try
            {
                byte[] headerBytes = ZLibAdapter.Decompress(
                    document.Reader.ReadBytes(entry.AbsoluteHeaderOffset, (int)entry.Raw.HdrCompSize),
                    (int)entry.MaskedHdrUncompSize);
                byte[] imageBytes = entry.Raw.DataCompSize > 0
                    ? ZLibAdapter.Decompress(
                        document.Reader.ReadBytes(entry.AbsoluteBodyOffset, (int)entry.Raw.DataCompSize),
                        (int)entry.MaskedDataUncompSize)
                    : Array.Empty<byte>();

                if (!NfsTexture.IsNfsTextureSignature(headerBytes))
                {
                    error = UiLocalization.TranslateId("preview.unsupportedTexture",
                        "Unsupported texture signature", _uiLanguage);
                    return null;
                }
                return NfsTexture.InitFromData(headerBytes, imageBytes);
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("TEXTURE_LOAD_FAILED", $"entry {entry.Index}: {ex.Message}", ex);
                error = ex.Message;
                return null;
            }
        }

        private void UpdatePreview(NfsTexture texture, string error, bool hasSelection)
        {
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            _currentSourceBmp?.Dispose();
            _currentSourceBmp = null;
            _previewTextureSource = null;
            previewBox.Image = null;

            if (texture == null)
            {
                channelToolStrip.Visible = false;
                if (resolutionLabel != null) resolutionLabel.Text = "";
                previewMessageLabel.Text = !hasSelection
                    ? UiLocalization.TranslateId("preview.noSelection", "Select a texture entry to preview it.", _uiLanguage)
                    : error != null
                        ? string.Format(UiLocalization.TranslateId("preview.failed",
                            "Cannot preview texture: {0}", _uiLanguage), error)
                        : UiLocalization.TranslateId("preview.noTexture",
                            "No texture preview for this entry.", _uiLanguage);
                previewMessageLabel.Visible = true;
                return;
            }

            try
            {
                _previewTextureSource = texture;
                // Input step: NFS textures provide decompressed RGB/RGBA bytes via
                // NfsTexture.Decompress(). Convert to the BGRA source bitmap expected by
                // the ported ProcessChannels pipeline (Prototype DdsDecoder.DecodeDds output).
                byte[] pixels = texture.Decompress();
                _currentSourceBmp = CreateSourceBitmap(texture.GetWidth(), texture.GetHeight(), texture.GetNumComponents(), pixels);
                channelToolStrip.Visible = true;
                RefreshTexturePreview();
                previewMessageLabel.Visible = false;
            }
            catch (Exception ex)
            {
                channelToolStrip.Visible = false;
                ApplicationLog.Write("PREVIEW_FAILED", ex.Message, ex);
                previewMessageLabel.Text = string.Format(
                    UiLocalization.TranslateId("preview.failed", "Cannot preview texture: {0}", _uiLanguage),
                    ex.Message);
                previewMessageLabel.Visible = true;
            }
        }

        private void SetChannels(bool r, bool g, bool b, bool a)
        {
            channelRButton.Checked = r;
            channelGButton.Checked = g;
            channelBButton.Checked = b;
            channelAButton.Checked = a;
            ApplyChannels();
        }

        private void ApplyChannels()
        {
            if (_previewBitmap != null)
            {
                previewBox.Image = null;
                _previewBitmap.Dispose();
                _previewBitmap = null;
            }

            if (_currentSourceBmp == null)
            {
                if (resolutionLabel != null) resolutionLabel.Text = "";
                return;
            }

            // Port of Prototype MainForm.ApplyChannels (MainForm.cs:4863-4886).
            _previewBitmap = TextureChannelProcessor.ProcessChannels(
                _currentSourceBmp,
                channelRButton.Checked,
                channelGButton.Checked,
                channelBButton.Checked,
                channelAButton.Checked);

            previewBox.Image = _previewBitmap;

            if (resolutionLabel != null)
                resolutionLabel.Text = $"{_currentSourceBmp.Width} × {_currentSourceBmp.Height}";
        }

        private void RefreshTexturePreview() => ApplyChannels();

        private static Bitmap CreateSourceBitmap(int width, int height, int components, byte[] pixels)
        {
            var source = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = source.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    var row = new byte[width * 4];
                    for (int x = 0; x < width; x++)
                    {
                        int src = (y * width + x) * components;
                        int dst = x * 4;
                        row[dst + 0] = components > 2 ? pixels[src + 2] : pixels[src]; // B
                        row[dst + 1] = components > 1 ? pixels[src + 1] : pixels[src]; // G
                        row[dst + 2] = pixels[src + 0]; // R
                        row[dst + 3] = components >= 4 ? pixels[src + 3] : (byte)255;
                    }
                    Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
                }
            }
            finally { source.UnlockBits(data); }
            return source;
        }

        // V-Flip, Fit/100%, mouse-wheel zoom removed: Prototype 2D viewer has no such controls.

        private void ExportTextures(bool selectedOnly)
        {
            if (_openDocuments.Count == 0) return;
            var textures = GetAllViews()
                .Where(view => view.Entry.FileType == (uint)NfsFileType.Texture).ToList();
            if (selectedOnly)
            {
                var selected = GetSelectedViews().Select(view => (view.Document, view.Entry.Index)).ToHashSet();
                textures.RemoveAll(view => !selected.Contains((view.Document, view.Entry.Index)));
            }
            if (textures.Count == 0)
            {
                MessageBox.Show(this,
                    UiLocalization.TranslateId("export.noTextures", "No textures selected.", _uiLanguage),
                    UiLocalization.TranslateId("export.textureTitle", "Export DDS textures", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var folderDialog = new FolderBrowserDialog
            {
                Description = UiLocalization.TranslateId("dialog.export.ddsFolder",
                    "Select output folder for .dds files", _uiLanguage)
            };
            if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

            int exported = 0;
            int failed = 0;
            foreach (BundleEntryView view in textures)
            {
                BundleEntry entry = view.Entry;
                string fileName = ExportFileName(view, "dds");
                try
                {
                    NfsTexture texture = TryLoadTexture(view.Document, entry, out string error);
                    if (texture == null) throw new BundleFormat.Primitives.BundleFormatException(error);
                    texture.SaveAsDdsFile(Path.Combine(folderDialog.SelectedPath, fileName));
                    exported++;
                    ApplicationLog.Write("EXPORT_DDS_OK", $"{fileName} from entry {entry.Index}.");
                }
                catch (Exception ex)
                {
                    failed++;
                    ApplicationLog.Write("EXPORT_DDS_FAILED", $"{fileName} (entry {entry.Index}): {ex.Message}", ex);
                }
            }

            string summary = string.Format(
                UiLocalization.TranslateId("export.summary.body", "Exported: {0}\nFailed: {1}", _uiLanguage),
                exported, failed);
            SetStatus(string.Format(
                UiLocalization.TranslateId("status.exportDone", "Exported {0}, failed {1}", _uiLanguage),
                exported, failed));
            MessageBox.Show(this, summary,
                UiLocalization.TranslateId("export.textureTitle", "Export DDS textures", _uiLanguage),
                MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void ConvertDdsToNfs()
        {
            using var openDialog = new OpenFileDialog
            {
                Title = UiLocalization.TranslateId("dialog.ddsToNfs.open", "Select a DDS texture", _uiLanguage),
                Filter = UiLocalization.TranslateId("dialog.ddsToNfs.filter", "DDS images (*.dds)|*.dds", _uiLanguage),
                CheckFileExists = true
            };
            if (openDialog.ShowDialog(this) != DialogResult.OK) return;

            using var saveDialog = new SaveFileDialog
            {
                Title = UiLocalization.TranslateId("dialog.ddsToNfs.save", "Save NFS texture", _uiLanguage),
                Filter = UiLocalization.TranslateId("dialog.ddsToNfs.saveFilter",
                    "NFS texture (*.nfs)|*.nfs", _uiLanguage),
                FileName = Path.GetFileNameWithoutExtension(openDialog.FileName) + ".nfs",
                OverwritePrompt = true
            };
            if (saveDialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                DdsToNfsConverter.ConvertFile(openDialog.FileName, saveDialog.FileName);
                ApplicationLog.Write("DDS_TO_NFS_OK", saveDialog.FileName);
                SetStatus(UiLocalization.TranslateId("status.ddsToNfsDone", "DDS converted to NFS", _uiLanguage));
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("DDS_TO_NFS_FAILED", ex.Message, ex);
                MessageBox.Show(this,
                    string.Format(UiLocalization.TranslateId("error.ddsToNfs",
                        "Could not convert DDS:\n{0}", _uiLanguage), ex.Message),
                    UiLocalization.TranslateId("export.textureTitle", "Export DDS textures", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
