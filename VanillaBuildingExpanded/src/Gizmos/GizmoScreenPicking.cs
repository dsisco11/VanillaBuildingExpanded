using System;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Shared screen-space distances for persistent-handle picking.</summary>
internal static class GizmoScreenPicking
{
    /// <summary>Measures framebuffer X/Y distance, ignoring depth and homogeneous W.</summary>
    internal static double Distance2D(Vector256<double> a, Vector256<double> b)
    {
        var difference = (a - b).WithElement(2, 0d).WithElement(3, 0d);
        return Math.Sqrt(Vector256.Dot(difference, difference));
    }

    /// <summary>Measures distance to a projected segment and returns its clamped screen interpolation.</summary>
    internal static double SegmentDistance(Vector256<double> point, Vector256<double> a,
        Vector256<double> b, out double t)
    {
        double dx = b[0] - a[0], dy = b[1] - a[1];
        double squared = dx * dx + dy * dy;
        t = squared <= 1e-12 ? 0
            : Math.Clamp(((point[0] - a[0]) * dx + (point[1] - a[1]) * dy) / squared, 0, 1);
        double ex = point[0] - a[0] - t * dx, ey = point[1] - a[1] - t * dy;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
