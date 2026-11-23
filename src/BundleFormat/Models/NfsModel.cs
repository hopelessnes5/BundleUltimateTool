using System.Buffers.Binary;
using BundleFormat.Primitives;

namespace BundleFormat.Models;

public readonly record struct NfsVector3(float X, float Y, float Z);
public readonly record struct NfsVector2(float X, float Y);
public readonly record struct NfsVertex(NfsVector3 Position, NfsVector3 Normal, NfsVector2 Uv0, NfsVector2 Uv1, NfsVector2 Uv2)
{
    public NfsVertex(NfsVector3 position, NfsVector3 normal, NfsVector2 uv)
        : this(position, normal, uv, default, default) { }

    /// <summary>Alias of <see cref="Uv0"/> kept for existing callers.</summary>
    public NfsVector2 Uv => Uv0;
}
public readonly record struct NfsFace(ushort A, ushort B, ushort C);

public sealed class NfsMesh
{
    public NfsMeshHeader Header { get; init; }
    public uint VertexSize { get; init; }
    public bool HasUv => VertexSize == 36 || VertexSize == 48 || VertexSize == 52 || VertexSize == 56 || VertexSize == 60 || VertexSize == 64;
    /// <summary>
    /// Material reference hash from mesh header fields when reliably identified.
    /// The known S_NFSMeshHeader layout (112 bytes) in nfs-bundle-explorer does not
    /// expose a named material-hash field; this is set to 0 until a verified field is found.
    /// </summary>
    public uint MaterialHash { get; init; }
    public NfsVertex[] Vertices { get; init; } = Array.Empty<NfsVertex>();
    public NfsFace[] Faces { get; init; } = Array.Empty<NfsFace>();
}

/// <summary>Port of CNFSModel::InitFromData. Unparseable meshes are skipped with a
/// diagnostic, mirroring the C++ `continue` for out-of-range mesh headers.</summary>
public sealed class NfsModel
{
    public const int MaxTotalVertices = 5_000_000;
    public const int MaxTotalFaces = 5_000_000;

    public IReadOnlyList<NfsMesh> Meshes { get; private set; } = Array.Empty<NfsMesh>();
    public IReadOnlyList<string> Diagnostics { get; private set; } = Array.Empty<string>();
    public int TotalVertices => Meshes.Sum(m => m.Vertices.Length);
    public int TotalFaces => Meshes.Sum(m => m.Faces.Length);

    public static NfsModel InitFromData(ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> modelBytes)
    {
        if (headerBytes.Length < NfsModelHeader.Size)
            throw new BundleFormatException($"Model header needs {NfsModelHeader.Size} bytes, got {headerBytes.Length}.");
        if (modelBytes.IsEmpty)
            throw new BundleFormatException("Model body is empty.");

        NfsModelHeader header = NfsModelHeader.Parse(headerBytes);
        uint[] meshOffsets = NfsModelHeader.ReadMeshHeaderOffsets(headerBytes, header.NumMeshes);
        var meshes = new List<NfsMesh>(header.NumMeshes);
        var diagnostics = new List<string>();
        long totalVertices = 0;
        long totalFaces = 0;

        for (int i = 0; i < meshOffsets.Length; i++)
        {
            long offset = meshOffsets[i];
            if (offset + NfsMeshHeader.Size > headerBytes.Length)
            {
                diagnostics.Add($"mesh[{i}]: header offset 0x{offset:X} out of range; skipped (as in C++).");
                continue;
            }

            NfsMeshHeader meshHeader = NfsMeshHeader.Parse(headerBytes.Slice((int)offset, NfsMeshHeader.Size));
            uint vertexSize = meshHeader.VertexSize;

            NfsFace[] faces = Array.Empty<NfsFace>();
            long indexBytes = (long)meshHeader.NumIndexes * 2;
            if (meshHeader.BeginMeshOffset + indexBytes <= modelBytes.Length)
            {
                var indices = new ushort[meshHeader.NumIndexes];
                for (int index = 0; index < indices.Length; index++)
                    indices[index] = BinaryPrimitives.ReadUInt16LittleEndian(
                        modelBytes.Slice((int)meshHeader.BeginMeshOffset + index * 2, 2));
                faces = TriStripToTriList(indices, meshHeader.NumIndexes).ToArray();
            }
            else
            {
                diagnostics.Add($"mesh[{i}]: index buffer out of range; no faces.");
            }

            NfsVertex[] vertices = Array.Empty<NfsVertex>();
            if (vertexSize == 0)
            {
                diagnostics.Add($"mesh[{i}]: no matching vertex stride for VBSize {meshHeader.VbSize}; no vertices (as in C++).");
            }
            else
            {
                long vertexBytes = (long)meshHeader.NumVertexes * vertexSize;
                if (meshHeader.VbStartOffset + vertexBytes <= modelBytes.Length)
                    vertices = ReadVertices(modelBytes, (int)meshHeader.VbStartOffset,
                        (int)meshHeader.NumVertexes, (int)vertexSize);
                else
                    diagnostics.Add($"mesh[{i}]: vertex buffer out of range; no vertices.");
            }

            totalVertices += vertices.Length;
            totalFaces += faces.Length;
            if (totalVertices > MaxTotalVertices || totalFaces > MaxTotalFaces)
            {
                diagnostics.Add($"mesh[{i}]: global vertex/face cap reached; mesh omitted.");
                break;
            }

            meshes.Add(new NfsMesh
            {
                Header = meshHeader,
                VertexSize = vertexSize,
                Vertices = vertices,
                Faces = faces
            });
        }

        return new NfsModel { Meshes = meshes, Diagnostics = diagnostics };
    }

    private static NfsVertex[] ReadVertices(ReadOnlySpan<byte> modelBytes, int start, int count, int stride)
    {
        var result = new NfsVertex[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> vertex = modelBytes.Slice(start + i * stride, stride);
            static float Scale(short value) => value / 32767.0f * 8.0f;
            static NfsVector2 ReadUv(ReadOnlySpan<byte> v, int offset) => new(
                (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(v.Slice(offset, 2))),
                (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(v.Slice(offset + 2, 2))));

            var position = new NfsVector3(
                Scale(BinaryPrimitives.ReadInt16LittleEndian(vertex.Slice(0, 2))),
                Scale(BinaryPrimitives.ReadInt16LittleEndian(vertex.Slice(2, 2))),
                Scale(BinaryPrimitives.ReadInt16LittleEndian(vertex.Slice(4, 2))));
            var normal = new NfsVector3(
                BinaryPrimitives.ReadSingleLittleEndian(vertex.Slice(8, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(vertex.Slice(12, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(vertex.Slice(16, 4)));

            NfsVector2 uv0 = default, uv1 = default, uv2 = default;
            // UV layouts from nfs-bundle-explorer NFSStructuresDef.h and real-file scan (VEH_118158_MS.BIN + NFS:HP bundles).
            // Known structs:
            //   48: u0/v0 @40, u1/v1 @44
            //   52: u0/v0 @40, u1/v1 @44, u2/v2 @48
            //   60: u0/v0 @52, u1/v1 @56 (binormal shifts UV base)
            // Credible scan-derived layouts (float16 pairs, finite and mostly in [-2,2]):
            //   36: u0/v0 @28, u1/v1 @32 (end-of-vertex; consistent with pos+normal+bones+weights=28)
            //   56: u0/v0 @52 (end-of-vertex; consistent with 60-layout minus second UV pair)
            //   64: u0/v0 @52, u1/v1 @56 (matches 60 UV positions; trailing 4 bytes unknown)
            if (stride == 36)
            {
                uv0 = ReadUv(vertex, 28);
                uv1 = ReadUv(vertex, 32);
            }
            else if (stride == 48)
            {
                uv0 = ReadUv(vertex, 40);
                uv1 = ReadUv(vertex, 44);
            }
            else if (stride == 52)
            {
                uv0 = ReadUv(vertex, 40);
                uv1 = ReadUv(vertex, 44);
                uv2 = ReadUv(vertex, 48);
            }
            else if (stride == 56)
            {
                uv0 = ReadUv(vertex, 52);
            }
            else if (stride == 60)
            {
                uv0 = ReadUv(vertex, 52);
                uv1 = ReadUv(vertex, 56);
            }
            else if (stride == 64)
            {
                uv0 = ReadUv(vertex, 52);
                uv1 = ReadUv(vertex, 56);
            }
            // Unknown strides: position/normal read from leading mesh_vertex_48-compatible fields; UVs remain (0,0).

            result[i] = new NfsVertex(position, normal, uv0, uv1, uv2);
        }
        return result;
    }

    /// <summary>Faithful port of TriStripToTriList, including winding swap and degenerate skip.</summary>
    public static List<NfsFace> TriStripToTriList(ushort[] indices, uint numIndexes)
    {
        var result = new List<NfsFace>();
        int count = (int)Math.Min(numIndexes, (uint)indices.Length);
        for (int i = 2; i < count; i++)
        {
            bool even = i % 2 == 0;
            var face = new NfsFace(
                indices[even ? i - 1 : i],
                indices[even ? i : i - 1],
                indices[i - 2]);
            if (face.A != face.B && face.B != face.C && face.C != face.A) result.Add(face);
        }
        return result;
    }
}
