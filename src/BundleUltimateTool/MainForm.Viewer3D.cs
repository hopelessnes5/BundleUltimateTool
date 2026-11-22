using System.Windows.Forms;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        /// <summary>Switches the single preview area between 2D/file content and the DX12 model view.</summary>
        private void SetPreviewMode(bool model3D)
        {
            if (model3DView == null) return;
            if (!model3DView.Visible) UpdateWorkspaceState();
            mode2DButton.Checked = !model3D;
            mode3DButton.Checked = model3D;
            model3DView.Visible = model3D;
            previewBox.Visible = !model3D && _document != null;
            previewScrollPanel.Visible = !model3D;
            if (channelToolStrip != null) channelToolStrip.Visible = !model3D && channelToolStrip.Visible && _previewTextureSource != null;
            filePreviewTextBox.Visible = !model3D && filePreviewTextBox.Visible;
            if (model3D)
            {
                previewMessageLabel.Visible = false;
                model3DView.BringToFront();
                model3DView.Invalidate();
            }
            if (model3D && !model3DView.HasModel) model3DView.ClearModel();
            UpdateWorkspaceState();
        }

        private void SetModel3DStatus()
        {
            SetStatus(UiLocalization.TranslateId("status.model3dHint",
                "Drag to rotate, wheel to zoom", _uiLanguage));
        }
    }
}
