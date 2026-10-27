using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.PositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoLabelTests
{
    [Theory]
    [InlineData(GizmoLabelVisibility.Always, true, true, true)]
    [InlineData(GizmoLabelVisibility.Hover, false, true, false)]
    [InlineData(GizmoLabelVisibility.Selected, false, false, true)]
    public void BuiltInVisibilityTracksTheOwningHandle(GizmoLabelVisibility mode,
        bool idle, bool hover, bool selected)
    {
        var context = Camera();
        using var manager = new GizmoManager();
        var gizmo = new PositionGizmo(new(V(0, 0, -10), 0), _ => { });
        manager.Add(gizmo);
        manager.Render(context);
        var label = new GizmoLabel("Center", visibility: mode);
        Assert.Equal(idle, label.IsVisible(gizmo, PositionGizmo.Center, context));
        manager.UpdateHover(context.Camera!.CreatePointer(context, 400, 300));
        Assert.Equal(hover, label.IsVisible(gizmo, PositionGizmo.Center, context));
        Assert.Equal(idle, label.IsVisible(gizmo, PositionGizmo.X, context));
        Assert.True(manager.TryBegin(context.Camera.CreatePointer(context, 400, 300)));
        Assert.Equal(selected, label.IsVisible(gizmo, PositionGizmo.Center, context));
        Assert.Equal(idle, label.IsVisible(gizmo, PositionGizmo.X, context));
        manager.Complete();
        Assert.Equal(idle, label.IsVisible(gizmo, PositionGizmo.Center, context));
    }

    [Fact]
    public void PredicateReceivesLiveContextAndCannotOverrideGizmoEligibility()
    {
        var context = Camera();
        using var gizmo = new PositionGizmo(new(V(0, 0, -10), 0), _ => { });
        bool show = false;
        int calls = 0;
        var label = new GizmoLabel("Custom", visibility: GizmoLabelVisibility.Predicate, predicate: state =>
        {
            calls++;
            Assert.Same(gizmo, state.Gizmo);
            Assert.Same(context, state.Context);
            Assert.Equal(PositionGizmo.X, state.Handle);
            Assert.False(state.IsHovered);
            Assert.False(state.IsSelected);
            return show;
        });
        Assert.False(label.IsVisible(gizmo, PositionGizmo.X, context));
        show = true;
        Assert.True(label.IsVisible(gizmo, PositionGizmo.X, context));
        gizmo.Visible = false;
        Assert.False(label.IsVisible(gizmo, PositionGizmo.X, context));
        gizmo.Visible = true;
        gizmo.Enabled = false;
        Assert.False(label.IsVisible(gizmo, PositionGizmo.X, context));
        gizmo.Enabled = true;
        Assert.False(label.IsVisible(gizmo, PositionGizmo.X, new(1, context.Camera)));
        gizmo.Dispose();
        Assert.False(label.IsVisible(gizmo, PositionGizmo.X, context));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1.33)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    public void LabelOffsetsUseFramebufferPixelsFromRenderedCenterOrArrowTip(int handle, double scale)
    {
        var reference = V(2000000000, 32768, -2000000000);
        var baseCamera = Camera(reference: reference).Camera!;
        var camera = new GizmoCamera(baseCamera.View.ToArray(), baseCamera.Projection.ToArray(), reference,
            1600, 1200, scale, windowWidth: 800, windowHeight: 600);
        var context = new GizmoContext(2, camera);
        PositionGizmoDraw draw = default;
        using var gizmo = new PositionGizmo(new(reference + V(0, 0, -10), 2), data => draw = data);
        var id = new GizmoHandleId(handle);
        var label = new GizmoLabel("<literal>", 17, -23);
        gizmo.SetLabel(id, label);
        gizmo.Render(context);
        var part = draw.Geometry.Parts.Single(p => p.Handle == id);
        var anchor = handle == 0 ? part.Origin : part.Transform(Vector256.Create(1d, 0d, 0d, 0d));
        Assert.True(camera.TryProject(anchor, out var screen));
        Assert.True(GizmoRenderAdapter.TryProjectLabel(draw, part, out var found, out double x, out double y));
        Assert.Same(label, found);
        Assert.Equal(screen[0] + 17, x, 8);
        Assert.Equal(screen[1] - 23, y, 8);
    }

    [Fact]
    public void LabelConfigurationDoesNotInvalidateHitGeometryAndDisposalReleasesPredicates()
    {
        var context = Camera();
        using var manager = new GizmoManager();
        var gizmo = new PositionGizmo(new(V(0, 0, -10), 0), _ => { });
        manager.Add(gizmo);
        manager.Render(context);
        gizmo.SetLabel(PositionGizmo.Center, new("First"));
        gizmo.SetLabel(PositionGizmo.Center, new("Second"));
        Assert.Single(gizmo.Labels);
        Assert.Equal("Second", gizmo.Labels[PositionGizmo.Center].Text);
        Assert.True(manager.TryBegin(context.Camera!.CreatePointer(context, 400, 300)));
        gizmo.RemoveLabel(PositionGizmo.Center);
        Assert.True(gizmo.IsInteracting);
        Assert.False(gizmo.RemoveLabel(PositionGizmo.Center));
        gizmo.SetLabel(PositionGizmo.Center, new("Custom", visibility: GizmoLabelVisibility.Predicate,
            predicate: _ => true));
        gizmo.Dispose();
        Assert.Empty(gizmo.Labels);
        Assert.Throws<ObjectDisposedException>(() => gizmo.SetLabel(PositionGizmo.X, new("X")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("two\nlines")]
    public void InvalidLabelTextIsRejected(string text) =>
        Assert.ThrowsAny<ArgumentException>(() => new GizmoLabel(text));

    [Fact]
    public void InvalidOffsetsAndPolicyConfigurationsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoLabel("X", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoLabel("X", offsetY: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoLabel("X", visibility: (GizmoLabelVisibility)99));
        Assert.Throws<ArgumentException>(() => new GizmoLabel("X", visibility: GizmoLabelVisibility.Predicate));
        Assert.Throws<ArgumentException>(() => new GizmoLabel("X", predicate: _ => true));
    }
}
