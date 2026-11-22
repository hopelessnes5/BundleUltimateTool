using System;
using System.IO;
using System.Text;

namespace BundleUltimateTool.Settings
{
    // Adapted from PrototypeUltimateTool: stable keys, Base64 UTF-8 paths, ten-item limit.
    internal sealed class EditorSettingsStore
    {
        internal EditorSettings Load(string path, EditorSettings settings)
        {
            if (!File.Exists(path)) return settings;
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (key == "DarkTheme" && bool.TryParse(value, out bool dark)) settings.DarkTheme = dark;
                else if (key.Equals("UiLanguage", StringComparison.OrdinalIgnoreCase)) settings.UiLanguage = value;
                else if (key.Equals("GameFolderPath", StringComparison.OrdinalIgnoreCase)) settings.GameFolderPath = value;
                else if (key.StartsWith("RecentFile", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string recent = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                        if (!string.IsNullOrWhiteSpace(recent)) settings.RecentFiles.Add(recent);
                    }
                    catch (FormatException) { /* Ignore malformed hand-edited recent entries. */ }
                }
            }
            return settings;
        }

        internal void Save(string path, EditorSettings settings)
        {
            var content = new StringBuilder();
            content.Append("DarkTheme=").Append(settings.DarkTheme).Append('\n');
            content.Append("UiLanguage=").Append(settings.UiLanguage ?? string.Empty).Append('\n');
            content.Append("GameFolderPath=").Append(settings.GameFolderPath ?? string.Empty).Append('\n');
            for (int i = 0; i < Math.Min(RecentFileHistory.Capacity, settings.RecentFiles.Count); i++)
            {
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.RecentFiles[i]));
                content.Append("RecentFile").Append(i).Append('=').Append(encoded).Append('\n');
            }
            AtomicFileWriter.Write(path, output =>
            {
                using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
                writer.Write(content.ToString());
            });
        }
    }
}
