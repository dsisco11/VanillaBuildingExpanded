using System.Numerics;

using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushSnappingRendererTests
{
    public static IEnumerable<object[]> Directions()
    {
        foreach (var face in BlockFacing.ALLFACES)
        foreach (var mode in new[] { EBuildBrushSnapping.Horizontal, EBuildBrushSnapping.Vertical,
            EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical })
        foreach (int u in new[] { -1, 0, 1 })
        foreach (int v in new[] { -1, 0, 1 })
        foreach (bool valid in new[] { true, false })
            yield return [face.Index, mode, u, v, valid];
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void UniformsUseResolvedWorldAxesAndValidityColorOnEveryFace(int faceIndex, EBuildBrushSnapping mode,
        int u, int v, bool valid)
    {
        using var f = new Fixture();
        f.Geometry.Inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(),
            ref It.Ref<string>.IsAny)).Returns(valid);
        var face = BlockFacing.ALLFACES[faceIndex];
        var presentation = f.Geometry.Presentation(face, 0.5 + u * 0.4, 0.5 + v * 0.4, mode: mode);
        f.Target = f.Geometry.Cache.GetTarget(presentation);
        int validations = f.Geometry.Inputs.Validator.Invocations.Count;
        f.Draw();
        Assert.Equal(validations, f.Geometry.Inputs.Validator.Invocations.Count);
        var resolved = presentation.Result.ResolvedSnapping;
        if (resolved.FaceU == 0 && resolved.FaceV == 0)
        {
            f.Program.Verify(p => p.Use(), Times.Never);
            return;
        }
        Assert.NotNull(f.Target);
        f.Program.VerifySet(p => p.SelectedNormal = new Vector3(face.Normalf.X, face.Normalf.Y, face.Normalf.Z), Times.Once);
        f.Program.VerifySet(p => p.AxisU = face.Axis == EnumAxis.X ? Vector3.UnitZ : Vector3.UnitX, Times.Once);
        f.Program.VerifySet(p => p.AxisV = face.Axis == EnumAxis.Y ? Vector3.UnitZ : Vector3.UnitY, Times.Once);
        f.Program.VerifySet(p => p.ResolvedDirections = new Vector2(resolved.FaceU, resolved.FaceV), Times.Once);
        f.Program.VerifySet(p => p.MarkingWidth = BrushSnappingRenderer.MarkingWidth, Times.Once);
        f.Program.VerifySet(p => p.ColorIn = valid ? new Vector4(0.2f, 0.85f, 1f, 0.7f)
            : new Vector4(1f, 0f, 0f, 0.7f), Times.Once);
        f.Geometry.Api.VerifyGet(a => a.BlockTextureAtlas, Times.Never);
    }

    [Fact]
    public void DrawingUsesActualMeshOriginalTargetAndCameraTransformWithoutTakingMeshOwnership()
    {
        using var f = new Fixture();
        f.Geometry.Position.Set(2_000_000_000, 12, -2_000_000_000);
        f.Geometry.Position.SetDimension(2);
        f.Target = f.Geometry.Cache.GetTarget(f.Geometry.Presentation());
        var camera = WorkAreaRenderingTests.CreatePlayer();
        camera.Entity.CameraPos.Set(2_000_000_000.125, 12.25 + 2d * BlockPos.DimensionBoundary, -2_000_000_000.5);
        Mock.Get(f.Geometry.Api.Object.World).Setup(w => w.Player).Returns(camera);
        var cameraMatrix = new Matrixf().RotateY(0.7f);
        f.Geometry.Render.SetupGet(r => r.CameraMatrixOriginf).Returns(cameraMatrix.Values);
        f.Draw();
        Assert.NotNull(f.Target);
        var expected = new Matrixf();
        f.Target.SetModelMatrix(expected, cameraMatrix.Values, camera.Entity.CameraPos);
        f.Program.VerifySet(p => p.ModelViewMatrix =
            It.Is<float[]>(m => m.SequenceEqual(expected.Values)), Times.Once);
        f.Program.VerifySet(p => p.ProjectionMatrix = f.Projection, Times.Once);
        f.Program.VerifySet(p => p.DepthBias = BrushSnappingRenderer.DepthBias, Times.Once);
        Assert.InRange(BrushSnappingRenderer.DepthBias, 0.000001f, 0.0001f);
        Assert.Equal(f.Geometry.Borrowed.xyz, f.Target.Mesh.Data.xyz);
        Assert.Equal(f.Geometry.Borrowed.Indices, f.Target.Mesh.Data.Indices);
        f.Geometry.Render.Verify(r => r.RenderMesh(f.Geometry.Uploaded.Single()), Times.Once);
        Assert.Equal(new[] { "capture", "previous-stop", "apply", "use", "draw", "stop", "restore", "previous-use" },
            f.Sequence);
        f.Renderer.Dispose();
        f.Renderer.Dispose();
        Assert.Equal(0, f.Geometry.Uploaded.Single().DisposeCount);
        f.Program.Verify(p => p.Dispose(), Times.Once);
        f.Geometry.Dispose();
        Assert.Equal(1, f.Geometry.Uploaded.Single().DisposeCount);
    }

    [Fact]
    public void MissingSuppressedAndWrongStageTargetsDoNotAllocateOrDrawAndClearingNeverReusesOldTarget()
    {
        using var f = new Fixture();
        f.Renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        f.Target = null;
        f.Draw();
        f.Target = f.Geometry.Cache.GetTarget(f.Geometry.Presentation(mode: EBuildBrushSnapping.None));
        f.Draw();
        f.Factory.Verify(create => create(), Times.Never);
        f.Target = f.Geometry.Cache.GetTarget(f.Geometry.Presentation());
        f.Draw();
        f.Target = null;
        f.Draw();
        Assert.Equal(1, f.Geometry.Uploads);
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Once);
        f.Renderer.Dispose();
        f.Draw();
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Once);
    }

    [Fact]
    public void CandidateUsesDomainAssetsAndOitWithoutRegisteringItself()
    {
        using var f = new Fixture();
        Assert.Equal(EnumRenderStage.OIT, BrushSnappingRenderer.Stage);
        Assert.Equal(0.52, f.Renderer.RenderOrder);
        f.Draw();
        f.Program.VerifySet(p => p.AssetDomain = "vanillabuildingexpanded", Times.Once);
        f.Program.VerifySet(p => p.Oit = true, Times.Once);
        f.Shaders.Verify(s => s.RegisterFileShaderProgram("vbe-snapping", f.Program.Object), Times.Once);
        f.Shaders.Verify(s => s.NewShaderProgram(), Times.Never);
        f.Geometry.Api.VerifyGet(a => a.Event, Times.Never);
    }

    [Theory]
    [InlineData("previous-stop")]
    [InlineData("apply")]
    [InlineData("use")]
    [InlineData("uniform")]
    [InlineData("draw")]
    [InlineData("stop")]
    [InlineData("restore")]
    public void FailuresRestoreStateAndPreviousShaderThenDisableOnlyTheOverlay(string fault)
    {
        using var f = new Fixture();
        f.Fault = fault;
        f.Draw();
        Assert.Equal("previous-use", f.Sequence.Last());
        Assert.Contains("restore", f.Sequence);
        Assert.Contains("stop", f.Sequence);
        int count = f.Sequence.Count;
        f.Draw();
        Assert.Equal(count, f.Sequence.Count);
        f.Geometry.Api.Verify(a => a.Logger.Error(
            "[BuildBrush] Snapping indicator unavailable until shader reload: {0}", It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public void CaptureFailureDoesNotApplyStateOrStopTheActiveShader()
    {
        using var f = new Fixture();
        f.Fault = "capture";
        f.Draw();
        Assert.Equal(new[] { "capture" }, f.Sequence);
        f.Previous.Verify(p => p.Stop(), Times.Never);
    }

    [Theory]
    [InlineData("creation")]
    [InlineData("registration")]
    [InlineData("load")]
    [InlineData("compile")]
    public void CompilationAndReplacementFailuresRetireOwnedProgramsDisableDrawingAndPermitExplicitRecovery(string fault)
    {
        using var f = new Fixture();
        f.Draw();
        var candidate = new Mock<IBrushSnappingShader>();
        candidate.Setup(p => p.Compile()).Returns(fault != "compile");
        candidate.SetupGet(p => p.LoadError).Returns(fault == "load");
        f.Factory.Setup(create => create()).Returns(fault == "creation" ? null! : candidate.Object);
        if (fault == "registration")
            f.Shaders.Setup(s => s.RegisterFileShaderProgram("vbe-snapping", candidate.Object))
                .Throws(new InvalidOperationException("GPU registration"));
        f.Renderer.ReloadShader();
        f.Program.Verify(p => p.Dispose(), Times.Once);
        candidate.Verify(p => p.Dispose(), fault == "creation" ? Times.Never : Times.Once);
        f.Draw();
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Once);
        var recovered = new Mock<IBrushSnappingShader>();
        recovered.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(recovered.Object);
        f.Renderer.ReloadShader();
        f.Draw();
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Exactly(2));
        Assert.Equal(1, f.Geometry.Uploads);
        f.Renderer.Dispose();
        recovered.Verify(p => p.Dispose(), Times.Once);
    }

    [Fact]
    public void InitialCompilationFailureNeverDrawsOrRetriesOnEveryFrame()
    {
        using var f = new Fixture();
        f.Program.Setup(p => p.Compile()).Returns(false);
        f.Draw();
        f.Draw();
        f.Program.Verify(p => p.Dispose(), Times.Once);
        f.Factory.Verify(create => create(), Times.Once);
        Assert.Equal(0, f.Geometry.Uploads);
        Assert.Empty(f.Sequence);
    }

    [Fact]
    public void UploadFailureDoesNotChangeRenderStateOrRetryUntilReload()
    {
        using var f = new Fixture();
        f.Geometry.Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>()))
            .Throws(new InvalidOperationException("GPU upload"));
        f.Draw();
        f.Draw();
        Assert.Empty(f.Sequence);
        f.Geometry.Render.Verify(r => r.UploadMesh(It.IsAny<MeshData>()), Times.Once);
        f.Geometry.Api.Verify(a => a.Logger.Error(
            "[BuildBrush] Snapping indicator unavailable until shader reload: {0}", It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public void DisposedRendererDoesNotCompileOnReloadOrObserveTargets()
    {
        using var f = new Fixture();
        f.Renderer.Dispose();
        f.Renderer.ReloadShader();
        f.Draw();
        f.Factory.Verify(create => create(), Times.Never);
        Assert.Empty(f.Sequence);
    }

    [Fact]
    public void NativeDisposedShaderIsReplacedBeforeAnotherDraw()
    {
        using var f = new Fixture();
        f.Draw();
        f.Program.SetupGet(p => p.Disposed).Returns(true);
        var replacement = new Mock<IBrushSnappingShader>();
        replacement.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(replacement.Object);
        f.Draw();
        replacement.Verify(p => p.Use(), Times.Once);
        f.Program.Verify(p => p.Dispose(), Times.Never);
        Assert.Equal(1, f.Geometry.Uploads);
    }

    [Fact]
    public void SourcesPreserveLocalGeometryAndMaskCellRegionsWithoutTexturesOrFaceNormalGlyphs()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "assets", "vanillabuildingexpanded", "shaders");
        string vertex = File.ReadAllText(Path.Combine(directory, "vbe-snapping.vsh"));
        string fragment = File.ReadAllText(Path.Combine(directory, "vbe-snapping.fsh"));
        Assert.Contains("localPosition = vertexPositionIn;", vertex);
        Assert.Contains("projectionMatrix * modelViewMatrix * vec4(vertexPositionIn, 1.0)", vertex);
        Assert.Contains("gl_Position.z -= depthBias * gl_Position.w;", vertex);
        Assert.Contains("layout(location = 1) in int renderFlagsIn;", vertex);
        Assert.Contains("unpackNormal(renderFlagsIn)", vertex);
        Assert.Contains("int zOffset = (renderFlagsIn & ZOffsetBitMask) >> 8;", vertex);
        Assert.Contains("terrainPass == 3 || terrainPass == 6", vertex);
        Assert.Contains("max(3.0, gl_Position.z * 0.05)", vertex);
        Assert.Contains("((gl_Position.z + 3.0) * 0.05)", vertex);
        Assert.Contains("if (dot(surfaceNormal, surfaceNormal) > 0.0) normal = normalize(surfaceNormal);", fragment);
        Assert.Contains("cross(dFdx(localPosition), dFdy(localPosition))", fragment);
        Assert.Contains("if (!gl_FrontFacing) normal = -normal;", fragment);
        Assert.Contains("dot(normal, selectedNormal) <= 0.0", fragment);
        Assert.Contains("dot(localPosition, axisU)", fragment);
        Assert.Contains("dot(localPosition, axisV)", fragment);
        Assert.Contains("uniform vec2 resolvedDirections;", fragment);
        Assert.Contains("directionalBand(uv.x, resolvedDirections.x, pixelWidth.x)", fragment);
        Assert.Contains("directionalBand(uv.y, resolvedDirections.y, pixelWidth.y)", fragment);
        Assert.Contains("direction * (coordinate - 0.5) - 0.15", fragment);
        Assert.Contains("direction == 0.0 || distance <= 0.0", fragment);
        Assert.Contains("fwidth(uv)", fragment);
        Assert.Contains("max(markingWidth, pixelWidth)", fragment);
        Assert.Contains("mask <= 0.0) discard;", fragment);
        Assert.Contains("colorIn.a * mask", fragment);
        Assert.Contains("#include oit.fsh", fragment);
        foreach (var forbidden in new[] { "sampler", "texture(", "vertexUV", "FaceW", "axisW" })
            Assert.DoesNotContain(forbidden, vertex + fragment);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly BrushTargetMeshTests.Fixture Geometry = new();
        public readonly Mock<IShaderAPI> Shaders = new();
        public readonly Mock<IBrushSnappingShader> Program = new();
        public readonly Mock<Func<IBrushSnappingShader>> Factory = new();
        public readonly Mock<IShaderProgram> Previous = new();
        public readonly List<string> Sequence = [];
        public readonly float[] Projection = new Matrixf().Values;
        public BrushTargetGeometry? Target;
        public string? Fault;
        public BrushSnappingRenderer Renderer { get; }

        public Fixture()
        {
            Geometry.Api.SetupGet(a => a.Shader).Returns(Shaders.Object);
            Mock.Get(Geometry.Api.Object.World).Setup(w => w.Player).Returns(WorkAreaRenderingTests.CreatePlayer());
            Geometry.Render.SetupGet(r => r.CameraMatrixOriginf).Returns(new Matrixf().Values);
            Geometry.Render.SetupGet(r => r.CurrentProjectionMatrix).Returns(Projection);
            Geometry.Render.SetupGet(r => r.CurrentActiveShader).Returns(Previous.Object);
            Factory.Setup(create => create()).Returns(Program.Object);
            Program.Setup(p => p.Compile()).Returns(true);
            Program.Setup(p => p.Use()).Callback(() => Step("use"));
            Program.Setup(p => p.Stop()).Callback(() => Step("stop"));
            Program.SetupSet(p => p.ProjectionMatrix = It.IsAny<float[]>())
                .Callback(() => { if (Fault == "uniform") throw new InvalidOperationException("GPU uniform"); });
            Previous.Setup(p => p.Stop()).Callback(() => Step("previous-stop"));
            Previous.Setup(p => p.Use()).Callback(() => Step("previous-use"));
            Geometry.Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>())).Callback(() => Step("draw"));
            Target = Geometry.Cache.GetTarget(Geometry.Presentation());
            Renderer = new BrushSnappingRenderer(Geometry.Api.Object, () => Target, new State(this), Factory.Object);
        }

        private void Step(string step)
        {
            Sequence.Add(step);
            if (Fault == step) throw new InvalidOperationException("GPU " + step);
        }

        public void Draw() => Renderer.OnRenderFrame(0, BrushSnappingRenderer.Stage);
        public void Dispose()
        {
            Renderer.Dispose();
            Geometry.Dispose();
        }

        private sealed class State(Fixture fixture) : IBrushOverlayRenderState
        {
            public void Capture() => fixture.Step("capture");
            public void Apply() => fixture.Step("apply");
            public void Restore() => fixture.Step("restore");
        }
    }
}
