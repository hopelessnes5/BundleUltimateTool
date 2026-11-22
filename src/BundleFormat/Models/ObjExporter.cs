using System.Globalization;
using System.Text;

namespace BundleFormat.Models;

public readonly record struct ObjSummary(int Meshes, int Vertices, int Faces);

/// <summary>Port of CNFSModel::SaveAsOBJ: UTF-8 text, \\n line endings, invariant floats,
/// per-mesh groups and a global 1-based vertex index offset.</summary>
public static class ObjExporter
{
    public static string Build(NfsModel model)
    {
        var text = new StringBuilder();
        Export(new StringWriter(text, CultureInfo.InvariantCulture), model);
        return text.ToString();
    }

    public static ObjSummary Export(TextWriter writer, NfsModel model)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        if (model == null) throw new ArgumentNullException(nameof(model));

        writer.Write("# Generated from Need For Speed: Hot Pursuit model file\n");
        writer.Write("# Exported with NFS:HP Bundle Explorer (Modernized 2026)\n\n");

        int meshIndex = 0;
        int lastIndex = 0;
        int totalVertices = 0;
        int totalFaces = 0;

        foreach (NfsMesh mesh in model.Meshes)
        {
            writer.Write("g Object_" + meshIndex++.ToString(CultureInfo.InvariantCulture) + "\n");

            foreach (NfsVertex vertex in mesh.Vertices)
            {
                writer.Write("v " + Format(vertex.Position.X) + " " + Format(vertex.Position.Y) + " " +
                             Format(vertex.Position.Z) + "\n");
            }
            writer.Write("# " + mesh.Vertices.Length.ToString(CultureInfo.InvariantCulture) + " vertexes\n\n");

            // C++ SaveAsOBJ always emits one vt per vertex; NFSVertex defaults UV to (0,0)
            // for strides without a UV specialization. Keep that exact behavior.
            foreach (NfsVertex vertex in mesh.Vertices)
                writer.Write("vt " + Format(vertex.Uv.X) + " " + Format(vertex.Uv.Y) + "\n");
            writer.Write("# " + mesh.Vertices.Length.ToString(CultureInfo.InvariantCulture) + " texcoords\n\n");

            foreach (NfsVertex vertex in mesh.Vertices)
            {
                writer.Write("vn " + Format(vertex.Normal.X) + " " + Format(vertex.Normal.Y) + " " +
                             Format(vertex.Normal.Z) + "\n");
            }
            writer.Write("# " + mesh.Vertices.Length.ToString(CultureInfo.InvariantCulture) + " normals\n\n");

            foreach (NfsFace face in mesh.Faces)
            {
                int a = face.A + lastIndex + 1;
                int b = face.B + lastIndex + 1;
                int c = face.C + lastIndex + 1;
                writer.Write("f " + a + "/" + a + "/" + a + " " + b + "/" + b + "/" + b + " " +
                             c + "/" + c + "/" + c + "\n");
            }
            writer.Write("# " + mesh.Faces.Length.ToString(CultureInfo.InvariantCulture) + " faces\n\n");

            lastIndex += mesh.Vertices.Length;
            totalVertices += mesh.Vertices.Length;
            totalFaces += mesh.Faces.Length;
        }

        writer.Flush();
        return new ObjSummary(model.Meshes.Count, totalVertices, totalFaces);
    }

    public static ObjSummary ExportFile(string path, NfsModel model)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        return Export(writer, model);
    }

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
