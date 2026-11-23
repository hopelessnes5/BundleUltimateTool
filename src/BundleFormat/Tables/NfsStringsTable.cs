using System.Text;
using BundleFormat.Primitives;

namespace BundleFormat.Tables;

/// <summary>Port of CNFSStringsTable::InitFromData. The input is the DECOMPRESSED
/// entry header, not the entry body. Malformed entries are skipped with a diagnostic
/// instead of aborting the whole table.</summary>
public sealed class NfsStringsTable
{
    public uint Version { get; private set; }
    public uint CharSize { get; private set; }
    public uint Count { get; private set; }
    public IReadOnlyList<string> Strings { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<uint> Ids { get; private set; } = Array.Empty<uint>();
    public IReadOnlyDictionary<uint, int> IdToIndex { get; private set; } = new Dictionary<uint, int>();
    public IReadOnlyList<string> Diagnostics { get; private set; } = Array.Empty<string>();

    public static NfsStringsTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            throw new BundleFormatException($"STRINGS header needs 16 bytes, got {data.Length}.");

        uint version = EndianReader.ReadUInt32(data, 0, "strings.version");
        uint count = EndianReader.ReadUInt32(data, 4, "strings.stringsCount");
        uint charSize = EndianReader.ReadUInt32(data, 8, "strings.charSize");
        uint infosOffset = EndianReader.ReadUInt32(data, 12, "strings.stringInfosOffset");
        if (charSize != 8 && charSize != 16)
            throw new BundleFormatException($"Unsupported STRINGS charSize {charSize}; expected 8 or 16.");
        if (charSize == 8)
            throw new BundleFormatException("STRINGS charSize 8 (single-byte characters) is not supported; expected UTF-16 (16).");
        if (count > 1_000_000)
            throw new BundleFormatException($"STRINGS count {count} exceeds the supported limit.");

        long infoBytes = (long)count * 8;
        if ((ulong)infosOffset + (ulong)infoBytes > (ulong)data.Length)
            throw new BundleFormatException(
                $"STRINGS info table at {infosOffset} needs {infoBytes} byte(s), beyond data length {data.Length}.");

        // Game STRINGS tables store a uint32 ID per string between the header and the
        // (offset,length) info array. The original C++ reader ignored those IDs, but they
        // are the keys used by VEHICLELIST and other tables. Older tables without an ID
        // array fall back to the string index as ID.
        bool hasIds = infosOffset >= 16 + count * 4 && data.Length >= 16 + count * 4;
        var ids = new uint[count];
        var idToIndex = new Dictionary<uint, int>();
        for (uint i = 0; i < count; i++)
        {
            ids[i] = hasIds ? EndianReader.ReadUInt32(data, 16 + (int)i * 4, $"strings.id[{i}]") : i;
            idToIndex[ids[i]] = (int)i;
        }

        var strings = new string[count];
        var diagnostics = new List<string>();
        for (uint i = 0; i < count; i++)
        {
            int infoAt = (int)infosOffset + (int)i * 8;
            uint stringOffset = EndianReader.ReadUInt32(data, infoAt, $"strings.info[{i}].offset");
            uint stringLength = EndianReader.ReadUInt32(data, infoAt + 4, $"strings.info[{i}].stringLength");
            long byteCount = (long)stringLength * (charSize >> 3);
            strings[i] = string.Empty;

            if ((ulong)stringOffset + (ulong)byteCount > (ulong)data.Length)
            {
                diagnostics.Add($"strings[{i}]: offset {stringOffset} + {byteCount} bytes exceeds table length; skipped.");
                continue;
            }
            if (byteCount > int.MaxValue)
            {
                diagnostics.Add($"strings[{i}]: string too long ({byteCount} bytes); skipped.");
                continue;
            }
            try
            {
                strings[i] = Encoding.Unicode.GetString(data.Slice((int)stringOffset, (int)byteCount));
            }
            catch (DecoderFallbackException ex)
            {
                diagnostics.Add($"strings[{i}]: invalid UTF-16 data: {ex.Message}; skipped.");
            }
        }

        return new NfsStringsTable
        {
            Version = version,
            CharSize = charSize,
            Count = count,
            Strings = strings,
            Ids = ids,
            IdToIndex = idToIndex,
            Diagnostics = diagnostics
        };
    }

    /// <summary>UTF-8 text without BOM, one string per line and \\n line endings.</summary>
    public string SaveAsTxt()
    {
        var text = new StringBuilder();
        foreach (string value in Strings) text.Append(value).Append('\n');
        return text.ToString();
    }

    public void SaveAsTxtFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        File.WriteAllText(path, SaveAsTxt(), new UTF8Encoding(false));
    }
}
