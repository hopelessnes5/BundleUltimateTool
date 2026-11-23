using BundleFormat.Primitives;

namespace BundleFormat.Models;

/// <summary>112-byte S_NFSMeshHeader (all offsets are relative to the decompressed model body).</summary>
public readonly record struct NfsMeshHeader(
    uint NumVertexes, uint NumIndexes, uint BeginMeshOffset,
    uint VbStartOffset, uint VbSize)
{
    public const int Size = 112;
    private static readonly uint[] KnownVertexSizes = { 24, 28, 32, 36, 44, 48, 52, 56, 60, 64 };

    public static NfsMeshHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
            throw new BundleFormatException($"NFS mesh header needs {Size} bytes, got {bytes.Length}.");
        return new NfsMeshHeader(
            EndianReader.ReadUInt32(bytes, 28, "mesh.numVertexes"),
            EndianReader.ReadUInt32(bytes, 80, "mesh.numIndexes"),
            EndianReader.ReadUInt32(bytes, 84, "mesh.beginMeshOffset"),
            EndianReader.ReadUInt32(bytes, 96, "mesh.VBStartOffset"),
            EndianReader.ReadUInt32(bytes, 104, "mesh.VBSize"));
    }

    /// <summary>Faithful port of ChooseVertexSize: align32(size * numVertexes) == VBSize.</summary>
    /// <remarks>
    /// Multiple strides can align to the same VBSize for small numVertexes; in that ambiguous case
    /// we prefer known-struct UV layouts (48/52/60), then scan-derived credible UV layouts, then original candidate order.
    /// Known/credible UV-carrying strides: 24,28,32,36,44,48,52,56,60,64.
    /// Adding 28/32/44 creates alignment collisions with existing strides; preference rules keep known-struct layouts stable.
    /// </remarks>
    public static uint ChooseVertexSize(uint numVertexes, uint vbAlignedSize)
    {
        uint firstMatch = 0;
        uint firstKnownUvMatch = 0;
        uint firstCredibleUvMatch = 0;
        foreach (uint vertexSize in KnownVertexSizes)
        {
            ulong unaligned = (ulong)vertexSize * numVertexes;
            ulong aligned = (unaligned + 31UL) & ~31UL;
            if (aligned != vbAlignedSize) continue;
            if (firstMatch == 0) firstMatch = vertexSize;
            if (firstKnownUvMatch == 0 && HasKnownStructUv(vertexSize)) firstKnownUvMatch = vertexSize;
            if (firstCredibleUvMatch == 0 && HasCredibleUv(vertexSize)) firstCredibleUvMatch = vertexSize;
        }
        if (firstMatch == 0) return 0;
        // Prefer known-struct UV layouts, then scan-derived credible UV layouts, then original order.
        if (firstKnownUvMatch != 0) return firstKnownUvMatch;
        if (firstCredibleUvMatch != 0) return firstCredibleUvMatch;
        return firstMatch;
    }

    private static bool HasKnownStructUv(uint vertexSize) =>
        vertexSize == 48 || vertexSize == 52 || vertexSize == 60;

    private static bool HasCredibleUv(uint vertexSize) =>
        vertexSize == 24 || vertexSize == 28 || vertexSize == 32 || vertexSize == 36 || vertexSize == 44 || vertexSize == 48 || vertexSize == 52 || vertexSize == 56 || vertexSize == 60 || vertexSize == 64;

    public uint VertexSize => ChooseVertexSize(NumVertexes, VbSize);
}
