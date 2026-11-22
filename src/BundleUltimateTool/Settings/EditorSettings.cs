using System.Collections.Generic;

namespace BundleUltimateTool.Settings
{
    // Persisted UI preferences: theme, language and recent bundles.
    internal sealed class EditorSettings
    {
        internal bool DarkTheme { get; set; } = true;
        internal string UiLanguage { get; set; }
        internal List<string> RecentFiles { get; } = new();
    }
}
