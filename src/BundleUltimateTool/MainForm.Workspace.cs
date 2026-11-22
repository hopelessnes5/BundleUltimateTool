using System.Windows.Forms;
using BundleUltimateTool.Commands;
using BundleUltimateTool.Documents;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private EditorEmptyStatePanel _emptyDocument;
        private Label _emptyProperties;

        private void InitializeWorkspaceStates(Panel viewPanel, Panel propertyBodyPanel)
        {
            _emptyProperties = new Label
            {
                Name = "workspace.properties.empty",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding = new Padding(8)
            };
            propertyBodyPanel.Controls.Add(_emptyProperties);

            // Sibling of the preview container, never an overlay inside the viewer.
            _emptyDocument = new EditorEmptyStatePanel(
                () => _commands[EditorDocumentCommandId.Open].Execute(null));
            viewPanel.Controls.Add(_emptyDocument);
            _emptyDocument.BringToFront();

            propGrid.SelectedObjectsChanged += (s, e) => UpdateWorkspaceState();
        }

        private void UpdateWorkspaceState()
        {
            if (_emptyDocument == null || IsDisposed) return;
            bool hasProperties = propGrid.SelectedObjects != null && propGrid.SelectedObjects.Length > 0;

            if (_filesMode)
            {
                _emptyDocument.Visible = false;
                if (!_gameFolderModel.HasRoot && noResultsPanel != null) noResultsPanel.Visible = false;
                previewBox.Visible = false;
                filePreviewTextBox.Visible = hasProperties && propGrid.SelectedObject is GameFileProperties;
                propGrid.Visible = hasProperties;
                _emptyProperties.Visible = !hasProperties;
                if (!hasProperties) _emptyProperties.BringToFront();
                typeFilterComboBox.Enabled = _gameFolderModel.HasRoot;
                return;
            }

            bool hasDocument = _document != null;
            if (!hasDocument && noResultsPanel != null) noResultsPanel.Visible = false;
            _emptyDocument.Visible = !hasDocument;
            previewBox.Visible = hasDocument;
            if (!hasDocument) _emptyDocument.BringToFront();
            filePreviewTextBox.Visible = false;

            propGrid.Visible = hasProperties;
            _emptyProperties.Visible = !hasProperties;
            if (!hasProperties) _emptyProperties.BringToFront();

            typeFilterComboBox.Enabled = hasDocument;
        }

        private void LocalizeWorkspaceStates()
        {
            if (_emptyDocument == null) return;
            _emptyDocument.SetText(
                UiLocalization.TranslateId("workspace.empty.title", "Open a bundle", _uiLanguage),
                UiLocalization.TranslateId("workspace.empty.description",
                    "Open a .bundle / .bndl file or drop it onto the editor window. Shortcut: Ctrl+O.", _uiLanguage),
                UiLocalization.TranslateId("workspace.empty.open", "Open bundle...", _uiLanguage));
            _emptyProperties.Text = UiLocalization.TranslateId("workspace.properties.empty",
                "Select an entry in the tree to see its properties.", _uiLanguage);
            if (noResultsLabel != null)
            {
                noResultsLabel.Text = UiLocalization.TranslateId("filter.noResults",
                    "No entries match the filter", _uiLanguage);
                clearFilterButton.Text = UiLocalization.TranslateId("filter.clear", "Clear filter", _uiLanguage);
            }
            UpdateWorkspaceState();
        }

        private void ApplyWorkspacePalette(EditorPalette palette)
        {
            if (_emptyDocument == null) return;
            _emptyDocument.ApplyPalette(palette);
            _emptyProperties.BackColor = palette.Surface;
            _emptyProperties.ForeColor = palette.SecondaryText;
        }
    }
}
