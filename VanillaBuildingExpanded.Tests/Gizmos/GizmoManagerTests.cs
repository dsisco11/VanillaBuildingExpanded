using VanillaBuildingExpanded.Gizmos;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoManagerTests
{
    [Fact]
    public void Arbitration_InstancePriorityThenDistanceThenHandleOrder()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        gizmo.Hits.Clear();
        gizmo.Hits.AddRange([new(new(9), 2, 1), new(new(2), 10), new(new(1), 10)]);
        var pointer = GizmoTests.Draw(manager, gizmo);
        Assert.Same(gizmo, manager.UpdateHover(pointer));
        Assert.Equal(new GizmoHandleId(1), gizmo.HoveredHandle);
        Assert.True(manager.TryBegin(pointer));
        Assert.Equal(new GizmoHandleId(1), manager.CapturedHandle);
    }

    [Fact]
    public void Arbitration_AcrossInstancesUsesQuantizedDepthThenInsertionNotHandlePriority()
    {
        using var manager = new GizmoManager();
        var first = new TestGizmo(0);
        var second = new TestGizmo(0);
        var third = new TestGizmo(0);
        first.Hits[0] = new(new(4), 10.0000009, 9);
        second.Hits[0] = new(new(0), 10.0000001);
        third.Hits[0] = new(new(0), 10.0000011);
        manager.Add(first);
        manager.Add(second);
        manager.Add(third);
        var context = new GizmoContext(0);
        manager.Render(context);
        var pointer = GizmoTests.Pointer(context);
        Assert.True(manager.TryBegin(pointer));
        Assert.Same(first, manager.CapturedGizmo);
        manager.Cancel();

        third.Hits[0] = new(new(0), 9.9999999);
        Assert.True(manager.TryBegin(pointer));
        Assert.Same(third, manager.CapturedGizmo);
    }

    [Fact]
    public void Capture_IsExclusiveAndTracksOutsideSilhouetteWithoutRetargeting()
    {
        using var manager = new GizmoManager();
        var first = new TestGizmo(1);
        var second = new TestGizmo(2);
        manager.Add(second);
        var pointer = GizmoTests.Draw(manager, first);
        first.Hits[0] = new(new(5), 1);
        Assert.True(manager.TryBegin(pointer));
        Assert.False(manager.TryBegin(pointer));
        first.Hits.Clear();
        second.Hits[0] = new(new(9), 0.1);
        Assert.Same(first, manager.UpdateHover(pointer));
        first.NextValue = 3;
        manager.Update(pointer);
        Assert.Equal(3, first.Value);
        Assert.Equal(2, second.Value);
        Assert.Equal(new GizmoHandleId(5), manager.CapturedHandle);
        manager.Complete();
        manager.Render(pointer.Context);
        Assert.True(manager.TryBegin(pointer));
        Assert.Same(second, manager.CapturedGizmo);
    }

    [Fact]
    public void OnlySuccessfullyRenderedEligibleInstancesCanBeHitInTheMatchingFrame()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        manager.Add(gizmo);
        var context = new GizmoContext(0);
        var pointer = GizmoTests.Pointer(context);
        Assert.False(manager.TryBegin(pointer));
        manager.Render(context);
        Assert.False(manager.TryBegin(GizmoTests.Pointer(new(0))));
        Assert.True(manager.TryBegin(pointer));
        manager.Cancel();
        gizmo.Visible = false;
        Assert.Null(gizmo.HoveredHandle);
        Assert.False(manager.TryBegin(pointer));
        gizmo.Visible = true;
        Assert.False(manager.TryBegin(pointer));
        manager.Render(context);
        Assert.True(manager.TryBegin(pointer));
        manager.Cancel();
        gizmo.Enabled = false;
        manager.Render(context);
        Assert.False(manager.TryBegin(pointer));
        gizmo.Enabled = true;
        manager.Render(new(1));
        Assert.False(manager.TryBegin(pointer));
    }

    [Fact]
    public void ValueChange_InvalidatesOldVisualHitUntilRedrawn()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        var pointer = GizmoTests.Draw(manager, gizmo);
        gizmo.Value = 5;
        Assert.False(manager.TryBegin(pointer));
        manager.Render(pointer.Context);
        Assert.True(manager.TryBegin(pointer));
    }

    [Fact]
    public void DimensionChangeDuringCapture_CancelsBeforeAnyFurtherUpdate()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        var pointer = GizmoTests.Draw(manager, gizmo);
        Assert.True(manager.TryBegin(pointer));
        gizmo.NextValue = 1;
        manager.Update(GizmoTests.Pointer(new(1)));
        Assert.Null(manager.CapturedGizmo);
        Assert.Equal(0, gizmo.Updates);
        Assert.Equal(0, gizmo.Value);
    }

    [Fact]
    public void Membership_IsUniqueAndRemovalDoesNotTransferCaptureOrDisposeTheInstance()
    {
        using var first = new GizmoManager();
        using var second = new GizmoManager();
        using var gizmo = new TestGizmo(0);
        var pointer = GizmoTests.Draw(first, gizmo);
        first.Add(gizmo);
        Assert.Equal(1, first.Count);
        Assert.Throws<InvalidOperationException>(() => second.Add(gizmo));
        Assert.True(first.TryBegin(pointer));
        first.Remove(gizmo);
        Assert.False(gizmo.IsDisposed);
        Assert.Null(first.CapturedGizmo);
        second.Add(gizmo);
        Assert.Equal(1, second.Count);
        Assert.False(second.TryBegin(pointer));
    }

    [Fact]
    public void RejectedBegin_DoesNotLeaveCaptureOrTerminalNotification()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0) { AcceptBegin = false };
        var pointer = GizmoTests.Draw(manager, gizmo);
        int terminal = 0;
        gizmo.OnInteractionCanceled += _ => terminal++;
        Assert.False(manager.TryBegin(pointer));
        Assert.Null(manager.CapturedGizmo);
        Assert.Null(gizmo.CapturedHandle);
        Assert.Equal(0, terminal);
        gizmo.AcceptBegin = true;
        Assert.True(manager.TryBegin(pointer));
    }

    [Fact]
    public void MutationDuringHitAndDraw_CannotCaptureRemovedOrUndrawnInstances()
    {
        using var manager = new GizmoManager();
        using var first = new TestGizmo(0);
        var added = new TestGizmo(0);
        first.OnDraw = () => { manager.Remove(first); manager.Add(added); };
        var pointer = GizmoTests.Draw(manager, first);
        Assert.Equal(0, added.Draws);
        Assert.False(manager.TryBegin(pointer));
        manager.Render(pointer.Context);
        added.OnHit = () => manager.Remove(added);
        Assert.False(manager.TryBegin(pointer));
        Assert.Null(manager.CapturedGizmo);
        added.Dispose();
    }

    [Fact]
    public void RendererFailure_IsExplicitAndCancelsDisablesAndRemovesInvisibleTargets()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(1);
        var pointer = GizmoTests.Draw(manager, gizmo);
        Assert.True(manager.TryBegin(pointer));
        gizmo.NextValue = 2;
        manager.Update(pointer);
        gizmo.OnDraw = () => throw new InvalidOperationException("render failed");
        var error = Assert.Throws<InvalidOperationException>(() => manager.Render(pointer.Context));
        Assert.Equal("render failed", error.Message);
        Assert.Equal(1, gizmo.Value);
        Assert.False(gizmo.Enabled);
        Assert.Null(manager.CapturedGizmo);
        Assert.False(manager.TryBegin(pointer));
    }

    [Fact]
    public void DisposalInsideRenderAndLateUse_CannotTouchTheNextSession()
    {
        var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        manager.Add(gizmo);
        gizmo.OnDraw = manager.Dispose;
        manager.Render(new(0));
        Assert.True(manager.IsDisposed);
        Assert.True(gizmo.IsDisposed);
        Assert.Equal(1, gizmo.Disposals);
        Assert.Equal(0, manager.Count);
        using var next = new GizmoManager();
        Assert.Throws<ObjectDisposedException>(() => manager.Render(new(0)));
        Assert.Throws<ObjectDisposedException>(() => manager.TryBegin(GizmoTests.Pointer(new(0))));
        Assert.Equal(0, next.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DimensionChange_CancelsBeforeRenderingOrReturningHover(bool render)
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        var pointer = GizmoTests.Draw(manager, gizmo);
        Assert.True(manager.TryBegin(pointer));
        gizmo.NextValue = 2;
        manager.Update(pointer);
        var next = new GizmoContext(1);
        if (render) manager.Render(next);
        else Assert.Null(manager.UpdateHover(GizmoTests.Pointer(next)));
        Assert.Null(manager.CapturedGizmo);
        Assert.Equal(0, gizmo.Value);
    }

    [Fact]
    public void DisposalFailure_StillDetachesAndDisposesAllMembersAndSurfacesTheFailure()
    {
        var manager = new GizmoManager();
        var first = new TestGizmo(0)
        {
            OnDispose = () => throw new InvalidOperationException("cleanup failed")
        };
        var second = new TestGizmo(0);
        manager.Add(first);
        manager.Add(second);
        var failure = Assert.Throws<AggregateException>(manager.Dispose);
        Assert.Equal("cleanup failed", Assert.Single(failure.InnerExceptions).Message);
        Assert.Equal(0, manager.Count);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        manager.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReentrantHitTesting_CannotAcquireANestedCapture(bool hover)
    {
        using var manager = new GizmoManager();
        var first = new TestGizmo(0);
        var second = new TestGizmo(0);
        first.Hits[0] = new(new(0), 1);
        second.Hits[0] = new(new(0), 2);
        manager.Add(second);
        var pointer = GizmoTests.Draw(manager, first);
        bool? nestedCapture = null;
        first.OnHit = () =>
        {
            var saved = first.Hits[0];
            first.Hits.Clear();
            first.OnHit = null;
            nestedCapture = manager.TryBegin(pointer);
            first.Hits.Add(saved);
        };

        if (hover) Assert.Same(first, manager.UpdateHover(pointer));
        else Assert.True(manager.TryBegin(pointer));

        Assert.False(nestedCapture);
        Assert.False(second.IsInteracting);
        Assert.Equal(!hover, first.IsInteracting);
        Assert.Same(hover ? null : first, manager.CapturedGizmo);
        if (hover) Assert.True(manager.TryBegin(pointer));
        manager.Cancel();
        Assert.False(first.IsInteracting);
        Assert.False(second.IsInteracting);
    }

    [Fact]
    public void TerminalCallbacksInsideBegin_DoNotReleaseTheOuterArbitrationGuard()
    {
        using var manager = new GizmoManager();
        var gizmo = new TestGizmo(0);
        var pointer = GizmoTests.Draw(manager, gizmo);
        bool? nestedCapture = null;
        gizmo.OnInteractionStarted += _ =>
        {
            manager.Cancel();
            manager.Render(pointer.Context);
            nestedCapture = manager.TryBegin(pointer);
        };
        Assert.False(manager.TryBegin(pointer));
        Assert.False(nestedCapture);
        Assert.Null(manager.CapturedGizmo);
        Assert.False(gizmo.IsInteracting);
    }
}
