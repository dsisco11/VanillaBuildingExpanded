using System;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Double-precision SIMD coordinates use lanes (X, Y, Z, 0).</summary>
public readonly record struct GizmoRay
{
    public Vector256<double> Origin { get; }
    public Vector256<double> Direction { get; }

    public GizmoRay(Vector256<double> origin, Vector256<double> direction)
    {
        if (!IsCoordinate(origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        double length = Math.Sqrt(Vector256.Dot(direction, direction));
        if (!IsCoordinate(direction) || !double.IsFinite(length) || length <= 1e-12)
            throw new ArgumentOutOfRangeException(nameof(direction));
        Origin = origin;
        Direction = direction / length;
    }

    internal bool IsValid => IsCoordinate(Origin) && IsCoordinate(Direction)
        && !Vector256.EqualsAll(Direction, Vector256<double>.Zero);

    internal static bool IsCoordinate(Vector256<double> value) =>
        double.IsFinite(value[0]) && double.IsFinite(value[1]) && double.IsFinite(value[2])
        && value[3] == 0;
}

/// <summary>
/// Shared, session-local camera/world frame identity. Native adapters supply projection data
/// at their boundary; rays and gizmo values use local Y and an explicit dimension.
/// </summary>
public sealed class GizmoContext
{
    public int Dimension { get; }
    public GizmoCamera? Camera { get; }

    public GizmoContext(int dimension, GizmoCamera? camera = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dimension);
        Dimension = dimension;
        Camera = camera;
    }
}

public readonly record struct GizmoPointer
{
    public GizmoContext Context { get; }
    public GizmoRay Ray { get; }
    public Vector256<double>? FarPoint { get; }

    public GizmoPointer(GizmoContext context, GizmoRay ray, Vector256<double>? farPoint = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ray.IsValid) throw new ArgumentException("A valid visual ray is required.", nameof(ray));
        if (farPoint is { } far && !GizmoRay.IsCoordinate(far))
            throw new ArgumentOutOfRangeException(nameof(farPoint));
        Context = context;
        Ray = ray;
        FarPoint = farPoint;
    }

    internal void Validate()
    {
        if (Context is null || !Ray.IsValid)
            throw new ArgumentException("A camera context and valid visual ray are required.");
    }
}

public readonly record struct GizmoHandleId(int Value);

/// <summary>
/// Lower priority values win within one instance (e.g. center before arrow roots).
/// Distance is positive distance along the normalized visual ray, not player reach.
/// </summary>
public readonly record struct GizmoHit
{
    public GizmoHandleId Handle { get; }
    public double Distance { get; }
    public int Priority { get; }

    public GizmoHit(GizmoHandleId handle, double distance, int priority = 0)
    {
        if (!double.IsFinite(distance) || distance <= 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        Handle = handle;
        Distance = distance;
        Priority = priority;
    }
}
