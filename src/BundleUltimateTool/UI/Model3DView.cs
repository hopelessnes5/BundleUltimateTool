using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Numerics;
using System.Windows.Forms;
using BundleFormat.Models;
using BundleUltimateTool.Rendering;

namespace BundleUltimateTool.UI;

/// <summary>
/// WinForms DX12 viewport for NFS models. Camera/drag/gizmo semantics ported from
/// PrototypeUltimateTool Model3DViewer (WPF removed; plain NFS model source).
/// </summary>
internal sealed class Model3DView : Control
{
    private readonly Timer _renderTimer = new() { Interval = 33 };
    private Dx12MeshRenderer _renderer;
    private Dx12SceneMesh[] _meshes = Array.Empty<Dx12SceneMesh>();
    private string _initError;
    private readonly ViewerDragState _drag = new();
    private Point _lastMouse;
    private bool _dirty;
    private bool _loggedRender;
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
    private DateTime _lastStatsUpdateUtc = DateTime.MinValue;

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

        _renderTimer.Tick += (s, e) =>
        {
            if (!_dirty) { _renderTimer.Stop(); return; }
            _dirty = false;
            RenderFrame();
        };
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
        _renderer?.EnsureScene(_meshes);
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
        if (_initError != null) return;
        _dirty = true;
        if (!_renderTimer.Enabled) _renderTimer.Start();
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

    private void RenderFrame()
    {
        if (_renderer == null || IsDisposed) return;
        try
        {
            _renderer.Render(_meshes, ShadingMode, _yaw, _pitch, _distance,
                _targetX, _targetY, _targetZ, _boundingRadius);
            UpdateStatsOverlay();
            if (!_loggedRender)
            {
                _loggedRender = true;
                ApplicationLog.Write("DX12_RENDER_OK",
                    $"vertices={_renderer.VertexCount}, triangles={_renderer.TriangleCount}, " +
                    $"shading={ShadingMode}, distance={_distance:F2}, radius={_boundingRadius:F2}");
            }
        }
        catch (Exception ex)
        {
            _initError = "DX12 render failed: " + ex.Message;
            ApplicationLog.Write("DX12_RENDER_FAILED", _initError, ex);
            _renderTimer.Stop();
            Invalidate();
        }
    }

    private void UpdateStatsOverlay()
    {
        if (_renderer == null || _meshes.Length == 0) { _statsOverlay.Visible = false; return; }
        DateTime now = DateTime.UtcNow;
        if ((now - _lastStatsUpdateUtc).TotalMilliseconds < 100.0) return;
        _lastStatsUpdateUtc = now;
        var snapshot = new Dx12StatsSnapshot
        {
            Fps = _renderer.Fps,
            FrameTimeMilliseconds = _renderer.FrameTimeMilliseconds,
            SubmittedFrameIntervalMilliseconds = _renderer.SubmittedFrameIntervalMilliseconds,
            CpuRenderMilliseconds = _renderer.CpuRenderMilliseconds,
            PresentAndSignalMilliseconds = _renderer.PresentAndSignalMilliseconds,
            RenderCallRate = _renderer.RenderCallRate,
            RenderCallCount = _renderer.RenderCallCount,
            SubmittedFrameCount = _renderer.SubmittedFrameCount,
            SkippedFrames = _renderer.SkippedFrames,
            MsaaSampleCount = _renderer.MsaaSampleCount,
            MeshCount = _renderer.MeshCount,
            DrawCallCount = _renderer.DrawCallCount,
            VertexCount = _renderer.VertexCount,
            EdgeCount = _renderer.EdgeCount,
            FaceCount = _renderer.FaceCount,
            TriangleCount = _renderer.TriangleCount,
            TextureMemoryBytes = _renderer.TextureMemoryBytes,
            Mode = ShadingMode.ToString()
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
        if (_renderer != null) return;
        try
        {
            _renderer = new Dx12MeshRenderer(Handle, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            ApplicationLog.Write("DX12_INIT_OK", $"device created, {ClientSize.Width}x{ClientSize.Height}");
        }
        catch (Exception ex)
        {
            _initError = "Direct3D 12 unavailable: " + ex.Message;
            ApplicationLog.Write("DX12_INIT_FAILED", _initError, ex);
        }
        RequestRender();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionOverlays();
        _renderer?.Resize(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
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
        if (_renderer != null && _initError == null) return;
        base.OnPaintBackground(pevent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop();
            _renderTimer.Dispose();
            _renderer?.Dispose();
            _renderer = null;
        }
        base.Dispose(disposing);
    }
}
