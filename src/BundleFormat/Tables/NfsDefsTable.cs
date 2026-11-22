using System.Text;
using BundleFormat.Primitives;

namespace BundleFormat.Tables;

/// <summary>Port of DlgDefsTableView::SetBundleAndIdx. Input is the decompressed
/// entry header: 68-byte S_NFSDefsTableHeader followed by 20-byte entries.</summary>
public sealed class NfsDefsTable
{
    public const int HeaderSize = 68;
    public const int EntrySize = 20;
    public const int DefNameMaxLen = 51;

    public readonly record struct DefEntry(uint DefId, uint Type, uint Data2, uint Offset, uint Data4)
    {
        public string DefIdHex => DefId.ToString("X8");
    }

    public string DefName { get; private set; } = string.Empty;
    public uint Count { get; private set; }
    public IReadOnlyList<DefEntry> Entries { get; private set; } = Array.Empty<DefEntry>();

    public static NfsDefsTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new BundleFormatException($"DEFS header needs {HeaderSize} bytes, got {data.Length}.");

        _ = EndianReader.ReadUInt32(data, 0, "defs.stuff0");
        _ = EndianReader.ReadUInt32(data, 4, "defs.stuff1");
        _ = EndianReader.ReadUInt32(data, 8, "defs.stuff2");
        uint count = EndianReader.ReadUInt32(data, 12, "defs.defsCount");
        _ = EndianReader.ReadByte(data, 16, "defs.zero0");
        string defName = DecodeDefName(data.Slice(17, DefNameMaxLen));

        if (count > 1_000_000)
            throw new BundleFormatException($"DEFS count {count} exceeds the supported limit.");
        long entriesBytes = (long)count * EntrySize;
        if (HeaderSize + entriesBytes > data.Length)
            throw new BundleFormatException(
                $"DEFS entries need {entriesBytes} byte(s) at offset {HeaderSize}, beyond data length {data.Length}.");

        var entries = new DefEntry[count];
        for (int i = 0; i < (int)count; i++)
        {
            int at = HeaderSize + i * EntrySize;
            entries[i] = new DefEntry(
                EndianReader.ReadUInt32(data, at, $"defs[{i}].defID"),
                EndianReader.ReadUInt32(data, at + 4, $"defs[{i}].type"),
                EndianReader.ReadUInt32(data, at + 8, $"defs[{i}].data2"),
                EndianReader.ReadUInt32(data, at + 12, $"defs[{i}].offset"),
                EndianReader.ReadUInt32(data, at + 16, $"defs[{i}].data4"));
        }

        return new NfsDefsTable { DefName = defName, Count = count, Entries = entries };
    }

    private static string DecodeDefName(ReadOnlySpan<byte> bytes)
    {
        string raw = Encoding.Latin1.GetString(bytes);
        int nul = raw.IndexOf('\0');
        return nul >= 0 ? raw.Substring(0, nul) : raw;
    }
}
