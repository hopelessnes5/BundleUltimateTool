using System;
using System.Globalization;

namespace BundleUltimateTool.Rendering
{
    internal static class Dx12StatsFormatter
    {
        internal static string Format(Dx12StatsSnapshot snapshot)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "BundleUltimateTool  |  Direct3D 12\r\n" +
                "FPS {0:0.0}   Call Δ {1:0.00} ms   Submit Δ {2:0.00} ms\r\n" +
                "CPU {3:0.00} ms   Prep {4:0.00} ms   Present+Signal {5:0.00} ms\r\n" +
                "Loop {6:0.0} Hz (avg Δ {19:0.00} ms)   Calls/Frames {7:N0}/{8:N0}   Fence skips {9:N0}\r\n" +
                "Mode: {10}   MSAA {11}x\r\n" +
                "Objects {12:N0}   Draw calls {13:N0}\r\n" +
                "Vertices {14:N0}   Edges {15:N0}\r\n" +
                "Faces {16:N0}   Triangles {17:N0}\r\n" +
                "Texture data {18:0.0} MiB\r\n" +
                "Last Δ {20:0.00} ms   Wait {21:0.00} ms   Late {22:0.00} ms",
                snapshot.Fps, snapshot.FrameTimeMilliseconds, snapshot.SubmittedFrameIntervalMilliseconds,
                snapshot.CpuRenderMilliseconds, snapshot.SceneCollectionMilliseconds, snapshot.PresentAndSignalMilliseconds,
                snapshot.RenderCallRate, snapshot.RenderCallCount, snapshot.SubmittedFrameCount,
                snapshot.SkippedFrames, snapshot.Mode ?? string.Empty, snapshot.MsaaSampleCount, snapshot.MeshCount, snapshot.DrawCallCount,
                snapshot.VertexCount, snapshot.EdgeCount, snapshot.FaceCount, snapshot.TriangleCount,
                snapshot.TextureMemoryBytes / (1024.0 * 1024.0),
                snapshot.RenderCallRate > 0.01 ? 1000.0 / snapshot.RenderCallRate : 0.0,
                snapshot.RenderLoopIntervalMilliseconds,
                snapshot.RenderWaitMilliseconds,
                snapshot.RenderWaitOvershootMilliseconds);
        }
    }
}
