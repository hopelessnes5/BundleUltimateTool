using System.IO;

namespace BundleFormat.Primitives;

/// <summary>Thrown when bundle bytes do not match the expected on-disk layout.</summary>
public sealed class BundleFormatException : IOException
{
    public BundleFormatException(string message) : base(message) { }
    public BundleFormatException(string message, Exception inner) : base(message, inner) { }
}
