using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.ComposedPositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoDragTargetTests
{
    private static readonly GizmoFrame axis = new(V(0, 0, 0), V(1, 0, 0), 0);
    private static GizmoPointer Pointer(double x = 0, double z = 10, Vector256<double>? direction = null,
        GizmoContext? context = null) => new(context ?? new(0), new(V(x, 0, z), direction ?? V(0, 0, -1)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Axis_PreservesDistantCoordinatesGrabOffsetAndOtherComponents(int component)
    {
        var start = V(-2000000000.25, 30.75, -2000000000.5);
        using var owner = new PositionOwner(new(start, 0));
        var direction = component == 2 ? V(0, 1, 0) : V(0, 0, -1);
        var frame = new GizmoFrame(start, V(0, 0, 0).WithElement(component, 1), 0);
        owner.Target = new GizmoAxisDragTarget(new GizmoFixedFrameSource(frame),
            f => new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), f, new(991)),
            owner.Edit);
        var origin = start.WithElement(component, start[component] + 0.75) - direction * 10;
        var press = new GizmoPointer(new(0), new(origin, direction));
        Assert.True(owner.BeginInteraction(new(991), press));
        owner.UpdateInteraction(press);
        Assert.Equal(start, owner.Value.Coordinates);
        owner.UpdateInteraction(new(press.Context, new(origin.WithElement(component, origin[component] - 2.5), direction)));
        Assert.Equal(start.WithElement(component, start[component] - 2.5), owner.Value.Coordinates);
        owner.CancelInteraction();
        Assert.Equal(start, owner.Value.Coordinates);
        Assert.Equal(new[] { "begin", "cancel" }, owner.Transactions);
    }

    [Theory]
    [InlineData(0.0099, false)]
    [InlineData(0.01, false)]
    [InlineData(0.0101, true)]
    public void Measurement_AxisBeginEnforcesSquaredPerpendicularThreshold(double perpendicular, bool accepted)
    {
        var parameter = new Parameters<double>();
        var edit = new Edit();
        var target = Axis(parameter, edit);
        var direction = V(Math.Sqrt(1 - perpendicular * perpendicular), 0, -perpendicular);
        Assert.Equal(accepted, target.TryPrepare(new(new(0), new(-direction * 10, direction)), out var session, out var reason));
        Assert.Equal(accepted ? null : nameof(PositionGizmoNotice.DegenerateAxis), reason);
        session?.Dispose();
        Assert.Empty(parameter.Trace);
        Assert.Empty(edit.Trace);
    }

    [Theory]
    [InlineData(0.0099, false)]
    [InlineData(0.01, false)]
    [InlineData(0.0101, true)]
    public void Measurement_AxisUpdateEnforcesDenominatorThreshold(double perpendicular, bool accepted)
    {
        var parameter = new Parameters<double>();
        using var session = Begin(Axis(parameter), Pointer());
        var direction = V(Math.Sqrt(1 - perpendicular * perpendicular), 0, -perpendicular);
        Assert.Equal(accepted, session.TryUpdate(new(new(0), new(V(1, 0, 0) - direction * 10, direction)), out var reason));
        Assert.Equal(accepted ? null : nameof(PositionGizmoNotice.DegenerateAxis), reason);
        Assert.Equal(accepted ? 1 : 0, parameter.Parameter, 10);
    }

    [Theory]
    [InlineData(80, 10, true)]
    [InlineData(80.001, 10, false)]
    [InlineData(1, 100000, true)]
    [InlineData(1, 100000.001, false)]
    [InlineData(1, -10, false)]
    public void Measurement_RangeAndJumpGuardsIncludeExactBoundaries(double x, double z, bool accepted)
    {
        var parameter = new Parameters<double>();
        using var session = Begin(Axis(parameter), Pointer());
        Assert.Equal(accepted, session.TryUpdate(Pointer(x, z), out _));
        Assert.Equal(accepted ? x : 0, parameter.Parameter, 8);
    }

    [Fact]
    public void Measurement_LastAcceptedUnsnappedCoordinateAdvancesOnlyOnAcceptance()
    {
        var parameter = new Parameters<double> { Reject = true };
        using var session = Begin(Axis(parameter), Pointer());
        Assert.False(session.TryUpdate(Pointer(79), out var reason));
        Assert.Equal("constraint-rejected", reason);
        parameter.Reject = false;
        Assert.False(session.TryUpdate(Pointer(81), out reason));
        Assert.Equal(nameof(PositionGizmoNotice.ExcessiveJump), reason);
        Assert.True(session.TryUpdate(Pointer(79), out _));
        Assert.True(session.TryUpdate(Pointer(159), out _));
        Assert.Equal(159, parameter.Parameter);
    }

    [Fact]
    public void Plane_FreezesCameraFrameAndPreservesGrabOffset()
    {
        var context = Camera();
        var start = V(1.25, 2.5, -20);
        using var owner = new PositionOwner(new(start, 0));
        owner.Target = new GizmoPlaneDragTarget(new GizmoCameraFrameSource(() => owner.Value),
            f => new GizmoPlanePositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), f, new(37)), owner.Edit);
        var press = new GizmoPointer(context, new(V(0, 0, 0), V(2, 3, -20)));
        Assert.True(owner.BeginInteraction(new(37), press));
        int changes = 0;
        owner.OnValueChanged += _ => changes++;
        for (int i = 0; i < 120; i++) owner.UpdateInteraction(press);
        Assert.Equal(start, owner.Value.Coordinates);
        Assert.Equal(0, changes);
        var movedCamera = Camera(view: Yaw(0.7));
        owner.UpdateInteraction(new(movedCamera, new(V(0, 0, 0), V(4.125, 1.75, -20))));
        AssertCoordinates(start + V(2.125, -1.25, 0), owner.Value.Coordinates);
        owner.CancelInteraction();
        Assert.Equal(start, owner.Value.Coordinates);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Plane_MeasurementRejectsDegeneracyJumpAndDimensionsWithoutValues()
    {
        var frame = new GizmoFrame(V(0, 0, 0), V(0, 0, -1), 0, V(1, 0, 0), V(0, 1, 0));
        var parameter = new Parameters<GizmoPlaneParameter>();
        var target = new GizmoPlaneDragTarget(new GizmoFixedFrameSource(frame), _ => parameter, new Edit());
        Assert.False(target.TryPrepare(Pointer(direction: V(1, 0, 0)), out _, out var reason));
        Assert.Equal(nameof(PositionGizmoNotice.DegeneratePlane), reason);
        using var session = Begin(target, Pointer());
        Assert.False(session.TryUpdate(Pointer(context: new(1)), out reason));
        Assert.Equal(nameof(PositionGizmoNotice.InvalidValue), reason);
        Assert.False(session.TryUpdate(Pointer(direction: V(1, 0, 0)), out reason));
        Assert.Equal(nameof(PositionGizmoNotice.DegeneratePlane), reason);
        Assert.False(session.TryUpdate(Pointer(80.001), out reason));
        Assert.Equal(nameof(PositionGizmoNotice.ExcessiveJump), reason);
        Assert.True(session.TryUpdate(Pointer(80), out _));
        Assert.Equal(new(80, 0), parameter.Parameter);
    }

    [Theory]
    [InlineData(0.49, 0)]
    [InlineData(-0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, -1)]
    public void Parameter_PositionConstraintsAndNoOpEchoesWorkWithoutPointerInput(double distance, double expected)
    {
        using var owner = new PositionOwner(new(V(-10, 4.25, -5), 0));
        var frame = new GizmoFrame(owner.Value.Coordinates, V(1, 0, 0), 0);
        var parameter = new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), frame, new(801), edit =>
        {
            Assert.Equal(new(801), edit.Handle);
            return new(edit.Start.Coordinates.WithElement(0, edit.Start.Coordinates[0]
                + Math.Round(edit.Candidate.Coordinates[0] - edit.Start.Coordinates[0], MidpointRounding.AwayFromZero)), 0);
        });
        int changes = 0;
        owner.OnValueChanged += value => { changes++; owner.Value = value; };
        parameter.BeginModification();
        Assert.True(parameter.TrySetParameter(distance, out _));
        Assert.True(parameter.TrySetParameter(distance, out _));
        parameter.EndModification();
        Assert.Equal(V(-10 + expected, 4.25, -5), owner.Value.Coordinates);
        Assert.Equal(expected == 0 ? 0 : 1, changes);
        Assert.Equal(distance, parameter.Parameter);
        Assert.Empty(owner.Transactions);
    }

    [Fact]
    public void Parameter_RepeatedModificationRebasesValueButNotAbsoluteParameter()
    {
        using var owner = new PositionOwner(new(V(10, 0, 0), 0));
        var source = new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), axis, new(8));
        source.BeginModification();
        Assert.True(source.TrySetParameter(3, out _));
        source.EndModification();
        owner.Value = new(V(20, 0, 0), 0);
        source.BeginModification();
        Assert.Equal(3, source.Parameter);
        Assert.True(source.TrySetParameter(5, out _));
        source.EndModification();
        Assert.Equal(V(22, 0, 0), owner.Value.Coordinates);
    }

    [Fact]
    public void Session_SnappedNoOpStillAdvancesAcceptedUnsnappedMeasurement()
    {
        using var owner = new PositionOwner(new(V(0, 0, 0), 0));
        int changes = 0;
        owner.OnValueChanged += _ => changes++;
        var target = new GizmoAxisDragTarget(new GizmoFixedFrameSource(axis),
            f => new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), f,
                new(8), edit => edit.Start), owner.Edit);
        using var session = Begin(target, Pointer());
        Assert.True(session.TryUpdate(Pointer(79), out _));
        Assert.True(session.TryUpdate(Pointer(159), out _));
        Assert.Equal(0, changes);
        Assert.Equal(0, owner.Value.Coordinates[0]);
    }

    [Fact]
    public void Parameter_RejectsConstraintDimensionCrossAxisAndNonfiniteCandidates()
    {
        using var owner = new PositionOwner(new(V(10, 20, 30), 0));
        GizmoPosition? accepted = null;
        var source = new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), axis,
            new(8), _ => accepted);
        source.BeginModification();
        foreach (var candidate in new GizmoPosition?[] { null, new(V(11, 20, 30), 1), new(V(11, 21, 30), 0) })
        {
            accepted = candidate;
            Assert.False(source.TrySetParameter(1, out var reason));
            Assert.Equal(nameof(PositionGizmoNotice.InvalidValue), reason);
        }
        Assert.False(source.TrySetParameter(double.PositiveInfinity, out _));
        Assert.Equal(V(10, 20, 30), owner.Value.Coordinates);
        Assert.Equal(0, source.Parameter);
        source.EndModification();
        Assert.False(source.TrySetParameter(1, out _));
        owner.Value = new(V(0, 0, 0), 1);
        Assert.Throws<InvalidOperationException>(source.BeginModification);
    }

    [Fact]
    public void Parameter_RejectsOverflowAndUnwindsThrowingOrReentrantReads()
    {
        using var owner = new PositionOwner(new(V(1e308, 0, 0), 0));
        var source = new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), axis, new(5));
        source.BeginModification();
        Assert.False(source.TrySetParameter(1e308, out var reason));
        Assert.Equal(nameof(PositionGizmoNotice.InvalidValue), reason);
        Assert.Equal(1e308, owner.Value.Coordinates[0]);
        source.EndModification();
        var value = new ReadSource();
        source = new(value, axis, new(5));
        value.Read = () =>
        {
            Assert.Throws<InvalidOperationException>(source.BeginModification);
            source.EndModification();
            return new(V(0, 0, 0), 0);
        };
        Assert.Throws<InvalidOperationException>(source.BeginModification);
        value.Read = () => throw new InvalidOperationException("read");
        Assert.Throws<InvalidOperationException>(source.BeginModification);
        value.Read = () => new(V(0, 0, 0), 0);
        source.BeginModification();
        source.EndModification();
    }

    [Theory]
    [InlineData(-2000000000.25, false)]
    [InlineData(-2000000000.25, true)]
    [InlineData(1e12, false)]
    [InlineData(1e12, true)]
    public void Parameter_DistantTiltedAxisAcceptsRoundedMovementAndAlongAxisConstraints(double x, bool constrain)
    {
        var start = V(x, 30.75, 0);
        var frame = new GizmoFrame(start, V(1, 1, 0), 0);
        using var owner = new PositionOwner(new(start, 0));
        var source = new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), frame, new(5),
            constrain ? edit => new(edit.Start.Coordinates + frame.Direction * 2, 0) : null);
        source.BeginModification();
        Assert.True(source.TrySetParameter(1, out var reason), reason);
        Assert.Equal(start + frame.Direction * (constrain ? 2 : 1), owner.Value.Coordinates);
        source.EndModification();
    }

    [Theory]
    [InlineData(0.0001, 0)]
    [InlineData(0, 1e-12)]
    public void Parameter_DistantTiltedAxisStillRejectsPerpendicularConstraintChanges(double perpendicular, double z)
    {
        var start = V(-2000000000.25, 30.75, 0);
        var frame = new GizmoFrame(start, V(1, 1, 0), 0);
        using var owner = new PositionOwner(new(start, 0));
        var source = new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), frame, new(5),
            edit => new(edit.Candidate.Coordinates + V(perpendicular, -perpendicular, z), 0));
        source.BeginModification();
        Assert.False(source.TrySetParameter(1, out var reason));
        Assert.Equal(nameof(PositionGizmoNotice.InvalidValue), reason);
        Assert.Equal(start, owner.Value.Coordinates);
        source.EndModification();
    }

    [Fact]
    public void Parameter_PlaneConstraintsSnapWorldAxesAndMayLeaveContinuousPlane()
    {
        using var owner = new PositionOwner(new(V(10, 20, 30), 0));
        var frame = new GizmoFrame(owner.Value.Coordinates, V(1, 0, 1), 0, V(1, 0, -1), V(0, 1, 0));
        var source = new GizmoPlanePositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), frame, new(6),
            edit => new(V(Math.Floor(edit.Candidate.Coordinates[0]), Math.Floor(edit.Candidate.Coordinates[1]),
                Math.Floor(edit.Candidate.Coordinates[2])), 0));
        source.BeginModification();
        Assert.True(source.TrySetParameter(new(1, 0.5), out _));
        source.EndModification();
        Assert.Equal(V(10, 20, 29), owner.Value.Coordinates);
        Assert.Empty(owner.Transactions);
    }

    [Fact]
    public void Composition_SameScalarSolverAcceptsDifferentTypedValuesAndReplaceableFramesAndHits()
    {
        using var position = new PositionOwner(new(V(0, 0, 0), 0));
        using var count = new CountOwner();
        var handle = new GizmoHandle(new(815), new() { Shape = GizmoShape.Arrow, Size = 64,
            Color = new(1, 0, 0, 1), LineWidth = 1 });
        position.AddHandle(handle);
        IGizmoHitTarget first = new GizmoDelegateHitTarget(position, _ => [new(position, handle, 1)]);
        IGizmoHitTarget second = new GizmoDelegateHitTarget(position, _ => [new(position, handle, 2)]);
        Assert.Same(handle, Assert.Single(first.HitTest(Pointer())).Handle);
        Assert.Same(handle, Assert.Single(second.HitTest(Pointer())).Handle);
        var target = new GizmoAxisDragTarget(new GizmoFixedFrameSource(axis),
            f => new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(position), f, handle.Id), position.Edit);
        using (var session = Begin(target, Pointer()))
        {
            Assert.True(session.TryUpdate(Pointer(3), out _));
            session.Complete();
        }
        var counter = new CountParameter(new GizmoValueSource<int>(count));
        target = new GizmoAxisDragTarget(new GizmoAxisFrameSource(() => new(V(0, 0, 0), 0), () => V(1, 0, 0)),
            _ => counter, new GizmoEditStateTarget(count, () => true, () => { }, () => { }));
        using (var session = Begin(target, Pointer()))
        {
            Assert.True(session.TryUpdate(Pointer(3), out _));
            session.Complete();
        }
        Assert.Equal(3, position.Value.Coordinates[0]);
        Assert.Equal(13, count.Value);
    }

    [Theory]
    [InlineData("parameter-throw")]
    [InlineData("edit-reject")]
    [InlineData("edit-throw")]
    [InlineData("parameter-reentrant-cancel")]
    [InlineData("edit-reentrant-cancel")]
    public void Session_FailedBeginUnwindsPartialScopesAndCannotRestart(string failure)
    {
        var trace = new List<string>();
        var parameter = new Parameters<double>(trace);
        var edit = new Edit(trace);
        Assert.True(Axis(parameter, edit).TryPrepare(Pointer(), out var session, out _));
        using var cleanup = session!;
        if (failure == "parameter-throw") parameter.BeginAction = () => throw new InvalidOperationException("parameter");
        if (failure == "edit-reject") edit.Reject = true;
        if (failure == "edit-throw") edit.BeginAction = () => throw new InvalidOperationException("edit");
        if (failure == "parameter-reentrant-cancel") parameter.BeginAction = session.Cancel;
        if (failure == "edit-reentrant-cancel") edit.BeginAction = session.Cancel;
        if (failure.EndsWith("throw"))
            Assert.Throws<InvalidOperationException>(() => session.TryBegin(out _));
        else Assert.False(session.TryBegin(out _));
        Assert.False(session.TryBegin(out _));
        session.Complete();
        session.Cancel();
        Assert.DoesNotContain("commit", trace);
        Assert.Equal(failure == "parameter-throw" ? 0 : 1, trace.Count(x => x == "parameter-end"));
        Assert.False(parameter.Active);
        Assert.False(edit.Active);
    }

    [Fact]
    public void Session_CleanupFailureCancelsInsteadOfCommittingAndTerminalCallsAreIdempotent()
    {
        var trace = new List<string>();
        var parameter = new Parameters<double>(trace) { EndAction = () => throw new InvalidOperationException("cleanup") };
        var edit = new Edit(trace);
        using var session = Begin(Axis(parameter, edit), Pointer());
        Assert.True(session.TryUpdate(Pointer(5), out _));
        Assert.Throws<InvalidOperationException>(session.Complete);
        session.Cancel();
        session.Complete();
        Assert.Equal(new[] { "parameter-begin", "edit-begin", "parameter-end", "cancel" }, trace);
    }

    [Fact]
    public void Session_CommitFailureDoesNotInventRollbackOrReportSuccess()
    {
        var edit = new Edit { CompleteAction = () => throw new InvalidOperationException("save") };
        using var session = Begin(Axis(new Parameters<double>(), edit), Pointer());
        Assert.Throws<InvalidOperationException>(session.Complete);
        session.Cancel();
        Assert.Equal(new[] { "edit-begin", "commit" }, edit.Trace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owner_EditAdapterRestoresOnceBeforeCancellationAndNoDuplicateTransactions(bool dispose)
    {
        using var owner = ComposedOwner();
        var values = new List<double>();
        owner.OnValueChanged += value => values.Add(value.Coordinates[0]);
        owner.OnInteractionCanceled += _ =>
        {
            Assert.Equal(0, owner.Value.Coordinates[0]);
            Assert.Equal(new[] { "begin", "cancel" }, owner.Transactions);
            owner.CancelInteraction();
        };
        Assert.True(owner.BeginInteraction(new(72), Pointer()));
        owner.UpdateInteraction(Pointer(5));
        if (dispose) owner.Dispose();
        else owner.CancelInteraction();
        owner.CancelInteraction();
        Assert.Equal(new[] { 5d, 0d }, values);
        Assert.Equal(new[] { "begin", "cancel" }, owner.Transactions);
        Assert.False(owner.IsInteracting);
    }

    [Fact]
    public void Owner_CompletionAndIndependentParameterEndDoNotDuplicateCommit()
    {
        using var owner = ComposedOwner();
        Assert.True(owner.BeginInteraction(new(72), Pointer()));
        owner.UpdateInteraction(Pointer(5));
        owner.CompleteInteraction();
        owner.CancelInteraction();
        Assert.Equal(5, owner.Value.Coordinates[0]);
        Assert.Equal(new[] { "begin", "commit" }, owner.Transactions);
    }

    [Fact]
    public void Owner_ThrowingCapturePublicationUnwindsSessionWithoutSuccessfulStart()
    {
        using var owner = ComposedOwner();
        int started = 0;
        owner.OnInteractionStarted += _ => started++;
        owner.HandleInvalidated += (_, _) =>
        {
            if (owner.IsInteracting) throw new InvalidOperationException("capture observer");
        };
        Assert.Throws<InvalidOperationException>(() => owner.BeginInteraction(new(72), Pointer()));
        Assert.False(owner.IsInteracting);
        Assert.Equal(0, started);
        Assert.Equal(new[] { "begin", "cancel" }, owner.Transactions);
    }

    [Fact]
    public void Owner_ParameterCleanupFailureCancelsAndCommitFailureNeverPublishesCompletion()
    {
        using var owner = ComposedOwner();
        int completed = 0;
        owner.OnInteractionCompleted += _ => completed++;
        var parameter = new Parameters<double> { EndAction = () => throw new InvalidOperationException("cleanup") };
        owner.Target = new GizmoAxisDragTarget(new GizmoFixedFrameSource(axis), _ => parameter, owner.Edit);
        Assert.True(owner.BeginInteraction(new(72), Pointer()));
        Assert.Throws<InvalidOperationException>(owner.CompleteInteraction);
        Assert.False(owner.IsInteracting);
        Assert.Equal(0, completed);
        Assert.Equal(new[] { "begin", "cancel" }, owner.Transactions);
        owner.Transactions.Clear();
        owner.CommitAction = () => throw new InvalidOperationException("save");
        owner.Target = new GizmoAxisDragTarget(new GizmoFixedFrameSource(axis),
            f => new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), f, new(72)), owner.Edit);
        Assert.True(owner.BeginInteraction(new(72), Pointer()));
        owner.UpdateInteraction(Pointer(5));
        Assert.Throws<InvalidOperationException>(owner.CompleteInteraction);
        owner.CancelInteraction();
        Assert.Equal(0, completed);
        Assert.Equal(5, owner.Value.Coordinates[0]);
        Assert.Equal(new[] { "begin", "commit" }, owner.Transactions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owner_ForcedIneligibilityCancelsTheSameScope(bool hide)
    {
        using var owner = ComposedOwner();
        Assert.True(owner.BeginInteraction(new(72), Pointer()));
        owner.UpdateInteraction(Pointer(5));
        if (hide) owner.Visible = false;
        else owner.Enabled = false;
        Assert.False(owner.IsInteracting);
        Assert.Equal(0, owner.Value.Coordinates[0]);
        Assert.Equal(new[] { "begin", "cancel" }, owner.Transactions);
    }

    [Fact]
    public void EditAdapter_RestoreObserverFailureStillCancelsOnceAndReleasesGuards()
    {
        using var owner = new CountOwner();
        var trace = new List<string>();
        var other = new GizmoEditStateTarget(owner, () => true, () => { }, () => { });
        var edit = new GizmoEditStateTarget(owner, () => true, () => { }, () =>
        {
            trace.Add("cancel");
            Assert.False(other.TryBegin(out _));
        });
        Assert.True(edit.TryBegin(out _));
        owner.SetInteractionValue(20);
        owner.OnValueChanged += Changed;
        Assert.Throws<InvalidOperationException>(edit.Cancel);
        edit.Cancel();
        Assert.Equal(10, owner.Value);
        Assert.Equal(new[] { "cancel" }, trace);
        owner.OnValueChanged -= Changed;
        Assert.True(other.TryBegin(out _));
        other.Cancel();
        void Changed(int _) => throw new InvalidOperationException("observer");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditAdapter_UnwindsRejectedAndThrowingOwnerBegin(bool throws)
    {
        using var owner = new CountOwner();
        var trace = new List<string>();
        var edit = new GizmoEditStateTarget(owner, () =>
        {
            trace.Add("begin");
            if (throws) throw new InvalidOperationException("owner");
            return false;
        }, () => trace.Add("commit"), () => trace.Add("cancel"));
        if (throws) Assert.Throws<InvalidOperationException>(() => edit.TryBegin(out _));
        else Assert.False(edit.TryBegin(out _));
        edit.Cancel();
        Assert.Equal(new[] { "begin", "cancel" }, trace);
        Assert.Equal(10, owner.Value);
    }

    [Fact]
    public void EditAdapter_ReentrantTerminationDuringInitializationUnwindsAndProtectsBaseline()
    {
        using var owner = new CountOwner();
        int cancellations = 0;
        GizmoEditStateTarget? edit = null;
        edit = new(owner, () =>
        {
            Assert.False(edit!.TryBegin(out _));
            owner.CancelInteraction();
            return true;
        }, () => throw new InvalidOperationException("must not commit"), () => cancellations++);
        Assert.False(edit.TryBegin(out var reason));
        Assert.Equal("owner-rejected", reason);
        Assert.Equal(1, cancellations);
    }

    [Fact]
    public void Session_ConstraintCancellationAndReentrantUpdateDoNotAdvanceMeasurements()
    {
        var parameter = new Parameters<double>();
        using var session = Begin(Axis(parameter), Pointer());
        parameter.SetAction = () =>
        {
            Assert.False(session.TryUpdate(Pointer(3), out _));
            session.Cancel();
        };
        Assert.False(session.TryUpdate(Pointer(5), out var reason));
        Assert.Equal("session-ended", reason);
        Assert.False(parameter.Active);
    }

    private static GizmoAxisDragTarget Axis(Parameters<double> parameter, Edit? edit = null) =>
        new(new GizmoFixedFrameSource(axis), _ => parameter, edit ?? new());

    private static IGizmoDragSession Begin(IGizmoDragTarget target, GizmoPointer pointer)
    {
        Assert.True(target.TryPrepare(pointer, out var session, out var reason), reason);
        Assert.True(session.TryBegin(out reason), reason);
        return session;
    }

    private static PositionOwner ComposedOwner()
    {
        var owner = new PositionOwner(new(V(0, 0, 0), 0));
        owner.Target = new GizmoAxisDragTarget(new GizmoFixedFrameSource(axis),
            f => new GizmoAxisPositionParameterSource(new GizmoValueSource<GizmoPosition>(owner), f, new(72)), owner.Edit);
        return owner;
    }

    private sealed class PositionOwner : Gizmo<GizmoPosition>
    {
        public IGizmoDragTarget? Target;
        public Action? CommitAction;
        private IGizmoDragSession? session;
        public List<string> Transactions { get; } = [];
        public GizmoEditStateTarget Edit { get; }
        public PositionOwner(GizmoPosition value) : base(value)
        {
            Edit = new(this, () => { Transactions.Add("begin"); return true; },
                () => { Transactions.Add("commit"); CommitAction?.Invoke(); }, () => Transactions.Add("cancel"));
            foreach (int id in new[] { 37, 72, 991 })
                AddHandle(new(new(id), new()
                {
                    Shape = GizmoShape.Arrow, Size = 64, Color = new(1, 0, 0, 1), LineWidth = 1
                }));
        }
        public override int Dimension => Value.Dimension;
        protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer) => [];
        protected override void RenderCore(GizmoContext context) { }
        protected override bool BeginInteractionCore(GizmoHandleId handle, GizmoPointer pointer)
        {
            if (!Target!.TryPrepare(pointer, out var prepared, out _)) return false;
            if (!prepared.TryBegin(out _)) { prepared.Dispose(); return false; }
            session = prepared;
            return true;
        }
        protected override void UpdateInteractionCore(GizmoPointer pointer) => session!.TryUpdate(pointer, out _);
        protected override void CompleteInteractionCore()
        {
            var active = session;
            session = null;
            try { active?.Complete(); }
            finally { active?.Dispose(); }
        }
        protected override void CancelInteractionCore()
        {
            var active = session;
            session = null;
            try { active?.Cancel(); }
            finally { active?.Dispose(); }
        }
    }

    private sealed class CountOwner() : Gizmo<int>(10)
    {
        public override int Dimension => 0;
        protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer) => [];
        protected override void RenderCore(GizmoContext context) { }
        protected override void UpdateInteractionCore(GizmoPointer pointer) { }
    }

    private sealed class CountParameter(IGizmoValueSource<int> value) : IInteractionParameterSource<double>
    {
        private int initial;
        public double Parameter { get; private set; }
        public void BeginModification() => initial = value.Value;
        public bool TrySetParameter(double parameter, [NotNullWhen(false)] out string? rejection)
        {
            value.Value = initial + (int)parameter;
            Parameter = parameter;
            rejection = null;
            return true;
        }
        public void EndModification() { }
    }

    private sealed class ReadSource : IGizmoValueSource<GizmoPosition>
    {
        public Func<GizmoPosition> Read = () => default;
        public GizmoPosition Value { get => Read(); set => throw new InvalidOperationException("unexpected write"); }
    }

    private sealed class Parameters<T>(List<string>? trace = null) : IInteractionParameterSource<T>
    {
        public List<string> Trace { get; } = trace ?? [];
        public T Parameter { get; private set; } = default!;
        public bool Active, Reject;
        public Action? BeginAction, EndAction, SetAction;
        public void BeginModification()
        {
            Trace.Add("parameter-begin");
            try { BeginAction?.Invoke(); Active = true; }
            catch { Active = false; throw; }
        }
        public bool TrySetParameter(T parameter, [NotNullWhen(false)] out string? rejection)
        {
            rejection = "constraint-rejected";
            if (Reject) return false;
            Parameter = parameter;
            SetAction?.Invoke();
            rejection = null;
            return true;
        }
        public void EndModification() { Active = false; Trace.Add("parameter-end"); EndAction?.Invoke(); }
    }

    private sealed class Edit(List<string>? trace = null) : IGizmoEditStateTarget
    {
        public List<string> Trace { get; } = trace ?? [];
        public bool Active, Reject;
        public Action? BeginAction, CompleteAction;
        public bool TryBegin([NotNullWhen(false)] out string? rejection)
        {
            Trace.Add("edit-begin");
            BeginAction?.Invoke();
            Active = !Reject;
            rejection = Reject ? "owner-rejected" : null;
            return !Reject;
        }
        public void Complete() { Active = false; Trace.Add("commit"); CompleteAction?.Invoke(); }
        public void Cancel() { Active = false; Trace.Add("cancel"); }
    }
}
