using System;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Continuous double-precision world coordinates with explicit local-Y dimension identity.</summary>
public readonly record struct GizmoPosition
{
    /// <summary>Gets coordinates in (X, Y, Z, 0) lanes.</summary>
    public Vector256<double> Coordinates { get; }
    /// <summary>Gets the nonnegative dimension identifier, independent of coordinate encoding.</summary>
    public int Dimension { get; }

    /// <summary>Creates a finite continuous position without block snapping.</summary>
    /// <param name="coordinates">Finite (X, Y, Z, 0) coordinates.</param>
    /// <param name="dimension">The nonnegative dimension identifier.</param>
    /// <exception cref="ArgumentOutOfRangeException">Coordinates are invalid or the dimension is negative.</exception>
    public GizmoPosition(Vector256<double> coordinates, int dimension)
    {
        if (!GizmoRay.IsCoordinate(coordinates)) throw new ArgumentOutOfRangeException(nameof(coordinates));
        ArgumentOutOfRangeException.ThrowIfNegative(dimension);
        Coordinates = coordinates;
        Dimension = dimension;
    }
}

/// <summary>Position measurement and constraint rejection reasons exposed through the session notice path.</summary>
public enum PositionGizmoNotice
{
    /// <summary>The candidate or constrained value is invalid.</summary>
    InvalidValue,
    /// <summary>The pointer cannot provide a stable forward axis measurement.</summary>
    DegenerateAxis,
    /// <summary>The pointer cannot provide a stable forward plane intersection.</summary>
    DegeneratePlane,
    /// <summary>The unsnapped measurement exceeds the accepted-step limit.</summary>
    ExcessiveJump
}

/// <summary>A position constraint request; identity is diagnostic, not a movement selector.</summary>
/// <param name="Start">The frozen position at modification begin.</param>
/// <param name="Candidate">The measured continuous candidate before application constraints.</param>
/// <param name="Handle">The arbitrary identity of the initiating control.</param>
public readonly record struct PositionGizmoEdit(GizmoPosition Start, GizmoPosition Candidate, GizmoHandleId Handle);
