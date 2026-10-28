using System;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Visual primitives shared by resolved geometry, rendering and default picking, independent of interaction.</summary>
public enum GizmoShape
{
    /// <summary>A unit-length line arrow with four head strokes.</summary>
    Arrow,
    /// <summary>A unit-radius 16-segment planar marker.</summary>
    Circle,
    /// <summary>A unit-radius four-segment planar marker.</summary>
    Diamond
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
