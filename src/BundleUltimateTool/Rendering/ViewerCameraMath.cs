using System;

namespace BundleUltimateTool.Rendering
{
    internal readonly record struct ViewerCameraVector(double X, double Y, double Z);
    internal readonly record struct ViewerCameraPoint(double X, double Y);

    // Same Z-up orbit used by WPF PerspectiveCamera and the DX12 CreateLookAt view.
    // Right and Up are unit vectors in world space; Backward points from target to eye.
    internal readonly record struct ViewerCameraBasis(
        ViewerCameraVector Right, ViewerCameraVector Up, ViewerCameraVector Backward)
    {
        internal ViewerCameraVector OrbitOffset(double distance) =>
            new(distance * Backward.X, distance * Backward.Y, distance * Backward.Z);

        internal ViewerCameraPoint Project(ViewerCameraVector axis, double centerX, double centerY, double length) =>
            new(centerX + length * (axis.X * Right.X + axis.Y * Right.Y + axis.Z * Right.Z),
                centerY - length * (axis.X * Up.X + axis.Y * Up.Y + axis.Z * Up.Z));

        // Mouse Y grows downward. Translate the target opposite screen-right and
        // toward screen-up so scene points follow the cursor on both screen axes.
        internal ViewerCameraVector PanDelta(double dragX, double dragY, double distance)
        {
            double scale = distance * 0.0015;
            double horizontal = dragX * scale;
            double vertical = dragY * scale;
            return new(-horizontal * Right.X + vertical * Up.X,
                -horizontal * Right.Y + vertical * Up.Y,
                -horizontal * Right.Z + vertical * Up.Z);
        }
    }

    internal static class ViewerCameraMath
    {
        internal static ViewerCameraBasis CreateBasis(double yawDegrees, double pitchDegrees)
        {
            double yaw = yawDegrees * Math.PI / 180.0;
            double pitch = pitchDegrees * Math.PI / 180.0;
            double sinYaw = Math.Sin(yaw), cosYaw = Math.Cos(yaw);
            double sinPitch = Math.Sin(pitch), cosPitch = Math.Cos(pitch);
            return new ViewerCameraBasis(
                new ViewerCameraVector(-cosYaw, sinYaw, 0.0),
                new ViewerCameraVector(-sinYaw * sinPitch, -cosYaw * sinPitch, cosPitch),
                new ViewerCameraVector(cosPitch * sinYaw, cosPitch * cosYaw, sinPitch));
        }
    }
}
