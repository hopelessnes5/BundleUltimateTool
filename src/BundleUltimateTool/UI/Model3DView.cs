using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using BundleFormat.Models;
using BundleUltimateTool.Rendering;

namespace BundleUltimateTool.UI;

/// <summary>
/// WinForms DX12 viewport for NFS models. Camera/drag/gizmo semantics ported from
/// PrototypeUltimateTool Model3DViewer; render scheduler ported from PrototypeUltimateTool
/// Model3DViewer.cs (dedicated 60 FPS render thread, high-resolution waitable timer).
/// </summary>
internal sealed class Model3DView : Control
{
    // --- Scheduler (Prototype Model3DViewer.cs:75-99,712-728,1436-1437,3265-3989) ---
    private readonly object _dx12FrameRequestSync = new();
    private readonly ManualResetEvent _dx12RenderStopEvent = new(false);
    private Dx12RenderFrameRequest _dx12LatestFrameRequest;
    private Thread _dx12RenderThread;
    private IntPtr _dx12RenderHwnd;
    private int _dx12InitialRenderWidth;
    private int _dx12InitialRenderHeight;
    private readonly System.Windows.Forms.Timer _dx12RenderTimer = new() { Interval = 16 };
    private volatile Dx12MeshRenderer _dx12Renderer;
    private bool _dx12RendererActive;
    private double _dx12SceneCollectionMilliseconds;
    private double _dx12RenderLoopIntervalMilliseconds;
    private double _dx12RenderWaitMilliseconds;
    private double _dx12RenderWaitOvershootMilliseconds;
    private DateTime _lastStatsUpdateUtc = DateTime.MinValue;

    private Dx12SceneMesh[] _meshes = Array.Empty<Dx12SceneMesh>();
    private string _initError;
    private readonly ViewerDragState _drag = new();
    private Point _lastMouse;
    private double _yaw = -45.0;
    private double _pitch = 15.0;
    private double _distance = 4.0;
    private double _fitDistance = 4.0;
    private double _targetX;
    private double _targetY;
    private double _targetZ;
    private double _sceneCenterX;
    private double _sceneCenterY;
    private double _sceneCenterZ;
    private float _boundingRadius = 1.0f;
    private bool _hasCameraView;
    private readonly Panel _gizmoOverlay;
    private readonly Label _statsOverlay;

    private sealed class Dx12RenderFrameRequest
    {
        public Dx12SceneMesh[] Meshes;
        public Dx12ShadingMode ShadingMode;
        public double Yaw;
        public double Pitch;
        public double Distance;
        public double TargetX;
        public double TargetY;
        public double TargetZ;
        public double BoundingRadius;
        public int Width;
        public int Height;
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint uMilliseconds);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint uMilliseconds);

    // High-resolution waitable timer support (Windows 10 1803+). If unavailable,
    // the render loop falls back to a timed wait with a short final spin interval.
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint Infinite = 0xFFFFFFFF;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr lpTimerAttributes, string lpTimerName, uint dwFlags, uint dwDesiredAccess);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SetWaitableTimer(IntPtr hTimer, ref long lpDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    internal Model3DView()
    {
        Name = "preview.model3d";
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(14, 17, 23);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);

        _gizmoOverlay = new Panel
        {
            Name = "viewer.dx12.orientationGizmo",
            Size = new Size(86, 86),
            BackColor = Color.FromArgb(34, 37, 43),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _gizmoOverlay.Paint += PaintGizmo;
        Controls.Add(_gizmoOverlay);

        _statsOverlay = new Label
        {
            Name = "viewer.dx12.sceneStats",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            BackColor = Color.FromArgb(158, 23, 27, 34),
            ForeColor = Color.FromArgb(246, 236, 241, 248),
            Font = new Font("Consolas", 7.5f, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(6, 4, 6, 4),
            Location = new Point(8, 8),
            Visible = false
        };
        Controls.Add(_statsOverlay);

        // Prototype Model3DViewer.cs:1434-1437 - timer snapshots frame state only; DX12 work on render thread.
        _dx12RenderTimer.Tick += (sender, args) => RenderDx12Frame();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Dx12ShadingMode ShadingMode { get; set; } = Dx12ShadingMode.Solid;

    internal bool HasModel => _meshes.Length > 0;

    internal void SetModel(NfsModel model) => SetModel(model, null, 0, 0);

    /// <summary>
    /// Sets model geometry. When texture data is available from an existing BundleDocument/link lookup,
    /// pass RGBA8 packed pixels; otherwise keep texture null so HasTexture is 0 (no wrong dummy sampled).
    /// </summary>
    internal void SetModel(NfsModel model, uint[] texturePixels, int textureWidth, int textureHeight)
    {
        IReadOnlyList<RenderMesh> renderMeshes = ModelRenderConverter.Convert(model);
        bool hasTexture = texturePixels != null && textureWidth > 0 && textureHeight > 0
            && texturePixels.Length >= textureWidth * textureHeight;
        var meshes = new List<Dx12SceneMesh>(renderMeshes.Count);
        for (int i = 0; i < renderMeshes.Count; i++)
        {
            RenderMesh source = renderMeshes[i];
            meshes.Add(new Dx12SceneMesh
            {
                Key = i,
                Positions = source.Positions,
                Normals = source.Normals,
                Uvs = source.Uvs,
                Indices = source.Indices,
                Transform = Matrix4x4.Identity,
                TexturePixels = hasTexture ? texturePixels : null,
                TextureWidth = hasTexture ? textureWidth : 0,
                TextureHeight = hasTexture ? textureHeight : 0
            });
        }
        _meshes = meshes.ToArray();
        RenderBounds bounds = ModelRenderConverter.GetBounds(renderMeshes);
        _sceneCenterX = bounds.Center.X;
        _sceneCenterY = bounds.Center.Y;
        _sceneCenterZ = bounds.Center.Z;
        // RenderBounds.Radius is AABB half-diagonal; Prototype _boundingRadius is full diagonal.
        _boundingRadius = Math.Max(0.1f, bounds.Radius * 2f);
        FitCameraToBounds(!_hasCameraView);
        PositionOverlays();
        RequestRender();
    }

    internal void ClearModel()
    {
        _meshes = Array.Empty<Dx12SceneMesh>();
        _hasCameraView = false;
        _statsOverlay.Visible = false;
        RequestRender();
    }

    internal void ResetCamera()
    {
        FitCameraToBounds(true);
        _gizmoOverlay.Invalidate();
        RequestRender();
    }

    internal void RequestRender()
    {
        // Continuous 16ms timer publishes latest frame snapshot; nothing else needed.
        if (_initError == null && !_dx12RenderTimer.Enabled && _dx12RendererActive)
            _dx12RenderTimer.Start();
    }

    private void FitCameraToBounds(bool resetOrientation)
    {
        if (resetOrientation || !_hasCameraView)
        {
            _yaw = -45.0;
            _pitch = 15.0;
            _targetX = _sceneCenterX;
            _targetY = _sceneCenterY;
            _targetZ = _sceneCenterZ;
        }

        double diagonal = Math.Max(0.001, _boundingRadius);
        double sphereRadius = diagonal * 0.5;
        double aspect = 1.0;
        if (ClientSize.Width > 1 && ClientSize.Height > 1)
            aspect = ClientSize.Width / (double)ClientSize.Height;

        // Keep the same horizontal 45-degree field of view as the prototype.
        // Use the narrower of the horizontal and vertical half-angles so models fit the viewport.
        double horizontalHalfFov = 45.0 * Math.PI / 360.0;
        double verticalHalfFov = Math.Atan(Math.Tan(horizontalHalfFov) / Math.Max(0.1, aspect));
        double limitingHalfFov = Math.Max(0.01, Math.Min(horizontalHalfFov, verticalHalfFov));
        double fitDistance = Math.Max(diagonal * 0.02, sphereRadius / Math.Sin(limitingHalfFov) * 1.12);

        if (resetOrientation || !_hasCameraView)
        {
            _distance = fitDistance;
        }
        else
        {
            double zoomFactor = _fitDistance > 0.000001 ? _distance / _fitDistance : 1.0;
            if (double.IsNaN(zoomFactor) || double.IsInfinity(zoomFactor)) zoomFactor = 1.0;
            zoomFactor = Math.Max(0.001, Math.Min(80.0, zoomFactor));
            _distance = fitDistance * zoomFactor;
        }

        _fitDistance = fitDistance;
        _hasCameraView = true;
    }

    // --- Initialization (Prototype Model3DViewer.cs:3265-3288) ---
    private void InitializeDx12Renderer()
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (_dx12Renderer != null || (_dx12RenderThread != null && _dx12RenderThread.IsAlive)) return;

        try
        {
            _dx12InitialRenderWidth = Math.Max(1, ClientSize.Width);
            _dx12InitialRenderHeight = Math.Max(1, ClientSize.Height);
            _dx12RenderHwnd = Handle;
            // DX12 device/swap-chain and shader/PSO setup can take a noticeable amount of time.
            // Run initialization on the same worker that will own Render/Resize/Dispose so the UI
            // stays responsive and no D3D12 object is used concurrently from multiple threads.
            _dx12RendererActive = true;
            _initError = null;
            PositionOverlays();
            StartDx12RenderThread();
            _dx12RenderTimer.Start();
        }
        catch (Exception ex)
        {
            _initError = "Direct3D 12 unavailable: " + ex.Message;
            ApplicationLog.Write("DX12_INIT_FAILED", _initError, ex);
            Invalidate();
        }
    }

    // --- Thread start (Prototype Model3DViewer.cs:3753-3766) ---
    private void StartDx12RenderThread()
    {
        if (_dx12RenderThread != null && _dx12RenderThread.IsAlive) return;
        _dx12RenderStopEvent.Reset();
        _dx12RenderThread = new Thread(Dx12RenderThreadMain)
        {
            IsBackground = true,
            Name = "BundleUltimateTool DX12 Render"
        };
        // Keep STA-compatible (matches Prototype; safe for future WPF-interop/clipboard usage).
        _dx12RenderThread.SetApartmentState(ApartmentState.STA);
        _dx12RenderThread.Start();
    }

    // --- Frame snapshot publish (Prototype Model3DViewer.cs:3768-3819) ---
    private void RenderDx12Frame()
    {
        if (!_dx12RendererActive || _dx12RenderHwnd == IntPtr.Zero ||
            ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        try
        {
            long sceneBuildStart = Stopwatch.GetTimestamp();
            // Plain-array scene: no WPF collection freeze required; snapshot is the array reference.
            Dx12SceneMesh[] meshesSnapshot = _meshes;
            _dx12SceneCollectionMilliseconds = (Stopwatch.GetTimestamp() - sceneBuildStart) * 1000.0 / Stopwatch.Frequency;

            var frame = new Dx12RenderFrameRequest
            {
                Meshes = meshesSnapshot,
                ShadingMode = ShadingMode,
                Yaw = _yaw,
                Pitch = _pitch,
                Distance = _distance,
                TargetX = _targetX,
                TargetY = _targetY,
                TargetZ = _targetZ,
                BoundingRadius = _boundingRadius,
                Width = ClientSize.Width,
                Height = ClientSize.Height
            };
            lock (_dx12FrameRequestSync)
                _dx12LatestFrameRequest = frame;

            if ((DateTime.UtcNow - _lastStatsUpdateUtc).TotalMilliseconds >= 250)
            {
                UpdateStatsOverlay();
                _lastStatsUpdateUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _dx12RenderTimer.Stop();
            ApplicationLog.Write("DX12_FRAME_SNAPSHOT_FAILED", ex.Message, ex);
        }
    }

    // --- Render thread (Prototype Model3DViewer.cs:3821-3931) ---
    private void Dx12RenderThreadMain()
    {
        Dx12MeshRenderer renderer = null;
        bool timerResolutionRequested = false;
        IntPtr waitableTimer = IntPtr.Zero;
        int renderWidth = Math.Max(1, _dx12InitialRenderWidth);
        int renderHeight = Math.Max(1, _dx12InitialRenderHeight);
        try
        {
            try { timerResolutionRequested = timeBeginPeriod(1) == 0; }
            catch { timerResolutionRequested = false; }

            // Prefer a true high-resolution timer over repeated relative 16 ms waits.
            // A relative WaitOne(16) accumulates scheduler oversleep and was producing
            // a ~40 Hz loop even though each individual measured interval often looked ~17 ms.
            try
            {
                waitableTimer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
                if (waitableTimer == IntPtr.Zero)
                    waitableTimer = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TimerAllAccess);
            }
            catch { waitableTimer = IntPtr.Zero; }

            if (_dx12RenderHwnd == IntPtr.Zero)
                throw new InvalidOperationException("DX12 render thread started without a valid panel HWND.");
            renderer = new Dx12MeshRenderer(_dx12RenderHwnd, renderWidth, renderHeight);
            _dx12Renderer = renderer;

            const double TargetFramesPerSecond = 60.0;
            long framePeriodTicks = Math.Max(1L, (long)Math.Round(Stopwatch.Frequency / TargetFramesPerSecond));
            long nextFrameDeadlineTicks = Stopwatch.GetTimestamp();
            long previousLoopStartTicks = 0;
            IntPtr[] waitHandles = waitableTimer != IntPtr.Zero
                ? new[] { _dx12RenderStopEvent.SafeWaitHandle.DangerousGetHandle(), waitableTimer }
                : null;

            while (!_dx12RenderStopEvent.WaitOne(0))
            {
                long loopStartTicks = Stopwatch.GetTimestamp();
                if (previousLoopStartTicks != 0)
                {
                    Volatile.Write(ref _dx12RenderLoopIntervalMilliseconds,
                        (loopStartTicks - previousLoopStartTicks) * 1000.0 / Stopwatch.Frequency);
                }
                previousLoopStartTicks = loopStartTicks;

                Dx12RenderFrameRequest frame;
                lock (_dx12FrameRequestSync)
                    frame = _dx12LatestFrameRequest;

                if (frame != null && frame.Width > 0 && frame.Height > 0)
                {
                    if (frame.Width != renderWidth || frame.Height != renderHeight)
                    {
                        renderer.Resize(frame.Width, frame.Height);
                        renderWidth = frame.Width;
                        renderHeight = frame.Height;
                    }

                    renderer.Render(frame.Meshes, frame.ShadingMode,
                        frame.Yaw, frame.Pitch, frame.Distance,
                        frame.TargetX, frame.TargetY, frame.TargetZ, frame.BoundingRadius);
                }

                // Pace against an absolute Stopwatch deadline. This avoids adding the render
                // work duration to a fixed 16 ms wait and measures timer overshoot directly.
                nextFrameDeadlineTicks += framePeriodTicks;
                long nowTicks = Stopwatch.GetTimestamp();
                if (nextFrameDeadlineTicks <= nowTicks)
                    nextFrameDeadlineTicks = nowTicks + framePeriodTicks; // Drop a missed deadline; never busy-loop to catch up.

                if (!WaitForDx12FrameDeadline(waitableTimer, waitHandles, nextFrameDeadlineTicks))
                    break;
            }
        }
        catch (Exception ex)
        {
            ApplicationLog.Write(_dx12Renderer == null ? "DX12_INIT_FAILED" : "DX12_RENDER_THREAD_FAILED", ex.Message, ex);
            try
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke((Action)(() => HandleDx12RenderThreadFailure(ex.Message, _dx12Renderer == null)));
            }
            catch { }
        }
        finally
        {
            if (waitableTimer != IntPtr.Zero)
            {
                try { CloseHandle(waitableTimer); } catch { }
            }
            if (timerResolutionRequested)
            {
                try { timeEndPeriod(1); } catch { }
            }
            if (renderer != null)
            {
                try { renderer.Dispose(); }
                catch (Exception ex) { ApplicationLog.Write("DX12_RENDER_THREAD_DISPOSE_FAILED", ex.Message, ex); }
            }
            if (ReferenceEquals(_dx12Renderer, renderer))
                _dx12Renderer = null;
        }
    }

    // --- Deadline wait (Prototype Model3DViewer.cs:3933-3989) ---
    private bool WaitForDx12FrameDeadline(IntPtr waitableTimer, IntPtr[] waitHandles, long deadlineTicks)
    {
        long waitStartTicks = Stopwatch.GetTimestamp();
        long remainingTicks = deadlineTicks - waitStartTicks;
        if (remainingTicks > 0)
        {
            if (waitableTimer != IntPtr.Zero && waitHandles != null)
            {
                long dueTime100ns = -Math.Max(1L, (long)Math.Ceiling(
                    remainingTicks * (10_000_000.0 / Stopwatch.Frequency)));
                if (SetWaitableTimer(waitableTimer, ref dueTime100ns, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    uint waitResult = WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, Infinite);
                    if (waitResult == WaitObject0) return false; // Stop event.
                    if (waitResult != WaitObject0 + 1 && waitResult != WaitFailed)
                    {
                        // Unexpected wait result: fall back below rather than terminate rendering.
                    }
                    else if (waitResult == WaitObject0 + 1)
                    {
                        long wakeTicks = Stopwatch.GetTimestamp();
                        Volatile.Write(ref _dx12RenderWaitMilliseconds,
                            (wakeTicks - waitStartTicks) * 1000.0 / Stopwatch.Frequency);
                        Volatile.Write(ref _dx12RenderWaitOvershootMilliseconds,
                            Math.Max(0.0, (wakeTicks - deadlineTicks) * 1000.0 / Stopwatch.Frequency));
                        return !_dx12RenderStopEvent.WaitOne(0);
                    }
                }
            }

            // Fallback path for systems without the high-resolution waitable timer.
            // Sleep most of the remaining duration, then spin only for the final <1 ms.
            while (!_dx12RenderStopEvent.WaitOne(0))
            {
                remainingTicks = deadlineTicks - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0) break;
                double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
                if (remainingMs > 1.0)
                {
                    int sleepMs = Math.Max(1, (int)Math.Floor(remainingMs - 0.5));
                    if (_dx12RenderStopEvent.WaitOne(sleepMs)) return false;
                }
                else
                {
                    Thread.SpinWait(32);
                }
            }
            if (_dx12RenderStopEvent.WaitOne(0)) return false;
        }

        long wakeTimeTicks = Stopwatch.GetTimestamp();
        Volatile.Write(ref _dx12RenderWaitMilliseconds,
            Math.Max(0.0, (wakeTimeTicks - waitStartTicks) * 1000.0 / Stopwatch.Frequency));
        Volatile.Write(ref _dx12RenderWaitOvershootMilliseconds,
            Math.Max(0.0, (wakeTimeTicks - deadlineTicks) * 1000.0 / Stopwatch.Frequency));
        return !_dx12RenderStopEvent.WaitOne(0);
    }

    private void HandleDx12RenderThreadFailure(string message, bool initializationFailure)
    {
        if (IsDisposed) return;
        _dx12RendererActive = false;
        _dx12RenderTimer.Stop();
        _initError = initializationFailure
            ? "Direct3D 12 unavailable: " + message
            : "DX12 render failed: " + message;
        _statsOverlay.Visible = false;
        Invalidate();
    }

    private void UpdateStatsOverlay()
    {
        Dx12MeshRenderer renderer = _dx12Renderer;
        if (renderer == null || _meshes.Length == 0) { _statsOverlay.Visible = false; return; }
        var snapshot = new Dx12StatsSnapshot
        {
            Fps = renderer.Fps,
            FrameTimeMilliseconds = renderer.FrameTimeMilliseconds,
            SubmittedFrameIntervalMilliseconds = renderer.SubmittedFrameIntervalMilliseconds,
            CpuRenderMilliseconds = renderer.CpuRenderMilliseconds,
            PresentAndSignalMilliseconds = renderer.PresentAndSignalMilliseconds,
            RenderCallRate = renderer.RenderCallRate,
            RenderCallCount = renderer.RenderCallCount,
            SubmittedFrameCount = renderer.SubmittedFrameCount,
            SkippedFrames = renderer.SkippedFrames,
            MsaaSampleCount = renderer.MsaaSampleCount,
            MeshCount = renderer.MeshCount,
            DrawCallCount = renderer.DrawCallCount,
            VertexCount = renderer.VertexCount,
            EdgeCount = renderer.EdgeCount,
            FaceCount = renderer.FaceCount,
            TriangleCount = renderer.TriangleCount,
            TextureMemoryBytes = renderer.TextureMemoryBytes,
            Mode = ShadingMode.ToString(),
            SceneCollectionMilliseconds = _dx12SceneCollectionMilliseconds,
            RenderLoopIntervalMilliseconds = Volatile.Read(ref _dx12RenderLoopIntervalMilliseconds),
            RenderWaitMilliseconds = Volatile.Read(ref _dx12RenderWaitMilliseconds),
            RenderWaitOvershootMilliseconds = Volatile.Read(ref _dx12RenderWaitOvershootMilliseconds)
        };
        _statsOverlay.Text = Dx12StatsFormatter.Format(snapshot);
        _statsOverlay.Visible = true;
        _statsOverlay.BringToFront();
        _gizmoOverlay.BringToFront();
    }

    private void PositionOverlays()
    {
        _gizmoOverlay.Location = new Point(Math.Max(4, ClientSize.Width - _gizmoOverlay.Width - 8), 8);
        _gizmoOverlay.BringToFront();
        _statsOverlay.BringToFront();
    }

    private void PaintGizmo(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(34, 37, 43));
        float cx = _gizmoOverlay.ClientSize.Width * 0.5f;
        float cy = _gizmoOverlay.ClientSize.Height * 0.5f + 2.0f;
        var basis = ViewerCameraMath.CreateBasis(_yaw, _pitch);
        float length = 27.0f;
        var xAxis = basis.Project(new ViewerCameraVector(1, 0, 0), cx, cy, length);
        var yAxis = basis.Project(new ViewerCameraVector(0, 1, 0), cx, cy, length);
        var zAxis = basis.Project(new ViewerCameraVector(0, 0, 1), cx, cy, length);
        DrawGizmoAxis(g, cx, cy, (float)xAxis.X, (float)xAxis.Y, Color.FromArgb(242, 82, 82), "X");
        DrawGizmoAxis(g, cx, cy, (float)yAxis.X, (float)yAxis.Y, Color.FromArgb(80, 205, 120), "Y");
        DrawGizmoAxis(g, cx, cy, (float)zAxis.X, (float)zAxis.Y, Color.FromArgb(82, 150, 255), "Z");
    }

    private static void DrawGizmoAxis(Graphics g, float cx, float cy, float ex, float ey, Color color, string label)
    {
        using var pen = new Pen(color, 2.4f);
        g.DrawLine(pen, cx, cy, ex, ey);
        using var brush = new SolidBrush(color);
        using var font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
        g.DrawString(label, font, brush, ex - 4.0f, ey - 8.0f);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeDx12Renderer();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionOverlays();
        RequestRender();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _lastMouse = e.Location;
        _drag.Orbiting = e.Button == MouseButtons.Left;
        _drag.Panning = e.Button == MouseButtons.Right;
        if (_drag.Orbiting || _drag.Panning)
        {
            Capture = true;
            if (!Capture) _drag.LostCapture();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_drag.Orbiting && !_drag.Panning) return;
        int dx = e.X - _lastMouse.X;
        int dy = e.Y - _lastMouse.Y;
        _lastMouse = e.Location;
        if (_drag.Orbiting)
        {
            _yaw += dx * 0.5;
            _pitch = Math.Max(-89.0, Math.Min(89.0, _pitch + dy * 0.5));
        }
        else if (_drag.Panning)
        {
            var delta = ViewerCameraMath.CreateBasis(_yaw, _pitch).PanDelta(dx, dy, _distance);
            _targetX += delta.X;
            _targetY += delta.Y;
            _targetZ += delta.Z;
        }
        _gizmoOverlay.Invalidate();
        RequestRender();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) _drag.Orbiting = false;
        if (e.Button == MouseButtons.Right) _drag.Panning = false;
        if (!_drag.Orbiting && !_drag.Panning) Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        double factor = e.Delta > 0 ? 0.85 : 1.15;
        double minDistance = Math.Max(0.001, _boundingRadius * 0.004);
        double maxDistance = Math.Max(minDistance * 100.0, _boundingRadius * 80.0);
        _distance = Math.Max(minDistance, Math.Min(maxDistance, _distance * factor));
        _gizmoOverlay.Invalidate();
        RequestRender();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_initError == null) return;
        using var brush = new SolidBrush(Color.FromArgb(200, 200, 205));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(_initError, Font, brush, ClientRectangle, format);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // DX12 owns the client area; avoid GDI background erase flicker.
        if (_dx12RendererActive && _initError == null) return;
        base.OnPaintBackground(pevent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dx12RenderTimer.Stop();
            _dx12RenderTimer.Dispose();
            _dx12RenderStopEvent.Set();
            Thread renderThread = _dx12RenderThread;
            bool joined = true;
            if (renderThread != null && renderThread.IsAlive && renderThread != Thread.CurrentThread)
            {
                // The render thread owns all post-initialization DX12 calls and disposes the renderer on exit.
                // Do not dispose resources concurrently with its command submission.
                joined = renderThread.Join(1500);
                if (!joined)
                    ApplicationLog.Write("DX12_RENDER_THREAD_STOP_TIMEOUT",
                        "Render thread did not stop within 1500 ms; it will finish disposal in the background.");
            }
            else if (_dx12Renderer != null)
            {
                try { _dx12Renderer.Dispose(); }
                catch (Exception ex) { ApplicationLog.Write("DX12_RENDER_DISPOSE_FAILED", ex.Message, ex); }
                _dx12Renderer = null;
            }
            if (joined)
                _dx12RenderStopEvent.Dispose();
        }
        base.Dispose(disposing);
    }
}
