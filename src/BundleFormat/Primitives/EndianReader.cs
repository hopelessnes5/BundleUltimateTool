using System.Buffers.Binary;
using System.Text;

namespace BundleFormat.Primitives;

/// <summary>
/// Explicit little-endian readers over spans. All bundle structures are packed byte-by-byte
/// in the original C++ with #pragma pack(1); host-order struct reads would be a portability trap.
/// </summary>
public static class EndianReader
{
    public static byte ReadByte(ReadOnlySpan<byte> data, int offset, string field)
    {
        EnsureAvailable(data.Length, offset, 1, field);
        return data[offset];
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, string field)
    {
        EnsureAvailable(data.Length, offset, 2, field);
        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string field)
    {
        EnsureAvailable(data.Length, offset, 4, field);
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length, string field)
    {
        EnsureAvailable(data.Length, offset, length, field);
        return Encoding.ASCII.GetString(data.Slice(offset, length));
    }

    public static void EnsureAvailable(int available, int offset, int count, string field)
    {
        if ((uint)offset + (uint)count > (uint)available)
        {
            throw new BundleFormatException(
                $"{field ?? "field"}: needs {count} byte(s) at offset {offset}, " +
                $"but only {Math.Max(0, available - Math.Max(0, offset))} byte(s) are available " +
                $"(region length {available}).");
        }
    }
}
