using Moq;
using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Client;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed partial class GizmoRenderAdapterTests
{
    [Fact]
    public void LinesRenderOnlyInOitAndLabelsRenderOnlyAfterBlitWithoutRedrawingLines()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        gizmo.Handles[ComposedPositionGizmo.Center].Label = new("Center");
        f.Adapter.SetActive(true);
        f.Adapter.OnRenderFrame(0, EnumRenderStage.Opaque);
        Assert.Equal(0, f.Draws);
        Assert.Null(f.Adapter.Context);
        f.Adapter.OnRenderFrame(0, EnumRenderStage.OIT);
        Assert.Equal(8, f.Draws);
        Assert.Equal(0, f.LabelUploads);
        Assert.Empty(f.LabelRects);
        Assert.Same(f.Camera, f.Adapter.Context);
        f.Render.Verify(r => r.GLDisableDepthTest(), Times.Never);
        f.Adapter.OnRenderFrame(0, EnumRenderStage.AfterBlit);
        Assert.Equal(9, f.Draws);
        Assert.Single(f.LabelRects);
        Assert.Equal(8, f.Colors.Count);
        f.Render.Verify(r => r.GLEnableDepthTest(), Times.Exactly(4));
        f.Render.Verify(r => r.GLDisableDepthTest(), Times.Once);
        Assert.Equal(f.State.Captures, f.State.Restores);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("remove")]
    [InlineData("deactivate")]
    public void LabelsCannotOutliveTheirOitHandlesBetweenStages(string mutation)
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        var handle = gizmo.Handles[ComposedPositionGizmo.Center];
        handle.Label = new("Center");
        f.Adapter.SetActive(true);
        f.Adapter.OnRenderFrame(0, EnumRenderStage.OIT);
        if (mutation == "hide") handle.Visible = false;
        else if (mutation == "remove") gizmo.RemoveHandle(handle);
        else f.Adapter.SetActive(false);
        f.Adapter.OnRenderFrame(0, EnumRenderStage.AfterBlit);
        Assert.Equal(0, f.LabelUploads);
        Assert.Empty(f.LabelRects);
        Assert.Equal(8, f.Draws);
        if (mutation == "remove") handle.Dispose();
    }

    [Theory]
    [InlineData("text-color")]
    [InlineData("background-color")]
    [InlineData("font")]
    [InlineData("size")]
    [InlineData("bold")]
    [InlineData("padding")]
    public void ResolvedStyleChangesRebuildOnlyTheAffectedLabel(string field)
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        var handle = gizmo.Handles[ComposedPositionGizmo.X];
        handle.Label = new("X");
        gizmo.Handles[ComposedPositionGizmo.Center].Label = new("Center");
        f.Adapter.SetActive(true);
        f.Frame();
        var baseline = handle.Appearances[EAppearenceState.Default];
        var changed = field switch
        {
            "text-color" => baseline with { LabelTextColor = new(0, 1, 0, 0.5f) },
            "background-color" => baseline with { LabelBackgroundColor = new(0.5f, 0, 0, 0.3f) },
            "font" => baseline with { LabelFontFamily = "monospace" },
            "size" => baseline with { LabelFontSize = 20 },
            "bold" => baseline with { LabelBold = false },
            _ => baseline with { LabelPadding = 7 }
        };
        handle.SetAppearance(EAppearenceState.Default, changed);
        f.Frame();
        Assert.Equal(3, f.LabelUploads);
        Assert.Equal(new[] { 102 }, f.DeletedTextures);
        handle.SetAppearance(EAppearenceState.Default, changed with { Color = new(1, 0, 1, 0.4f), LineWidth = 3 });
        handle.Label = new("X", 30, 40);
        f.Frame();
        Assert.Equal(3, f.LabelUploads);
        Assert.Single(f.DeletedTextures);
    }

    [Fact]
    public void SelectedStyleAndStateDependentGeometryUseFreshAnchorsWithoutRebasingCapture()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        var handle = gizmo.Handles[ComposedPositionGizmo.X];
        handle.Label = new("X", 10, 12);
        f.Adapter.SetActive(true);
        f.Frame();
        var baselineRect = f.LabelRects.Single();
        handle.SetAppearance(EAppearenceState.Selected, new()
        {
            Size = 96, LabelFontSize = 22, Color = new(0, 1, 1, 0.6f), LineWidth = 4
        });
        Assert.True(gizmo.BeginInteraction(handle.Id,
            new(f.Camera, new(ComposedPositionGizmoTests.V(0, 0, 0), ComposedPositionGizmoTests.V(0.5, 0, -10)))));
        f.Frame();
        var snapshot = gizmo.GetGeometrySource(handle).Snapshot!;
        Assert.True(f.Camera.Camera!.TryProject(snapshot.Geometry.LabelAnchor, out var screen));
        var rect = f.LabelRects.Last();
        Assert.Equal((float)(2 * (screen[0] + 10) / f.Camera.Camera.Width - 1), rect.X, 6);
        Assert.Equal((float)(1 - 2 * (screen[1] + 12) / f.Camera.Camera.Height), rect.Y, 6);
        Assert.NotEqual(baselineRect.X, rect.X);
        Assert.Equal(2, f.LabelUploads);
        Assert.True(gizmo.IsInteracting);
        f.Shader.Verify(s => s.Uniform("colorIn", 0f, 1f, 1f, 0.6f), Times.Once);
        f.Shader.Verify(s => s.Uniform("colorIn", 0f, 1f, 1f, 0.6f * 0.3f), Times.Once);
        f.Render.VerifySet(r => r.LineWidth = 4, Times.Once);
        gizmo.CancelInteraction();
        f.Frame();
        Assert.Equal(3, f.LabelUploads);
    }

    [Fact]
    public void ReusingAnIdentityCannotReuseADetachedHandlesTexture()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        var original = gizmo.Handles[ComposedPositionGizmo.Center];
        original.Label = new("Same");
        f.Adapter.SetActive(true);
        f.Frame();
        gizmo.RemoveHandle(original);
        var replacement = new GizmoHandle(original.Id, original.Appearances[EAppearenceState.Default], new("Same"))
        {
            FrameSource = original.FrameSource
        };
        gizmo.AddHandle(replacement);
        f.Frame();
        Assert.Equal(2, f.LabelUploads);
        Assert.Equal(new[] { 101 }, f.DeletedTextures);
        original.Dispose();
        replacement.Dispose();
        f.Frame();
        Assert.Equal(new[] { 101, 102 }, f.DeletedTextures);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("disable")]
    [InlineData("remove")]
    public void PredicateCannotPublishALabelAfterInvalidatingItsHandle(string mutation)
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        var handle = gizmo.Handles[ComposedPositionGizmo.Center];
        handle.Label = new("Custom", visibility: GizmoLabelVisibility.Predicate, predicate: _ =>
        {
            if (mutation == "hide") handle.Visible = false;
            else if (mutation == "disable") handle.Enabled = false;
            else gizmo.RemoveHandle(handle);
            return true;
        });
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Equal(0, f.LabelUploads);
        Assert.Empty(f.LabelRects);
        if (mutation == "remove") handle.Dispose();
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("compile")]
    [InlineData("quad")]
    [InlineData("oversized")]
    public void LabelResourceFailuresAreReportedAndAllAllocatedResourcesRetireOnce(string failure)
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        var handle = gizmo.Handles[ComposedPositionGizmo.Center];
        handle.Label = new("Center");
        if (failure == "upload")
            f.Api.Setup(a => a.Gui.LoadCairoTexture(It.IsAny<Cairo.ImageSurface>(), It.IsAny<bool>())).Returns(0);
        if (failure == "compile") f.LabelShader.Setup(s => s.Compile()).Returns(false);
        if (failure == "quad")
            f.Render.Setup(r => r.UploadMesh(It.Is<MeshData>(m => m.VerticesCount == 4 && m.IndicesCount == 6)))
                .Throws(new InvalidOperationException("quad failure"));
        if (failure == "oversized") handle.SetAppearance(EAppearenceState.Default,
            handle.Appearances[EAppearenceState.Default] with { LabelPadding = double.MaxValue });
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Equal(1, f.Notices);
        Assert.Null(f.Adapter.Context);
        f.Flush();
        Assert.Empty(f.Active);
        f.Adapter.Dispose();
        Assert.All(f.Meshes, mesh => Assert.Equal(1, mesh.Disposals));
        Assert.Equal(f.DeletedTextures.Count, f.DeletedTextures.Distinct().Count());
        if (failure is "compile" or "quad") Assert.Equal(new[] { 101 }, f.DeletedTextures);
    }

    [Fact]
    public void LabelTexturesAreLazyReusedWhileHoveredAndRetiredWhenRemoved()
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        gizmo.Handles[ComposedPositionGizmo.Center].Label = new("Center", visibility: GizmoLabelVisibility.Hover);
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
        gizmo.Handles[ComposedPositionGizmo.Center].Label = null;
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
        gizmo.Handles[ComposedPositionGizmo.X].Label = new("X");
        f.Adapter.SetActive(true);
        f.Frame();
        gizmo.Handles[ComposedPositionGizmo.X].Label = new("X", 20, 20);
        f.Frame();
        Assert.Equal(1, f.LabelUploads);
        gizmo.Handles[ComposedPositionGizmo.X].Label = new("Changed");
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
        gizmo.Handles[ComposedPositionGizmo.Center].Label = new("Selected", visibility: GizmoLabelVisibility.Selected);
        gizmo.Handles[ComposedPositionGizmo.X].Label = new("Predicate", visibility: GizmoLabelVisibility.Predicate,
            predicate: _ => show);
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
        gizmo.Handles[ComposedPositionGizmo.Center].Label = new("Scale");
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
        gizmo.Handles[ComposedPositionGizmo.Center].Label = new("Visible");
        gizmo.Value = new(ComposedPositionGizmoTests.V(0, 0, 10), 0);
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Empty(f.LabelRects);
        gizmo.Value = new(ComposedPositionGizmoTests.V(0, 0, -10), 1);
        f.Frame();
        gizmo.Value = new(ComposedPositionGizmoTests.V(0, 0, -10), 0);
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
        gizmo.Handles[ComposedPositionGizmo.Center].Label = new("Center");
        f.LabelShader.Setup(s => s.BindTexture2D("labelTexture", It.IsAny<int>(), 0))
            .Throws(new InvalidOperationException("label draw"));
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Equal(1, f.Notices);
        Assert.Null(f.Adapter.Context);
        Assert.Equal(f.State.Captures, f.State.Restores);
        f.Previous.Verify(s => s.Use(), Times.Exactly(5));
        f.Flush();
        Assert.Empty(f.Active);
        f.Adapter.Dispose();
        Assert.Equal(new[] { 101 }, f.DeletedTextures);
    }
}
