using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.PositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoLabelTests
{
    private static ComposedPositionGizmo Create(GizmoPosition position, Action<GizmoHandleDraw>? draw = null) =>
        new(position, draw ?? (_ => { }), g => new GizmoEditStateTarget(g, () => true, () => { }, () => { }));

    [Theory]
    [InlineData(GizmoLabelVisibility.Always, true, true, true)]
    [InlineData(GizmoLabelVisibility.Hover, false, true, false)]
    [InlineData(GizmoLabelVisibility.Selected, false, false, true)]
    public void BuiltInVisibilityTracksTheOwningHandle(GizmoLabelVisibility mode,
        bool idle, bool hover, bool selected)
    {
        var context = Camera();
        using var manager = new GizmoManager();
        var gizmo = Create(new(V(0, 0, -10), 0));
        manager.Add(gizmo);
        manager.Render(context);
        var label = new GizmoLabel("Center", visibility: mode);
        Assert.Equal(idle, label.IsVisible(gizmo.Handles[PositionGizmo.Center], context));
        manager.UpdateHover(context.Camera!.CreatePointer(context, 400, 300));
        Assert.Equal(hover, label.IsVisible(gizmo.Handles[PositionGizmo.Center], context));
        Assert.Equal(idle, label.IsVisible(gizmo.Handles[PositionGizmo.X], context));
        Assert.True(manager.TryBegin(context.Camera.CreatePointer(context, 400, 300)));
        Assert.Equal(selected, label.IsVisible(gizmo.Handles[PositionGizmo.Center], context));
        Assert.Equal(idle, label.IsVisible(gizmo.Handles[PositionGizmo.X], context));
        manager.Complete();
        Assert.Equal(idle, label.IsVisible(gizmo.Handles[PositionGizmo.Center], context));
    }

    [Fact]
    public void PredicateReceivesLiveContextAndCannotOverrideGizmoEligibility()
    {
        var context = Camera();
        using var gizmo = Create(new(V(0, 0, -10), 0));
        var handle = gizmo.Handles[PositionGizmo.X];
        bool show = false;
        int calls = 0;
        var label = new GizmoLabel("Custom", visibility: GizmoLabelVisibility.Predicate, predicate: state =>
        {
            calls++;
            Assert.Same(gizmo, state.Gizmo);
            Assert.Same(context, state.Context);
            Assert.Same(handle, state.Handle);
            Assert.False(state.IsHovered);
            Assert.False(state.IsSelected);
            return show;
        });
        Assert.False(label.IsVisible(handle, context));
        show = true;
        Assert.True(label.IsVisible(handle, context));
        gizmo.Visible = false;
        Assert.False(label.IsVisible(handle, context));
        gizmo.Visible = true;
        gizmo.Enabled = false;
        Assert.False(label.IsVisible(handle, context));
        gizmo.Enabled = true;
        Assert.False(label.IsVisible(handle, new(1, context.Camera)));
        handle.Visible = false;
        Assert.False(label.IsVisible(handle, context));
        handle.Visible = true;
        handle.Enabled = false;
        Assert.False(label.IsVisible(handle, context));
        handle.Enabled = true;
        gizmo.RemoveHandle(handle);
        Assert.False(label.IsVisible(handle, context));
        gizmo.Dispose();
        Assert.False(label.IsVisible(handle, context));
        handle.Dispose();
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
        List<GizmoHandleDraw> draws = [];
        using var gizmo = Create(new(reference + V(0, 0, -10), 2), draws.Add);
        var id = new GizmoHandleId(handle);
        var label = new GizmoLabel("<literal>", 17, -23);
        gizmo.Handles[id].Label = label;
        gizmo.Render(context);
        var draw = draws.Single(p => p.Geometry.Handle.Id == id);
        var anchor = draw.Geometry.LabelAnchor;
        Assert.True(camera.TryProject(anchor, out var screen));
        Assert.True(GizmoRenderAdapter.TryProjectLabel(draw, out var found, out double x, out double y));
        Assert.Same(label, found);
        Assert.Equal(screen[0] + 17, x, 8);
        Assert.Equal(screen[1] - 23, y, 8);
    }

    [Fact]
    public void LabelConfigurationDoesNotInvalidateHitGeometryAndDisposalReleasesPredicates()
    {
        var context = Camera();
        using var manager = new GizmoManager();
        var gizmo = Create(new(V(0, 0, -10), 0));
        var handle = gizmo.Handles[PositionGizmo.Center];
        manager.Add(gizmo);
        manager.Render(context);
        handle.Label = new("First");
        handle.Label = new("Second");
        Assert.Single(gizmo.Handles.Values.Where(h => h.Label is not null));
        Assert.Equal("Second", handle.Label.Text);
        Assert.True(manager.TryBegin(context.Camera!.CreatePointer(context, 400, 300)));
        handle.Label = null;
        Assert.True(gizmo.IsInteracting);
        Assert.Null(handle.Label);
        handle.Label = new("Custom", visibility: GizmoLabelVisibility.Predicate, predicate: _ => true);
        gizmo.Dispose();
        Assert.Null(handle.Label);
        Assert.Throws<ObjectDisposedException>(() => handle.Label = new("X"));
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
