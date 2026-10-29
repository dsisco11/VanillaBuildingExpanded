using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.MathTools;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.ComposedPositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoCameraTests
{
    [Theory]
    [InlineData(800, 600, 1)]
    [InlineData(801, 601, 2)]
    [InlineData(400, 900, 0.75)]
    [InlineData(1920, 500, 1.5)]
    public void CursorRays_RoundTripEdgesAndCenterWithoutGuiScaleOrOddViewportDrift(int width, int height, double scale)
    {
        var context = Camera(width, height, scale, V(2000000000.125, 32768, -2000000000.25));
        var camera = context.Camera!;
        foreach (var (x, y) in new[] { (0d, 0d), (width / 2d, height / 2d), (width - 1d, height - 1d) })
        {
            var pointer = camera.CreatePointer(context, x, y);
            Assert.NotNull(pointer.FarPoint);
            Assert.True(camera.TryProject(pointer.Ray.Origin + pointer.Ray.Direction * 10, out var screen));
            Assert.Equal(x, screen[0], 3);
            Assert.Equal(y, screen[1], 3);
            Assert.True(camera.TryProject(pointer.FarPoint!.Value, out var far));
            Assert.Equal(1d, far[2], 5);
        }
    }

    [Fact]
    public void ReticleUsesNativeIntegerCenterAndThirdPersonCursorStaysOnItsVisualLine()
    {
        var view = Mat4d.Translate(new double[16], Mat4d.Create(), -3, -2, -4);
        var context = Camera(801, 601, grabbed: true, view: view);
        var pointer = context.Camera!.CreatePointer(context, 20, 40);
        Assert.True(context.Camera.TryProject(pointer.Ray.Origin + pointer.Ray.Direction * 10, out var screen));
        Assert.Equal(400d, screen[0], 8);
        Assert.Equal(300d, screen[1], 8);
        Assert.Equal(V(3, 2, 4), context.Camera.EyePosition);
        var free = Camera(801, 601, view: view);
        pointer = free.Camera!.CreatePointer(free, 70, 90);
        Assert.True(free.Camera.TryProject(pointer.Ray.Origin + pointer.Ray.Direction * 40, out screen));
        Assert.Equal(70d, screen[0], 8);
        Assert.Equal(90d, screen[1], 8);
        Assert.NotEqual(V(0, 0, 0), pointer.Ray.Origin);
    }

    [Fact]
    public void Projection_RejectsBehindCameraAndBeyondClipButAllowsTargetsBeyondPlayerReach()
    {
        var camera = Camera(far: 500).Camera!;
        Assert.False(camera.TryProject(V(0, 0, 1), out _));
        Assert.False(camera.TryProject(V(0, 0, -0.01), out _));
        Assert.False(camera.TryProject(V(0, 0, -510), out _));
        Assert.True(camera.TryProject(V(0, 0, -200), out _));
        Assert.True(camera.TryProject(V(0, 0, -500), out _));
    }

    [Fact]
    public void CameraCopiesMatricesAndMapsDifferentWindowAndViewportSizes()
    {
        var view = Mat4d.Create();
        var projection = Mat4d.Perspective(new double[16], Math.PI / 2, 2, 0.1, 1000);
        var camera = new GizmoCamera(view, projection, default, 1000, 500, windowWidth: 500, windowHeight: 250);
        var context = new GizmoContext(0, camera);
        Array.Clear(view);
        Array.Clear(projection);
        var pointer = camera.CreatePointer(context, 125, 75);
        Assert.True(camera.TryProject(pointer.Ray.Origin + pointer.Ray.Direction * 10, out var screen));
        Assert.Equal(250d, screen[0], 8);
        Assert.Equal(150d, screen[1], 8);
    }

    [Fact]
    public void CameraRejectsInvalidInputsInsteadOfFallingBackToCenter()
    {
        Assert.Throws<ArgumentException>(() => new GizmoCamera(new double[16], Mat4d.Create(), default, 800, 600));
        Assert.Throws<ArgumentOutOfRangeException>(() => Camera(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Camera(scale: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoPosition(V(double.NaN, 0, 0), 0));
        var context = Camera();
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Camera!.CreatePointer(context, double.NaN, 0));
        Assert.Throws<ArgumentException>(() => context.Camera!.CreatePointer(new(0), 1, 1));
    }

    [Theory]
    [InlineData(1, -0.2, 0.0625)]
    [InlineData(1, -10, 1.0666666666666667)]
    [InlineData(2, -10, 2.1333333333333333)]
    [InlineData(1, -200, 4)]
    public void GeometryUsesActualClampedSizeAndMatchingCenterAxisHits(double scale, double z, double length)
    {
        var context = Camera(scale: scale);
        List<GizmoHandleDraw> drawn = [];
        using var manager = new GizmoManager();
        var gizmo = Create(new(V(0, 0, z), 0), drawn.Add);
        manager.Add(gizmo);
        manager.Render(context);
        var x = drawn.Single(part => part.Geometry.Handle.Id == ComposedPositionGizmo.X).Geometry;
        Assert.Equal(length, x.Extent, 8);
        var center = drawn.Single(part => part.Geometry.Handle.Id == ComposedPositionGizmo.Center).Geometry;
        Assert.Equal(length * 7 / 32, Math.Sqrt(Vector256.Dot(center.BasisX, center.BasisX)), 8);
        var pointer = context.Camera!.CreatePointer(context, 400, 300);
        Assert.True(manager.TryBegin(pointer));
        Assert.Equal(ComposedPositionGizmo.Center, manager.CapturedHandle);
        manager.Cancel();
        Assert.True(context.Camera.TryProject(x.Frame.Origin + x.BasisX, out var screen));
        // At long range the shorter shaft overlaps the center's hit padding; use the exposed tip padding.
        screen = screen.WithElement(0, screen[0] + 3);
        pointer = context.Camera.CreatePointer(context, screen[0], screen[1]);
        Assert.True(manager.TryBegin(pointer));
        Assert.Equal(ComposedPositionGizmo.X, manager.CapturedHandle);
        manager.Cancel();
        if (z == -10)
        {
            Assert.True(context.Camera.TryProject(x.Frame.Origin + x.BasisX * 1.75, out screen));
            Assert.False(manager.TryBegin(context.Camera.CreatePointer(context, screen[0], screen[1])));
        }
    }

    [Fact]
    public void GeometryHitPaddingAndVisibilityTrackRenderedPartsAndOverlapsAreDeterministic()
    {
        var context = Camera();
        using var manager = new GizmoManager();
        var first = Create(new(V(0, 0, -10), 0), _ => { });
        var second = Create(new(V(0, 0, -10), 0), _ => { }, marker: GizmoShape.Diamond);
        manager.Add(first);
        manager.Add(second);
        manager.Render(context);
        Assert.True(manager.TryBegin(context.Camera!.CreatePointer(context, 410, 300)));
        Assert.Same(first, manager.CapturedGizmo);
        manager.Cancel();
        Assert.False(manager.TryBegin(context.Camera.CreatePointer(context, 400, 312)));
        first.Visible = false;
        second.Visible = false;
        Assert.False(manager.TryBegin(context.Camera.CreatePointer(context, 400, 300)));
        first.Visible = true;
        first.Value = new(V(0, 0, 10), 0);
        manager.Render(context);
        Assert.False(manager.TryBegin(context.Camera.CreatePointer(context, 400, 300)));
    }
}
