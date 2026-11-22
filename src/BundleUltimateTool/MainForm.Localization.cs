using System;
using System.Windows.Forms;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private void SetUiLanguage(string language)
        {
            string normalized = UiLocalization.NormalizeLanguageCode(language);
            if (string.Equals(normalized, _uiLanguage, StringComparison.OrdinalIgnoreCase)) return;
            _uiLanguage = normalized;
            UpdateLanguageMenuChecks();
            SaveSettings();
            if (IsDisposed) return;
            if (IsHandleCreated)
            {
                try { BeginInvoke((MethodInvoker)(() => { if (!IsDisposed) ApplyLocalizationToUi(); })); }
                catch (InvalidOperationException) { ApplyLocalizationToUi(); }
            }
            else ApplyLocalizationToUi();
        }

        private void ApplyLocalizationToUi()
        {
            if (_applyingUiLocalization || IsDisposed) return;
            _applyingUiLocalization = true;
            try
            {
                UiLocalization.ApplyControlTree(this, _uiLanguage);
                LocalizeWorkspaceStates();
                PopulateTypeFilter();
                UpdateLanguageMenuChecks();
                if (defsGrid != null && defsGrid.Columns.Count >= 3)
                {
                    defsGrid.Columns[0].HeaderText = UiLocalization.TranslateId("defs.col.fieldId", "Field ID", _uiLanguage);
                    defsGrid.Columns[1].HeaderText = UiLocalization.TranslateId("defs.col.offset", "Offset", _uiLanguage);
                    defsGrid.Columns[2].HeaderText = UiLocalization.TranslateId("defs.col.type", "Type", _uiLanguage);
                }
                UpdateBundleStatus();
                if (_filesMode) UpdateGameStatus();
            }
            finally { _applyingUiLocalization = false; }
        }

        private void UpdateLanguageMenuChecks()
        {
            if (languageRuItem == null) return;
            languageRuItem.Checked = string.Equals(_uiLanguage, "ru", StringComparison.OrdinalIgnoreCase);
            languageEnItem.Checked = !languageRuItem.Checked;
        }

        private void PopulateTypeFilter() => RefreshTypeFilterOptions();
    }
}
