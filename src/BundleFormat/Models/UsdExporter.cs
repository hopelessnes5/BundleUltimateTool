using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BundleFormat.Models;

public readonly record struct UsdSummary(int Meshes, int Vertices, int Faces);

/// <summary>
/// Exports NFS models as USDA ASCII. Coordinates are normalized to a sane display range using
/// the Prototype viewer convention: (X * 1000, -Z * 1000, Y * 1000), producing a Z-up stage.
/// This matches ModelRenderConverter's display transform; OBJ export keeps raw model coordinates.
/// </summary>
public static class UsdExporter
{
    public static UsdSummary ExportFile(string path, NfsModel model)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        if (model == null) throw new ArgumentNullException(nameof(model));

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        Export(writer, model);
        return new UsdSummary(model.Meshes.Count, model.TotalVertices, model.TotalFaces);
    }

    public static void Export(TextWriter writer, NfsModel model)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        if (model == null) throw new ArgumentNullException(nameof(model));

        writer.Write("#usda 1.0\n");
        writer.Write("(\n");
        writer.Write("    defaultPrim = \"Model\"\n");
        writer.Write("    metersPerUnit = 1\n");
        writer.Write("    upAxis = \"Z\"\n");
        writer.Write(")\n\n");

        (string[] materialNames, List<(string Key, string Name)> materials) = BuildMaterials(model);
        WriteMaterials(writer, materials);
        writer.Write("def Xform \"Model\"\n");
        writer.Write("{\n");

        for (int meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
        {
            NfsMesh mesh = model.Meshes[meshIndex];
            WriteMesh(writer, mesh, meshIndex, materialNames[meshIndex]);
        }

        writer.Write("}\n");
        writer.Flush();
    }

    /// <summary>Stable signature of all UV channels of a mesh. Meshes with identical UVs
    /// share one material; meshes with different UVs get different materials.</summary>
    public static string UvSignature(NfsMesh mesh)
    {
        if (mesh == null) return "EMPTY";
        using var buffer = new MemoryStream();
        foreach (NfsVertex vertex in mesh.Vertices)
        {
            WriteUvTo(buffer, vertex.Uv0);
            WriteUvTo(buffer, vertex.Uv1);
            WriteUvTo(buffer, vertex.Uv2);
        }
        byte[] hash = SHA256.HashData(buffer.ToArray());
        return Convert.ToHexString(hash, 0, 8);
    }

    private static void WriteUvTo(Stream stream, NfsVector2 uv)
    {
        byte[] bytes = BitConverter.GetBytes(uv.X);
        stream.Write(bytes, 0, bytes.Length);
        bytes = BitConverter.GetBytes(uv.Y);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static (string[] PerMesh, List<(string Key, string Name)> Unique) BuildMaterials(NfsModel model)
    {
        var perMesh = new string[model.Meshes.Count];
        var unique = new List<(string, string)>();
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < model.Meshes.Count; i++)
        {
            string key = UvSignature(model.Meshes[i]);
            if (!byKey.TryGetValue(key, out string name))
            {
                name = "Mat_UV_" + key;
                byKey[key] = name;
                unique.Add((key, name));
            }
            perMesh[i] = name;
        }
        return (perMesh, unique);
    }

    private static void WriteMaterials(TextWriter writer, List<(string Key, string Name)> materials)
    {
        writer.Write("def Scope \"Materials\"\n");
        writer.Write("{\n");
        for (int i = 0; i < materials.Count; i++)
        {
            (string key, string matName) = materials[i];
            uint hash = (uint)key.GetHashCode(StringComparison.Ordinal);
            (float r, float g, float b) = ColorForHash(hash, i);
            writer.Write("    def Material \"" + matName + "\"\n");
            writer.Write("    {\n");
            writer.Write("        token outputs:surface.connect = </Materials/" + matName + "/PreviewSurface.outputs:surface>\n");
            writer.Write("        def Shader \"PreviewSurface\"\n");
            writer.Write("        {\n");
            writer.Write("            uniform token info:id = \"UsdPreviewSurface\"\n");
            writer.Write("            color3f inputs:diffuseColor = (" + F(r) + ", " + F(g) + ", " + F(b) + ")\n");
            writer.Write("            float inputs:roughness = 0.5\n");
            writer.Write("            float inputs:metallic = 0\n");
            writer.Write("            token outputs:surface\n");
            writer.Write("        }\n");
            writer.Write("    }\n");
        }
        writer.Write("}\n\n");
    }

    private static void WriteMesh(TextWriter writer, NfsMesh mesh, int meshIndex, string matName)
    {
        string xformName = "Mesh_" + meshIndex.ToString(CultureInfo.InvariantCulture);

        writer.Write("    def Xform \"" + xformName + "\"\n");
        writer.Write("    {\n");
        writer.Write("        def Mesh \"Geom\"\n");
        writer.Write("        {\n");

        // points
        writer.Write("            point3f[] points = [");
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            if (i > 0) writer.Write(", ");
            NfsVector3 p = mesh.Vertices[i].Position;
            // Prototype viewer transform: (X, -Z, Y) * 1000, Z-up display space.
            writer.Write("(" + F(p.X * 1000f) + ", " + F(-p.Z * 1000f) + ", " + F(p.Y * 1000f) + ")");
        }
        writer.Write("]\n");

        // faceVertexCounts / faceVertexIndices
        writer.Write("            int[] faceVertexCounts = [");
        for (int i = 0; i < mesh.Faces.Length; i++)
        {
            if (i > 0) writer.Write(", ");
            writer.Write("3");
        }
        writer.Write("]\n");

        writer.Write("            int[] faceVertexIndices = [");
        for (int i = 0; i < mesh.Faces.Length; i++)
        {
            if (i > 0) writer.Write(", ");
            NfsFace f = mesh.Faces[i];
            writer.Write(f.A.ToString(CultureInfo.InvariantCulture) + ", " +
                         f.B.ToString(CultureInfo.InvariantCulture) + ", " +
                         f.C.ToString(CultureInfo.InvariantCulture));
        }
        writer.Write("]\n");

        // normals (vertex interpolation)
        writer.Write("            normal3f[] normals = [");
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            if (i > 0) writer.Write(", ");
            NfsVector3 n = mesh.Vertices[i].Normal;
            // Apply same Z-up transform to normals (no translation, scale irrelevant for direction).
            writer.Write("(" + F(n.X) + ", " + F(-n.Z) + ", " + F(n.Y) + ")");
        }
        writer.Write("] (\n");
        writer.Write("                interpolation = \"vertex\"\n");
        writer.Write("            )\n");

        WriteUvPrimvar(writer, "primvars:uv0", mesh, v => v.Uv0, 0);
        WriteUvPrimvar(writer, "primvars:uv1", mesh, v => v.Uv1, 1);
        WriteUvPrimvar(writer, "primvars:uv2", mesh, v => v.Uv2, 2);

        writer.Write("            rel material:binding = </Materials/" + matName + ">\n");
        writer.Write("        }\n");
        writer.Write("    }\n");
    }

    private static void WriteUvPrimvar(TextWriter writer, string name, NfsMesh mesh,
        Func<NfsVertex, NfsVector2> selector, int uvIndex)
    {
        if (!HasUvChannel(mesh, uvIndex)) return;

        writer.Write("            float2[] " + name + " = [");
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            if (i > 0) writer.Write(", ");
            NfsVector2 uv = selector(mesh.Vertices[i]);
            // NFS UVs use a top-left origin; USD/Blender use bottom-left. Flip V.
            writer.Write("(" + F(uv.X) + ", " + F(1f - uv.Y) + ")");
        }
        writer.Write("] (\n");
        writer.Write("                interpolation = \"vertex\"\n");
        writer.Write("            )\n");
        writer.Write("            int[] " + name + ":indices = [");
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            if (i > 0) writer.Write(", ");
            writer.Write(i.ToString(CultureInfo.InvariantCulture));
        }
        writer.Write("]\n");
    }

    private static bool HasUvChannel(NfsMesh mesh, int uvIndex)
    {
        // Channel presence follows stride layouts parsed in NfsModel.ReadVertices.
        // 48/52/60 are known from NFSStructuresDef.h; 24/28/32/36/44/56/64 are credible scan-derived layouts.
        bool strideHasChannel = uvIndex switch
        {
            0 => mesh.VertexSize == 24 || mesh.VertexSize == 28 || mesh.VertexSize == 32 || mesh.VertexSize == 36 || mesh.VertexSize == 44 || mesh.VertexSize == 48 || mesh.VertexSize == 52 || mesh.VertexSize == 56 || mesh.VertexSize == 60 || mesh.VertexSize == 64,
            1 => mesh.VertexSize == 36 || mesh.VertexSize == 48 || mesh.VertexSize == 52 || mesh.VertexSize == 60 || mesh.VertexSize == 64,
            2 => mesh.VertexSize == 52,
            _ => false
        };
        if (!strideHasChannel) return false;

        foreach (NfsVertex v in mesh.Vertices)
        {
            NfsVector2 uv = uvIndex switch { 0 => v.Uv0, 1 => v.Uv1, 2 => v.Uv2, _ => default };
            if (uv.X != 0f || uv.Y != 0f) return true;
        }
        return false;
    }

    private static (float r, float g, float b) ColorForHash(uint hash, int meshIndex)
    {
        // Deterministic per-part color: when the header hash is missing (0), use the
        // mesh index so every geometry part is visually distinguishable in Blender.
        uint key = hash != 0 ? hash : (uint)(meshIndex * 2654435761u + 1u);
        float r = ((key >> 16) & 0xFF) / 255f;
        float g = ((key >> 8) & 0xFF) / 255f;
        float b = (key & 0xFF) / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        if (max < 0.2f) { r += 0.4f; g += 0.4f; b += 0.4f; }
        return (r, g, b);
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
