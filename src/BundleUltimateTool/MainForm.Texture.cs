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

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private Bitmap _previewBitmap;

        private NfsTexture TryLoadTexture(BundleEntry entry, out string error)
        {
            error = null;
            try
            {
                byte[] headerBytes = ZLibAdapter.Decompress(
                    _document.Reader.ReadBytes(entry.AbsoluteHeaderOffset, (int)entry.Raw.HdrCompSize),
                    (int)entry.MaskedHdrUncompSize);
                byte[] imageBytes = entry.Raw.DataCompSize > 0
                    ? ZLibAdapter.Decompress(
                        _document.Reader.ReadBytes(entry.AbsoluteBodyOffset, (int)entry.Raw.DataCompSize),
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
            previewBox.Image = null;

            if (texture == null)
            {
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
                _previewBitmap = CreateBitmap(texture, texture.Decompress());
                previewBox.Image = _previewBitmap;
                previewMessageLabel.Visible = false;
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("PREVIEW_FAILED", ex.Message, ex);
                previewMessageLabel.Text = string.Format(
                    UiLocalization.TranslateId("preview.failed", "Cannot preview texture: {0}", _uiLanguage),
                    ex.Message);
                previewMessageLabel.Visible = true;
            }
        }

        private static Bitmap CreateBitmap(NfsTexture texture, byte[] pixels)
        {
            int width = texture.GetWidth();
            int height = texture.GetHeight();
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] rgba = pixels;
                if (texture.GetNumComponents() == 3)
                {
                    rgba = new byte[width * height * 4];
                    for (int i = 0; i < width * height; i++)
                    {
                        rgba[i * 4] = pixels[i * 3];
                        rgba[i * 4 + 1] = pixels[i * 3 + 1];
                        rgba[i * 4 + 2] = pixels[i * 3 + 2];
                        rgba[i * 4 + 3] = 255;
                    }
                }
                for (int y = 0; y < height; y++)
                    Marshal.Copy(rgba, y * width * 4, IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        private void ExportTextures(bool selectedOnly)
        {
            if (_document == null) return;
            var textures = new List<BundleEntry>(_document.Entries
                .Where(entry => entry.FileType == (uint)NfsFileType.Texture));
            if (selectedOnly)
            {
                var selectedIndices = nodeTreeView.SelectedNodes.OfType<TreeNode>()
                    .Select(node => node.Tag).OfType<int>().ToHashSet();
                textures.RemoveAll(entry => !selectedIndices.Contains(entry.Index));
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
            foreach (BundleEntry entry in textures)
            {
                string fileName = $"file_{entry.Index:D4}.dds";
                try
                {
                    NfsTexture texture = TryLoadTexture(entry, out string error);
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
