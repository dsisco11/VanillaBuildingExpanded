using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoInteractionContractTests
{
    private static readonly GizmoPointer pointer = new(new(0),
        new(Vector256<double>.Zero, Vector256.Create(0d, 0d, -1d, 0d)));
    private static readonly Vector256<double> axis = Vector256.Create(1d, 0d, 0d, 0d);

    [Fact]
    public void ValueAdapter_UsesExistingNotifyingSetterAndReadsExternalAssignments()
    {
        using var owner = new ValueGizmo();
        IGizmoValueSource<int> source = new GizmoValueSource<int>(owner);
        List<int> values = [];
        owner.OnValueChanged += values.Add;
        source.Value = 5;
        source.Value = 5;
        owner.Value = 9;
        Assert.Equal(9, source.Value);
        Assert.Equal(new[] { 5, 9 }, values);
        Assert.Throws<ArgumentNullException>(() => new GizmoValueSource<int>(null!));
        owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.Value = 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void HitContract_RejectsInvalidDistances(double distance)
    {
        using var owner = new ValueGizmo();
        var handle = new Handle(owner, 42);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoHandleHit(owner, handle, distance));
    }

    [Fact]
    public void HitContract_RequiresCurrentOwnershipAndRejectsDefaultOrStaleHits()
    {
        using var first = new ValueGizmo();
        using var second = new ValueGizmo();
        var handle = new Handle(first, 42);
        var hit = new GizmoHandleHit(first, handle, 1);
        Assert.Same(handle, hit.Handle);
        Assert.Throws<ArgumentNullException>(() => new GizmoHandleHit(first, null!, 1));
        Assert.Throws<ArgumentException>(() => new GizmoHandleHit(second, handle, 1));
        Assert.Throws<InvalidOperationException>(() => default(GizmoHandleHit).Validate(first));
        handle.Owner = second;
        Assert.Throws<InvalidOperationException>(() => hit.Validate(first));
        handle.Owner = null;
        Assert.Throws<ArgumentException>(() => new GizmoHandleHit(first, handle, 1));
    }

    [Fact]
    public void HitContract_OrdersWithinOwnerByPriorityThenDistanceThenIdentity()
    {
        using var owner = new ValueGizmo();
        var a = new Handle(owner, 42);
        var b = new Handle(owner, 99);
        Assert.True(new GizmoHandleHit(owner, b, 100, -1)
            .CompareWithinOwner(new(owner, a, 1)) < 0);
        Assert.True(new GizmoHandleHit(owner, b, 1)
            .CompareWithinOwner(new(owner, a, 2)) < 0);
        Assert.True(new GizmoHandleHit(owner, a, 1)
            .CompareWithinOwner(new(owner, b, 1)) < 0);
        using var foreign = new ValueGizmo();
        Assert.Throws<InvalidOperationException>(() => new GizmoHandleHit(owner, a, 1)
            .CompareWithinOwner(new(foreign, new Handle(foreign, 1), 1)));
    }

    [Fact]
    public void HitAndFrameSources_AreReplaceableWithoutRenderingOrValueSemantics()
    {
        using var owner = new ValueGizmo();
        var handle = new Handle(owner, 789);
        IGizmoHitTarget first = new HitTarget(owner, handle, 2);
        IGizmoHitTarget second = new HitTarget(owner, handle, 7);
        Assert.Equal(2, Assert.Single(first.HitTest(pointer)).Distance);
        Assert.Equal(7, Assert.Single(second.HitTest(pointer)).Distance);
        first.UpdateHover(handle, true);
        second.UpdateInteracting(handle, true);
        Assert.True(handle.Hovered);
        Assert.True(handle.Interacting);
        first.UpdateHover(handle, false);
        Assert.False(handle.Hovered);
        IGizmoFrameSource frame = new FrameSource(new(Vector256<double>.Zero, axis * 2, 0));
        Assert.Equal(axis, frame.GetFrame(pointer.Context).Direction);
        frame = new FrameSource(new(Vector256.Create(10d, 20d, 30d, 0d), axis, 0));
        Assert.Equal(10, frame.GetFrame(pointer.Context).Origin[0]);
    }

    [Fact]
    public void FrameContract_ValidatesNormalizedAxisPlaneAndDimensionWithoutMeshes()
    {
        var frame = new GizmoFrame(Vector256<double>.Zero, axis * 3, 0,
            Vector256.Create(0d, 2d, 0d, 0d), Vector256.Create(0d, 0d, 5d, 0d));
        frame.Validate(pointer.Context);
        Assert.Equal(axis, frame.Direction);
        Assert.Equal(1, frame.TangentX!.Value[1]);
        Assert.Throws<ArgumentException>(() => new GizmoFrame(Vector256<double>.Zero, axis, 0, axis));
        Assert.Throws<ArgumentException>(() => new GizmoFrame(Vector256<double>.Zero, axis, 0, axis, axis));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoFrame(Vector256<double>.Zero,
            Vector256<double>.Zero, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoFrame(Vector256.Create(0d, 0d, 0d, 1d), axis, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoFrame(Vector256<double>.Zero, axis, -1));
        Assert.Throws<InvalidOperationException>(() => default(GizmoFrame).Validate(pointer.Context));
        Assert.Throws<InvalidOperationException>(() => frame.Validate(new(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoPlaneParameter(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoPlaneParameter(0, double.PositiveInfinity));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lifecycle_InitializesBeforeStartAndEndsParametersBeforeCommitOrRollback(bool complete)
    {
        using var owner = new ValueGizmo();
        List<string> trace = [];
        var source = new GizmoValueSource<int>(owner);
        var parameter = new ParameterSource<double>(source, x => (int)x, trace);
        var edit = new EditTarget(source, trace);
        var session = new Session<double>(parameter, edit, _ => 8, trace);
        using var input = new InputBehavior();
        owner.OnValueChanged += value => trace.Add($"value:{value}");
        Assert.True(input.TryBegin(new Target(() => session, trace), pointer, out var reason));
        Assert.Null(reason);
        trace.Add("started");
        Assert.True(input.TryUpdate(pointer, out reason));
        if (complete) input.Complete();
        else input.Cancel();
        trace.Add(complete ? "completed" : "canceled");
        input.Complete();
        input.Cancel();
        Assert.False(input.IsInteracting);
        Assert.Equal(complete ? 8 : 0, owner.Value);
        Assert.Equal(complete
            ? new[] { "prepare", "parameter-begin", "edit-begin", "started", "measure", "value:8",
                "parameter-end", "commit", "release", "completed" }
            : new[] { "prepare", "parameter-begin", "edit-begin", "started", "measure", "value:8",
                "parameter-end", "value:0", "rollback", "release", "canceled" }, trace);
    }

    [Fact]
    public void Lifecycle_PreparationRejectionLeavesNoSessionOrEditAndProvidesReason()
    {
        List<string> trace = [];
        using var input = new InputBehavior();
        Assert.False(input.TryBegin(new Target(() => null, trace), pointer, out var reason));
        Assert.Equal("unusable-measurement", reason);
        Assert.False(input.IsInteracting);
        Assert.Equal(new[] { "prepare" }, trace);
    }

    [Theory]
    [InlineData("parameter-throw")]
    [InlineData("edit-reject")]
    [InlineData("edit-throw")]
    public void Lifecycle_FailedInitializationUnwindsOnlyInitializedScopes(string failure)
    {
        using var owner = new ValueGizmo();
        List<string> trace = [];
        var source = new GizmoValueSource<int>(owner);
        var parameters = new ParameterSource<double>(source, x => (int)x, trace)
        {
            ThrowOnBegin = failure == "parameter-throw"
        };
        var edit = new EditTarget(source, trace)
        {
            RejectBegin = failure == "edit-reject", ThrowOnBegin = failure == "edit-throw"
        };
        var session = new Session<double>(parameters, edit, _ => 1, trace);
        using var input = new InputBehavior();
        var target = new Target(() => session, trace);
        if (failure.EndsWith("throw"))
            Assert.Throws<InvalidOperationException>(() => input.TryBegin(target, pointer, out _));
        else
        {
            Assert.False(input.TryBegin(target, pointer, out var reason));
            Assert.Equal("owner-rejected", reason);
        }
        Assert.False(input.IsInteracting);
        Assert.False(parameters.Modifying);
        Assert.False(edit.Active);
        Assert.Equal(0, owner.Value);
        Assert.DoesNotContain("commit", trace);
        Assert.DoesNotContain("rollback", trace);
        Assert.Equal(failure == "parameter-throw" ? 0 : 1, trace.Count(x => x == "parameter-end"));
        Assert.Equal(1, trace.Count(x => x == "release"));
    }

    [Fact]
    public void ParameterEnd_IsBookkeepingOnlyAndDoesNotOwnTransactionRollback()
    {
        using var owner = new ValueGizmo();
        List<string> trace = [];
        var source = new ParameterSource<double>(new GizmoValueSource<int>(owner), x => (int)x, trace);
        source.BeginModification();
        Assert.True(source.TrySetParameter(6, out _));
        source.EndModification();
        Assert.Equal(6, owner.Value);
        Assert.Equal(new[] { "parameter-begin", "parameter-end" }, trace);
    }

    [Fact]
    public void Lifecycle_ParameterCleanupFailureCancelsInsteadOfCommittingAndStillReleasesSession()
    {
        using var owner = new ValueGizmo();
        List<string> trace = [];
        var value = new GizmoValueSource<int>(owner);
        var parameter = new ParameterSource<double>(value, x => (int)x, trace) { ThrowOnEnd = true };
        using var input = new InputBehavior();
        Assert.True(input.TryBegin(new Target(() => new Session<double>(parameter,
            new EditTarget(value, trace), _ => 5, trace), trace), pointer, out _));
        Assert.True(input.TryUpdate(pointer, out _));
        Assert.Throws<InvalidOperationException>(input.Complete);
        Assert.False(input.IsInteracting);
        Assert.Equal(0, owner.Value);
        Assert.DoesNotContain("commit", trace);
        Assert.Equal(1, trace.Count(x => x == "rollback"));
        Assert.Equal(1, trace.Count(x => x == "release"));
    }

    [Fact]
    public void Lifecycle_ReentrantBeginAndRestartAfterDisposalAreRejected()
    {
        using var owner = new ValueGizmo();
        List<string> trace = [];
        var value = new GizmoValueSource<int>(owner);
        using var input = new InputBehavior();
        var target = new Target(() =>
        {
            Assert.False(input.TryBegin(new Target(() => null, trace), pointer, out var reason));
            Assert.Equal("busy", reason);
            return new Session<double>(new ParameterSource<double>(value, x => (int)x, trace),
                new EditTarget(value, trace), _ => 1, trace);
        }, trace);
        Assert.True(input.TryBegin(target, pointer, out _));
        input.Dispose();
        Assert.Throws<ObjectDisposedException>(() => input.TryBegin(target, pointer, out _));
        Assert.Equal(1, trace.Count(x => x == "prepare"));
    }

    [Fact]
    public void NonGenericInput_AcceptsScalarAndPlaneSessionsWithoutValueCasts()
    {
        using var owner = new ValueGizmo();
        List<string> trace = [];
        var value = new GizmoValueSource<int>(owner);
        var scalar = new ParameterSource<double>(value, x => (int)x, trace);
        var plane = new ParameterSource<GizmoPlaneParameter>(value, p => (int)(p.X + p.Y), trace);
        using var input = new InputBehavior();
        Assert.True(input.TryBegin(new Target(() => new Session<double>(scalar,
            new EditTarget(value, trace), _ => 2, trace), trace), pointer, out _));
        Assert.True(input.TryUpdate(pointer, out _));
        input.Complete();
        Assert.Equal(2, owner.Value);
        Assert.True(input.TryBegin(new Target(() => new Session<GizmoPlaneParameter>(plane,
            new EditTarget(value, trace), _ => new(3, 4), trace), trace), pointer, out _));
        Assert.True(input.TryUpdate(pointer, out _));
        input.Complete();
        Assert.Equal(9, owner.Value);
    }

    [Fact]
    public void Lifecycle_InvalidUpdateRetainsLastValueAndCancellationIsReentrantAndIdempotent()
    {
        using var owner = new ValueGizmo();
        List<string> trace = [];
        var value = new GizmoValueSource<int>(owner);
        var parameter = new ParameterSource<double>(value, x => (int)x, trace);
        var edit = new EditTarget(value, trace);
        using var input = new InputBehavior();
        Assert.True(input.TryBegin(new Target(() => new Session<double>(parameter, edit, _ => 4, trace),
            trace), pointer, out _));
        Assert.False(input.TryBegin(new Target(() => null, trace), pointer, out var reason));
        Assert.Equal("busy", reason);
        Assert.True(input.TryUpdate(pointer, out _));
        parameter.RejectUpdate = true;
        Assert.False(input.TryUpdate(pointer, out reason));
        Assert.Equal("constraint-rejected", reason);
        Assert.Equal(4, owner.Value);
        owner.OnValueChanged += _ => input.Cancel();
        input.Dispose();
        input.Dispose();
        Assert.Equal(0, owner.Value);
        Assert.Equal(1, trace.Count(x => x == "rollback"));
        Assert.Equal(1, trace.Count(x => x == "release"));
        Assert.False(input.IsInteracting);
    }

    private sealed class ValueGizmo() : Gizmo<int>(0)
    {
        public override int Dimension => 0;
        protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer) => [];
        protected override void RenderCore(GizmoContext context) { }
        protected override void UpdateInteractionCore(GizmoPointer pointer) { }
    }

    private sealed class Handle(Gizmo owner, int id) : IGizmoHandle
    {
        public GizmoHandleId Id { get; } = new(id);
        public Gizmo? Owner { get; set; } = owner;
        public bool Hovered { get; set; }
        public bool Interacting { get; set; }
    }

    private sealed class HitTarget(Gizmo owner, Handle handle, double distance) : IGizmoHitTarget
    {
        public Gizmo Owner => owner;
        public IEnumerable<GizmoHandleHit> HitTest(GizmoPointer pointer) => [new(owner, handle, distance)];
        public void UpdateHover(IGizmoHandle target, bool hovered)
        {
            Assert.Same(handle, target);
            handle.Hovered = hovered;
        }
        public void UpdateInteracting(IGizmoHandle target, bool interacting)
        {
            Assert.Same(handle, target);
            handle.Interacting = interacting;
        }
    }

    private sealed class FrameSource(GizmoFrame frame) : IGizmoFrameSource
    {
        public GizmoFrame GetFrame(GizmoContext context)
        {
            frame.Validate(context);
            return frame;
        }
    }

    private sealed class ParameterSource<T>(IGizmoValueSource<int> value, Func<T, int> map, List<string> trace)
        : IInteractionParameterSource<T>
    {
        private int baseline;
        public T Parameter { get; private set; } = default!;
        public bool Modifying { get; private set; }
        public bool ThrowOnBegin { get; init; }
        public bool ThrowOnEnd { get; init; }
        public bool RejectUpdate { get; set; }
        public void BeginModification()
        {
            trace.Add("parameter-begin");
            Modifying = true;
            if (ThrowOnBegin)
            {
                Modifying = false;
                trace.Add("parameter-abort");
                throw new InvalidOperationException("Parameter initialization failed.");
            }
            baseline = value.Value;
        }
        public bool TrySetParameter(T parameter, [NotNullWhen(false)] out string? rejection)
        {
            Assert.True(Modifying);
            if (RejectUpdate) { rejection = "constraint-rejected"; return false; }
            rejection = null;
            Parameter = parameter;
            value.Value = baseline + map(parameter);
            return true;
        }
        public void EndModification()
        {
            Assert.True(Modifying);
            Modifying = false;
            trace.Add("parameter-end");
            if (ThrowOnEnd) throw new InvalidOperationException("Parameter cleanup failed.");
        }
    }

    private sealed class EditTarget(IGizmoValueSource<int> value, List<string> trace) : IGizmoEditStateTarget
    {
        private int baseline;
        public bool Active { get; private set; }
        public bool RejectBegin { get; init; }
        public bool ThrowOnBegin { get; init; }
        public bool TryBegin([NotNullWhen(false)] out string? rejection)
        {
            trace.Add("edit-begin");
            Active = true;
            if (ThrowOnBegin)
            {
                Active = false;
                trace.Add("edit-abort");
                throw new InvalidOperationException("Owner initialization failed.");
            }
            if (RejectBegin)
            {
                rejection = "owner-rejected";
                Active = false;
                trace.Add("edit-abort");
                return false;
            }
            rejection = null;
            baseline = value.Value;
            return true;
        }
        public void Complete()
        {
            Assert.True(Active);
            Active = false;
            trace.Add("commit");
        }
        public void Cancel()
        {
            Assert.True(Active);
            Active = false;
            value.Value = baseline;
            trace.Add("rollback");
        }
    }

    private sealed class Target(Func<IGizmoDragSession?> create, List<string> trace) : IGizmoDragTarget
    {
        public bool TryPrepare(GizmoPointer pointer, [NotNullWhen(true)] out IGizmoDragSession? session,
            [NotNullWhen(false)] out string? rejection)
        {
            trace.Add("prepare");
            session = create();
            rejection = session is null ? "unusable-measurement" : null;
            return session is not null;
        }
    }

    private sealed class Session<T>(IInteractionParameterSource<T> parameter, IGizmoEditStateTarget edit,
        Func<GizmoPointer, T> measure, List<string> trace) : IGizmoDragSession
    {
        private bool modifying, editing, ended;
        public bool TryBegin([NotNullWhen(false)] out string? rejection)
        {
            if (ended) { rejection = "session-ended"; return false; }
            try
            {
                parameter.BeginModification();
                modifying = true;
                if (!edit.TryBegin(out rejection)) { Cancel(); return false; }
                editing = true;
                return true;
            }
            catch { Cancel(); throw; }
        }
        public bool TryUpdate(GizmoPointer pointer, [NotNullWhen(false)] out string? rejection)
        {
            if (ended || !editing) { rejection = "no-active-session"; return false; }
            trace.Add("measure");
            return parameter.TrySetParameter(measure(pointer), out rejection);
        }
        public void Complete() => End(true);
        public void Cancel() => End(false);
        public void Dispose() => Cancel();
        private void End(bool complete)
        {
            if (ended) return;
            ended = true;
            try
            {
                bool parameterEnded = false;
                try
                {
                    if (modifying) { modifying = false; parameter.EndModification(); }
                    parameterEnded = true;
                }
                finally
                {
                    if (editing)
                    {
                        editing = false;
                        if (complete && parameterEnded) edit.Complete();
                        else edit.Cancel();
                    }
                }
            }
            finally { trace.Add("release"); }
        }
    }

    private sealed class InputBehavior : IGizmoDragInputBehavior
    {
        private IGizmoDragSession? session;
        private bool transitioning, disposed;
        public bool IsInteracting => session is not null;
        public bool TryBegin(IGizmoDragTarget target, GizmoPointer pointer,
            [NotNullWhen(false)] out string? rejection)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (session is not null || transitioning) { rejection = "busy"; return false; }
            transitioning = true;
            try
            {
                if (!target.TryPrepare(pointer, out var prepared, out rejection)) return false;
                bool accepted = false;
                try
                {
                    if (!prepared.TryBegin(out rejection)) return false;
                    session = prepared;
                    accepted = true;
                    return true;
                }
                finally { if (!accepted) prepared.Dispose(); }
            }
            finally { transitioning = false; }
        }
        public bool TryUpdate(GizmoPointer pointer, [NotNullWhen(false)] out string? rejection)
        {
            if (session is null) { rejection = "no-active-session"; return false; }
            return session.TryUpdate(pointer, out rejection);
        }
        public void Complete() => End(true);
        public void Cancel() => End(false);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Cancel();
        }
        private void End(bool complete)
        {
            var current = session;
            session = null;
            if (current is null) return;
            transitioning = true;
            try
            {
                if (complete) current.Complete();
                else current.Cancel();
            }
            finally
            {
                try { current.Dispose(); }
                finally { transitioning = false; }
            }
        }
    }
}
