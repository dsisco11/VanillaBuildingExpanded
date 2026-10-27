using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;

using static VanillaBuildingExpanded.Gizmos.GizmoScreenPicking;

namespace VanillaBuildingExpanded.Gizmos;

public enum GizmoShape { Arrow, Circle, Diamond }

public readonly record struct PositionGizmoDraw(
    PositionGizmo Gizmo, GizmoContext Context, PositionGizmoGeometry Geometry);

public readonly record struct PositionGizmoPart(GizmoHandleId Handle, GizmoShape Shape,
    Vector256<double> Origin, Vector256<double> BasisX, Vector256<double> BasisY, Vector256<double> BasisZ)
{
    public Vector256<double> Transform(Vector256<double> point) =>
        Origin + BasisX * point[0] + BasisY * point[1] + BasisZ * point[2];
}

public sealed class PositionGizmoGeometry
{
    private readonly GizmoCamera camera;
    public IReadOnlyList<PositionGizmoPart> Parts { get; }
    public double AxisLength { get; }
    public double PickPadding => 4 * camera.GuiScale;

    private PositionGizmoGeometry(GizmoCamera camera, List<PositionGizmoPart> parts, double axisLength)
    {
        this.camera = camera;
        Parts = parts.AsReadOnly();
        AxisLength = axisLength;
    }

    internal static PositionGizmoGeometry? Create(GizmoCamera camera, Vector256<double> anchor, GizmoShape marker)
    {
        if (!camera.TryProject(anchor, out _)) return null;
        var (right, up, unitsPerPixel) = camera.Billboard(anchor);
        double length = Math.Clamp(64 * camera.GuiScale * unitsPerPixel, 0.125, 8);
        var axes = new[]
        {
            Vector256.Create(1d, 0d, 0d, 0d),
            Vector256.Create(0d, 1d, 0d, 0d),
            Vector256.Create(0d, 0d, 1d, 0d)
        };
        List<PositionGizmoPart> parts = [];
        AddIfVisible(new(PositionGizmo.Center, marker, anchor, right * (length * 7 / 64),
            up * (length * 7 / 64), Vector256<double>.Zero));
        for (int i = 0; i < 3; i++)
            AddIfVisible(new(new(i + 1), GizmoShape.Arrow, anchor,
                axes[i] * length, axes[(i + 1) % 3] * length, axes[(i + 2) % 3] * length));
        return new(camera, parts, length);

        void AddIfVisible(PositionGizmoPart part)
        {
            // Omit clipped parts from both drawing and hit testing, never create invisible targets.
            foreach (var vertex in GizmoShapes.Vertices(part.Shape))
                if (!camera.TryProject(part.Transform(vertex), out _)) return;
            parts.Add(part);
        }
    }

    internal IEnumerable<GizmoHit> HitTest(GizmoPointer pointer)
    {
        // Avoid near-plane roundoff after subtracting very large world coordinates.
        var probe = pointer.FarPoint is { } far
            ? pointer.Ray.Origin + (far - pointer.Ray.Origin) * 0.5
            : pointer.Ray.Origin + pointer.Ray.Direction;
        if (!camera.TryProject(probe, out var screen)) return [];
        List<GizmoHit> hits = [];
        foreach (var part in Parts)
        {
            if (part.Handle == PositionGizmo.Center)
            {
                camera.TryProject(part.Origin, out var center);
                camera.TryProject(part.Origin + part.BasisX, out var edge);
                double radius = Distance2D(center, edge);
                double dx = Math.Abs(screen[0] - center[0]), dy = Math.Abs(screen[1] - center[1]);
                bool within;
                if (part.Shape == GizmoShape.Diamond)
                {
                    within = dx + dy <= radius;
                    var corners = GizmoShapes.Vertices(GizmoShape.Diamond);
                    for (int i = 0; i < corners.Length && !within; i++)
                    {
                        camera.TryProject(part.Transform(corners[i]), out var a);
                        camera.TryProject(part.Transform(corners[(i + 1) % corners.Length]), out var b);
                        within = SegmentDistance(screen, a, b, out _) <= PickPadding;
                    }
                }
                else within = dx * dx + dy * dy <= (radius + PickPadding) * (radius + PickPadding);
                double distance = Vector256.Dot(part.Origin - pointer.Ray.Origin, pointer.Ray.Direction);
                if (within && distance > 0) hits.Add(new(part.Handle, distance, 0));
                continue;
            }
            var vertices = GizmoShapes.Vertices(part.Shape);
            var indices = GizmoShapes.Indices(part.Shape);
            double nearest = double.PositiveInfinity;
            for (int i = 0; i < indices.Length; i += 2)
            {
                var a = part.Transform(vertices[indices[i]]);
                var b = part.Transform(vertices[indices[i + 1]]);
                camera.TryProject(a, out var sa);
                camera.TryProject(b, out var sb);
                if (SegmentDistance(screen, sa, sb, out double t) > PickPadding) continue;
                double worldT = t * sa[3] / ((1 - t) * sb[3] + t * sa[3]);
                double distance = Vector256.Dot(a + (b - a) * worldT - pointer.Ray.Origin, pointer.Ray.Direction);
                if (distance > 0) nearest = Math.Min(nearest, distance);
            }
            if (double.IsFinite(nearest)) hits.Add(new(part.Handle, nearest, 1));
        }
        return hits;
    }

}

internal static class GizmoShapes
{
    private static readonly Vector256<double>[] arrow =
    [
        Vector256.Create(0d, 0d, 0d, 0d), Vector256.Create(1d, 0d, 0d, 0d),
        Vector256.Create(0.8125, 0.09375, 0d, 0d), Vector256.Create(0.8125, -0.09375, 0d, 0d),
        Vector256.Create(0.8125, 0d, 0.09375, 0d), Vector256.Create(0.8125, 0d, -0.09375, 0d)
    ];
    private static readonly int[] arrowIndices = [0, 1, 1, 2, 1, 3, 1, 4, 1, 5];
    private static readonly Vector256<double>[] circle = CreateCircle(16);
    private static readonly int[] circleIndices = LoopIndices(16);
    private static readonly Vector256<double>[] diamond = CreateCircle(4);
    private static readonly int[] diamondIndices = LoopIndices(4);

    internal static ReadOnlySpan<Vector256<double>> Vertices(GizmoShape shape) => shape switch
    {
        GizmoShape.Arrow => arrow, GizmoShape.Circle => circle, GizmoShape.Diamond => diamond,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static ReadOnlySpan<int> Indices(GizmoShape shape) => shape switch
    {
        GizmoShape.Arrow => arrowIndices, GizmoShape.Circle => circleIndices, GizmoShape.Diamond => diamondIndices,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    private static Vector256<double>[] CreateCircle(int count)
    {
        var vertices = new Vector256<double>[count];
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            vertices[i] = Vector256.Create(Math.Cos(angle), Math.Sin(angle), 0d, 0d);
        }
        return vertices;
    }

    private static int[] LoopIndices(int count)
    {
        var indices = new int[count * 2];
        for (int i = 0; i < count; i++) { indices[2 * i] = i; indices[2 * i + 1] = (i + 1) % count; }
        return indices;
    }
}
