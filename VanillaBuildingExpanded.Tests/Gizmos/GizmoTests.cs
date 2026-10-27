using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoTests
{
    [Fact]
    public void Setter_StoresBeforeNotifyingAndSuppressesEqualOwnerEchoes()
    {
        using var gizmo = new TestGizmo(5);
        List<int> values = [];
        gizmo.OnValueChanged += value =>
        {
            Assert.Equal(value, gizmo.Value);
            values.Add(value);
            gizmo.Value = value;
        };

        gizmo.Value = 5;
        gizmo.Value = 8;
        gizmo.Value = 8;
        gizmo.Value = -3;

        Assert.Equal(new[] { 8, -3 }, values);
    }

    [Fact]
    public void Lifecycle_BaselinePrecedesBeginOwnerNotificationAndRollbackPrecedesCancel()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(5);
        var pointer = Draw(manager, gizmo);
        List<string> events = [];
        gizmo.OnValueChanged += value => events.Add($"value:{value}");
        gizmo.OnInteractionStarted += handle =>
        {
            Assert.Equal(handle, manager.CapturedHandle);
            events.Add("begin");
            gizmo.Value = 9;
        };
        gizmo.OnInteractionCanceled += _ =>
        {
            Assert.Null(manager.CapturedGizmo);
            Assert.False(gizmo.IsInteracting);
            events.Add("cancel");
        };
        gizmo.OnInteractionCompleted += _ => events.Add("complete");

        Assert.True(manager.TryBegin(pointer));
        gizmo.NextValue = 12;
        manager.Update(pointer);
        manager.Update(pointer);
        manager.Cancel();
        manager.Cancel();
        manager.Complete();

        Assert.Equal(5, gizmo.Value);
        Assert.Equal(new[] { "begin", "value:9", "value:12", "value:5", "cancel" }, events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletionAndNoOpCancellation_EmitExactlyOneTerminalEvent(bool complete)
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(1);
        var pointer = Draw(manager, gizmo);
        int values = 0, completions = 0, cancellations = 0;
        gizmo.OnValueChanged += _ => values++;
        gizmo.OnInteractionCompleted += _ => completions++;
        gizmo.OnInteractionCanceled += _ => cancellations++;
        Assert.True(manager.TryBegin(pointer));
        if (complete) manager.Complete();
        else manager.Cancel();
        manager.Complete();
        manager.Cancel();
        Assert.Equal(0, values);
        Assert.Equal(complete ? 1 : 0, completions);
        Assert.Equal(complete ? 0 : 1, cancellations);
    }

    [Fact]
    public void ExternalReplacement_CancelsBeforeAssigningAndCannotBeOverwrittenByLateRollback()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(1);
        var pointer = Draw(manager, gizmo);
        List<string> events = [];
        gizmo.OnValueChanged += value => events.Add($"value:{value}");
        gizmo.OnInteractionCanceled += _ => events.Add("cancel");
        Assert.True(manager.TryBegin(pointer));
        gizmo.NextValue = 2;
        manager.Update(pointer);

        gizmo.CancelInteraction();
        gizmo.Value = 20;
        manager.Cancel();
        manager.Update(pointer);

        Assert.Equal(20, gizmo.Value);
        Assert.Null(manager.CapturedGizmo);
        Assert.Equal(new[] { "value:2", "value:1", "cancel", "value:20" }, events);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("removed")]
    [InlineData("disposed")]
    [InlineData("manager-disposed")]
    public void LosingEligibility_RestoresAndReleasesCaptureExactlyOnce(string reason)
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(1);
        var pointer = Draw(manager, gizmo);
        int cancellations = 0, completions = 0;
        List<int> values = [];
        gizmo.OnValueChanged += values.Add;
        gizmo.OnInteractionCanceled += _ => cancellations++;
        gizmo.OnInteractionCompleted += _ => completions++;
        Assert.True(manager.TryBegin(pointer));
        gizmo.NextValue = 2;
        manager.Update(pointer);

        switch (reason)
        {
            case "hidden": gizmo.Visible = false; break;
            case "disabled": gizmo.Enabled = false; break;
            case "removed": manager.Remove(gizmo); manager.Remove(gizmo); break;
            case "disposed": gizmo.Dispose(); gizmo.Dispose(); break;
            case "manager-disposed": manager.Dispose(); manager.Dispose(); break;
        }

        Assert.Equal(1, gizmo.Value);
        Assert.Equal(new[] { 2, 1 }, values);
        Assert.Null(manager.CapturedGizmo);
        Assert.Null(gizmo.CapturedHandle);
        Assert.Equal(1, cancellations);
        Assert.Equal(0, completions);
        if (!manager.IsDisposed) Assert.False(manager.TryBegin(pointer));
        gizmo.Dispose();
        Assert.Equal(1, gizmo.Disposals);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("preview")]
    [InlineData("rollback")]
    [InlineData("complete")]
    [InlineData("cancel")]
    public void DisposalInsideOwnerNotification_IsSafeAndHasOneTerminalNotification(string when)
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(1);
        var pointer = Draw(manager, gizmo);
        int cancellations = 0, completions = 0;
        gizmo.OnInteractionStarted += _ => { if (when == "begin") gizmo.Dispose(); };
        gizmo.OnValueChanged += value =>
        {
            if ((when == "preview" && value == 2) || (when == "rollback" && value == 1))
                gizmo.Dispose();
        };
        gizmo.OnInteractionCompleted += _ =>
        {
            completions++;
            if (when == "complete") gizmo.Dispose();
        };
        gizmo.OnInteractionCanceled += _ =>
        {
            cancellations++;
            if (when == "cancel") gizmo.Dispose();
        };

        bool began = manager.TryBegin(pointer);
        if (when != "begin")
        {
            Assert.True(began);
            gizmo.NextValue = 2;
            manager.Update(pointer);
            if (when == "complete") manager.Complete();
            else manager.Cancel();
        }
        else Assert.False(began);

        Assert.Null(manager.CapturedGizmo);
        Assert.Equal(0, manager.Count);
        Assert.Equal(1, gizmo.Disposals);
        Assert.Equal(when == "complete" ? 1 : 0, completions);
        Assert.Equal(when == "complete" ? 0 : 1, cancellations);
        Assert.Equal(when == "complete" ? 2 : 1, gizmo.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OwnerRemovalDuringTerminalNotification_CannotEmitASecondTerminal(bool complete)
    {
        using var manager = new GizmoManager();
        using var gizmo = new TestGizmo(1);
        var pointer = Draw(manager, gizmo);
        int terminal = 0;
        void Remove(GizmoHandleId _)
        {
            terminal++;
            manager.Remove(gizmo);
            gizmo.CancelInteraction();
            manager.Complete();
        }
        gizmo.OnInteractionCompleted += Remove;
        gizmo.OnInteractionCanceled += Remove;
        Assert.True(manager.TryBegin(pointer));
        if (complete) manager.Complete(); else manager.Cancel();
        Assert.Equal(1, terminal);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void RepeatedCaptures_UseNewBaselinesAndNeverDuplicateCallbacks()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        manager.Add(gizmo);
        int begins = 0, completions = 0, cancellations = 0;
        gizmo.OnInteractionStarted += _ => begins++;
        gizmo.OnInteractionCompleted += _ => completions++;
        gizmo.OnInteractionCanceled += _ => cancellations++;
        for (int i = 1; i <= 100; i++)
        {
            var context = new GizmoContext(0);
            manager.Render(context);
            var pointer = Pointer(context);
            Assert.True(manager.TryBegin(pointer));
            int baseline = gizmo.Value;
            gizmo.NextValue = i;
            manager.Update(pointer);
            if (i % 2 == 0) manager.Cancel();
            else manager.Complete();
            Assert.Equal(i % 2 == 0 ? baseline : i, gizmo.Value);
            Assert.Null(manager.CapturedGizmo);
        }
        Assert.Equal(100, begins);
        Assert.Equal(50, completions);
        Assert.Equal(50, cancellations);
    }

    [Fact]
    public void Setter_UsesDefaultEqualityIncludingNullAndImmutableReferenceValues()
    {
        using var gizmo = new ReferenceGizmo(null);
        List<Payload?> values = [];
        gizmo.OnValueChanged += values.Add;
        gizmo.Value = null;
        gizmo.Value = new(7);
        gizmo.Value = new(7);
        gizmo.Value = null;
        Assert.Equal(new Payload?[] { new(7), null }, values);
    }

    [Fact]
    public void InvalidOwnerArguments_AreExplicitAndDisposedInstancesRejectEdits()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(1);
        Assert.Throws<ArgumentNullException>(() => manager.Add(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoContext(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoHit(new(1), double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoHit(new(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoRay(default, default));
        Assert.Throws<ArgumentException>(() => manager.TryBegin(default));
        Assert.Throws<ArgumentException>(() => new GizmoPointer(new(0), default));
        gizmo.Dispose();
        Assert.Throws<ObjectDisposedException>(() => gizmo.Value = 3);
        Assert.Throws<ObjectDisposedException>(() => gizmo.Visible = true);
        Assert.Throws<ObjectDisposedException>(() => manager.Add(gizmo));
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.Add(new TestGizmo(0)));
    }

    internal static GizmoPointer Draw(GizmoManager manager, TestGizmo gizmo)
    {
        manager.Add(gizmo);
        var context = new GizmoContext(0);
        manager.Render(context);
        return Pointer(context);
    }

    internal static GizmoPointer Pointer(GizmoContext context) =>
        new(context, new(Vector256<double>.Zero, Vector256.Create(0d, 0d, -1d, 0d)));

    [Fact]
    public void Ray_UsesSimdDoublesWithoutLosingLargeCoordinateFractions()
    {
        var origin = Vector256.Create(1073741824.125, -17.5, -1073741824.25, 0d);
        var ray = new GizmoRay(origin, Vector256.Create(3d, 4d, 0d, 0d));

        Assert.Equal(origin, ray.Origin);
        Assert.Equal(0.6, ray.Direction[0], 12);
        Assert.Equal(0.8, ray.Direction[1], 12);
        Assert.Equal(0d, ray.Direction[2]);
        Assert.Equal(0d, ray.Direction[3]);
        Assert.Equal(1d, Vector256.Dot(ray.Direction, ray.Direction), 12);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Ray_RejectsNonfiniteSimdCoordinates(double invalid)
    {
        var valid = Vector256.Create(0d, 0d, -1d, 0d);
        for (int lane = 0; lane < 3; lane++)
        {
            var vector = valid.WithElement(lane, invalid);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoRay(vector, valid));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoRay(valid, vector));
        }
    }

    [Fact]
    public void Ray_RejectsNonzeroPaddingAndDegenerateDirections()
    {
        var valid = Vector256.Create(0d, 0d, -1d, 0d);
        var padded = valid.WithElement(3, 1d);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoRay(padded, valid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoRay(valid, padded));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GizmoRay(valid, Vector256.Create(1e-13, 0d, 0d, 0d)));
    }

    private sealed record Payload(int Number);

    private sealed class ReferenceGizmo(Payload? value) : Gizmo<Payload?>(value)
    {
        public override int Dimension => 0;
        protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer) => [];
        protected override void RenderCore(GizmoContext context) { }
        protected override void UpdateInteractionCore(GizmoPointer pointer) { }
    }
}

internal sealed class TestGizmo(int value) : Gizmo<int>(value)
{
    public override int Dimension => CurrentDimension;
    public int CurrentDimension { get; set; }
    public List<GizmoHit> Hits { get; } = [new(new(0), 10)];
    public int NextValue { get; set; } = value;
    public int Updates { get; private set; }
    public int Disposals { get; private set; }
    public int Draws { get; private set; }
    public bool AcceptBegin { get; set; } = true;
    public Action? OnDraw { get; set; }
    public Action? OnHit { get; set; }
    public Action? OnDispose { get; set; }
    protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer)
    {
        OnHit?.Invoke();
        return Hits;
    }
    protected override void RenderCore(GizmoContext context)
    {
        Draws++;
        OnDraw?.Invoke();
    }
    protected override bool BeginInteractionCore(GizmoHandleId handle, GizmoPointer pointer) => AcceptBegin;
    protected override void UpdateInteractionCore(GizmoPointer pointer)
    {
        Updates++;
        Value = NextValue;
    }
    protected override void DisposeCore()
    {
        Disposals++;
        OnDispose?.Invoke();
    }
}
