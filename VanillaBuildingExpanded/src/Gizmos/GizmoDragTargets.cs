using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Prepares analytic axis measurement without consulting shapes, handle IDs or application values.</summary>
public sealed class GizmoAxisDragTarget : IGizmoDragTarget
{
    private readonly IGizmoFrameSource frame;
    private readonly Func<GizmoFrame, IInteractionParameterSource<double>> parameter;
    private readonly IGizmoEditStateTarget edit;

    /// <summary>Creates a reusable measurement definition with capture-local parameter binding.</summary>
    /// <param name="frame">Borrowed logical frame source.</param>
    /// <param name="parameter">Creates/returns a borrowed binding for the exact frozen frame; called only after valid measurement.</param>
    /// <param name="edit">Borrowed adapter for the existing edit transaction.</param>
    /// <exception cref="ArgumentNullException">A source or factory is null.</exception>
    public GizmoAxisDragTarget(IGizmoFrameSource frame,
        Func<GizmoFrame, IInteractionParameterSource<double>> parameter, IGizmoEditStateTarget edit)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(edit);
        this.frame = frame;
        this.parameter = parameter;
        this.edit = edit;
    }

    /// <summary>Freezes the frame, grab coordinate and drag plane without initializing edits.</summary>
    /// <param name="pointer">Validated press pointer.</param>
    /// <param name="session">The caller-owned capture session on success.</param>
    /// <param name="rejection">A notice name for the existing error/notice path on failure.</param>
    /// <returns>Whether the axis is measurable.</returns>
    /// <exception cref="ArgumentException">The pointer is invalid.</exception>
    /// <exception cref="InvalidOperationException">The frame is invalid or the parameter factory returns null.</exception>
    public bool TryPrepare(GizmoPointer pointer, [NotNullWhen(true)] out IGizmoDragSession? session,
        [NotNullWhen(false)] out string? rejection)
    {
        pointer.Validate();
        var captured = frame.GetFrame(pointer.Context);
        captured.Validate(pointer.Context);
        session = null;
        rejection = nameof(PositionGizmoNotice.DegenerateAxis);
        var normal = pointer.Ray.Direction - captured.Direction * Vector256.Dot(pointer.Ray.Direction, captured.Direction);
        double squared = Vector256.Dot(normal, normal);
        if (squared <= 1e-4) return false;
        normal /= Math.Sqrt(squared);
        var measurement = new GizmoDragMeasurement(captured, normal, pointer);
        if (!measurement.TryIntersection(pointer.Ray, out var initial)) return false;
        double coordinate = Vector256.Dot(initial, captured.Direction);
        if (!double.IsFinite(coordinate)) return false;
        var source = parameter(captured) ?? throw new InvalidOperationException("The parameter factory returned null.");
        session = new GizmoAxisDragSession(measurement, coordinate, source, edit);
        rejection = null;
        return true;
    }
}

/// <summary>Prepares analytic plane measurement, including a camera-facing frame frozen at press.</summary>
public sealed class GizmoPlaneDragTarget : IGizmoDragTarget
{
    private readonly IGizmoFrameSource frame;
    private readonly Func<GizmoFrame, IInteractionParameterSource<GizmoPlaneParameter>> parameter;
    private readonly IGizmoEditStateTarget edit;

    /// <summary>Creates a plane definition independent of visual geometry and value type.</summary>
    /// <param name="frame">Borrowed plane source, normally camera-derived.</param>
    /// <param name="parameter">Provides the binding for the exact frozen frame.</param>
    /// <param name="edit">Borrowed existing edit-state adapter.</param>
    /// <exception cref="ArgumentNullException">A source or factory is null.</exception>
    public GizmoPlaneDragTarget(IGizmoFrameSource frame,
        Func<GizmoFrame, IInteractionParameterSource<GizmoPlaneParameter>> parameter, IGizmoEditStateTarget edit)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(edit);
        this.frame = frame;
        this.parameter = parameter;
        this.edit = edit;
    }

    /// <summary>Captures the plane and initial intersection; later camera changes cannot rebase it.</summary>
    /// <param name="pointer">Validated press pointer.</param>
    /// <param name="session">The caller-owned session on success.</param>
    /// <param name="rejection">An existing notice name when measurement is unavailable.</param>
    /// <returns>Whether the plane is measurable.</returns>
    /// <exception cref="ArgumentException">The pointer is invalid or the frame lacks plane tangents.</exception>
    /// <exception cref="InvalidOperationException">The frame is invalid or the factory returns null.</exception>
    public bool TryPrepare(GizmoPointer pointer, [NotNullWhen(true)] out IGizmoDragSession? session,
        [NotNullWhen(false)] out string? rejection)
    {
        pointer.Validate();
        var captured = frame.GetFrame(pointer.Context);
        captured.Validate(pointer.Context);
        if (captured.TangentX is null)
            throw new ArgumentException("Plane measurement requires plane tangents.", nameof(frame));
        session = null;
        rejection = nameof(PositionGizmoNotice.DegeneratePlane);
        var measurement = new GizmoDragMeasurement(captured, captured.Direction, pointer);
        if (!measurement.TryIntersection(pointer.Ray, out var initial)) return false;
        var source = parameter(captured) ?? throw new InvalidOperationException("The parameter factory returned null.");
        session = new GizmoPlaneDragSession(measurement, initial, source, edit);
        rejection = null;
        return true;
    }
}

/// <summary>Frozen analytic intersection guards shared by scalar and planar sessions.</summary>
internal readonly struct GizmoDragMeasurement(GizmoFrame frame, Vector256<double> normal, GizmoPointer pointer)
{
    internal GizmoFrame Frame { get; } = frame;
    private double InitialDistance { get; } = Distance(frame.Origin
        - (pointer.Context.Camera?.EyePosition ?? pointer.Ray.Origin));
    internal double StepLimit => Math.Max(64, 8 * InitialDistance);
    internal static double Distance(Vector256<double> delta) => Math.Sqrt(Vector256.Dot(delta, delta));

    internal bool TryIntersection(GizmoRay ray, out Vector256<double> intersection)
    {
        intersection = default;
        double denominator = Vector256.Dot(ray.Direction, normal);
        if (!double.IsFinite(InitialDistance) || Math.Abs(denominator) <= 1e-2) return false;
        double t = Vector256.Dot(Frame.Origin - ray.Origin, normal) / denominator;
        if (!double.IsFinite(t) || t < 0 || t > 10000 * Math.Max(1, InitialDistance)) return false;
        // Anchor-relative intersections preserve distant-world precision and the initial grab offset.
        intersection = (ray.Origin - Frame.Origin) + ray.Direction * t;
        return GizmoRay.IsCoordinate(intersection);
    }
}

/// <summary>Owns only capture bookkeeping; borrowed sources retain their composition lifetime.</summary>
internal abstract class GizmoDragSession<TParameter>(
    GizmoDragMeasurement measurement, IInteractionParameterSource<TParameter> parameter,
    IGizmoEditStateTarget edit) : IGizmoDragSession
{
    private enum State { Prepared, Beginning, Active, Ending, Ended }
    private State state;
    private bool parameterBegun, editBegun, updating;
    protected GizmoDragMeasurement Measurement { get; } = measurement;
    protected TParameter InitialParameter { get; private set; } = default!;

    public bool TryBegin([NotNullWhen(false)] out string? rejection)
    {
        rejection = "session-inactive-or-busy";
        if (state != State.Prepared) return false;
        state = State.Beginning;
        try
        {
            InitialParameter = parameter.Parameter;
            parameter.BeginModification();
            parameterBegun = true;
            if (state != State.Beginning) { rejection = "session-ended"; return false; }
            if (!edit.TryBegin(out rejection)) return false;
            editBegun = true;
            if (state != State.Beginning) { rejection = "session-ended"; return false; }
            state = State.Active;
            rejection = null;
            return true;
        }
        finally
        {
            if (state != State.Active)
            {
                state = State.Ended;
                try { EndParameter(); }
                finally { CancelEdit(); }
            }
        }
    }

    public bool TryUpdate(GizmoPointer pointer, [NotNullWhen(false)] out string? rejection)
    {
        pointer.Validate();
        rejection = "session-inactive-or-busy";
        if (state != State.Active || updating) return false;
        if (pointer.Context.Dimension != Measurement.Frame.Dimension)
        {
            rejection = nameof(PositionGizmoNotice.InvalidValue);
            return false;
        }
        updating = true;
        try
        {
            if (!TryMeasure(pointer.Ray, out var candidate, out rejection)) return false;
            if (!parameter.TrySetParameter(candidate, out rejection)) return false;
            if (state != State.Active) { rejection = "session-ended"; return false; }
            AcceptMeasurement();
            rejection = null;
            return true;
        }
        finally { updating = false; }
    }

    protected abstract bool TryMeasure(GizmoRay ray, out TParameter parameter,
        [NotNullWhen(false)] out string? rejection);
    protected abstract void AcceptMeasurement();

    public void Complete() => End(true);
    public void Cancel() => End(false);
    public void Dispose() => Cancel();

    private void End(bool complete)
    {
        if (state is State.Ending or State.Ended) return;
        if (state == State.Beginning) { state = State.Ending; return; }
        state = State.Ending;
        try
        {
            bool parametersEnded = false;
            try { EndParameter(); parametersEnded = true; }
            finally
            {
                if (!complete || !parametersEnded) CancelEdit();
            }
            if (editBegun)
            {
                editBegun = false;
                edit.Complete();
            }
        }
        finally { state = State.Ended; }
    }

    private void EndParameter()
    {
        if (!parameterBegun) return;
        parameterBegun = false;
        parameter.EndModification();
    }

    private void CancelEdit()
    {
        if (!editBegun) return;
        editBegun = false;
        edit.Cancel();
    }
}

internal sealed class GizmoAxisDragSession(GizmoDragMeasurement measurement, double initial,
    IInteractionParameterSource<double> parameter, IGizmoEditStateTarget edit)
    : GizmoDragSession<double>(measurement, parameter, edit)
{
    private readonly double initialCoordinate = initial;
    private double last = initial, pending;
    protected override bool TryMeasure(GizmoRay ray, out double parameter, [NotNullWhen(false)] out string? rejection)
    {
        parameter = default;
        rejection = nameof(PositionGizmoNotice.DegenerateAxis);
        if (!Measurement.TryIntersection(ray, out var intersection)) return false;
        pending = Vector256.Dot(intersection, Measurement.Frame.Direction);
        if (!double.IsFinite(pending)) return false;
        rejection = nameof(PositionGizmoNotice.ExcessiveJump);
        if (Math.Abs(pending - last) > Measurement.StepLimit) return false;
        parameter = InitialParameter + (pending - initialCoordinate);
        rejection = nameof(PositionGizmoNotice.InvalidValue);
        if (!double.IsFinite(parameter)) return false;
        rejection = null;
        return true;
    }
    protected override void AcceptMeasurement() => last = pending;
}

internal sealed class GizmoPlaneDragSession(GizmoDragMeasurement measurement, Vector256<double> initial,
    IInteractionParameterSource<GizmoPlaneParameter> parameter, IGizmoEditStateTarget edit)
    : GizmoDragSession<GizmoPlaneParameter>(measurement, parameter, edit)
{
    private readonly Vector256<double> initialIntersection = initial;
    private Vector256<double> last = initial, pending;
    protected override bool TryMeasure(GizmoRay ray, out GizmoPlaneParameter parameter,
        [NotNullWhen(false)] out string? rejection)
    {
        parameter = default;
        rejection = nameof(PositionGizmoNotice.DegeneratePlane);
        if (!Measurement.TryIntersection(ray, out pending)) return false;
        double distance = GizmoDragMeasurement.Distance(pending - last);
        rejection = nameof(PositionGizmoNotice.ExcessiveJump);
        if (!double.IsFinite(distance) || distance > Measurement.StepLimit) return false;
        var delta = pending - initialIntersection;
        double x = InitialParameter.X + Vector256.Dot(delta, Measurement.Frame.TangentX!.Value);
        double y = InitialParameter.Y + Vector256.Dot(delta, Measurement.Frame.TangentY!.Value);
        rejection = nameof(PositionGizmoNotice.InvalidValue);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        parameter = new(x, y);
        rejection = null;
        return true;
    }
    protected override void AcceptMeasurement() => last = pending;
}
