using System.Collections.Generic;

namespace BundleUltimateTool.Settings
{
    internal sealed class RecentFileHistory
    {
        internal const int Capacity = 10;
        private readonly List<string> _paths = new();
        internal int Count => _paths.Count;
        internal string[] Snapshot() => _paths.ToArray();

        internal bool Record(string path)
        {
            if (!FilePathIdentity.TryNormalize(path, out string fullPath)) return false;
            Remove(fullPath);
            _paths.Insert(0, fullPath);
            if (_paths.Count > Capacity) _paths.RemoveRange(Capacity, _paths.Count - Capacity);
            return true;
        }

        internal bool Remove(string path) => _paths.RemoveAll(existing => FilePathIdentity.AreSame(existing, path)) > 0;

        // Restore saved order rather than replaying Record, which would reverse it.
        // Keep the original spelling for menu/tooltips while ignoring malformed paths.
        internal void Restore(IEnumerable<string> paths)
        {
            var restored = new List<string>();
            foreach (string path in paths)
            {
                if (!FilePathIdentity.TryNormalize(path, out _) || restored.Exists(existing => FilePathIdentity.AreSame(existing, path)))
                    continue;
                restored.Add(path);
                if (restored.Count == Capacity) break;
            }
            _paths.Clear();
            _paths.AddRange(restored);
        }
    }
}
