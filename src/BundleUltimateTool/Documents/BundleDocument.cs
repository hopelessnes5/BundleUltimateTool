using System;
using System.IO;
using BundleFormat.Bundle;

namespace BundleUltimateTool.Documents;

/// <summary>UI-side document holder. No dialogs and no WinForms controls.</summary>
public sealed class BundleDocument : IDisposable
{
    public BundleDocument(BundleReader reader, string path)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string Path { get; }
    public BundleReader Reader { get; }
    public BundleHeader Header => Reader.Header;
    public System.Collections.Generic.IReadOnlyList<BundleEntry> Entries => Reader.Entries;
    public string DisplayName => System.IO.Path.GetFileName(Path);
    public bool HasEntries => Entries.Count > 0;

    public void Dispose() => Reader.Dispose();
}
