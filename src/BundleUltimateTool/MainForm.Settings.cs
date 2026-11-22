using System;
using System.IO;
using BundleUltimateTool.Settings;

namespace BundleUltimateTool
{
    public partial class MainForm
    {
        private static string GetConfigPath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");

        private EditorSettings CaptureSettings()
        {
            var settings = new EditorSettings
            {
                DarkTheme = _isDarkTheme,
                UiLanguage = _uiLanguage
            };
            settings.RecentFiles.AddRange(_recentFiles.Snapshot());
            return settings;
        }

        private void SaveSettings()
        {
            try { _settingsStore.Save(GetConfigPath(), CaptureSettings()); }
            catch { /* Preferences must not block editing or shutdown. */ }
        }

        private void LoadSettings()
        {
            try
            {
                EditorSettings settings = _settingsStore.Load(GetConfigPath(), CaptureSettings());
                _isDarkTheme = settings.DarkTheme;
                _uiLanguage = UiLocalization.NormalizeLanguageCode(settings.UiLanguage);
                _recentFiles.Restore(settings.RecentFiles);
            }
            catch { /* Preserve defaults when preferences cannot be read. */ }
        }
    }
}
