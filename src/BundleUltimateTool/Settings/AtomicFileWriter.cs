using System;
using System.IO;

namespace BundleUltimateTool.Settings
{
    internal static class AtomicFileWriter
    {
        internal static void Write(string path, Action<Stream> write)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(write);
            string destination = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(destination),
                "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownsTemporary = false;
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.ReadWrite, FileShare.None))
                {
                    ownsTemporary = true;
                    write(output);
                    output.Flush(flushToDisk: true);
                }
                // Same-directory replacement. Do not fall back to deleting/truncating
                // the destination if replacement is denied or unsupported.
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                // Only our unique temporary file is eligible for cleanup.
                try { if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
