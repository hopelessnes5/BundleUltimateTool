using BundleFormat.Bundle;

namespace BundleUltimateTool.Documents;

/// <summary>Binds a tree node to the document that owns the entry (multi-document support).</summary>
public sealed class BundleEntryView
{
    public BundleEntryView(BundleDocument document, BundleEntry entry)
    {
        Document = document;
        Entry = entry;
    }

    public BundleDocument Document { get; }
    public BundleEntry Entry { get; }
}
