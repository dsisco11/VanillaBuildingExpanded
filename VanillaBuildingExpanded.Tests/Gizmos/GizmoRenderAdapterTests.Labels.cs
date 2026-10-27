using Moq;
using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Client;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed partial class GizmoRenderAdapterTests
{
    [Fact]
    public void LabelTexturesAreLazyReusedWhileHoveredAndRetiredWhenRemoved()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        gizmo.SetLabel(PositionGizmo.Center, new("Center", visibility: GizmoLabelVisibility.Hover));
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Equal(0, f.LabelUploads);
        var pointer = f.Camera.Camera!.CreatePointer(f.Camera, 400, 300);
        f.Manager.UpdateHover(pointer);
        f.Frame();
        Assert.Equal(1, f.LabelUploads);
        Assert.Single(f.LabelRects);
        Assert.Equal(3, f.Meshes.Count);
        var rect = f.LabelRects[0];
        Assert.Equal(0.02f, rect.X, 5);
        Assert.Equal(8f / 300, rect.Y, 5);
        Assert.True(rect.Width > 0);
        Assert.True(rect.Height < 0);
        f.Frame();
        Assert.Equal(1, f.LabelUploads);
        Assert.Equal(2, f.LabelRects.Count);
        f.Manager.UpdateHover(f.Camera.Camera.CreatePointer(f.Camera, 0, 0));
        f.Frame();
        Assert.Equal(2, f.LabelRects.Count);
        Assert.Empty(f.DeletedTextures);
        f.Manager.UpdateHover(pointer);
        f.Frame();
        Assert.Equal(1, f.LabelUploads);
        gizmo.RemoveLabel(PositionGizmo.Center);
        f.Frame();
        Assert.Equal(new[] { 101 }, f.DeletedTextures);
        f.Adapter.Dispose();
        Assert.All(f.Meshes, mesh => Assert.Equal(1, mesh.Disposals));
        f.LabelShader.Verify(s => s.Dispose(), Times.Once);
        Assert.Single(f.DeletedTextures);
    }

    [Fact]
    public void LabelReplacementAndGizmoRemovalDisposeTexturesWithoutRebuildingUnchangedLabels()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        gizmo.SetLabel(PositionGizmo.X, new("X"));
        f.Adapter.SetActive(true);
        f.Frame();
        gizmo.SetLabel(PositionGizmo.X, new("X", 20, 20));
        f.Frame();
        Assert.Equal(1, f.LabelUploads);
        gizmo.SetLabel(PositionGizmo.X, new("Changed"));
        f.Frame();
        Assert.Equal(2, f.LabelUploads);
        Assert.Equal(new[] { 101 }, f.DeletedTextures);
        f.Manager.Remove(gizmo);
        f.Frame();
        Assert.Equal(new[] { 101, 102 }, f.DeletedTextures);
        gizmo.Dispose();
    }

    [Fact]
    public void SelectedAndPredicateLabelsAreEvaluatedEachFrame()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        bool show = false;
        gizmo.SetLabel(PositionGizmo.Center, new("Selected", visibility: GizmoLabelVisibility.Selected));
        gizmo.SetLabel(PositionGizmo.X, new("Predicate", visibility: GizmoLabelVisibility.Predicate,
            predicate: _ => show));
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Empty(f.LabelRects);
        show = true;
        f.Frame();
        Assert.Single(f.LabelRects);
        Assert.True(f.Manager.TryBegin(f.Camera.Camera!.CreatePointer(f.Camera, 400, 300)));
        f.Frame();
        Assert.Equal(3, f.LabelRects.Count);
        f.Manager.Cancel();
        show = false;
        f.Frame();
        Assert.Equal(3, f.LabelRects.Count);
    }

    [Fact]
    public void ScaleChangesRebuildTextOnceAndLabelShaderReloadPreservesTextureCache()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        gizmo.SetLabel(PositionGizmo.Center, new("Scale"));
        f.Adapter.SetActive(true);
        f.Frame();
        var originalRect = f.LabelRects.Single();
        var old = f.Camera.Camera!;
        f.Camera = new(0, new GizmoCamera(old.View.ToArray(), old.Projection.ToArray(),
            old.ReferencePosition, old.Width, old.Height, 2));
        f.Frame();
        Assert.Equal(2, f.LabelUploads);
        Assert.Equal(new[] { 101 }, f.DeletedTextures);
        Assert.True(f.LabelRects.Last().Width > originalRect.Width);
        f.Frame();
        Assert.Equal(2, f.LabelUploads);
        var replacement = new Mock<IShaderProgram>();
        replacement.Setup(s => s.Compile()).Returns(true);
        f.LabelShader.SetupGet(s => s.Disposed).Returns(true);
        f.Shaders.Setup(s => s.NewShaderProgram()).Returns(replacement.Object);
        f.Frame();
        Assert.Equal(2, f.LabelUploads);
        replacement.Verify(s => s.Compile(), Times.Once);
        replacement.Verify(s => s.BindTexture2D("labelTexture", 102, 0), Times.Once);
        f.Adapter.Dispose();
        replacement.Verify(s => s.Dispose(), Times.Once);
        f.LabelShader.Verify(s => s.Dispose(), Times.Never);
        Assert.Equal(new[] { 101, 102 }, f.DeletedTextures);
    }

    [Fact]
    public void LabelsDoNotRenderForClippedHiddenDisabledOrWrongDimensionGizmos()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        gizmo.SetLabel(PositionGizmo.Center, new("Visible"));
        gizmo.Value = new(PositionGizmoTests.V(0, 0, 10), 0);
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Empty(f.LabelRects);
        gizmo.Value = new(PositionGizmoTests.V(0, 0, -10), 1);
        f.Frame();
        gizmo.Value = new(PositionGizmoTests.V(0, 0, -10), 0);
        gizmo.Visible = false;
        f.Frame();
        gizmo.Visible = true;
        gizmo.Enabled = false;
        f.Frame();
        Assert.Empty(f.LabelRects);
        Assert.Equal(0, f.LabelUploads);
        gizmo.Enabled = true;
        f.Frame();
        Assert.Single(f.LabelRects);
    }

    [Fact]
    public void LabelDrawFailureRestoresStateReportsAndRetiresInteraction()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        gizmo.SetLabel(PositionGizmo.Center, new("Center"));
        f.LabelShader.Setup(s => s.BindTexture2D("labelTexture", It.IsAny<int>(), 0))
            .Throws(new InvalidOperationException("label draw"));
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Equal(1, f.Notices);
        Assert.Null(f.Adapter.Context);
        Assert.Equal(f.State.Captures, f.State.Restores);
        f.Previous.Verify(s => s.Use(), Times.Once);
        f.Flush();
        Assert.Empty(f.Active);
        f.Adapter.Dispose();
        Assert.Equal(new[] { 101 }, f.DeletedTextures);
    }
}
