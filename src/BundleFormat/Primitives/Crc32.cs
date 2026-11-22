namespace BundleFormat.Primitives;

/// <summary>Standard zlib CRC-32 (reflected polynomial 0xEDB88320), seeded with 0xFFFFFFFF.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Begin => 0xFFFFFFFFu;

    public static uint Compute(ReadOnlySpan<byte> data) => Compute(0, data);

    /// <summary>
    /// Continues a CRC from a previously finalized value (zlib crc32 semantics):
    /// crc32(0, data) starts a new checksum, crc32(previousFinal, data) continues it.
    /// </summary>
    public static uint Compute(uint seed, ReadOnlySpan<byte> data) => Finish(Update(seed ^ 0xFFFFFFFFu, data));

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        uint value = crc;
        foreach (byte b in data)
            value = Table[(value ^ b) & 0xFF] ^ (value >> 8);
        return value;
    }

    public static uint Finish(uint crc) => crc ^ 0xFFFFFFFFu;

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
