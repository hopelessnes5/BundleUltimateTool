using System.Buffers.Binary;
using BundleFormat.Primitives;

namespace BundleFormat.Models;

public readonly record struct NfsVector3(float X, float Y, float Z);
public readonly record struct NfsVector2(float X, float Y);
public readonly record struct NfsVertex(NfsVector3 Position, NfsVector3 Normal, NfsVector2 Uv);
public readonly record struct NfsFace(ushort A, ushort B, ushort C);

public sealed class NfsMesh
{
    public NfsMeshHeader Header { get; init; }
    public uint VertexSize { get; init; }
    public bool HasUv => VertexSize == 48 || VertexSize == 60;
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
            var position = new NfsVector3(
                Scale(BinaryPrimitives.ReadInt16LittleEndian(vertex.Slice(0, 2))),
                Scale(BinaryPrimitives.ReadInt16LittleEndian(vertex.Slice(2, 2))),
                Scale(BinaryPrimitives.ReadInt16LittleEndian(vertex.Slice(4, 2))));
            var normal = new NfsVector3(
                BinaryPrimitives.ReadSingleLittleEndian(vertex.Slice(8, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(vertex.Slice(12, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(vertex.Slice(16, 4)));

            NfsVector2 uv = default;
            // Only 48/60 have a UV specialization. Other strides still read position and
            // normal from the leading mesh_vertex_48 fields, but leave UV at (0,0); the
            // OBJ exporter omits vt lines in that case (see ObjExporter).
            if (stride == 48 || stride == 60)
            {
                int uvOffset = stride == 60 ? 52 : 40;
                uv = new NfsVector2(
                    (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(vertex.Slice(uvOffset, 2))),
                    (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(vertex.Slice(uvOffset + 2, 2))));
            }
            result[i] = new NfsVertex(position, normal, uv);
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
