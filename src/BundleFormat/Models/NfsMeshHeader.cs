using BundleFormat.Primitives;

namespace BundleFormat.Models;

/// <summary>112-byte S_NFSMeshHeader (all offsets are relative to the decompressed model body).</summary>
public readonly record struct NfsMeshHeader(
    uint NumVertexes, uint NumIndexes, uint BeginMeshOffset,
    uint VbStartOffset, uint VbSize)
{
    public const int Size = 112;
    private static readonly uint[] KnownVertexSizes = { 36, 48, 52, 56, 60, 64 };

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
    public static uint ChooseVertexSize(uint numVertexes, uint vbAlignedSize)
    {
        foreach (uint vertexSize in KnownVertexSizes)
        {
            ulong unaligned = (ulong)vertexSize * numVertexes;
            ulong aligned = (unaligned + 31UL) & ~31UL;
            if (aligned == vbAlignedSize) return vertexSize;
        }
        return 0;
    }

    public uint VertexSize => ChooseVertexSize(NumVertexes, VbSize);
}
