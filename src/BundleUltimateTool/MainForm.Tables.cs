using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleFormat.Compression;
using BundleFormat.Primitives;
using BundleFormat.Tables;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private BundleFileInfo TryGetFileInfo(BundleEntry entry, out string error)
        {
            error = null;
            try
            {
                byte[] packedHeader = _document.Reader.ReadBytes(
                    entry.AbsoluteHeaderOffset, (int)entry.Raw.HdrCompSize);
                byte[] packedBody = entry.Raw.DataCompSize > 0
                    ? _document.Reader.ReadBytes(entry.AbsoluteBodyOffset, (int)entry.Raw.DataCompSize)
                    : Array.Empty<byte>();
                return new BundleFileInfo(entry, packedHeader, packedBody);
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("FILEINFO_FAILED", $"entry {entry.Index}: {ex.Message}", ex);
                error = ex.Message;
                return null;
            }
        }

        private byte[] GetDecompressedHeader(BundleEntry entry)
        {
            byte[] packed = _document.Reader.ReadBytes(
                entry.AbsoluteHeaderOffset, (int)entry.Raw.HdrCompSize);
            return ZLibAdapter.Decompress(packed, (int)entry.MaskedHdrUncompSize);
        }

        private BundleEntry GetSelectedEntry()
        {
            if (_document == null || nodeTreeView.SelectedNode?.Tag is not int index ||
                index < 0 || index >= _document.Entries.Count) return null;
            return _document.Entries[index];
        }

        private void SaveStrings()
        {
            BundleEntry entry = GetSelectedEntry();
            if (entry == null || entry.FileType != (uint)NfsFileType.StringsTable) return;
            try
            {
                NfsStringsTable table = NfsStringsTable.Parse(GetDecompressedHeader(entry));
                using var dialog = new SaveFileDialog
                {
                    Title = UiLocalization.TranslateId("dialog.strings.save", "Save strings table", _uiLanguage),
                    Filter = UiLocalization.TranslateId("dialog.strings.filter", "Text files (*.txt)|*.txt", _uiLanguage),
                    FileName = $"file_{entry.Index:D4}_strings.txt",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                table.SaveAsTxtFile(dialog.FileName);
                string message = string.Format(
                    UiLocalization.TranslateId("strings.saved", "Saved {0} strings.", _uiLanguage), table.Strings.Count);
                SetStatus(message);
                ApplicationLog.Write("STRINGS_SAVED", $"{dialog.FileName}: {table.Strings.Count} strings.");
                MessageBox.Show(this, message,
                    UiLocalization.TranslateId("strings.title", "Strings table", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("STRINGS_FAILED", $"entry {entry.Index}: {ex.Message}", ex);
                MessageBox.Show(this,
                    string.Format(UiLocalization.TranslateId("strings.failed",
                        "Could not save strings:\n{0}", _uiLanguage), ex.Message),
                    UiLocalization.TranslateId("strings.title", "Strings table", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ViewDefs()
        {
            BundleEntry entry = GetSelectedEntry();
            if (entry == null || entry.FileType != (uint)NfsFileType.DefsTable) return;
            try
            {
                NfsDefsTable table = NfsDefsTable.Parse(GetDecompressedHeader(entry));
                defsGrid.Rows.Clear();
                foreach (NfsDefsTable.DefEntry def in table.Entries)
                    defsGrid.Rows.Add(def.DefIdHex, def.Offset.ToString(), def.Type.ToString());
                defsNameLabel.Text = string.Format(
                    UiLocalization.TranslateId("defs.name", "Defs: {0} ({1} entries)", _uiLanguage),
                    table.DefName, table.Count);
                defsPanel.Visible = true;
                defsPanel.BringToFront();
                ApplicationLog.Write("DEFS_VIEWED", $"entry {entry.Index}: '{table.DefName}', {table.Count} entries.");
            }
            catch (Exception ex)
            {
                ClearDefsView();
                ApplicationLog.Write("DEFS_FAILED", $"entry {entry.Index}: {ex.Message}", ex);
                MessageBox.Show(this,
                    string.Format(UiLocalization.TranslateId("defs.failed",
                        "Could not read defs table:\n{0}", _uiLanguage), ex.Message),
                    UiLocalization.TranslateId("defs.title", "Defs table", _uiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearDefsView()
        {
            if (defsPanel == null) return;
            defsPanel.Visible = false;
            defsGrid.Rows.Clear();
            defsNameLabel.Text = string.Empty;
        }
    }
}
