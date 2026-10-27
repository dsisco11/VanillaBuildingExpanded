using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

public readonly record struct GizmoPosition
{
    public Vector256<double> Coordinates { get; }
    public int Dimension { get; }

    public GizmoPosition(Vector256<double> coordinates, int dimension)
    {
        if (!GizmoRay.IsCoordinate(coordinates)) throw new ArgumentOutOfRangeException(nameof(coordinates));
        ArgumentOutOfRangeException.ThrowIfNegative(dimension);
        Coordinates = coordinates;
        Dimension = dimension;
    }
}

public enum PositionGizmoNotice { MissingTarget, InvalidValue, DegenerateAxis, ExcessiveJump }
public readonly record struct PositionGizmoEdit(GizmoPosition Start, GizmoPosition Candidate, GizmoHandleId Handle);

public sealed class PositionGizmo : Gizmo<GizmoPosition>
{
    public static readonly GizmoHandleId Center = new(0);
    public static readonly GizmoHandleId X = new(1);
    public static readonly GizmoHandleId Y = new(2);
    public static readonly GizmoHandleId Z = new(3);
    private readonly Action<PositionGizmoDraw> draw;
    private readonly Func<GizmoPointer, GizmoPosition?> centerTarget;
    private readonly Func<PositionGizmoEdit, GizmoPosition?>? constrain;
    private readonly GizmoShape marker;
    private PositionGizmoGeometry? geometry;
    private GizmoContext? drawnContext;
    private GizmoPosition start;
    private Vector256<double> axis;
    private Vector256<double> normal;
    private double initialCoordinate;
    private double lastCoordinate;
    private double initialDistance;
    private int component;
    public override int Dimension => Value.Dimension;
    public event Action<PositionGizmoNotice>? NoticeRequested;

    public PositionGizmo(GizmoPosition initialValue, Action<PositionGizmoDraw> draw,
        Func<GizmoPointer, GizmoPosition?> centerTarget,
        Func<PositionGizmoEdit, GizmoPosition?>? constrain = null, GizmoShape marker = GizmoShape.Circle)
        : base(initialValue)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(centerTarget);
        if (marker is not (GizmoShape.Circle or GizmoShape.Diamond)) throw new ArgumentOutOfRangeException(nameof(marker));
        this.draw = draw;
        this.centerTarget = centerTarget;
        this.constrain = constrain;
        this.marker = marker;
    }

    protected override void RenderCore(GizmoContext context)
    {
        var camera = context.Camera ?? throw new InvalidOperationException("Positioning requires a perspective camera.");
        geometry = PositionGizmoGeometry.Create(camera, Value.Coordinates, marker);
        drawnContext = context;
        if (geometry is not null) draw(new(this, context, geometry));
    }

    protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer) =>
        ReferenceEquals(drawnContext, pointer.Context) && geometry is not null
            ? geometry.HitTest(pointer) : [];

    protected override bool BeginInteractionCore(GizmoHandleId handle, GizmoPointer pointer)
    {
        start = Value;
        if (handle == Center) return true;
        component = handle.Value - 1;
        if (component is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(handle));
        axis = Vector256<double>.Zero.WithElement(component, 1d);
        normal = pointer.Ray.Direction - axis * Vector256.Dot(pointer.Ray.Direction, axis);
        double squared = Vector256.Dot(normal, normal);
        if (squared <= 1e-4) return Reject(PositionGizmoNotice.DegenerateAxis);
        normal /= Math.Sqrt(squared);
        var cameraPosition = pointer.Context.Camera?.EyePosition ?? pointer.Ray.Origin;
        var difference = start.Coordinates - cameraPosition;
        initialDistance = Math.Sqrt(Vector256.Dot(difference, difference));
        if (!double.IsFinite(initialDistance) || !TryCoordinate(pointer.Ray, out initialCoordinate))
            return Reject(PositionGizmoNotice.DegenerateAxis);
        lastCoordinate = initialCoordinate;
        return true;
    }

    protected override void UpdateInteractionCore(GizmoPointer pointer)
    {
        GizmoPosition candidate;
        if (CapturedHandle == Center)
        {
            if (centerTarget(pointer) is not { } target) { Reject(PositionGizmoNotice.MissingTarget); return; }
            candidate = target;
        }
        else
        {
            if (!TryCoordinate(pointer.Ray, out double coordinate)) { Reject(PositionGizmoNotice.DegenerateAxis); return; }
            if (Math.Abs(coordinate - lastCoordinate) > Math.Max(64, 8 * initialDistance))
            {
                Reject(PositionGizmoNotice.ExcessiveJump);
                return;
            }
            var coordinates = start.Coordinates.WithElement(component,
                start.Coordinates[component] + (coordinate - initialCoordinate));
            if (!GizmoRay.IsCoordinate(coordinates)) { Reject(PositionGizmoNotice.InvalidValue); return; }
            candidate = new(coordinates, start.Dimension);
            if (!Publish(candidate)) return;
            lastCoordinate = coordinate;
            return;
        }
        Publish(candidate);
    }

    private bool Publish(GizmoPosition candidate)
    {
        if (!IsInteracting || IsDisposed) return false;
        if (candidate.Dimension != start.Dimension) return Reject(PositionGizmoNotice.InvalidValue);
        var accepted = constrain is null ? candidate : constrain(new(start, candidate, CapturedHandle!.Value));
        if (!IsInteracting || IsDisposed) return false;
        if (accepted is not { } value || value.Dimension != start.Dimension
            || (CapturedHandle != Center && !OtherComponentsUnchanged(value.Coordinates)))
            return Reject(PositionGizmoNotice.InvalidValue);
        Value = value;
        return true;
    }

    private bool OtherComponentsUnchanged(Vector256<double> coordinates)
    {
        for (int i = 0; i < 3; i++)
            if (i != component && coordinates[i] != start.Coordinates[i]) return false;
        return true;
    }

    private bool TryCoordinate(GizmoRay ray, out double coordinate)
    {
        coordinate = 0;
        double denominator = Vector256.Dot(ray.Direction, normal);
        if (Math.Abs(denominator) <= 1e-2) return false;
        double t = Vector256.Dot(start.Coordinates - ray.Origin, normal) / denominator;
        if (!double.IsFinite(t) || t < 0 || t > 10000 * Math.Max(1, initialDistance)) return false;
        coordinate = Vector256.Dot(ray.Origin + ray.Direction * t - start.Coordinates, axis);
        return double.IsFinite(coordinate);
    }

    private bool Reject(PositionGizmoNotice notice)
    {
        NoticeRequested?.Invoke(notice);
        return false;
    }

    protected override void DisposeCore()
    {
        geometry = null;
        drawnContext = null;
        NoticeRequested = null;
    }
}
