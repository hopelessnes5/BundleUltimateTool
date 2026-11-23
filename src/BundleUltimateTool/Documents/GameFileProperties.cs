using System.ComponentModel;
using BundleFormat.IO;

namespace BundleUltimateTool.Documents;

/// <summary>Read-only PropertyGrid view for a file/folder node in game-folder mode.</summary>
public sealed class GameFileProperties
{
    private readonly GameFileEntry _entry;

    public GameFileProperties(GameFileEntry entry) => _entry = entry;

    [DisplayName("Name")]
    public string Name => _entry.Name;

    [DisplayName("Full path")]
    public string FullPath => _entry.FullPath;

    [DisplayName("Extension")]
    public string Extension => _entry.IsDirectory ? "—" : _entry.Extension;

    [DisplayName("Size")]
    public string Size => _entry.IsDirectory ? "—" : GamePathRules.FormatSize(_entry.Length);

    [DisplayName("Modified UTC")]
    public string ModifiedUtc => _entry.LastWriteTimeUtc == System.DateTime.MinValue
        ? "—" : _entry.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss");

    [DisplayName("Is bundle")]
    public bool IsBundle => _entry.IsBundle;

    [DisplayName("Is folder")]
    public bool IsFolder => _entry.IsDirectory;

    [DisplayName("Vehicle name")]
    public string VehicleName => string.IsNullOrWhiteSpace(_entry.VehicleName) ? "—" : _entry.VehicleName;
}
