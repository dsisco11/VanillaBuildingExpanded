using System;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Reads a position and an independent logical axis, without consulting meshes or handle IDs.</summary>
public sealed class GizmoAxisFrameSource : IGizmoFrameSource
{
    private readonly Func<GizmoPosition> position;
    private readonly Func<Vector256<double>> direction;

    /// <summary>Creates a live position/axis adapter.</summary>
    /// <param name="position">The borrowed position reader, including dimension.</param>
    /// <param name="direction">The borrowed world-axis reader; need not return a unit vector.</param>
    /// <exception cref="ArgumentNullException">A reader is null.</exception>
    public GizmoAxisFrameSource(Func<GizmoPosition> position, Func<Vector256<double>> direction)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(direction);
        this.position = position;
        this.direction = direction;
    }

    /// <summary>Reads and validates the current logical axis.</summary>
    /// <param name="context">The expected dimension.</param>
    /// <returns>A normalized frame independent of rendered shape.</returns>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="ArgumentException">The supplied position or direction is invalid.</exception>
    /// <exception cref="InvalidOperationException">The position belongs to another dimension.</exception>
    public GizmoFrame GetFrame(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = position();
        var frame = new GizmoFrame(current.Coordinates, direction(), current.Dimension);
        frame.Validate(context);
        return frame;
    }
}

/// <summary>Supplies a camera-facing logical plane, including for origins outside the visible clip interval.</summary>
public sealed class GizmoCameraFrameSource : IGizmoFrameSource
{
    private readonly Func<GizmoPosition> position;

    /// <summary>Creates a live camera-plane adapter.</summary>
    /// <param name="position">The borrowed position/dimension reader.</param>
    /// <exception cref="ArgumentNullException">The reader is null.</exception>
    public GizmoCameraFrameSource(Func<GizmoPosition> position)
    {
        ArgumentNullException.ThrowIfNull(position);
        this.position = position;
    }

    /// <summary>Builds an orthogonal camera plane without requiring projected vertices.</summary>
    /// <param name="context">The perspective camera and expected dimension.</param>
    /// <returns>The current plane; retaining this value freezes it for a capture.</returns>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="InvalidOperationException">The camera is missing or the dimension differs.</exception>
    /// <exception cref="ArgumentException">The position or camera basis is invalid.</exception>
    public GizmoFrame GetFrame(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var camera = context.Camera ?? throw new InvalidOperationException("A camera-plane source requires a camera.");
        var current = position();
        var normal = camera.ViewDirection;
        var view = camera.View;
        var right = Vector256.Create(view[0], view[4], view[8], 0d);
        right -= normal * Vector256.Dot(right, normal);
        var frame = new GizmoFrame(current.Coordinates, normal, current.Dimension,
            right, GizmoFrame.Cross(right, normal));
        frame.Validate(context);
        return frame;
    }

    /// <summary>Rejects missing-camera measurement context without disguising other frame failures.</summary>
    /// <param name="context">The perspective context to read.</param>
    /// <param name="frame">The current plane on success; otherwise default.</param>
    /// <returns>Whether a camera is available; invalid dimensions or bases still throw.</returns>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="InvalidOperationException">The position belongs to another dimension.</exception>
    /// <exception cref="ArgumentException">The position or camera basis is invalid.</exception>
    public bool TryGetFrame(GizmoContext context, out GizmoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(context);
        frame = default;
        if (context.Camera is null) return false;
        frame = GetFrame(context);
        return true;
    }
}

/// <summary>Reuses an immutable logical frame, for example a drag-start snapshot.</summary>
public sealed class GizmoFixedFrameSource : IGizmoFrameSource
{
    private readonly GizmoFrame frame;

    /// <summary>Retains a validated frame without retaining a mesh or live position reader.</summary>
    /// <param name="frame">The frame to freeze.</param>
    /// <exception cref="InvalidOperationException">The frame is default/uninitialized.</exception>
    public GizmoFixedFrameSource(GizmoFrame frame)
    {
        frame.Validate(new(frame.Dimension));
        this.frame = frame;
    }

    /// <summary>Returns the frozen frame after validating its dimension.</summary>
    /// <param name="context">The expected dimension.</param>
    /// <returns>The unchanged frame.</returns>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="InvalidOperationException">The context has another dimension.</exception>
    public GizmoFrame GetFrame(GizmoContext context)
    {
        frame.Validate(context);
        return frame;
    }
}
