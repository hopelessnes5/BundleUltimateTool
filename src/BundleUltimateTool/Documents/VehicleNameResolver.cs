using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BundleFormat.Bundle;
using BundleFormat.Compression;
using BundleFormat.IO;
using BundleFormat.Tables;

namespace BundleUltimateTool.Documents;

/// <summary>
/// Resolves VEH_&lt;id&gt; bundle/file names to human-readable car names by correlating
/// VEHICLELIST.BUNDLE (type 261) records with STRINGS tables (type 513) in the game
/// language bundles. UI-side only; uses BundleFormat readers.
/// </summary>
internal static class VehicleNameResolver
{
    private static readonly Regex VehIdRegex = new(@"^VEH_(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] BrandTokens =
    {
        "BUGATTI", "NISSAN", "AUDI", "LAMBORGHINI", "KOENIGSEGG", "MCLAREN", "SHELBY",
        "PORSCHE", "BMW", "MASERATI", "DODGE", "FORD", "ASTON", "BENTLEY", "JAGUAR",
        "MERCEDES", "SUBARU", "MAZDA", "PAGANI", "ALFA", "CHEVROLET", "TOYOTA", "CADILLAC",
        "GMC", "VOLKSWAGEN", "HYUNDAI", "MITSUBISHI"
    };

    private static readonly string[] CarTokens =
    {
        "BUGATTI", "VEYRON", "NISSAN", "GT-R", "GTR", "370Z", "AUDI", "R8", "TT RS",
        "LAMBORGHINI", "GALLARDO", "MURCI", "REVENT", "KOENIGSEGG", "CCX", "CCXR", "AGERA",
        "MCLAREN", "MP4", "SLR", "F1", "SHELBY", "GT500", "LANCER", "EVOLUTION",
        "PORSCHE", "911", "CAYMAN", "CAYENNE", "PANAMERA", "CARRERA", "918", "BOXSTER",
        "BMW", "M3", "M6", "Z4", "MASERATI", "QUATTROPORTE", "GRANTURISMO", "GRANCABRIO",
        "CORVETTE", "CAMARO", "VIPER", "DODGE", "CHARGER", "CHALLENGER", "FORD", "MUSTANG",
        "ASTON", "DBS", "VANTAGE", "ONE-77", "BENTLEY", "CONTINENTAL", "JAGUAR", "XKR",
        "MERCEDES", "SL65", "SLS", "AMG", "SUBARU", "IMPREZA", "WRX", "MAZDA", "RX-8",
        "ZONDA", "CINQUE", "PAGANI", "ALFA", "8C", "TAURUS", "INTERCEPTOR", "TRAF",
        "RAM", "TOPKICK", "EXPRESSVAN", "VERSA", "FRONTIER", "CALIBER", "MAGNUM", "CARAVAN"
    };

    internal static uint? ParseVehId(string fileName) =>
        BundleFormat.IO.GamePathRules.ParseVehId(fileName);

    internal static IReadOnlyDictionary<uint, string> Build(string gameRoot, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return Empty;

        try
        {
            var bundlePaths = EnumerateBundleFiles(gameRoot, token);
            var vehIds = CollectVehIds(bundlePaths);
            if (vehIds.Count == 0)
            {
                ApplicationLog.Write("VEHICLE_NAMES", $"root='{gameRoot}', vehIds=0, names=0");
                return Empty;
            }

            NfsStringsTable strings = LoadPreferredStrings(gameRoot, bundlePaths, token);
            if (strings.Strings.Count == 0)
            {
                ApplicationLog.Write("VEHICLE_NAMES", $"root='{gameRoot}', vehIds={vehIds.Count}, strings=0, names=0");
                return Empty;
            }

            // Authoritative source: the vehicle bundle itself. Every VEH_*_MS.BIN has a
            // type-19 record of 576 bytes whose u32 at +104/+132 are STRINGS IDs for the
            // specific/model name and the display name. VEHICLELIST cross-references are
            // only a fallback — they picked wrong cars for some IDs (e.g. 380028).
            var names = BuildFromVehicleBundles(bundlePaths, strings, token);
            if (names.Count == 0)
            {
                var stringIndex = BuildStringIndex(strings);
                CorrelateVehicleLists(bundlePaths, vehIds, strings, stringIndex, names, token);
            }
            ApplicationLog.Write("VEHICLE_NAMES",
                $"root='{gameRoot}', vehIds={vehIds.Count}, strings={strings.Strings.Count}, names={names.Count}");
            return names;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ApplicationLog.Write("VEHICLE_NAMES_FAILED", $"root='{gameRoot}': {ex.Message}", ex);
            return Empty;
        }
    }

    private static IReadOnlyDictionary<uint, string> Empty => new Dictionary<uint, string>();

    private static List<string> EnumerateBundleFiles(string gameRoot, CancellationToken token)
    {
        var result = new List<string>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                if (IsBundleFile(path)) result.Add(path);
            }
        }
        catch (Exception ex)
        {
            ApplicationLog.Write("VEHICLE_NAMES_ENUM_FAILED", $"root='{gameRoot}': {ex.Message}", ex);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static bool IsBundleFile(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".bundle", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bndl", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bin", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<uint> CollectVehIds(IEnumerable<string> bundlePaths)
    {
        var ids = new HashSet<uint>();
        foreach (string path in bundlePaths)
        {
            uint? id = ParseVehId(Path.GetFileName(path));
            if (id.HasValue) ids.Add(id.Value);
        }
        return ids;
    }

    private static NfsStringsTable LoadPreferredStrings(string gameRoot, List<string> bundlePaths, CancellationToken token)
    {
        // Prefer UI\LANGUAGE\0001.BNDL (full car names), then 0013, then any 513.
        var preferred = new[]
        {
            Path.Combine(gameRoot, "UI", "LANGUAGE", "0001.BNDL"),
            Path.Combine(gameRoot, "UI", "LANGUAGE", "0013.BNDL"),
        };

        foreach (string candidate in preferred)
        {
            token.ThrowIfCancellationRequested();
            NfsStringsTable strings = TryLoadStringsFromBundle(candidate, token);
            if (strings.Strings.Count > 0)
            {
                ApplicationLog.Write("VEHICLE_NAMES_STRINGS", $"source='{candidate}', entries={strings.Strings.Count}");
                return strings;
            }
        }

        foreach (string path in bundlePaths)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            if (name.Equals("0001.BNDL", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("0013.BNDL", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!name.EndsWith(".BNDL", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".BUNDLE", StringComparison.OrdinalIgnoreCase))
                continue;
            NfsStringsTable strings = TryLoadStringsFromBundle(path, token);
            if (strings.Strings.Count >= 100)
            {
                ApplicationLog.Write("VEHICLE_NAMES_STRINGS", $"source='{path}', entries={strings.Strings.Count}");
                return strings;
            }
        }

        return new NfsStringsTable();
    }

    private static NfsStringsTable TryLoadStringsFromBundle(string path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new NfsStringsTable();
        try
        {
            using BundleReader reader = BundleReader.Open(path);
            foreach (BundleEntry entry in reader.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.FileType != 513) continue;
                try
                {
                    byte[] compressed = reader.ReadBytes(entry.AbsoluteHeaderOffset, checked((int)entry.Raw.HdrCompSize));
                    byte[] data = ZLibAdapter.Decompress(compressed, checked((int)entry.MaskedHdrUncompSize));
                    NfsStringsTable table = NfsStringsTable.Parse(data);
                    if (table.Strings.Count > 0) return table;
                }
                catch (Exception ex)
                {
                    ApplicationLog.Write("VEHICLE_NAMES_STRINGS_ENTRY_FAILED", $"'{path}'[{entry.Index}]: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            ApplicationLog.Write("VEHICLE_NAMES_STRINGS_FAILED", $"'{path}': {ex.Message}");
        }
        return new NfsStringsTable();
    }

    private static HashSet<uint> BuildStringIndex(NfsStringsTable table)
    {
        var index = new HashSet<uint>();
        foreach (uint id in table.Ids)
        {
            if (table.IdToIndex.TryGetValue(id, out int i) && IsLikelyCarName(table.Strings[i]))
                index.Add(id);
        }
        return index;
    }

    private static Dictionary<uint, string> BuildFromVehicleBundles(List<string> bundlePaths,
        NfsStringsTable strings, CancellationToken token)
    {
        var names = new Dictionary<uint, string>();
        foreach (string path in bundlePaths
            .Where(p => ParseVehId(Path.GetFileName(p)).HasValue)
            .OrderBy(p => Path.GetFileName(p).Contains("_MS.", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            uint? id = ParseVehId(Path.GetFileName(path));
            if (!id.HasValue || names.ContainsKey(id.Value)) continue;
            try
            {
                using BundleReader reader = BundleReader.Open(path);
                foreach (BundleEntry entry in reader.Entries)
                {
                    if (entry.FileType != 19 || entry.MaskedHdrUncompSize != 576) continue;
                    byte[] data = DecompressEntryHeader(reader, entry);
                    if (data.Length < 136) continue;
                    uint specificId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(104, 4));
                    uint displayId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(132, 4));
                    string specific = ResolveString(strings, specificId);
                    string display = ResolveString(strings, displayId);
                    string chosen = !string.IsNullOrWhiteSpace(display) ? display : specific;
                    if (string.IsNullOrWhiteSpace(chosen)) continue;
                    names[id.Value] = NormalizeName(chosen);
                    break;
                }
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("VEHICLE_NAMES_BUNDLE_FAILED", $"'{path}': {ex.Message}");
            }
        }
        ApplicationLog.Write("VEHICLE_NAMES_PER_BUNDLE", $"resolved={names.Count}");
        return names;
    }

    private static string ResolveString(NfsStringsTable table, uint id)
    {
        if (id == 0 || !table.IdToIndex.TryGetValue(id, out int index)) return string.Empty;
        string value = table.Strings[index] ?? string.Empty;
        return ScoreCarName(value) > 0 ? value.Trim().Trim('\0', ' ', '\r', '\n') : string.Empty;
    }

    private static void CorrelateVehicleLists(List<string> bundlePaths, HashSet<uint> vehIds,
        NfsStringsTable strings, HashSet<uint> carNameStringIds,
        Dictionary<uint, string> names, CancellationToken token)
    {
        var candidates = bundlePaths
            .Where(p => Path.GetFileName(p).IndexOf("VEHICLELIST", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        if (candidates.Count == 0)
            candidates = bundlePaths; // fallback: scan all bundles

        int scanned = 0;
        foreach (string path in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using BundleReader reader = BundleReader.Open(path);
                bool has261 = false;
                foreach (BundleEntry entry in reader.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.FileType != 261) continue;
                    has261 = true;
                    byte[] data = DecompressEntryHeader(reader, entry);
                    if (data.Length == 0) continue;
                    CorrelateVehicleListData(data, vehIds, strings, carNameStringIds, names);
                }
                if (has261) scanned++;
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("VEHICLE_NAMES_261_FAILED", $"'{path}': {ex.Message}");
            }
        }
        ApplicationLog.Write("VEHICLE_NAMES_261_SCAN", $"candidates={candidates.Count}, scannedWith261={scanned}, resolved={names.Count}");
    }

    private static byte[] DecompressEntryHeader(BundleReader reader, BundleEntry entry)
    {
        try
        {
            byte[] compressed = reader.ReadBytes(entry.AbsoluteHeaderOffset, checked((int)entry.Raw.HdrCompSize));
            return ZLibAdapter.Decompress(compressed, checked((int)entry.MaskedHdrUncompSize));
        }
        catch { return Array.Empty<byte>(); }
    }

    private static void CorrelateVehicleListData(byte[] data, HashSet<uint> vehIds,
        NfsStringsTable strings, HashSet<uint> carNameStringIds,
        Dictionary<uint, string> names)
    {
        // VEHICLELIST.BUNDLE type 261: 32-byte header + 153 records × 92 bytes + 257 uint32 tail.
        // We parse generically: treat 92-byte chunks after the initial header as records, and
        // also scan the remaining tail as uint32s.
        const int RecordSize = 92;
        const int HeaderSize = 32;
        if (data.Length < HeaderSize + RecordSize) return;

        int recordsArea = data.Length - HeaderSize;
        int recordCount = recordsArea / RecordSize;
        // Be conservative: if it doesn't divide cleanly into records + tail, still scan what we can.
        int recordsBytes = recordCount * RecordSize;
        int offset = HeaderSize;
        for (int i = 0; i < recordCount; i++, offset += RecordSize)
        {
            CorrelateRecord(data.AsSpan(offset, RecordSize), vehIds, strings, carNameStringIds, names);
        }

        // Scan tail as uint32 values. VEH IDs appear in the tail; correlate by proximity to string IDs.
        int tailOffset = HeaderSize + recordsBytes;
        if (tailOffset + 4 <= data.Length)
            CorrelateTail(data.AsSpan(tailOffset), vehIds, strings, carNameStringIds, names);
    }

    private static void CorrelateRecord(ReadOnlySpan<byte> record, HashSet<uint> vehIds,
        NfsStringsTable strings, HashSet<uint> carNameStringIds,
        Dictionary<uint, string> names)
    {
        if (record.Length < 4) return;
        int fieldCount = record.Length / 4;
        Span<uint> fields = fieldCount <= 64 ? stackalloc uint[fieldCount] : new uint[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            fields[i] = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(i * 4, 4));

        uint? vehId = null;
        for (int i = 0; i < fieldCount; i++)
        {
            if (vehIds.Contains(fields[i])) { vehId = fields[i]; break; }
        }
        if (!vehId.HasValue || names.ContainsKey(vehId.Value)) return;
        string best = null;
        int bestScore = 0;
        for (int i = 0; i < fieldCount; i++)
        {
            uint value = fields[i];
            if (value == vehId.Value) continue;
            if (!carNameStringIds.Contains(value) || !strings.IdToIndex.TryGetValue(value, out int stringIndex))
                continue;
            string candidate = strings.Strings[stringIndex];
            int score = CorrelationScore(candidate, value, vehId.Value);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        if (bestScore >= 8 && !string.IsNullOrWhiteSpace(best))
            names[vehId.Value] = NormalizeName(best);
    }

    private static void CorrelateTail(ReadOnlySpan<byte> tail, HashSet<uint> vehIds,
        NfsStringsTable strings, HashSet<uint> carNameStringIds,
        Dictionary<uint, string> names)
    {
        int count = tail.Length / 4;
        if (count <= 0) return;
        // Read tail uint32s. To avoid allocating a huge array, scan directly.
        for (int i = 0; i < count; i++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(i * 4, 4));
            if (!vehIds.Contains(value) || names.ContainsKey(value)) continue;
            string best = FindNearbyCarName(tail, i, count, value, strings, carNameStringIds);
            if (!string.IsNullOrWhiteSpace(best))
                names[value] = NormalizeName(best);
        }
    }

    private static string FindNearbyCarName(ReadOnlySpan<byte> tail, int vehIndex, int count, uint vehId,
        NfsStringsTable strings, HashSet<uint> carNameStringIds)
    {
        // Search a local window around the VEH ID for a string ID that looks like a car name.
        // Also search a few fixed offsets; the tail layout can vary, so keep it heuristic.
        const int Window = 32;
        int start = Math.Max(0, vehIndex - Window);
        int end = Math.Min(count, vehIndex + Window + 1);
        string best = null;
        int bestScore = 0;
        for (int i = start; i < end; i++)
        {
            if (i == vehIndex) continue;
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(i * 4, 4));
            if (!carNameStringIds.Contains(value) || !strings.IdToIndex.TryGetValue(value, out int stringIndex))
                continue;
            string candidate = strings.Strings[stringIndex];
            int score = CorrelationScore(candidate, value, vehId);
            // Prefer closer matches slightly.
            int distance = Math.Abs(i - vehIndex);
            score -= Math.Min(distance, 8);
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        return bestScore >= 8 ? best : null;
    }

    private static int CorrelationScore(string candidate, uint stringId, uint vehId)
    {
        int score = ScoreCarName(candidate);
        if (score < 2) return 0;
        // NFS vehicle records reference string IDs numerically close to the vehicle ID.
        long diff = Math.Abs((long)stringId - vehId);
        if (diff < 1000) score += 4;
        if (diff < 200) score += 4;
        if (CarTokens.Any(token => candidate.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            score += 3;
        if (BrandTokens.Any(token => candidate.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            score += 7;
        // Prefer short display names over long marketing descriptions.
        if (candidate.Length > 48) score -= 5;
        if (candidate.Length >= 6 && candidate.Length <= 32) score += 3;
        if (candidate.IndexOf('{') >= 0 || candidate.IndexOf('}') >= 0) score -= 8;
        if (candidate.IndexOf("CamerasProperties", StringComparison.OrdinalIgnoreCase) >= 0) score -= 8;
        return score;
    }

    private static int ScoreCarName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        string s = value.Trim().Trim('\0', ' ', '\r', '\n');
        if (s.Length < 3 || s.Length > 80) return 0;
        if (s.IndexOf('\\') >= 0 || s.IndexOf('/') >= 0 || s.IndexOf('.') >= 0) return 0;

        int letters = 0;
        int spaces = 0;
        int upper = 0;
        int digits = 0;
        int other = 0;
        foreach (char c in s)
        {
            if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
            else if (c == ' ') spaces++;
            else if (char.IsDigit(c)) digits++;
            else if (c == '-' || c == '\'' || c == '&') other++;
            else return 0;
        }
        if (letters < 3) return 0;
        if (spaces == 0 && s.Length < 5) return 0;

        int score = 1;
        if (spaces >= 1) score += 2;
        if (upper >= letters / 2) score += 2; // many car names are uppercase in NFS strings
        if (digits > 0 && digits <= 6) score += 1;
        if (s.Length >= 6) score += 1;
        // Penalize likely non-name strings.
        if (s.StartsWith("ID_", StringComparison.OrdinalIgnoreCase)) score -= 5;
        if (s.Contains("TEXTURE", StringComparison.OrdinalIgnoreCase)) score -= 5;
        if (s.Contains("VEHICLE", StringComparison.OrdinalIgnoreCase)) score -= 2;
        return score;
    }

    private static bool IsLikelyCarName(string value) => ScoreCarName(value) >= 2;

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
        // Keep original casing mostly; just trim and collapse whitespace.
        var sb = new StringBuilder(value.Length);
        bool prevSpace = true;
        foreach (char c in value.Trim())
        {
            if (c == '\0') continue;
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (!prevSpace) sb.Append(' ');
            }
            else sb.Append(c);
            prevSpace = isSpace;
        }
        return sb.ToString();
    }
}
