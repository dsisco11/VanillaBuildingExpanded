using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.ComposedPositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitComposition_PreservesVisualsWithArbitraryIdentities(bool diamond)
    {
        using var f = new Fixture(marker: diamond ? GizmoShape.Diamond : GizmoShape.Circle);
        Assert.Equal(4, f.Draws.Count);
        var center = f.Gizmo.Handles[f.Center];
        Assert.Equal(diamond ? GizmoShape.Diamond : GizmoShape.Circle, center.Appearance.Shape);
        Assert.Equal(7, center.Appearance.Size);
        Assert.Equal(new Vector4(1, 0.85f, 0.25f, 1), center.Appearance.Color);
        AssertAxis(f.X, new(1, 0.2f, 0.2f, 1), V(1, 0, 0));
        AssertAxis(f.Y, new(0.2f, 1, 0.2f, 1), V(0, 1, 0));
        AssertAxis(f.Z, new(0.25f, 0.5f, 1, 1), V(0, 0, 1));
        Assert.Same(f.Gizmo, f.Manager.UpdateHover(f.Pointer()));
        Assert.Equal(f.Center, f.Gizmo.HoveredHandle);
        Assert.Equal(1, center.Appearance.Color.W);
        Assert.Equal(2.8f, center.Appearance.LineWidth);
        Assert.True(f.Manager.TryBegin(f.Pointer()));
        Assert.Same(center, f.Gizmo.CapturedHandleInstance);
        Assert.Equal(EAppearenceState.Selected, center.AppearanceState);
        f.Manager.Cancel();
        Assert.Equal(1, center.Appearance.Color.W);
        Assert.Equal(1.6f, center.Appearance.LineWidth);

        void AssertAxis(GizmoHandleId id, Vector4 color, System.Runtime.Intrinsics.Vector256<double> direction)
        {
            var handle = f.Gizmo.Handles[id];
            Assert.Equal(GizmoShape.Arrow, handle.Appearance.Shape);
            Assert.Equal(64, handle.Appearance.Size);
            Assert.Equal(color, handle.Appearance.Color);
            Assert.Equal(direction, handle.FrameSource!.GetFrame(f.Context).Direction);
        }
    }

    [Fact]
    public void ActualManager_RepeatedEditsAreExclusiveAndRestoreBeforeOwnerAndPublicCancellation()
    {
        using var f = new Fixture();
        using var other = new ComposedPositionGizmo(f.Baseline, _ => { }, g =>
            new GizmoEditStateTarget(g, () => true, () => { }, () => { }));
        f.Manager.Add(other);
        for (int i = 0; i < 30; i++)
        {
            f.Gizmo.Value = f.Baseline;
            f.Frame();
            f.Trace.Clear();
            Assert.True(f.Manager.TryBegin(f.Pointer()));
            Assert.Same(f.Gizmo, f.Manager.CapturedGizmo);
            Assert.False(f.Manager.TryBegin(f.Pointer()));
            f.Manager.Update(f.Pointer(2, 1));
            AssertCoordinates(f.Baseline.Coordinates + V(2, 1, 0), f.Gizmo.Value.Coordinates);
            if (i % 2 == 0)
            {
                f.Manager.Complete();
                f.Manager.Complete();
                Assert.Equal(new[] { "owner-begin", "started", "value", "owner-complete", "completed" }, f.Trace);
            }
            else
            {
                f.Manager.Cancel();
                f.Manager.Cancel();
                Assert.Equal(f.Baseline, f.Gizmo.Value);
                Assert.Equal(new[] { "owner-begin", "started", "value", "value", "owner-cancel", "canceled" }, f.Trace);
            }
            Assert.Null(f.Manager.CapturedGizmo);
            Assert.Null(other.CapturedHandleInstance);
        }
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("removed")]
    [InlineData("disposed")]
    [InlineData("frame")]
    [InlineData("hit")]
    [InlineData("interaction")]
    [InlineData("owner-hidden")]
    [InlineData("owner-disabled")]
    [InlineData("owner-removed")]
    [InlineData("owner-disposed")]
    [InlineData("dimension")]
    [InlineData("external-value")]
    public void CaptureMutation_CancelsExactlyOnceBeforeReplacement(string mutation)
    {
        using var f = new Fixture();
        var handle = f.Gizmo.Handles[f.Center];
        Assert.True(f.Manager.TryBegin(f.Pointer()));
        f.Manager.Update(f.Pointer(2));
        f.Trace.Clear();
        f.Gizmo.OnInteractionCanceled += _ =>
        {
            Assert.Equal(f.Baseline, f.Gizmo.Value);
            if (mutation == "owner-removed") Assert.Equal(1, f.Manager.Count);
            if (mutation == "hidden") Assert.True(handle.Visible);
            if (mutation == "disabled") Assert.True(handle.Enabled);
            if (mutation == "removed") Assert.Same(f.Gizmo, handle.Owner);
            if (mutation == "owner-hidden") Assert.True(f.Gizmo.Visible);
            if (mutation == "owner-disabled") Assert.True(f.Gizmo.Enabled);
        };
        switch (mutation)
        {
            case "hidden": handle.Visible = false; break;
            case "disabled": handle.Enabled = false; break;
            case "removed": f.Gizmo.RemoveHandle(handle); break;
            case "disposed": handle.Dispose(); break;
            case "frame": handle.FrameSource = new GizmoCameraFrameSource(() => f.Gizmo.Value); break;
            case "hit": handle.HitTarget = new GizmoDelegateHitTarget(f.Gizmo, _ => []); break;
            case "interaction": handle.InteractionTarget = new Target(new Session()); break;
            case "owner-hidden": f.Gizmo.Visible = false; break;
            case "owner-disabled": f.Gizmo.Enabled = false; break;
            case "owner-removed": f.Manager.Remove(f.Gizmo); break;
            case "owner-disposed": f.Gizmo.Dispose(); break;
            case "dimension": f.Gizmo.Value = new(f.Baseline.Coordinates, 1); break;
            case "external-value": f.Gizmo.Value = new(V(9, 8, -20), 0); break;
        }
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.False(f.Gizmo.IsInteracting);
        Assert.Equal(new[] { "value", "owner-cancel", "canceled" },
            f.Trace.Take(3));
        Assert.Equal(1, f.Trace.Count(t => t == "owner-cancel"));
        Assert.DoesNotContain("owner-complete", f.Trace);
        f.Manager.Cancel();
    }

    [Fact]
    public void VisualAndLabelChangesAndEqualAssignments_DoNotRebaseCapturedPlane()
    {
        using var f = new Fixture();
        Assert.True(f.Manager.TryBegin(f.Pointer(0.25)));
        var handle = f.Gizmo.Handles[f.Center];
        f.Manager.Update(f.Pointer(2.25));
        f.Gizmo.Value = f.Gizmo.Value;
        handle.SetAppearance(EAppearenceState.Selected, new() { Size = 11, Color = Vector4.One });
        handle.Label = new("live label");
        f.Frame();
        f.Manager.Update(f.Pointer(3.25));
        AssertCoordinates(f.Baseline.Coordinates + V(3, 0, 0), f.Gizmo.Value.Coordinates);
        Assert.Same(handle, f.Gizmo.CapturedHandleInstance);
        Assert.DoesNotContain("owner-cancel", f.Trace);
        f.Manager.Cancel();
        Assert.Equal(f.Baseline, f.Gizmo.Value);
    }

    [Fact]
    public void ReplacingFrameAndPicking_PreservesConfiguredAxisWithoutIdentityDispatch()
    {
        using var f = new Fixture();
        var handle = f.Gizmo.Handles[f.X];
        handle.FrameSource = new GizmoAxisFrameSource(() => f.Gizmo.Value, () => V(0, 1, 0));
        foreach (var member in f.Gizmo.Handles.Values) member.HitTarget = null;
        handle.HitTarget = new GizmoDelegateHitTarget(f.Gizmo, _ => [new(f.Gizmo, handle, 10, 0)]);
        f.Frame();
        Assert.True(f.Manager.TryBegin(f.Pointer()));
        f.Manager.Update(f.Pointer(4, 3));
        AssertCoordinates(f.Baseline.Coordinates + V(0, 3, 0), f.Gizmo.Value.Coordinates);
        f.Manager.Cancel();
        Assert.Equal(f.Baseline, f.Gizmo.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedOrThrowingBegin_ReleasesSessionAndDoesNotPublishStart(bool throws)
    {
        using var f = new Fixture();
        var session = new Session { Reject = !throws, ThrowBegin = throws };
        f.Gizmo.Handles[f.Center].InteractionTarget = new Target(session);
        if (throws) Assert.Throws<InvalidOperationException>(() => f.Manager.TryBegin(f.Pointer()));
        else Assert.False(f.Manager.TryBegin(f.Pointer()));
        Assert.Equal(1, session.Cancels);
        Assert.Equal(1, session.Disposals);
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.DoesNotContain("started", f.Trace);
        Assert.DoesNotContain("completed", f.Trace);
    }

    [Theory]
    [InlineData("handle")]
    [InlineData("manager")]
    [InlineData("dispose")]
    public void InitializationMutation_UnwindsWithoutPublishingSuccessfulStart(string kind)
    {
        using var f = new Fixture();
        f.BeginCallback = () =>
        {
            if (kind == "handle") f.Gizmo.Handles[f.Center].Enabled = false;
            else if (kind == "manager") f.Manager.Remove(f.Gizmo);
            else f.Gizmo.Dispose();
        };
        Assert.False(f.Manager.TryBegin(f.Pointer()));
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.Equal(1, f.Trace.Count(t => t == "owner-cancel"));
        Assert.DoesNotContain("started", f.Trace);
        Assert.DoesNotContain("completed", f.Trace);
    }

    [Fact]
    public void ReentrantTerminalCallbacks_CannotAcquireAnotherCaptureOrDuplicateTransactions()
    {
        using var f = new Fixture();
        Assert.True(f.Manager.TryBegin(f.Pointer()));
        f.Gizmo.OnInteractionCanceled += _ =>
        {
            f.Manager.Cancel();
            Assert.False(f.Manager.TryBegin(f.Pointer()));
            f.Gizmo.Dispose();
        };
        f.Manager.Update(f.Pointer(1));
        f.Manager.Cancel();
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.Equal(1, f.Trace.Count(t => t == "owner-cancel"));
        Assert.Equal(1, f.Trace.Count(t => t == "canceled"));
    }

    [Fact]
    public void HoverTeardown_CanRemoveOtherMembersWithoutEnumeratingStaleEntries()
    {
        using var f = new Fixture();
        using var other = new ComposedPositionGizmo(f.Baseline, _ => { }, g =>
            new GizmoEditStateTarget(g, () => true, () => { }, () => { }));
        f.Manager.Add(other);
        f.Frame();
        Assert.Same(f.Gizmo, f.Manager.UpdateHover(f.Pointer()));
        var handle = f.Gizmo.Handles[f.Center];
        handle.Invalidated += (_, _) =>
        {
            if (!handle.IsHovered) f.Manager.Remove(other);
        };
        Assert.Same(f.Gizmo, f.Manager.UpdateHover(f.Pointer()));
        Assert.Equal(1, f.Manager.Count);
        Assert.True(f.Manager.TryBegin(f.Pointer()));
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("dispose")]
    [InlineData("manager-dispose")]
    public void HoverTeardown_AllowsReentrantOwnerRemovalAndDisposal(string mutation)
    {
        using var f = new Fixture();
        Assert.Same(f.Gizmo, f.Manager.UpdateHover(f.Pointer()));
        var handle = f.Gizmo.Handles[f.Center];
        bool retired = false;
        handle.Invalidated += (_, _) =>
        {
            if (handle.IsHovered || retired) return;
            retired = true;
            if (mutation == "remove") f.Manager.Remove(f.Gizmo);
            else if (mutation == "dispose") f.Gizmo.Dispose();
            else f.Manager.Dispose();
        };
        Assert.Null(f.Manager.UpdateHover(f.Pointer()));
        Assert.Equal(0, f.Manager.Count);
        Assert.Null(f.Manager.CapturedGizmo);
        if (mutation == "remove")
        {
            f.Manager.Add(f.Gizmo);
            f.Frame();
            Assert.True(f.Manager.TryBegin(f.Pointer()));
        }
    }

    [Fact]
    public void GestureProtocol_ReentrantPreparationAndCompletionDisposeOneSession()
    {
        using var behavior = new GizmoDragInputBehavior();
        var session = new Session();
        var target = new Target(session) { Preparing = () => behavior.Cancel() };
        Assert.False(behavior.TryBegin(target, new(new(0), new(V(0, 0, 1), V(0, 0, -1))), out var reason));
        Assert.Equal("gesture-terminated", reason);
        Assert.Equal(1, session.Cancels);
        Assert.Equal(1, session.Disposals);
        session = new Session();
        session.Completing = () => { behavior.Cancel(); behavior.Complete(); };
        Assert.True(behavior.TryBegin(new Target(session),
            new(new(0), new(V(0, 0, 1), V(0, 0, -1))), out _));
        behavior.Complete();
        behavior.Cancel();
        Assert.Equal(1, session.Completes);
        Assert.Equal(0, session.Cancels);
        Assert.Equal(1, session.Disposals);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GizmoManager Manager = new();
        public readonly GizmoHandleId Center = new(83), X = new(41), Y = new(9), Z = new(772);
        public readonly GizmoPosition Baseline = new(V(0, 0, -20), 0);
        public readonly ComposedPositionGizmo Gizmo;
        public readonly List<string> Trace = [];
        public readonly List<GizmoHandleDraw> Draws = [];
        public GizmoContext Context = Camera();
        public Action? BeginCallback;

        public Fixture(GizmoShape marker = GizmoShape.Circle)
        {
            Gizmo = new(Baseline, Draws.Add, g => new GizmoEditStateTarget(g,
                () => { Trace.Add("owner-begin"); BeginCallback?.Invoke(); return true; },
                () => Trace.Add("owner-complete"), () => Trace.Add("owner-cancel")),
                marker: marker, centerId: Center, xId: X, yId: Y, zId: Z);
            Gizmo.OnValueChanged += _ => Trace.Add("value");
            Gizmo.OnInteractionStarted += _ => Trace.Add("started");
            Gizmo.OnInteractionCompleted += _ => Trace.Add("completed");
            Gizmo.OnInteractionCanceled += _ => Trace.Add("canceled");
            Manager.Add(Gizmo);
            Frame();
        }

        public void Frame()
        {
            Context = Camera();
            Draws.Clear();
            Manager.Render(Context);
        }

        public GizmoPointer Pointer(double x = 0, double y = 0) => new(Context, new(V(0, 0, 0), V(x, y, -20)));
        public void Dispose() => Manager.Dispose();
    }

    private sealed class Target(Session session) : IGizmoDragTarget
    {
        public Action? Preparing;
        public bool TryPrepare(GizmoPointer pointer, [NotNullWhen(true)] out IGizmoDragSession? prepared,
            [NotNullWhen(false)] out string? rejection)
        {
            Preparing?.Invoke();
            prepared = session;
            rejection = null;
            return true;
        }
    }

    private sealed class Session : IGizmoDragSession
    {
        public int Cancels, Completes, Disposals;
        public bool Reject, ThrowBegin;
        public Action? Completing;
        public bool TryBegin([NotNullWhen(false)] out string? rejection)
        {
            if (ThrowBegin) throw new InvalidOperationException("initialization failed");
            rejection = Reject ? "rejected" : null;
            return !Reject;
        }
        public bool TryUpdate(GizmoPointer pointer, [NotNullWhen(false)] out string? rejection)
        {
            rejection = null;
            return true;
        }
        public void Complete() { Completes++; Completing?.Invoke(); }
        public void Cancel() => Cancels++;
        public void Dispose() => Disposals++;
    }
}
