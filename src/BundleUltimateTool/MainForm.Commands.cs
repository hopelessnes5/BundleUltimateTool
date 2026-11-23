using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BundleFormat.Bundle;
using BundleUltimateTool.Commands;
using BundleUltimateTool.Documents;
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
                [EditorDocumentCommandId.ExportUsdSelected] = ExportUsdSelected,
                [EditorDocumentCommandId.ExportAuto] = () =>
                {
                    // Selected archive root -> export that archive only; selected entries ->
                    // export them; nothing selected -> export everything.
                    BundleDocument selectedArchive = nodeTreeView?.SelectedNode?.Tag as BundleDocument;
                    bool selectedEntries = selectedArchive == null && GetSelectedViews().Count > 0;
                    ExportAuto(selectedEntries, selectedArchive);
                },
                [EditorDocumentCommandId.SaveStrings] = SaveStrings,
                [EditorDocumentCommandId.ViewDefs] = ViewDefs,
                [EditorDocumentCommandId.AssembleBundle] = ShowAssembler,
                [EditorDocumentCommandId.SelectGameFolder] = SelectGameFolder,
                [EditorDocumentCommandId.FolderUp] = () => NavigateGameUp(),
                [EditorDocumentCommandId.OpenFromFolder] = () => OpenFromFolder(),
                [EditorDocumentCommandId.CopyPath] = () => CopySelectedPaths()
            }, CanExecuteCommand);

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
            ToolStripCommandBinding.Bind(exportObjSelectedContextItem, _commands[EditorDocumentCommandId.ExportUsdSelected]);
            ToolStripCommandBinding.Bind(exportObjAllContextItem, _commands[EditorDocumentCommandId.ExportObjAll]);
            ToolStripCommandBinding.Bind(toolsExtractItem, _commands[EditorDocumentCommandId.ExportAuto]);
            ToolStripCommandBinding.Bind(saveStringsContextItem, _commands[EditorDocumentCommandId.SaveStrings]);
            ToolStripCommandBinding.Bind(viewDefsContextItem, _commands[EditorDocumentCommandId.ViewDefs]);
            ToolStripCommandBinding.Bind(toolsAssembleItem, _commands[EditorDocumentCommandId.AssembleBundle]);
            ToolStripCommandBinding.Bind(selectGameFolderMenuItem, _commands[EditorDocumentCommandId.SelectGameFolder]);
            ToolStripCommandBinding.Bind(selectGameFolderButton, _commands[EditorDocumentCommandId.SelectGameFolder]);
            ToolStripCommandBinding.Bind(folderUpButton, _commands[EditorDocumentCommandId.FolderUp]);
            ToolStripCommandBinding.Bind(openFromFolderItem, _commands[EditorDocumentCommandId.OpenFromFolder]);
            ToolStripCommandBinding.Bind(copyPathItem, _commands[EditorDocumentCommandId.CopyPath]);

            if (treeContextMenu != null)
                treeContextMenu.Opening += (s, e) => UpdateTreeContextMenuState();
            if (gameFileContextMenu != null)
                gameFileContextMenu.Opening += (s, e) => _commands?.Refresh();

            aboutMenuItem.Click += (s, e) => OnAbout();
        }

        // Command enablement pattern ported from PrototypeUltimateTool MainForm.Commands.cs:
        // central state evaluation, refreshed on document/selection changes.
        private bool CanExecuteCommand(EditorDocumentCommandId id) => id switch
        {
            EditorDocumentCommandId.ExportRawSelected =>
                !_filesMode && GetSelectedViews().Count > 0,
            EditorDocumentCommandId.ExportRawAll =>
                !_filesMode && _openDocuments.Count > 0,
            EditorDocumentCommandId.ExportTextureSelected =>
                !_filesMode && GetSelectedViews().Any(view => NfsFileTypeInfo.SupportsTextureExport(view.Entry.FileType)),
            EditorDocumentCommandId.ExportTextureAll =>
                !_filesMode && _openDocuments.Count > 0,
            EditorDocumentCommandId.ExportObjSelected =>
                !_filesMode && GetSelectedViews().Any(view => NfsFileTypeInfo.SupportsModelExport(view.Entry.FileType)),
            EditorDocumentCommandId.ExportObjAll =>
                !_filesMode && _openDocuments.Count > 0,
            EditorDocumentCommandId.ExportUsdSelected =>
                !_filesMode && GetSelectedViews().Any(view => NfsFileTypeInfo.SupportsModelExport(view.Entry.FileType)),
            EditorDocumentCommandId.ExportAuto =>
                !_filesMode && _openDocuments.Count > 0,
            EditorDocumentCommandId.SaveStrings =>
                !_filesMode && _openDocuments.Count > 0,
            EditorDocumentCommandId.ViewDefs =>
                !_filesMode && _openDocuments.Count > 0,
            EditorDocumentCommandId.FolderUp => _filesMode && _gameFolderModel.CurrentRelativePath.Length > 0,
            EditorDocumentCommandId.OpenFromFolder or EditorDocumentCommandId.CopyPath =>
                _filesMode && gameFileTreeView.SelectedNode?.Tag is GameFileEntry,
            _ => true
        };

        private void UpdateTreeContextMenuState()
        {
            _commands?.Refresh();
            var selected = nodeTreeView == null || _filesMode
                ? new List<BundleEntryView>()
                : nodeTreeView.SelectedNodes
                    .Select(node => node?.Tag)
                    .OfType<BundleEntryView>()
                    .ToList();

            bool any = selected.Count > 0;
            bool anyTexture = selected.Any(view => NfsFileTypeInfo.SupportsTextureExport(view.Entry.FileType));
            bool anyModel = selected.Any(view => NfsFileTypeInfo.SupportsModelExport(view.Entry.FileType));
            bool anyStrings = selected.Any(view => NfsFileTypeInfo.SupportsStringsSave(view.Entry.FileType));
            bool anyDefs = selected.Any(view => NfsFileTypeInfo.SupportsDefsView(view.Entry.FileType));

            // With a selection, show ONLY actions valid for that selection. A texture
            // selection must not offer geometry export, and vice versa.
            void SetItem(ToolStripItem item, bool visible, bool enabled)
            {
                if (item == null) return;
                item.Visible = visible;
                item.Enabled = enabled;
            }

            SetItem(exportRawSelectedContextItem, any, any && CanExecuteCommand(EditorDocumentCommandId.ExportRawSelected));
            SetItem(exportRawAllContextItem, !any, !any && CanExecuteCommand(EditorDocumentCommandId.ExportRawAll));
            SetItem(exportTextureSelectedContextItem, anyTexture,
                anyTexture && CanExecuteCommand(EditorDocumentCommandId.ExportTextureSelected));
            SetItem(exportTextureAllContextItem, !any, !any && CanExecuteCommand(EditorDocumentCommandId.ExportTextureAll));
            SetItem(exportObjSelectedContextItem, anyModel,
                anyModel && CanExecuteCommand(EditorDocumentCommandId.ExportUsdSelected));
            SetItem(exportObjAllContextItem, !any, !any && CanExecuteCommand(EditorDocumentCommandId.ExportObjAll));
            SetItem(saveStringsContextItem, anyStrings, anyStrings && CanExecuteCommand(EditorDocumentCommandId.SaveStrings));
            SetItem(viewDefsContextItem, anyDefs, anyDefs && CanExecuteCommand(EditorDocumentCommandId.ViewDefs));
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
