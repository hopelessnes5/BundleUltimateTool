using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleUltimateTool.Commands;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private void InitializeCommands()
        {
            _commands = new EditorDocumentCommands(new Dictionary<EditorDocumentCommandId, Action>
            {
                [EditorDocumentCommandId.Open] = OnOpenDialog,
                [EditorDocumentCommandId.Exit] = () => Application.Exit(),
                [EditorDocumentCommandId.ToggleTheme] = ToggleTheme,
                [EditorDocumentCommandId.LanguageRussian] = () => SetUiLanguage("ru"),
                [EditorDocumentCommandId.LanguageEnglish] = () => SetUiLanguage("en"),
                [EditorDocumentCommandId.ExportRawSelected] = () => ExportRaw(selectedOnly: true),
                [EditorDocumentCommandId.ExportRawAll] = () => ExportRaw(selectedOnly: false),
                [EditorDocumentCommandId.ExportTextureSelected] = () => ExportTextures(selectedOnly: true),
                [EditorDocumentCommandId.ExportTextureAll] = () => ExportTextures(selectedOnly: false),
                [EditorDocumentCommandId.DdsToNfs] = ConvertDdsToNfs,
                [EditorDocumentCommandId.ExportObjSelected] = () => ExportObj(selectedOnly: true),
                [EditorDocumentCommandId.ExportObjAll] = () => ExportObj(selectedOnly: false),
                [EditorDocumentCommandId.SaveStrings] = SaveStrings,
                [EditorDocumentCommandId.ViewDefs] = ViewDefs,
                [EditorDocumentCommandId.AssembleBundle] = ShowAssembler
            }, id => id switch
            {
                EditorDocumentCommandId.ExportRawSelected or EditorDocumentCommandId.ExportRawAll => _document != null,
                EditorDocumentCommandId.ExportTextureSelected or EditorDocumentCommandId.ExportTextureAll =>
                    _document != null && _document.Entries.Any(e => e.FileType == (uint)NfsFileType.Texture),
                EditorDocumentCommandId.ExportObjSelected or EditorDocumentCommandId.ExportObjAll =>
                    _document != null && _document.Entries.Any(e => e.FileType == (uint)NfsFileType.Model),
                EditorDocumentCommandId.SaveStrings =>
                    _document != null && _document.Entries.Any(e => e.FileType == (uint)NfsFileType.StringsTable),
                EditorDocumentCommandId.ViewDefs =>
                    _document != null && _document.Entries.Any(e => e.FileType == (uint)NfsFileType.DefsTable),
                _ => true
            });

            ToolStripCommandBinding.Bind(openMenuItem, _commands[EditorDocumentCommandId.Open]);
            ToolStripCommandBinding.Bind(openButton, _commands[EditorDocumentCommandId.Open]);
            ToolStripCommandBinding.Bind(exitMenuItem, _commands[EditorDocumentCommandId.Exit]);
            ToolStripCommandBinding.Bind(themeMenuItem, _commands[EditorDocumentCommandId.ToggleTheme]);
            ToolStripCommandBinding.Bind(languageRuItem, _commands[EditorDocumentCommandId.LanguageRussian]);
            ToolStripCommandBinding.Bind(languageEnItem, _commands[EditorDocumentCommandId.LanguageEnglish]);

            ToolStripCommandBinding.Bind(exportRawSelectedMenuItem, _commands[EditorDocumentCommandId.ExportRawSelected]);
            ToolStripCommandBinding.Bind(exportRawAllMenuItem, _commands[EditorDocumentCommandId.ExportRawAll]);
            ToolStripCommandBinding.Bind(exportRawButton, _commands[EditorDocumentCommandId.ExportRawSelected]);
            ToolStripCommandBinding.Bind(exportRawSelectedContextItem, _commands[EditorDocumentCommandId.ExportRawSelected]);
            ToolStripCommandBinding.Bind(exportRawAllContextItem, _commands[EditorDocumentCommandId.ExportRawAll]);
            ToolStripCommandBinding.Bind(exportTextureSelectedMenuItem, _commands[EditorDocumentCommandId.ExportTextureSelected]);
            ToolStripCommandBinding.Bind(exportTextureAllMenuItem, _commands[EditorDocumentCommandId.ExportTextureAll]);
            ToolStripCommandBinding.Bind(exportTextureSelectedContextItem, _commands[EditorDocumentCommandId.ExportTextureSelected]);
            ToolStripCommandBinding.Bind(exportTextureAllContextItem, _commands[EditorDocumentCommandId.ExportTextureAll]);
            ToolStripCommandBinding.Bind(ddsToNfsMenuItem, _commands[EditorDocumentCommandId.DdsToNfs]);
            ToolStripCommandBinding.Bind(exportObjSelectedMenuItem, _commands[EditorDocumentCommandId.ExportObjSelected]);
            ToolStripCommandBinding.Bind(exportObjAllMenuItem, _commands[EditorDocumentCommandId.ExportObjAll]);
            ToolStripCommandBinding.Bind(exportObjSelectedContextItem, _commands[EditorDocumentCommandId.ExportObjSelected]);
            ToolStripCommandBinding.Bind(exportObjAllContextItem, _commands[EditorDocumentCommandId.ExportObjAll]);
            ToolStripCommandBinding.Bind(saveStringsContextItem, _commands[EditorDocumentCommandId.SaveStrings]);
            ToolStripCommandBinding.Bind(viewDefsContextItem, _commands[EditorDocumentCommandId.ViewDefs]);
            ToolStripCommandBinding.Bind(toolsAssembleItem, _commands[EditorDocumentCommandId.AssembleBundle]);

            aboutMenuItem.Click += (s, e) => OnAbout();
        }

        private void OnOpenDialog()
        {
            using var dialog = new OpenFileDialog
            {
                Title = UiLocalization.TranslateId("dialog.open.title", "Open bundle", _uiLanguage),
                Filter = UiLocalization.TranslateId("dialog.open.filter",
                    "Bundle files (*.bundle;*.bndl;*.bin)|*.bundle;*.bndl;*.bin|All files (*.*)|*.*", _uiLanguage),
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) OpenBundle(dialog.FileName);
        }

        private void ShowAssembler()
        {
            using var form = new BundleAssemblerForm(_isDarkTheme, _uiLanguage, path => OpenBundle(path));
            form.ShowDialog(this);
        }

        private void OnAbout()
        {
            MessageBox.Show(
                UiLocalization.TranslateId("about.message",
                    "Bundle Ultimate Tool - bundle explorer, extractor and assembler for\n" +
                    "Need for Speed: Hot Pursuit (2010) / Burnout Paradise (.bundle, .bndl).", _uiLanguage),
                UiLocalization.TranslateId("about.title", "About Bundle Ultimate Tool", _uiLanguage),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
