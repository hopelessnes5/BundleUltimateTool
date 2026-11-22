using System;
using System.Globalization;
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
            string languageCode = _uiLanguage;
            try
            {
                UiLocalization.ApplyControlTree(this, languageCode);
                if (treeContextMenu != null) UiLocalization.ApplyToolStripItems(treeContextMenu.Items, languageCode);
                if (gameFileContextMenu != null) UiLocalization.ApplyToolStripItems(gameFileContextMenu.Items, languageCode);

                LocalizeWorkspaceStates();
                PopulateTypeFilter();
                PopulateSortOptions();
                UpdateLanguageMenuChecks();

                if (searchTextBox != null)
                    searchTextBox.ToolTipText = UiLocalization.TranslateId("hint.search", "Search by name (Ctrl+F)", languageCode);
                if (sortComboBox != null)
                    sortComboBox.ToolTipText = UiLocalization.TranslateId("hint.sort", "Sort tree items", languageCode);
                if (typeFilterComboBox != null)
                    typeFilterComboBox.ToolTipText = UiLocalization.TranslateId("hint.typeFilter", "Filter by type", languageCode);

                if (defsGrid != null && defsGrid.Columns.Count >= 3)
                {
                    defsGrid.Columns[0].HeaderText = UiLocalization.TranslateId("defs.col.fieldId", "Field ID", languageCode);
                    defsGrid.Columns[1].HeaderText = UiLocalization.TranslateId("defs.col.offset", "Offset", languageCode);
                    defsGrid.Columns[2].HeaderText = UiLocalization.TranslateId("defs.col.type", "Type", languageCode);
                }
                UpdateBundleStatus();
                if (_filesMode) UpdateGameStatus();
            }
            finally { _applyingUiLocalization = false; }

            ApplyCommandChrome();
            SaveSettings();
        }

        private void UpdateLanguageMenuChecks()
        {
            if (languageRuItem == null) return;
            languageRuItem.Checked = string.Equals(_uiLanguage, "ru", StringComparison.OrdinalIgnoreCase);
            languageEnItem.Checked = !languageRuItem.Checked;
        }

        private void PopulateTypeFilter() => RefreshTypeFilterOptions();

        // Copied from PrototypeUltimateTool:MainForm.Localization.cs (sort options pattern).
        private void PopulateSortOptions()
        {
            if (sortComboBox == null) return;
            string[] fallbacks =
            {
                "По умолчанию", "По имени (А → Я)", "По имени (Я → А)",
                "По размеру (убыв.)", "По размеру (возр.)",
                "По разрешению (убыв.)", "По разрешению (возр.)", "По типу"
            };
            string[] keys =
            {
                "sort.default", "sort.nameAsc", "sort.nameDesc", "sort.sizeDesc",
                "sort.sizeAsc", "sort.resolutionDesc", "sort.resolutionAsc", "sort.type"
            };
            int selectedIndex = sortComboBox.SelectedIndex;
            sortComboBox.BeginUpdate();
            try
            {
                sortComboBox.Items.Clear();
                for (int i = 0; i < fallbacks.Length; i++)
                    sortComboBox.Items.Add(UiLocalization.TranslateId(keys[i], fallbacks[i], _uiLanguage));
                sortComboBox.SelectedIndex = selectedIndex >= 0 && selectedIndex < fallbacks.Length ? selectedIndex : 0;
            }
            finally { sortComboBox.EndUpdate(); }
        }

        private string FormatLocalizedText(string textKey, string fallbackFormat, params object[] formatArguments)
        {
            string format = UiLocalization.TranslateId(textKey, fallbackFormat, _uiLanguage);
            if (formatArguments == null || formatArguments.Length == 0) return format;
            try { return string.Format(CultureInfo.CurrentCulture, format, formatArguments); }
            catch (FormatException)
            {
                try { return string.Format(CultureInfo.CurrentCulture, fallbackFormat ?? string.Empty, formatArguments); }
                catch (FormatException) { return format; }
            }
        }

        private DialogResult ShowLocalizedMessageBoxById(IWin32Window owner, string textKey, string fallbackText,
            string captionKey, string fallbackCaption, MessageBoxButtons buttons, MessageBoxIcon icon, params object[] formatArguments)
        {
            string text = FormatLocalizedText(textKey, fallbackText, formatArguments);
            string caption = UiLocalization.TranslateId(captionKey, fallbackCaption, _uiLanguage);
            return owner != null
                ? MessageBox.Show(owner, text, caption, buttons, icon)
                : MessageBox.Show(text, caption, buttons, icon);
        }
    }
}
