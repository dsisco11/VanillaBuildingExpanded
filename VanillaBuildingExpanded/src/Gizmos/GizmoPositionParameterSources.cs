using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Maps typed parameter deltas to positions through the existing constraint and notifying value paths.</summary>
/// <typeparam name="TParameter">The scalar or planar measurement type.</typeparam>
public abstract class GizmoPositionParameterSource<TParameter> : IInteractionParameterSource<TParameter>
{
    private readonly IGizmoValueSource<GizmoPosition> value;
    private readonly Func<PositionGizmoEdit, GizmoPosition?>? constrain;
    private readonly GizmoHandleId handle;
    private GizmoPosition start;
    private TParameter initial = default!;
    private bool modifying, setting;
    private int generation;

    /// <summary>Gets the last accepted parameter, including accepted snapped no-ops.</summary>
    public TParameter Parameter { get; private set; } = default!;

    /// <summary>Gets the immutable frame captured for this parameter adapter.</summary>
    protected GizmoFrame Frame { get; }

    /// <summary>Creates bookkeeping over borrowed value and constraint sources, without copying authoritative storage.</summary>
    /// <param name="value">The existing notifying value source.</param>
    /// <param name="frame">The exact measurement frame captured at preparation.</param>
    /// <param name="handle">An identity passed to the existing constraint, never interpreted as an operation.</param>
    /// <param name="constrain">Optional existing snapping/validation policy; null results reject the candidate.</param>
    /// <exception cref="ArgumentNullException">The value source is null.</exception>
    /// <exception cref="InvalidOperationException">The frame is uninitialized.</exception>
    protected GizmoPositionParameterSource(IGizmoValueSource<GizmoPosition> value, GizmoFrame frame,
        GizmoHandleId handle, Func<PositionGizmoEdit, GizmoPosition?>? constrain)
    {
        ArgumentNullException.ThrowIfNull(value);
        frame.Validate(new(frame.Dimension));
        this.value = value;
        Frame = frame;
        this.handle = handle;
        this.constrain = constrain;
    }

    /// <summary>Records the current parameter/value relationship without writing or starting a transaction.</summary>
    /// <exception cref="InvalidOperationException">Modification is already active or the value is in another dimension.</exception>
    public void BeginModification()
    {
        if (modifying || setting) throw new InvalidOperationException("Position parameter modification is busy.");
        generation++;
        modifying = true;
        bool initialized = false;
        try
        {
            int currentGeneration = generation;
            var current = value.Value;
            if (!modifying || generation != currentGeneration)
                throw new InvalidOperationException("Position parameter initialization was terminated.");
            if (current.Dimension != Frame.Dimension)
                throw new InvalidOperationException("The position and captured frame must share a dimension.");
            start = current;
            initial = Parameter;
            initialized = true;
        }
        finally
        {
            if (!initialized) EndModification();
        }
    }

    /// <summary>Maps an absolute parameter relative to begin, constrains it and uses the notifying setter.</summary>
    /// <param name="parameter">The measured parameter, including its initial grab offset.</param>
    /// <param name="rejection">A PositionGizmoNotice name or lifecycle reason when rejected.</param>
    /// <returns>Whether accepted, including unchanged constrained values; rejection does not advance the parameter.</returns>
    public bool TrySetParameter(TParameter parameter, [NotNullWhen(false)] out string? rejection)
    {
        rejection = nameof(PositionGizmoNotice.InvalidValue);
        if (!modifying || setting) { rejection = "parameter-inactive-or-busy"; return false; }
        int currentGeneration = generation;
        setting = true;
        try
        {
            var coordinates = start.Coordinates + Delta(initial, parameter);
            if (!GizmoRay.IsCoordinate(coordinates)) return false;
            var candidate = new GizmoPosition(coordinates, start.Dimension);
            var accepted = constrain is null ? candidate : constrain(new(start, candidate, handle));
            if (!modifying || generation != currentGeneration) { rejection = "parameter-ended"; return false; }
            if (accepted is not { } position || position.Dimension != start.Dimension
                || !Accepts(start.Coordinates, position.Coordinates)) return false;
            value.Value = position;
            if (!modifying || generation != currentGeneration) { rejection = "parameter-ended"; return false; }
            Parameter = parameter;
            rejection = null;
            return true;
        }
        finally { setting = false; }
    }

    /// <summary>Releases only bookkeeping; never commits, cancels or restores the edited value.</summary>
    public void EndModification()
    {
        modifying = false;
        generation++;
        start = default;
        initial = default!;
    }

    /// <summary>Converts typed parameter differences to a double-precision world displacement.</summary>
    /// <param name="initialParameter">The parameter captured at begin.</param>
    /// <param name="parameter">The candidate parameter.</param>
    /// <returns>The displacement; nonfinite results are explicitly rejected before publication.</returns>
    protected abstract Vector256<double> Delta(TParameter initialParameter, TParameter parameter);

    /// <summary>Checks constraint output against the permitted spatial movement.</summary>
    /// <param name="initialCoordinates">The initial world coordinates.</param>
    /// <param name="coordinates">The constrained world coordinates.</param>
    /// <returns>Whether the displacement preserves required components.</returns>
    protected virtual bool Accepts(Vector256<double> initialCoordinates, Vector256<double> coordinates) => true;
}

/// <summary>Interprets scalar distance as translation along a captured axis, independently of pointer input.</summary>
public sealed class GizmoAxisPositionParameterSource : GizmoPositionParameterSource<double>
{
    /// <summary>Creates an axis position binding using an arbitrary handle identity.</summary>
    /// <param name="value">The notifying position source.</param>
    /// <param name="frame">The captured logical axis.</param>
    /// <param name="handle">Identity supplied to the existing constraint.</param>
    /// <param name="constrain">Optional existing constraint/snapping delegate.</param>
    /// <exception cref="ArgumentNullException">The value source is null.</exception>
    /// <exception cref="InvalidOperationException">The frame is uninitialized.</exception>
    public GizmoAxisPositionParameterSource(IGizmoValueSource<GizmoPosition> value, GizmoFrame frame,
        GizmoHandleId handle, Func<PositionGizmoEdit, GizmoPosition?>? constrain = null)
        : base(value, frame, handle, constrain) { }

    /// <inheritdoc />
    protected override Vector256<double> Delta(double initialParameter, double parameter) =>
        Frame.Direction * (parameter - initialParameter);

    /// <inheritdoc />
    protected override bool Accepts(Vector256<double> initialCoordinates, Vector256<double> coordinates)
    {
        var delta = coordinates - initialCoordinates;
        for (int i = 0; i < 3; i++)
            if (Frame.Direction[i] == 0 && delta[i] != 0) return false;
        var residual = delta - Frame.Direction * Vector256.Dot(delta, Frame.Direction);
        if (!GizmoRay.IsCoordinate(residual)) return false;
        var rounding = Vector256<double>.Zero;
        double projectionError = 0, magnitude = 0;
        for (int i = 0; i < 3; i++)
        {
            double error = Spacing(initialCoordinates[i]) + Spacing(coordinates[i]);
            rounding = rounding.WithElement(i, error);
            projectionError += error * Math.Abs(Frame.Direction[i]);
            magnitude = Math.Max(magnitude, Math.Abs(delta[i]));
        }
        // World-coordinate rounding survives subtraction. Bound that error and the projection's
        // arithmetic separately, rather than imposing a fixed world-space tolerance.
        double arithmeticError = 1.7763568394002505e-15 * magnitude; // Eight double-precision machine epsilons.
        for (int i = 0; i < 3; i++)
            if (Math.Abs(residual[i]) > rounding[i]
                + Math.Abs(Frame.Direction[i]) * projectionError + arithmeticError) return false;
        return true;
    }

    private static double Spacing(double coordinate)
    {
        double magnitude = Math.Abs(coordinate);
        return magnitude - Math.BitDecrement(magnitude);
    }
}

/// <summary>Interprets planar parameters as translation in the frozen plane; constraints may snap world components.</summary>
public sealed class GizmoPlanePositionParameterSource : GizmoPositionParameterSource<GizmoPlaneParameter>
{
    /// <summary>Creates a planar position binding using the same basis as measurement.</summary>
    /// <param name="value">The notifying position source.</param>
    /// <param name="frame">The captured plane with both tangents.</param>
    /// <param name="handle">Identity supplied to the existing constraint.</param>
    /// <param name="constrain">Optional existing world-grid constraint/snapping delegate.</param>
    /// <exception cref="ArgumentNullException">The value source is null.</exception>
    /// <exception cref="ArgumentException">The frame has no plane tangents.</exception>
    /// <exception cref="InvalidOperationException">The frame is uninitialized.</exception>
    public GizmoPlanePositionParameterSource(IGizmoValueSource<GizmoPosition> value, GizmoFrame frame,
        GizmoHandleId handle, Func<PositionGizmoEdit, GizmoPosition?>? constrain = null)
        : base(value, frame, handle, constrain)
    {
        if (frame.TangentX is null) throw new ArgumentException("A planar binding requires plane tangents.", nameof(frame));
    }

    /// <inheritdoc />
    protected override Vector256<double> Delta(GizmoPlaneParameter initialParameter, GizmoPlaneParameter parameter) =>
        Frame.TangentX!.Value * (parameter.X - initialParameter.X)
        + Frame.TangentY!.Value * (parameter.Y - initialParameter.Y);
}
