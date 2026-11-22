namespace BundleUltimateTool.Rendering
{
    // Captured numeric values only: formatting never reads a live renderer or UI.
    // Per-property capture uses the existing renderer's thread-safe getters; it is
    // not a new global/atomic snapshot of all counters across a render frame.
    internal readonly record struct Dx12StatsSnapshot
    {
        internal double Fps { get; init; }
        internal double FrameTimeMilliseconds { get; init; }
        internal double SubmittedFrameIntervalMilliseconds { get; init; }
        internal double CpuRenderMilliseconds { get; init; }
        internal double PresentAndSignalMilliseconds { get; init; }
        internal double RenderCallRate { get; init; }
        internal long RenderCallCount { get; init; }
        internal long SubmittedFrameCount { get; init; }
        internal long SkippedFrames { get; init; }
        internal int MsaaSampleCount { get; init; }
        internal int MeshCount { get; init; }
        internal int DrawCallCount { get; init; }
        internal int VertexCount { get; init; }
        internal int EdgeCount { get; init; }
        internal int FaceCount { get; init; }
        internal int TriangleCount { get; init; }
        internal long TextureMemoryBytes { get; init; }
        internal string Mode { get; init; }
        internal double SceneCollectionMilliseconds { get; init; }
        internal double RenderLoopIntervalMilliseconds { get; init; }
        internal double RenderWaitMilliseconds { get; init; }
        internal double RenderWaitOvershootMilliseconds { get; init; }
    }
}
