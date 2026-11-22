using System.Numerics;

namespace BundleFormat.Models;

/// <summary>Plain render arrays for one mesh; no UI or graphics API dependency.</summary>
public readonly record struct RenderMesh(
    Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, int[] Indices);

public readonly record struct RenderBounds(Vector3 Center, float Radius);

/// <summary>Maps parsed NFS model data to the arrays consumed by the DX12 mesh renderer.</summary>
public static class ModelRenderConverter
{
    // Ported from PrototypeUltimateTool/src/Model3DViewer.cs:6201-6204 ToViewerPoint:
    // display-space geometry is converted to Z-up by (X*1000, -Z*1000, Y*1000).
    private static Vector3 ToViewerPoint(Vector3 value) =>
        new(value.X * 1000.0f, -value.Z * 1000.0f, value.Y * 1000.0f);

    private static Vector3 ToViewerNormal(Vector3 value)
    {
        var n = new Vector3(value.X, -value.Z, value.Y);
        float lenSq = n.LengthSquared();
        return lenSq > 0.000001f ? n / MathF.Sqrt(lenSq) : Vector3.UnitZ;
    }

    public static IReadOnlyList<RenderMesh> Convert(NfsModel model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        var result = new List<RenderMesh>(model.Meshes.Count);
        foreach (NfsMesh mesh in model.Meshes)
        {
            var positions = new Vector3[mesh.Vertices.Length];
            var normals = new Vector3[mesh.Vertices.Length];
            var uvs = new Vector2[mesh.Vertices.Length];
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                NfsVertex vertex = mesh.Vertices[i];
                positions[i] = ToViewerPoint(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
                normals[i] = ToViewerNormal(new Vector3(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z));
                uvs[i] = new Vector2(vertex.Uv.X, vertex.Uv.Y);
            }
            var indices = new int[mesh.Faces.Length * 3];
            for (int i = 0; i < mesh.Faces.Length; i++)
            {
                indices[i * 3] = mesh.Faces[i].A;
                indices[i * 3 + 1] = mesh.Faces[i].B;
                indices[i * 3 + 2] = mesh.Faces[i].C;
            }
            result.Add(new RenderMesh(positions, normals, uvs, indices));
        }
        return result;
    }

    public static RenderBounds GetBounds(IReadOnlyList<RenderMesh> meshes)
    {
        if (meshes == null || meshes.Count == 0) return new RenderBounds(Vector3.Zero, 1f);
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (RenderMesh mesh in meshes)
        {
            foreach (Vector3 position in mesh.Positions)
            {
                minX = Math.Min(minX, position.X); maxX = Math.Max(maxX, position.X);
                minY = Math.Min(minY, position.Y); maxY = Math.Max(maxY, position.Y);
                minZ = Math.Min(minZ, position.Z); maxZ = Math.Max(maxZ, position.Z);
            }
        }
        var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
        float dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
        // AABB half-diagonal; multiply by 2 for Prototype's _boundingRadius (full diagonal).
        float halfDiagonal = MathF.Sqrt(dx * dx + dy * dy + dz * dz) * 0.5f;
        return new RenderBounds(center, Math.Max(0.1f, halfDiagonal));
    }
}
