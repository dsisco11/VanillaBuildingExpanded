using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoHandleTests
{
    private static readonly GizmoHandleId id = new(47);
    private static readonly GizmoPointer pointer = new(new(0),
        new(Vector256<double>.Zero, Vector256.Create(0d, 0d, -1d, 0d)));
    private static readonly AppearenceProperties baseline = new()
    {
        Shape = GizmoShape.Arrow, Size = 64, Color = new Vector4(1, 0.2f, 0.2f, 0.8f), LineWidth = 1.6f,
        LabelTextColor = Vector4.One, LabelBackgroundColor = new Vector4(0, 0, 0, 0.65f),
        LabelFontFamily = "Sans", LabelFontSize = 14, LabelBold = true, LabelPadding = 3
    };

    [Fact]
    public void Appearance_EveryNullablePropertyOverridesIndependentlyAndOtherFieldsUseDefault()
    {
        var cases = new (AppearenceProperties Override, AppearenceProperties Expected)[]
        {
            (new() { Shape = GizmoShape.Diamond }, baseline with { Shape = GizmoShape.Diamond }),
            (new() { Size = 7 }, baseline with { Size = 7 }),
            (new() { Color = Vector4.Zero }, baseline with { Color = Vector4.Zero }),
            (new() { LineWidth = 2.8f }, baseline with { LineWidth = 2.8f }),
            (new() { LabelTextColor = Vector4.Zero }, baseline with { LabelTextColor = Vector4.Zero }),
            (new() { LabelBackgroundColor = Vector4.One }, baseline with { LabelBackgroundColor = Vector4.One }),
            (new() { LabelFontFamily = "Serif" }, baseline with { LabelFontFamily = "Serif" }),
            (new() { LabelFontSize = 18 }, baseline with { LabelFontSize = 18 }),
            (new() { LabelBold = false }, baseline with { LabelBold = false }),
            (new() { LabelPadding = 0 }, baseline with { LabelPadding = 0 })
        };
        using var handle = new GizmoHandle(id, baseline, new("Label"));
        var normal = handle.Appearance;
        foreach (var (properties, expected) in cases)
        {
            using var expectedHandle = new GizmoHandle(id, expected, new("Label"));
            handle.SetAppearance(EAppearenceState.Hover, properties);
            Assert.Equal(expectedHandle.Appearance, handle.ResolveAppearance(EAppearenceState.Hover));
            Assert.Equal(normal, handle.ResolveAppearance(EAppearenceState.Selected));
            Assert.Equal(normal, handle.Appearance);
        }
        handle.SetAppearance(EAppearenceState.Hover, default);
        Assert.Equal(normal, handle.ResolveAppearance(EAppearenceState.Hover));
        Assert.True(handle.RemoveAppearance(EAppearenceState.Hover));
        Assert.False(handle.RemoveAppearance(EAppearenceState.Hover));
        Assert.Equal(normal, handle.ResolveAppearance(EAppearenceState.Hover));
    }

    [Fact]
    public void Appearance_ColorOverridesReplaceWholeRgbaVectorsAndSelectedDoesNotInheritHover()
    {
        using var handle = new GizmoHandle(id, baseline, new("Label"));
        handle.SetAppearance(EAppearenceState.Hover, new()
        {
            Color = new Vector4(0.1f, 0.3f, 0.4f, 0), Shape = GizmoShape.Diamond,
            LabelTextColor = new Vector4(0.2f, 0.3f, 0.4f, 0.5f),
            LabelBackgroundColor = Vector4.Zero
        });
        var hover = handle.ResolveAppearance(EAppearenceState.Hover);
        Assert.Equal(new Vector4(0.1f, 0.3f, 0.4f, 0), hover.Color);
        var style = hover.LabelStyle ?? throw new InvalidOperationException("Label style is required.");
        Assert.Equal(new Vector4(0.2f, 0.3f, 0.4f, 0.5f), style.TextColor);
        Assert.Equal(Vector4.Zero, style.BackgroundColor);
        handle.SetAppearance(EAppearenceState.Selected, new() { LineWidth = 2.8f });
        var selected = handle.ResolveAppearance(EAppearenceState.Selected);
        Assert.Equal(GizmoShape.Arrow, selected.Shape);
        Assert.Equal(baseline.Color, selected.Color);
        Assert.Equal(2.8f, selected.LineWidth);
        Assert.Equal(handle.Appearance.LabelStyle, selected.LabelStyle);
    }

    [Fact]
    public void Appearance_EachRequiredBaselineFieldIsEnforcedEvenIfAnOverrideCouldSupplyIt()
    {
        var incomplete = new[]
        {
            baseline with { Shape = null }, baseline with { Size = null }, baseline with { Color = null },
            baseline with { LineWidth = null }, baseline with { LabelTextColor = null },
            baseline with { LabelBackgroundColor = null }, baseline with { LabelFontFamily = null },
            baseline with { LabelFontSize = null }, baseline with { LabelBold = null },
            baseline with { LabelPadding = null }
        };
        using var handle = new GizmoHandle(id, baseline, new("Label"));
        handle.SetAppearance(EAppearenceState.Hover, baseline);
        int notifications = 0;
        handle.Invalidated += (_, _) => notifications++;
        foreach (var properties in incomplete)
        {
            Assert.Throws<ArgumentException>(() => new GizmoHandle(id, properties, new("Label")));
            Assert.Throws<ArgumentException>(() => handle.SetAppearance(EAppearenceState.Default, properties));
            Assert.Equal(baseline, handle.Appearances[EAppearenceState.Default]);
            Assert.Equal(baseline.ResolveOver(default, true), handle.Appearance);
        }
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void Appearance_InvalidSuppliedPropertiesAreRejectedAtomicallyEvenInInactiveStates()
    {
        var invalid = new AppearenceProperties[]
        {
            new() { Shape = (GizmoShape)999 }, new() { Size = 0 }, new() { Size = double.NaN },
            new() { Size = double.PositiveInfinity }, new() { LineWidth = 0 }, new() { LineWidth = float.NaN },
            new() { LabelFontFamily = " " }, new() { LabelFontFamily = "Sans\n" },
            new() { LabelFontSize = 0 }, new() { LabelFontSize = double.NaN },
            new() { LabelPadding = -1 }, new() { LabelPadding = double.PositiveInfinity }
        };
        using var handle = new GizmoHandle(id, baseline);
        int notifications = 0;
        handle.Invalidated += (_, _) => notifications++;
        foreach (var properties in invalid)
            Assert.ThrowsAny<ArgumentException>(() => handle.SetAppearance(EAppearenceState.Selected, properties));
        foreach (var color in new[]
        {
            new Vector4(-0.1f, 0, 0, 0), new Vector4(0, 1.1f, 0, 0),
            new Vector4(0, 0, float.NaN, 0), new Vector4(0, 0, 0, float.PositiveInfinity)
        })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                handle.SetAppearance(EAppearenceState.Selected, new() { Color = color }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                handle.SetAppearance(EAppearenceState.Selected, new() { LabelTextColor = color }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                handle.SetAppearance(EAppearenceState.Selected, new() { LabelBackgroundColor = color }));
        }
        Assert.Single(handle.Appearances);
        Assert.Throws<ArgumentException>(() => handle.RemoveAppearance(EAppearenceState.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => handle.SetAppearance((EAppearenceState)999, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => handle.RemoveAppearance((EAppearenceState)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => handle.ResolveAppearance((EAppearenceState)999));
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void LabelAttachment_RequiresCompleteBaselineStylingBeforeMutation()
    {
        var simple = new AppearenceProperties
        {
            Shape = GizmoShape.Circle, Size = 7, Color = Vector4.One, LineWidth = 1
        };
        using var handle = new GizmoHandle(id, simple);
        Assert.Null(handle.Appearance.LabelStyle);
        Assert.Throws<ArgumentException>(() => handle.Label = new("Label"));
        Assert.Null(handle.Label);
        handle.SetAppearance(EAppearenceState.Default, baseline);
        handle.Label = new("Label");
        Assert.NotNull(handle.Appearance.LabelStyle);
        handle.Label = null;
        Assert.Null(handle.Appearance.LabelStyle);
    }

    [Fact]
    public void Ownership_RejectsDuplicatesForeignAndDisposedInstancesAndProtectsCollections()
    {
        using var first = new HandleGizmo();
        using var second = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline);
        using var duplicate = new GizmoHandle(id, baseline);
        first.AddHandle(handle);
        Assert.Same(handle, first.Handles[id]);
        Assert.Same(first, handle.Owner);
        Assert.Throws<InvalidOperationException>(() => first.AddHandle(handle));
        Assert.Throws<InvalidOperationException>(() => second.AddHandle(handle));
        Assert.Throws<ArgumentException>(() => first.AddHandle(duplicate));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<GizmoHandleId, GizmoHandle>)first.Handles).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<EAppearenceState, AppearenceProperties>)handle.Appearances).Clear());
        Assert.False(first.RemoveHandle(duplicate));
        Assert.True(first.RemoveHandle(handle));
        Assert.Null(handle.Owner);
        Assert.False(first.BeginInteraction(id, pointer));
        first.HoveredHandle = id;
        Assert.Null(first.HoveredHandle);
        second.AddHandle(handle);
        Assert.Empty(first.Handles);
        Assert.Same(second, handle.Owner);
        using var disposed = new GizmoHandle(new(99), baseline);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.AddHandle(disposed));
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.AddHandle(duplicate));
    }

    [Fact]
    public void Ownership_DetachmentRemovesOwnerSubscriptionsButRetainsReusableConfiguration()
    {
        using var first = new HandleGizmo();
        using var second = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline);
        int firstEvents = 0, secondEvents = 0, handleEvents = 0;
        first.HandleInvalidated += (_, _) => firstEvents++;
        second.HandleInvalidated += (_, _) => secondEvents++;
        handle.Invalidated += (_, _) => handleEvents++;
        first.AddHandle(handle);
        first.RemoveHandle(handle);
        second.AddHandle(handle);
        firstEvents = secondEvents = handleEvents = 0;
        handle.SetAppearance(EAppearenceState.Default, baseline with { Color = Vector4.One });
        Assert.Equal(0, firstEvents);
        Assert.Equal(1, secondEvents);
        Assert.Equal(1, handleEvents);
        second.Dispose();
        Assert.True(handle.IsDisposed);
        Assert.Null(handle.Owner);
        Assert.Empty(second.Handles);
        Assert.Empty(handle.Appearances);
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => handle.SetAppearance(EAppearenceState.Hover, default));
        Assert.Throws<ObjectDisposedException>(() => handle.ResolveAppearance(EAppearenceState.Default));
    }

    [Fact]
    public void Eligibility_CombinesLocalOwnerDimensionAndDisposalWithoutInvisibleCapture()
    {
        using var owner = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline);
        Assert.False(handle.IsEligible(0));
        owner.AddHandle(handle);
        Assert.True(handle.IsEligible(0));
        Assert.False(handle.IsEligible(1));
        owner.HoveredHandle = id;
        Assert.True(handle.IsHovered);
        handle.Visible = false;
        Assert.False(handle.IsHovered);
        Assert.False(handle.IsEligible(0));
        Assert.False(owner.BeginInteraction(id, pointer));
        owner.HoveredHandle = id;
        Assert.Null(owner.HoveredHandleInstance);
        Assert.Null(owner.HoveredHandle);
        handle.Visible = true;
        owner.HoveredHandle = id;
        handle.Enabled = false;
        Assert.Null(owner.HoveredHandleInstance);
        Assert.False(handle.IsEligible(0));
        Assert.False(owner.BeginInteraction(id, pointer));
        handle.Enabled = true;
        owner.Visible = false;
        Assert.False(handle.IsEligible(0));
        owner.Visible = true;
        owner.Enabled = false;
        Assert.False(handle.IsEligible(0));
        owner.Enabled = true;
        owner.Dispose();
        Assert.False(handle.IsEligible(0));
    }

    [Fact]
    public void State_UsesActualInstancesAndCapturePrecedenceWithoutHoverInheritance()
    {
        using var owner = new HandleGizmo();
        using var manager = new GizmoManager();
        using var handle = new GizmoHandle(id, baseline, new("Label"));
        handle.SetAppearance(EAppearenceState.Hover, new() { Color = Vector4.Zero, Shape = GizmoShape.Circle });
        handle.SetAppearance(EAppearenceState.Selected, new() { LineWidth = 2.8f });
        owner.AddHandle(handle);
        manager.Add(owner);
        manager.Render(pointer.Context);
        manager.UpdateHover(pointer);
        Assert.Same(handle, owner.HoveredHandleInstance);
        Assert.True(handle.IsHovered);
        Assert.Equal(GizmoShape.Circle, handle.Appearance.Shape);
        Assert.True(manager.TryBegin(pointer));
        Assert.Same(handle, owner.CapturedHandleInstance);
        Assert.False(handle.IsHovered);
        owner.HoveredHandle = id;
        Assert.True(handle.IsHovered);
        Assert.True(handle.IsSelected);
        Assert.Equal(EAppearenceState.Selected, handle.AppearanceState);
        Assert.Equal(GizmoShape.Arrow, handle.Appearance.Shape);
        Assert.Equal(baseline.Color, handle.Appearance.Color);
        Assert.Equal(2.8f, handle.Appearance.LineWidth);
        manager.Complete();
        Assert.Null(owner.CapturedHandleInstance);
        Assert.Null(owner.HoveredHandleInstance);
        Assert.Equal(EAppearenceState.Default, handle.AppearanceState);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("removed")]
    [InlineData("disposed")]
    [InlineData("owner-hidden")]
    [InlineData("owner-disposed")]
    public void Capture_LosingEligibilityRollsBackBeforeMutationAndCancelsOnce(string reason)
    {
        using var owner = new HandleGizmo();
        using var manager = new GizmoManager();
        using var handle = new GizmoHandle(id, baseline);
        owner.AddHandle(handle);
        manager.Add(owner);
        manager.Render(pointer.Context);
        Assert.True(manager.TryBegin(pointer));
        owner.Value = 12;
        int cancellations = 0;
        owner.OnInteractionCanceled += _ =>
        {
            Assert.Equal(0, owner.Value);
            Assert.Null(owner.CapturedHandleInstance);
            Assert.Same(owner, handle.Owner);
            if (reason == "hidden") Assert.True(handle.Visible);
            if (reason == "disabled") Assert.True(handle.Enabled);
            cancellations++;
        };
        switch (reason)
        {
            case "hidden": handle.Visible = false; break;
            case "disabled": handle.Enabled = false; break;
            case "removed": owner.RemoveHandle(handle); break;
            case "disposed": handle.Dispose(); break;
            case "owner-hidden": owner.Visible = false; break;
            default: owner.Dispose(); break;
        }
        Assert.Equal(1, cancellations);
        Assert.Null(manager.CapturedGizmo);
        Assert.Null(owner.CapturedHandleInstance);
        manager.Cancel();
        Assert.Equal(1, cancellations);
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("hit")]
    [InlineData("interaction")]
    public void ConfigurationReplacement_CancelsBeforeChangingBorrowedTargets(string replacement)
    {
        using var owner = new HandleGizmo();
        using var foreign = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline);
        var frame = new FrameSource();
        var hit = new HitTarget(owner);
        var target = new DragTarget();
        handle.Configure(frame, hit, target);
        owner.AddHandle(handle);
        using var manager = new GizmoManager();
        manager.Add(owner);
        manager.Render(pointer.Context);
        Assert.True(manager.TryBegin(pointer));
        owner.Value = 12;
        handle.Configure(frame, hit, target);
        Assert.True(handle.IsSelected);
        Assert.Throws<ArgumentException>(() => handle.HitTarget = new HitTarget(foreign));
        Assert.True(handle.IsSelected);
        int cancellations = 0;
        owner.OnInteractionCanceled += _ =>
        {
            Assert.Same(frame, handle.FrameSource);
            Assert.Same(hit, handle.HitTarget);
            Assert.Same(target, handle.InteractionTarget);
            Assert.Equal(0, owner.Value);
            cancellations++;
        };
        switch (replacement)
        {
            case "frame": handle.FrameSource = new FrameSource(); break;
            case "hit": handle.HitTarget = new HitTarget(owner); break;
            default: handle.InteractionTarget = new DragTarget(); break;
        }
        Assert.Equal(1, cancellations);
        Assert.False(handle.IsSelected);
    }

    [Fact]
    public void Invalidation_DistinguishesGeometryRasterAndPlacementWithoutCancelingCapture()
    {
        using var owner = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline, new("Label"));
        owner.AddHandle(handle);
        using var manager = new GizmoManager();
        manager.Add(owner);
        manager.Render(pointer.Context);
        Assert.True(manager.TryBegin(pointer));
        List<GizmoHandleInvalidation> changes = [];
        handle.Invalidated += (_, change) => changes.Add(change);
        handle.SetAppearance(EAppearenceState.Selected, new() { Color = Vector4.One });
        Assert.Equal(GizmoHandleInvalidation.Visual, Assert.Single(changes));
        changes.Clear();
        handle.SetAppearance(EAppearenceState.Selected, new() { Shape = GizmoShape.Diamond, Size = 7 });
        Assert.True(Assert.Single(changes).HasFlag(GizmoHandleInvalidation.Geometry));
        changes.Clear();
        handle.SetAppearance(EAppearenceState.Selected, new() { LabelFontSize = 18 });
        Assert.True(Assert.Single(changes).HasFlag(GizmoHandleInvalidation.LabelTexture));
        changes.Clear();
        handle.Label = new("Label", 20, 30, GizmoLabelVisibility.Hover);
        Assert.Equal(GizmoHandleInvalidation.Visual | GizmoHandleInvalidation.LabelPlacement, Assert.Single(changes));
        changes.Clear();
        handle.Label = new("Renamed", 20, 30);
        Assert.True(Assert.Single(changes).HasFlag(GizmoHandleInvalidation.LabelTexture));
        Assert.True(handle.IsSelected);
        Assert.Same(owner, manager.CapturedGizmo);
    }

    [Fact]
    public void Removal_ReentrantReplacementCannotDetachAnotherInstanceWithTheSameId()
    {
        using var owner = new HandleGizmo();
        using var original = new GizmoHandle(id, baseline);
        using var replacement = new GizmoHandle(id, baseline);
        owner.AddHandle(original);
        Assert.True(owner.BeginInteraction(id, pointer));
        owner.OnInteractionCanceled += _ =>
        {
            Assert.True(owner.RemoveHandle(original));
            owner.AddHandle(replacement);
        };
        Assert.True(owner.RemoveHandle(original));
        Assert.Null(original.Owner);
        Assert.Same(replacement, owner.Handles[id]);
        Assert.Same(owner, replacement.Owner);
    }

    [Fact]
    public void Removal_ThrowingCancellationStillDetachesAndSurfacesFailure()
    {
        using var owner = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline);
        owner.AddHandle(handle);
        Assert.True(owner.BeginInteraction(id, pointer));
        owner.Value = 10;
        owner.OnInteractionCanceled += _ => throw new InvalidOperationException("Observer failed.");
        Assert.Throws<InvalidOperationException>(() => owner.RemoveHandle(handle));
        Assert.Equal(0, owner.Value);
        Assert.Null(handle.Owner);
        Assert.Empty(owner.Handles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_ThrowingStateObserverCannotSkipRollbackOrTermination(bool hovered)
    {
        using var owner = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline);
        owner.AddHandle(handle);
        Assert.True(owner.BeginInteraction(id, pointer));
        owner.Value = 10;
        if (hovered) owner.HoveredHandle = id;
        int ended = 0, canceled = 0;
        owner.InteractionEnded += _ => ended++;
        owner.OnInteractionCanceled += _ => canceled++;
        Action<GizmoHandle, GizmoHandleInvalidation> observer =
            (_, _) => throw new InvalidOperationException("State observer failed.");
        handle.Invalidated += observer;
        Assert.Throws<InvalidOperationException>(owner.CancelInteraction);
        handle.Invalidated -= observer;
        Assert.Equal(0, owner.Value);
        Assert.Equal(1, owner.CancelCount);
        Assert.Equal(1, ended);
        Assert.Equal(1, canceled);
        Assert.False(owner.IsInteracting);
        Assert.Null(owner.HoveredHandleInstance);
        Assert.Null(owner.CapturedHandleInstance);
        owner.CancelInteraction();
        Assert.Equal(1, owner.CancelCount);
    }

    [Fact]
    public void Cancellation_ReentrantDisposalDefersResourcesUntilRollbackAndNotification()
    {
        using var owner = new HandleGizmo();
        using var handle = new GizmoHandle(id, baseline);
        owner.AddHandle(handle);
        Assert.True(owner.BeginInteraction(id, pointer));
        owner.Value = 10;
        int canceled = 0;
        owner.OnInteractionCanceled += _ =>
        {
            Assert.Equal(0, owner.Value);
            Assert.Equal(1, owner.CancelCount);
            Assert.False(owner.ResourcesDisposed);
            Assert.False(handle.IsDisposed);
            canceled++;
        };
        handle.Invalidated += (_, _) =>
        {
            if (owner.IsDisposed) return;
            owner.Dispose();
            Assert.False(owner.ResourcesDisposed);
        };
        owner.CancelInteraction();
        Assert.Equal(1, canceled);
        Assert.True(owner.IsDisposed);
        Assert.True(owner.ResourcesDisposed);
        Assert.True(handle.IsDisposed);
        Assert.Null(handle.Owner);
        owner.Dispose();
        Assert.Equal(1, owner.CancelCount);
    }

    [Fact]
    public void Disposal_AttemptsEveryHandleAndClearsSubscriptionsDespiteObserverFailures()
    {
        var owner = new HandleGizmo();
        var first = new GizmoHandle(id, baseline);
        var second = new GizmoHandle(new(99), baseline);
        owner.AddHandle(first);
        owner.AddHandle(second);
        owner.HandleInvalidated += (_, _) => throw new InvalidOperationException("Observer failed.");
        var error = Assert.Throws<AggregateException>(owner.Dispose);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Null(first.Owner);
        Assert.Null(second.Owner);
        Assert.Empty(owner.Handles);
        owner.Dispose();
        first.Dispose();
        second.Dispose();
    }

    private sealed class HandleGizmo() : Gizmo<int>(0)
    {
        public int CancelCount { get; private set; }
        public bool ResourcesDisposed { get; private set; }
        public override int Dimension => 0;
        protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer) =>
            Handles.Values.Select(handle => new GizmoHit(handle.Id, 1));
        protected override void RenderCore(GizmoContext context) { }
        protected override void UpdateInteractionCore(GizmoPointer pointer) { }
        protected override void CancelInteractionCore() => CancelCount++;
        protected override void DisposeCore() => ResourcesDisposed = true;
    }

    private sealed class FrameSource : IGizmoFrameSource
    {
        public GizmoFrame GetFrame(GizmoContext context) =>
            new(Vector256<double>.Zero, Vector256.Create(1d, 0d, 0d, 0d), context.Dimension);
    }

    private sealed class HitTarget(Gizmo owner) : IGizmoHitTarget
    {
        public Gizmo Owner => owner;
        public IEnumerable<GizmoHandleHit> HitTest(GizmoPointer pointer) => [];
        public void UpdateHover(IGizmoHandle handle, bool hovered) { }
        public void UpdateInteracting(IGizmoHandle handle, bool interacting) { }
    }

    private sealed class DragTarget : IGizmoDragTarget
    {
        public bool TryPrepare(GizmoPointer pointer, [NotNullWhen(true)] out IGizmoDragSession? session,
            [NotNullWhen(false)] out string? rejection)
        {
            session = null;
            rejection = "Test configuration only.";
            return false;
        }
    }
}
