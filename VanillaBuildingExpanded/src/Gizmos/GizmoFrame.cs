using System;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Validated logical axis/plane frame in double-precision (X, Y, Z, 0) coordinates.</summary>
public readonly record struct GizmoFrame
{
    /// <summary>Gets the local-world origin.</summary>
    public Vector256<double> Origin { get; }

    /// <summary>Gets the unit axis direction, or plane normal when tangents are present.</summary>
    public Vector256<double> Direction { get; }

    /// <summary>Gets the first unit plane tangent, or null for an axis-only frame.</summary>
    public Vector256<double>? TangentX { get; }

    /// <summary>Gets the second unit plane tangent, or null for an axis-only frame.</summary>
    public Vector256<double>? TangentY { get; }

    /// <summary>Gets the explicit local-Y dimension identity.</summary>
    public int Dimension { get; }

    /// <summary>Creates a normalized axis or orthogonal plane frame without consulting geometry.</summary>
    /// <param name="origin">The finite local-world origin.</param>
    /// <param name="direction">A nonzero axis direction or plane normal.</param>
    /// <param name="dimension">The nonnegative dimension.</param>
    /// <param name="tangentX">Optional first nonzero plane tangent; requires tangentY.</param>
    /// <param name="tangentY">Optional second nonzero plane tangent; requires tangentX.</param>
    /// <exception cref="ArgumentOutOfRangeException">Coordinates/directions are invalid or dimension is negative.</exception>
    /// <exception cref="ArgumentException">Tangents are unpaired or the normalized basis is not orthogonal within 1e-8.</exception>
    public GizmoFrame(Vector256<double> origin, Vector256<double> direction, int dimension,
        Vector256<double>? tangentX = null, Vector256<double>? tangentY = null)
    {
        if (!GizmoRay.IsCoordinate(origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        ArgumentOutOfRangeException.ThrowIfNegative(dimension);
        if (tangentX.HasValue != tangentY.HasValue)
            throw new ArgumentException("Plane tangents must be supplied together.");
        Origin = origin;
        Direction = Normalize(direction, nameof(direction));
        Dimension = dimension;
        TangentX = tangentX is { } x ? Normalize(x, nameof(tangentX)) : null;
        TangentY = tangentY is { } y ? Normalize(y, nameof(tangentY)) : null;
        if (TangentX is { } tx && TangentY is { } ty
            && (Math.Abs(Vector256.Dot(Direction, tx)) > 1e-8
                || Math.Abs(Vector256.Dot(Direction, ty)) > 1e-8
                || Math.Abs(Vector256.Dot(tx, ty)) > 1e-8))
            throw new ArgumentException("A plane frame requires mutually orthogonal directions.");
    }

    /// <summary>Rejects default frames and dimension mismatches at a consumer boundary.</summary>
    /// <param name="context">The frame's expected camera/dimension context.</param>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="InvalidOperationException">The frame is default/uninitialized or belongs to another dimension.</exception>
    public void Validate(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Dimension != context.Dimension || !GizmoRay.IsCoordinate(Direction)
            || Math.Abs(Vector256.Dot(Direction, Direction) - 1) > 1e-8)
            throw new InvalidOperationException("A spatial source must supply a valid frame in the current dimension.");
    }

    /// <summary>Normalizes a finite, nondegenerate SIMD direction.</summary>
    /// <param name="value">The direction to normalize.</param>
    /// <param name="parameter">The argument name for validation failures.</param>
    /// <returns>The unit direction.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The direction is nonfinite, has a nonzero fourth lane or is degenerate.</exception>
    private static Vector256<double> Normalize(Vector256<double> value, string parameter)
    {
        double length = Math.Sqrt(Vector256.Dot(value, value));
        if (!GizmoRay.IsCoordinate(value) || !double.IsFinite(length) || length <= 1e-12)
            throw new ArgumentOutOfRangeException(parameter);
        return value / length;
    }
}
