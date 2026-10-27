using System;
using System.Runtime.Intrinsics;

using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Immutable perspective snapshot. Matrices operate relative to ReferencePosition.</summary>
public sealed class GizmoCamera
{
    private readonly double[] view;
    private readonly double[] projection;
    private readonly double[] combined;
    private readonly double[] inverse;
    public Vector256<double> ReferencePosition { get; }
    public Vector256<double> EyePosition { get; }
    public int Width { get; }
    public int Height { get; }
    public int WindowWidth { get; }
    public int WindowHeight { get; }
    public double GuiScale { get; }
    public bool MouseGrabbed { get; }
    public EnumCameraMode CameraMode { get; }
    internal ReadOnlySpan<double> View => view;
    internal ReadOnlySpan<double> Projection => projection;

    public GizmoCamera(double[] relativeView, double[] projection, Vector256<double> referencePosition,
        int width, int height, double guiScale = 1, bool mouseGrabbed = false,
        EnumCameraMode cameraMode = EnumCameraMode.FirstPerson, int? windowWidth = null, int? windowHeight = null)
    {
        if (width <= 0 || height <= 0 || windowWidth <= 0 || windowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Camera viewport/window must be nonempty.");
        if (!double.IsFinite(guiScale) || guiScale <= 0) throw new ArgumentOutOfRangeException(nameof(guiScale));
        ValidateMatrix(relativeView);
        ValidateMatrix(projection);
        if (!GizmoRay.IsCoordinate(referencePosition)) throw new ArgumentOutOfRangeException(nameof(referencePosition));
        view = (double[])relativeView.Clone();
        this.projection = (double[])projection.Clone();
        combined = Mat4d.Multiply(new double[16], this.projection, view);
        inverse = new double[16];
        if (Mat4d.Invert(inverse, combined) is null || !Array.TrueForAll(inverse, double.IsFinite))
            throw new ArgumentException("Camera matrices must have a finite inverse.", nameof(relativeView));
        ReferencePosition = referencePosition;
        var inverseView = Mat4d.Invert(new double[16], view);
        if (inverseView is null) throw new ArgumentException("Camera view must be invertible.", nameof(relativeView));
        var eye = Transform(inverseView, Vector256.Create(0d, 0d, 0d, 1d));
        if (!double.IsFinite(eye[3]) || Math.Abs(eye[3]) <= 1e-12)
            throw new ArgumentException("Camera eye must be finite.", nameof(relativeView));
        EyePosition = (eye / eye[3]).WithElement(3, 0d) + ReferencePosition;
        if (!GizmoRay.IsCoordinate(EyePosition))
            throw new ArgumentException("Camera eye must be finite.", nameof(relativeView));
        Width = width;
        Height = height;
        WindowWidth = windowWidth ?? width;
        WindowHeight = windowHeight ?? height;
        GuiScale = guiScale;
        MouseGrabbed = mouseGrabbed;
        CameraMode = cameraMode;
    }

    internal static GizmoContext Capture(ICoreClientAPI api)
    {
        var player = api.World.Player;
        int dimension = player.Entity.Pos.Dimension;
        var camera = player.Entity.CameraPos;
        var relativeView = Mat4d.Translate(new double[16], api.Render.PerspectiveViewMat,
            camera.X, camera.Y, camera.Z);
        var reference = Vector256.Create(camera.X,
            camera.Y - (double)dimension * BlockPos.DimensionBoundary, camera.Z, 0d);
        return new(dimension, new GizmoCamera(relativeView, api.Render.PerspectiveProjectionMat, reference,
            api.Render.FrameWidth, api.Render.FrameHeight, RuntimeEnv.GUIScale, api.Input.MouseGrabbed,
            player.CameraMode));
    }

    public GizmoPointer CreatePointer(GizmoContext context, double windowX, double windowY)
    {
        if (!ReferenceEquals(context.Camera, this))
            throw new ArgumentException("Pointer must use its camera's shared context.", nameof(context));
        if (!double.IsFinite(windowX) || !double.IsFinite(windowY))
            throw new ArgumentOutOfRangeException(nameof(windowX));
        double x = MouseGrabbed ? WindowWidth / 2 : windowX;
        double y = MouseGrabbed ? WindowHeight / 2 : windowY;
        double ndcX = 2 * x / WindowWidth - 1;
        double ndcY = 1 - 2 * y / WindowHeight;
        var near = Unproject(ndcX, ndcY, -1);
        var far = Unproject(ndcX, ndcY, 1);
        return new(context, new GizmoRay(near, far - near), far);
    }

    private Vector256<double> Unproject(double x, double y, double z)
    {
        var homogeneous = Transform(inverse, Vector256.Create(x, y, z, 1d));
        double w = homogeneous[3];
        if (!double.IsFinite(w) || Math.Abs(w) <= 1e-12)
            throw new InvalidOperationException("The camera cannot unproject this pointer.");
        var point = (homogeneous / w).WithElement(3, 0d) + ReferencePosition;
        if (!GizmoRay.IsCoordinate(point))
            throw new InvalidOperationException("The camera produced a nonfinite pointer.");
        return point;
    }

    public bool TryProject(Vector256<double> localPosition, out Vector256<double> screen)
    {
        screen = default;
        if (!GizmoRay.IsCoordinate(localPosition)) return false;
        var clip = Transform(combined, (localPosition - ReferencePosition).WithElement(3, 1d));
        double w = clip[3];
        if (!double.IsFinite(w) || w <= 1e-12) return false;
        var ndc = clip / w;
        if (!double.IsFinite(ndc[0]) || !double.IsFinite(ndc[1])
            || !double.IsFinite(ndc[2]) || ndc[2] < -1 - 1e-6 || ndc[2] > 1 + 1e-6) return false;
        screen = Vector256.Create((ndc[0] + 1) * Width / 2d,
            (1 - ndc[1]) * Height / 2d, ndc[2], w);
        return true;
    }

    internal (Vector256<double> Right, Vector256<double> Up, double UnitsPerPixel) Billboard(
        Vector256<double> anchor)
    {
        if (!TryProject(anchor, out var screen)) throw new ArgumentException("Anchor is outside the camera clip interval.");
        double x = 2 * screen[0] / Width - 1;
        double y = 1 - 2 * screen[1] / Height;
        var right = Unproject(x + 2d / Width, y, screen[2]) - anchor;
        var up = Unproject(x, y + 2d / Height, screen[2]) - anchor;
        double length = Math.Sqrt(Vector256.Dot(right, right));
        double upLength = Math.Sqrt(Vector256.Dot(up, up));
        if (!double.IsFinite(length) || length <= 1e-12 || !double.IsFinite(upLength) || upLength <= 1e-12)
            throw new InvalidOperationException("Camera cannot size the gizmo.");
        return (right / length, up / upLength, length);
    }

    internal static Vector256<double> Transform(ReadOnlySpan<double> matrix, Vector256<double> vector) =>
        Vector256.Create(matrix[0], matrix[1], matrix[2], matrix[3]) * vector[0]
        + Vector256.Create(matrix[4], matrix[5], matrix[6], matrix[7]) * vector[1]
        + Vector256.Create(matrix[8], matrix[9], matrix[10], matrix[11]) * vector[2]
        + Vector256.Create(matrix[12], matrix[13], matrix[14], matrix[15]) * vector[3];

    private static void ValidateMatrix(double[] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.Length != 16 || !Array.TrueForAll(matrix, double.IsFinite))
            throw new ArgumentException("A finite 4x4 matrix is required.", nameof(matrix));
    }
}
