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
        Assert.NotNull(f.Target);
        f.Program.VerifySet(p => p.SelectedNormal = new Vector3(face.Normalf.X, face.Normalf.Y, face.Normalf.Z), Times.Once);
        f.Program.VerifySet(p => p.AxisU = face.Axis == EnumAxis.X ? Vector3.UnitZ : Vector3.UnitX, Times.Once);
        f.Program.VerifySet(p => p.AxisV = face.Axis == EnumAxis.Y ? Vector3.UnitZ : Vector3.UnitY, Times.Once);
        f.Program.VerifySet(p => p.ResolvedDirections = new Vector2(resolved.FaceU, resolved.FaceV), Times.Once);
        f.ChevronProgram.VerifySet(p => p.SelectedNormal = new Vector3(face.Normalf.X, face.Normalf.Y, face.Normalf.Z), Times.Once);
        f.ChevronProgram.VerifySet(p => p.AxisU = face.Axis == EnumAxis.X ? Vector3.UnitZ : Vector3.UnitX, Times.Once);
        f.ChevronProgram.VerifySet(p => p.AxisV = face.Axis == EnumAxis.Y ? Vector3.UnitZ : Vector3.UnitY, Times.Once);
        f.ChevronProgram.VerifySet(p => p.ResolvedDirections = new Vector2(resolved.FaceU, resolved.FaceV), Times.Once);
        f.ChevronProgram.VerifySet(p => p.ChevronColor = valid ? new Vector4(1f, 0.5f, 0f, 1f)
            : new Vector4(1f, 0f, 0f, 1f), Times.Once);
        f.Program.VerifySet(p => p.CenterThreshold = BrushSnapping.CenterThreshold, Times.Once);
        f.Program.VerifySet(p => p.ColorIn = new Vector4(1f, 1f, 1f, 0.08f), Times.Once);
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
        f.ChevronProgram.VerifySet(p => p.ModelViewMatrix =
            It.Is<float[]>(m => m.SequenceEqual(expected.Values)), Times.Once);
        f.ChevronProgram.VerifySet(p => p.ProjectionMatrix = f.Projection, Times.Once);
        f.ChevronProgram.VerifySet(p => p.DepthBias = BrushSnappingRenderer.DepthBias, Times.Once);
        Assert.InRange(BrushSnappingRenderer.DepthBias, 0.000001f, 0.0001f);
        Assert.Equal(f.Geometry.Borrowed.xyz, f.Target.Mesh.Data.xyz);
        Assert.Equal(f.Geometry.Borrowed.Indices, f.Target.Mesh.Data.Indices);
        f.Geometry.Render.Verify(r => r.RenderMesh(f.Geometry.Uploaded.Single()), Times.Once);
        f.Geometry.Render.Verify(r => r.RenderMesh(f.Geometry.ChevronUploaded.Single()), Times.Once);
        Assert.Equal(new[] { "capture", "previous-stop", "apply", "use", "draw", "stop",
            "chevron-use", "chevron-draw", "chevron-stop", "restore", "previous-use" },
            f.Sequence);
        f.Renderer.Dispose();
        f.Renderer.Dispose();
        Assert.Equal(0, f.Geometry.Uploaded.Single().DisposeCount);
        Assert.Equal(0, f.Geometry.ChevronUploaded.Single().DisposeCount);
        f.Program.Verify(p => p.Dispose(), Times.Once);
        f.ChevronProgram.Verify(p => p.Dispose(), Times.Once);
        f.Geometry.Dispose();
        Assert.Equal(1, f.Geometry.Uploaded.Single().DisposeCount);
        Assert.Equal(1, f.Geometry.ChevronUploaded.Single().DisposeCount);
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
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Exactly(2));
        f.Renderer.Dispose();
        f.Draw();
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Exactly(2));
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
        f.ChevronProgram.VerifySet(p => p.AssetDomain = "vanillabuildingexpanded", Times.Once);
        f.ChevronProgram.VerifySet(p => p.Oit = true, Times.Once);
        f.Shaders.Verify(s => s.RegisterFileShaderProgram("vbe-snapping", f.Program.Object), Times.Once);
        f.Shaders.Verify(s => s.RegisterFileShaderProgram("vbe-snapping-chevron", f.ChevronProgram.Object), Times.Once);
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
    [InlineData("chevron-use")]
    [InlineData("chevron-uniform")]
    [InlineData("chevron-draw")]
    [InlineData("chevron-stop")]
    public void FailuresRestoreStateAndPreviousShaderThenDisableOnlyTheOverlay(string fault)
    {
        using var f = new Fixture();
        f.Fault = fault;
        f.Draw();
        Assert.Equal("previous-use", f.Sequence.Last());
        Assert.Contains("restore", f.Sequence);
        if (fault is not ("previous-stop" or "apply"))
            Assert.Contains("stop", f.Sequence);
        if (fault.StartsWith("chevron-", StringComparison.Ordinal))
            Assert.Contains("chevron-stop", f.Sequence);
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
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Exactly(2));
        var recovered = new Mock<IBrushSnappingShader>();
        recovered.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(recovered.Object);
        f.Renderer.ReloadShader();
        f.Draw();
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Exactly(4));
        Assert.Equal(1, f.Geometry.Uploads);
        Assert.Single(f.Geometry.ChevronUploaded);
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

    [Theory]
    [InlineData("creation")]
    [InlineData("registration")]
    [InlineData("load")]
    [InlineData("compile")]
    public void ChevronCompilationFailuresDisableBothPassesAndExplicitReloadRecovers(string fault)
    {
        using var f = new Fixture();
        f.Draw();
        var fill = new Mock<IBrushSnappingShader>();
        fill.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(fill.Object);
        var candidate = new Mock<IBrushSnappingChevronShader>();
        candidate.Setup(p => p.Compile()).Returns(fault != "compile");
        candidate.SetupGet(p => p.LoadError).Returns(fault == "load");
        f.ChevronFactory.Setup(create => create()).Returns(fault == "creation" ? null! : candidate.Object);
        if (fault == "registration")
            f.Shaders.Setup(s => s.RegisterFileShaderProgram("vbe-snapping-chevron", candidate.Object))
                .Throws(new InvalidOperationException("GPU registration"));
        Assert.False(f.Renderer.ReloadShader());
        f.Program.Verify(p => p.Dispose(), Times.Once);
        f.ChevronProgram.Verify(p => p.Dispose(), Times.Once);
        candidate.Verify(p => p.Dispose(), fault == "creation" ? Times.Never : Times.Once);
        f.Draw();
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Exactly(2));
        var recoveredFill = new Mock<IBrushSnappingShader>();
        recoveredFill.Setup(p => p.Compile()).Returns(true);
        var recoveredChevron = new Mock<IBrushSnappingChevronShader>();
        recoveredChevron.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(recoveredFill.Object);
        f.ChevronFactory.Setup(create => create()).Returns(recoveredChevron.Object);
        Assert.True(f.Renderer.ReloadShader());
        fill.Verify(p => p.Dispose(), Times.Once);
        f.Draw();
        recoveredFill.Verify(p => p.Use(), Times.Once);
        recoveredChevron.Verify(p => p.Use(), Times.Once);
        f.Geometry.Render.Verify(r => r.RenderMesh(It.IsAny<MeshRef>()), Times.Exactly(4));
        Assert.Equal(1, f.Geometry.Uploads);
        Assert.Single(f.Geometry.ChevronUploaded);
        f.Renderer.Dispose();
        recoveredFill.Verify(p => p.Dispose(), Times.Once);
        recoveredChevron.Verify(p => p.Dispose(), Times.Once);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDisposedShaderIsReplacedBeforeAnotherDraw(bool chevron)
    {
        using var f = new Fixture();
        f.Draw();
        if (chevron) f.ChevronProgram.SetupGet(p => p.Disposed).Returns(true);
        else f.Program.SetupGet(p => p.Disposed).Returns(true);
        var fillReplacement = new Mock<IBrushSnappingShader>();
        fillReplacement.Setup(p => p.Compile()).Returns(true);
        var chevronReplacement = new Mock<IBrushSnappingChevronShader>();
        chevronReplacement.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(fillReplacement.Object);
        f.ChevronFactory.Setup(create => create()).Returns(chevronReplacement.Object);
        f.Draw();
        fillReplacement.Verify(p => p.Use(), chevron ? Times.Never : Times.Once);
        chevronReplacement.Verify(p => p.Use(), chevron ? Times.Once : Times.Never);
        f.Program.Verify(p => p.Dispose(), Times.Never);
        f.ChevronProgram.Verify(p => p.Dispose(), Times.Never);
        Assert.Equal(1, f.Geometry.Uploads);
    }

    [Theory]
    [InlineData(false, 9)]
    [InlineData(true, 8)]
    public void EveryShaderUniformHasATypedSetterOnTheNativeSubclass(bool chevron, int uniformCount)
    {
        // No GL program is created; native Dispose requires a live OpenGL context.
        Vintagestory.Client.NoObf.ShaderProgram nativeShader = chevron
            ? new BrushSnappingChevronShader() : new BrushSnappingShader();
        Type shaderType = nativeShader.GetType();
        Type contract = chevron ? typeof(IBrushSnappingChevronShader) : typeof(IBrushSnappingShader);
        string directory = Path.Combine(AppContext.BaseDirectory, "assets", "vanillabuildingexpanded", "shaders");
        string pass = chevron ? "vbe-snapping-chevron" : "vbe-snapping";
        string sources = File.ReadAllText(Path.Combine(directory, pass + ".vsh"))
            + File.ReadAllText(Path.Combine(directory, pass + ".fsh"));
        sources = sources.Replace("#include vbesnappingposition.ash",
            File.ReadAllText(Path.Combine(directory, "..", "shaderincludes", "vbesnappingposition.ash")));
        var uniforms = System.Text.RegularExpressions.Regex.Matches(sources, @"uniform\s+\w+\s+(\w+)\s*;")
            .Select(uniform => uniform.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(uniformCount, uniforms.Length);
        var collectUniformNames = typeof(Vintagestory.Client.NoObf.ShaderProgram).GetMethod(
            "collectUniformNames", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(collectUniformNames);
        var discoveredUniforms = new HashSet<string>();
        collectUniformNames.Invoke(nativeShader, [sources, discoveredUniforms]);
        Assert.Equal(uniforms.Order(), discoveredUniforms.Order());
        foreach (string name in uniforms)
        {
            string propertyName = char.ToUpperInvariant(name[0]) + name[1..];
            var property = shaderType.GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.True(typeof(BrushSnappingSurfaceShader).IsAssignableFrom(property.DeclaringType));
            Assert.NotNull(property.SetMethod);
            Assert.Null(property.GetMethod);
            Assert.Contains(contract.GetInterfaces().Append(contract).SelectMany(type => type.GetProperties()),
                member => member.Name == propertyName);
        }
        Assert.DoesNotContain("glyphPass", sources);
        Assert.Null(shaderType.GetProperty("GlyphPass"));
    }

    [Fact]
    public void SourcesPreserveLocalGeometryAndMaskCellRegionsWithoutTexturesOrFaceNormalGlyphs()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "assets", "vanillabuildingexpanded", "shaders");
        string vertex = File.ReadAllText(Path.Combine(directory, "vbe-snapping.vsh"));
        string fragment = File.ReadAllText(Path.Combine(directory, "vbe-snapping.fsh"));
        string chevronVertex = File.ReadAllText(Path.Combine(directory, "vbe-snapping-chevron.vsh"));
        string chevronFragment = File.ReadAllText(Path.Combine(directory, "vbe-snapping-chevron.fsh"));
        string position = File.ReadAllText(Path.Combine(directory, "..", "shaderincludes", "vbesnappingposition.ash"));
        Assert.Contains("#include vbesnappingposition.ash", vertex);
        Assert.Contains("#include vbesnappingposition.ash", chevronVertex);
        Assert.DoesNotContain("glyph", vertex, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chevron", fragment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("regionBounds", chevronFragment);
        Assert.DoesNotContain("centerThreshold", chevronFragment);
        Assert.DoesNotContain("colorIn", chevronFragment);
        Assert.Contains("localPosition = vertexPositionIn;", vertex);
        Assert.Contains("projectionMatrix * modelViewMatrix * vec4(vertex, 1.0)", position);
        Assert.Contains("position.z -= depthBias * position.w;", position);
        Assert.Contains("layout(location = 1) in int renderFlagsIn;", vertex);
        Assert.Contains("unpackNormal(renderFlagsIn)", vertex);
        Assert.Contains("float((renderFlagsIn & ZOffsetBitMask) >> 8)", vertex);
        Assert.Contains("layout(location = 2) in vec3 glyphDataIn;", chevronVertex);
        Assert.Contains("layout(location = 3) in vec3 glyphNormalIn;", chevronVertex);
        Assert.Contains("layout(location = 4) in vec3 sourcePositionAIn;", chevronVertex);
        Assert.Contains("layout(location = 7) in vec3 sourceWeightsIn;", chevronVertex);
        Assert.Contains("layout(location = 8) in vec3 sourceOffsetsIn;", chevronVertex);
        Assert.Contains("nativePosition(sourcePositionAIn, sourceOffsetsIn.x, terrainPass) * sourceWeightsIn.x", chevronVertex);
        Assert.Contains("nativePosition(sourcePositionBIn, sourceOffsetsIn.y, terrainPass) * sourceWeightsIn.y", chevronVertex);
        Assert.Contains("nativePosition(sourcePositionCIn, sourceOffsetsIn.z, terrainPass) * sourceWeightsIn.z", chevronVertex);
        Assert.Contains("terrainPass == 3 || terrainPass == 6", position);
        Assert.Contains("max(3.0, position.z * 0.05)", position);
        Assert.Contains("((position.z + 3.0) * 0.05)", position);
        Assert.Contains("if (dot(surfaceNormal, surfaceNormal) > 0.0) normal = normalize(surfaceNormal);", fragment);
        Assert.Contains("cross(dFdx(localPosition), dFdy(localPosition))", fragment);
        Assert.Contains("if (!gl_FrontFacing) normal = -normal;", fragment);
        Assert.Contains("dot(normal, selectedNormal) <= 0.0", fragment);
        Assert.Contains("dot(localPosition, axisU)", fragment);
        Assert.Contains("dot(localPosition, axisV)", fragment);
        Assert.Contains("uniform vec2 resolvedDirections;", fragment);
        Assert.Contains("uniform vec4 chevronColor;", chevronFragment);
        Assert.Contains("uniform float centerThreshold;", fragment);
        Assert.Contains("vec2 edgeDistance = min(uv, vec2(1.0) - uv);", chevronFragment);
        Assert.Contains("smoothstep(0.0, 0.15, min(edgeDistance.x, edgeDistance.y))", chevronFragment);
        Assert.Contains("min(boundsU.y - boundsU.x, boundsV.y - boundsV.x)", fragment);
        Assert.Contains("length(max(offset, vec2(0.0)))", fragment);
        Assert.Contains("1.0 - smoothstep(-feather, 0.0, distance)", fragment);
        Assert.Contains("float feather = max(0.05, max(fwidth(uv.x), fwidth(uv.y)));", fragment);
        Assert.Contains("all(equal(glyphDirections, resolvedDirections))", chevronFragment);
        Assert.Contains("active ? chevronColor : vec4(1.0, 0.5, 0.0, 0.2)", chevronFragment);
        Assert.Contains("color.a * glyphCoverage * fade", chevronFragment);
        Assert.DoesNotContain("chevronMask", chevronFragment);
        Assert.DoesNotContain("segmentDistance", chevronFragment);
        Assert.Contains("if (alpha <= 0.0) discard;", fragment);
        Assert.DoesNotContain("directionalBand", fragment);
        Assert.DoesNotContain("divider", fragment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lineMask", fragment);
        Assert.Contains("#include oit.fsh", fragment);
        foreach (var forbidden in new[] { "sampler", "texture(", "vertexUV", "FaceW", "axisW" })
            Assert.DoesNotContain(forbidden, vertex + fragment + chevronVertex + chevronFragment);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly BrushTargetMeshTests.Fixture Geometry = new();
        public readonly Mock<IShaderAPI> Shaders = new();
        public readonly Mock<IBrushSnappingShader> Program = new();
        public readonly Mock<Func<IBrushSnappingShader>> Factory = new();
        public readonly Mock<IBrushSnappingChevronShader> ChevronProgram = new();
        public readonly Mock<Func<IBrushSnappingChevronShader>> ChevronFactory = new();
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
            int chevronPrograms = 0;
            ChevronFactory.Setup(create => create()).Returns(() =>
            {
                var candidate = chevronPrograms++ == 0 ? ChevronProgram : new Mock<IBrushSnappingChevronShader>();
                candidate.Setup(p => p.Compile()).Returns(true);
                candidate.Setup(p => p.Use()).Callback(() => Step("chevron-use"));
                candidate.Setup(p => p.Stop()).Callback(() => Step("chevron-stop"));
                candidate.SetupSet(p => p.ProjectionMatrix = It.IsAny<float[]>())
                    .Callback(() => { if (Fault == "chevron-uniform") throw new InvalidOperationException("GPU uniform"); });
                return candidate.Object;
            });
            Previous.Setup(p => p.Stop()).Callback(() => Step("previous-stop"));
            Previous.Setup(p => p.Use()).Callback(() => Step("previous-use"));
            Geometry.Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>())).Callback<MeshRef>(mesh =>
                Step(Geometry.ChevronUploaded.Any(glyph => ReferenceEquals(glyph, mesh)) ? "chevron-draw" : "draw"));
            Target = Geometry.Cache.GetTarget(Geometry.Presentation());
            Renderer = new BrushSnappingRenderer(Geometry.Api.Object, () => Target, new State(this),
                Factory.Object, ChevronFactory.Object);
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
