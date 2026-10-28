using System.Reflection;
using System.Runtime.CompilerServices;

using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

[Collection("Table composition")]
public sealed class WorkAreaRenderingTests
{
    [Fact]
    public void MultipleVisibleAreasShareOneRendererAndMeshEvenWhenTheSelectedAreaIsHidden()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        var first = f.WorkArea.SelectedId!.Value;
        var second = f.WorkArea.CreateArea("Second");
        f.WorkArea.RestoreEndpoints(new(30, 65, 20, 0), new(34, 67, 23, 0));
        f.Draw();
        Assert.Equal(4, f.Draws);
        Assert.Equal(1, f.Uploads);
        Assert.Equal(1, f.Registers);
        f.WorkArea.SetVisibility(second, false);
        Assert.False(f.WorkArea.State.ShowWorkArea);
        f.Draw();
        Assert.Equal(6, f.Draws);
        Assert.Single(f.Active);
        f.WorkArea.SetVisibility(first, false);
        Assert.Empty(f.Active);
        f.WorkArea.SetVisibility(second, true);
        f.Draw();
        Assert.Equal(8, f.Draws);
        Assert.Equal(1, f.Uploads);
    }

    [Fact]
    public void MultipleAreasOnlyDrawVisibleValidBoxesInThePlayersCurrentDimension()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        f.WorkArea.CreateArea("Other dimension");
        f.WorkArea.RestoreEndpoints(new(30, 65, 20, 2), new(34, 67, 23, 2));
        f.Draw();
        Assert.Equal(2, f.Draws);
        f.Player.Entity.Pos.Dimension = 2;
        f.Binding.Refresh();
        f.Draw();
        Assert.Equal(4, f.Draws);
        Assert.Equal(5.004f, f.Model![0]);
        f.Player.Entity.Pos.Dimension = 3;
        f.Binding.Refresh();
        Assert.Empty(f.Active);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void NativeStateRestore_RestoresCapturedValuesAndAttemptsAllCleanup(
        bool depthTest, bool depthWrite, bool failWidth)
    {
        var render = new Mock<IRenderAPI>();
        var state = new LineRenderState(render.Object);
        SetField(state, "depthTest", depthTest);
        SetField(state, "depthWrite", depthWrite);
        SetField(state, "lineWidth", 1.25f);
        if (failWidth) render.SetupSet(r => r.LineWidth = 1.25f).Throws(new InvalidOperationException("GPU"));
        if (failWidth) Assert.Throws<InvalidOperationException>(state.Restore);
        else state.Restore();
        render.VerifySet(r => r.LineWidth = 1.25f, Times.Once);
        render.Verify(r => r.GLDepthMask(depthWrite), Times.Once);
        render.Verify(r => r.GLEnableDepthTest(), depthTest ? Times.Once : Times.Never);
        render.Verify(r => r.GLDisableDepthTest(), depthTest ? Times.Never : Times.Once);
        render.Verify(r => r.GlToggleBlend(It.IsAny<bool>(), It.IsAny<EnumBlendMode>()), Times.Never);
    }

    [Fact]
    public void VertexShader_PreservesProjectedDepthForTerrainOcclusion()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "assets", "vanillabuildingexpanded", "shaders", "vbe-oit-lines.vsh"));
        Assert.Contains("gl_Position = projectionMatrix * modelViewMatrix * vec4(vertexPositionIn, 1.0);", source);
        Assert.Equal(1, source.Split("gl_Position", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void UnitMesh_HasExactlyTwelveUniqueCellEdgesAndNoWarpOrExtraDepthFlags()
    {
        var mesh = WorkAreaRenderer.CreateUnitCube();
        Assert.Equal(EnumDrawMode.Lines, mesh.mode);
        Assert.Equal(8, mesh.VerticesCount);
        Assert.Equal(24, mesh.IndicesCount);
        Assert.All(mesh.Flags, value => Assert.Equal(0, value));
        Assert.All(mesh.Rgba, value => Assert.Equal(255, value));
        var edges = new HashSet<(int, int)>();
        for (int i = 0; i < mesh.IndicesCount; i += 2)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1];
            Assert.InRange(a, 0, 7);
            Assert.InRange(b, 0, 7);
            Assert.True(edges.Add((Math.Min(a, b), Math.Max(a, b))));
            float distance = Enumerable.Range(0, 3).Sum(axis =>
                Math.Abs(mesh.xyz[a * 3 + axis] - mesh.xyz[b * 3 + axis]));
            Assert.Equal(1, distance);
        }
        for (int vertex = 0; vertex < 8; vertex++)
            Assert.Equal(3, edges.Count(edge => edge.Item1 == vertex || edge.Item2 == vertex));
        Assert.Equal(8, Enumerable.Range(0, 8).Select(i =>
            (mesh.xyz[i * 3], mesh.xyz[i * 3 + 1], mesh.xyz[i * 3 + 2])).Distinct().Count());
        Assert.All(mesh.xyz, value => Assert.True(value is 0 or 1));
    }

    [Theory]
    [InlineData(10, 65, 20, 14, 67, 23, 0, 5, 3, 4)]
    [InlineData(14, 67, 23, 10, 65, 20, 2, 5, 3, 4)]
    [InlineData(-14, 67, -23, -10, 65, -20, 0, 5, 3, 4)]
    [InlineData(10, 65, 20, 10, 65, 20, 2, 1, 1, 1)]
    [InlineData(2000000000, 65, -2000000000, 2000000004, 67, -1999999997, 2, 5, 3, 4)]
    public void Matrix_UsesExactInclusiveExtentsAndDoubleRelativeTranslation(
        int ax, int ay, int az, int bx, int by, int bz, int dimension, long sx, long sy, long sz)
    {
        var bounds = new WorkAreaBounds(new(ax, ay, az, dimension), new(bx, by, bz, dimension));
        var before = bounds;
        var camera = new Vec3d(bounds.RenderMinX + 0.125,
            bounds.RenderMinY + (double)dimension * BlockPos.DimensionBoundary + 0.25,
            bounds.RenderMinZ - 0.375);
        var model = new Matrixf();
        WorkAreaRenderer.SetModelMatrix(model, new Matrixf().Values, bounds, camera);
        Assert.Equal(Math.Min(ax, bx), bounds.RenderMinX);
        Assert.Equal(Math.Max(ax, bx) + 1d, bounds.RenderMaxExclusiveX);
        Assert.Equal(Math.Min(ay, by), bounds.RenderMinY);
        Assert.Equal(Math.Max(ay, by) + 1d, bounds.RenderMaxExclusiveY);
        Assert.Equal(Math.Min(az, bz), bounds.RenderMinZ);
        Assert.Equal(Math.Max(az, bz) + 1d, bounds.RenderMaxExclusiveZ);
        double expansion = WorkAreaRenderer.VisualExpansion;
        Assert.InRange(expansion, 0.0001, 0.005);
        Assert.Equal((float)(sx + 2 * expansion), model.Values[0]);
        Assert.Equal((float)(sy + 2 * expansion), model.Values[5]);
        Assert.Equal((float)(sz + 2 * expansion), model.Values[10]);
        Assert.Equal((float)(-0.125 - expansion), model.Values[12]);
        Assert.Equal((float)(-0.25 - expansion), model.Values[13]);
        Assert.Equal((float)(0.375 - expansion), model.Values[14]);
        Assert.Equal(before, bounds);
    }

    [Fact]
    public void Matrix_AppliesNativeCameraRotationAndKeepsLargeBoxWithoutDistanceCulling()
    {
        using var f = new Fixture();
        f.WorkArea.RestoreEndpoints(new(-100000, 65, -100000, 0), new(100000, 67, 100000, 0));
        f.Enable();
        var cameraMatrix = new Matrixf().RotateY((float)Math.PI / 2);
        f.Render.Setup(r => r.CameraMatrixOriginf).Returns(cameraMatrix.Values);
        f.Player.Entity.CameraPos.Set(0, 66, 0);
        f.Draw();
        Assert.Single(f.Active);
        Assert.Equal(1, f.Uploads);
        var expected = new Matrixf();
        WorkAreaRenderer.SetModelMatrix(expected, cameraMatrix.Values, f.WorkArea.State.Bounds!.Value,
            f.Player.Entity.CameraPos);
        Assert.Equal(expected.Values, f.Model);
    }

    [Fact]
    public void Binding_RequiresEveryEligibilityConditionAndReusesOneRendererAndMesh()
    {
        using var f = new Fixture();
        Assert.Empty(f.Active);
        Assert.Equal(0, f.Uploads);
        f.WorkArea.SetContext(true, true);
        f.WorkArea.SetVisibility(true);
        Assert.Empty(f.Active);
        f.WorkArea.RestoreEndpoints(new(10, 65, 20, 0), null);
        Assert.Empty(f.Active);
        f.Complete();
        var first = Assert.Single(f.Active);
        f.Draw();
        Assert.Equal(1, f.Uploads);
        f.Binding.Refresh();
        f.Complete();
        Assert.Same(first, Assert.Single(f.Active));
        f.WorkArea.RestoreEndpoints(new(11, 64, 21, 0), new(19, 70, 30, 0));
        f.Draw();
        Assert.Equal(1, f.Registers);
        Assert.Equal(1, f.Uploads);
        Assert.Equal(9.004f, f.Model![0]);
        Assert.Equal(7.004f, f.Model[5]);
        Assert.Equal(10.004f, f.Model[10]);
        f.WorkArea.SetVisibility(false);
        Assert.Empty(f.Active);
        f.WorkArea.SetVisibility(true);
        Assert.Same(first, Assert.Single(f.Active));
        f.Draw();
        Assert.Equal(1, f.Uploads);
        f.WorkArea.SetContext(false, false);
        Assert.Empty(f.Active);
        Assert.True(f.WorkArea.State.ShowWorkArea);
        Assert.NotNull(f.WorkArea.State.Bounds);
        f.WorkArea.SetContext(true, false);
        Assert.Empty(f.Active);
        f.WorkArea.SetContext(true, true);
        Assert.Single(f.Active);
        Assert.Throws<ArgumentException>(() =>
            f.WorkArea.RestoreEndpoints(new(0, 65, 0, 0), new(0, 65, 0, 1)));
        Assert.Single(f.Active);
        f.WorkArea.RestoreEndpoints(new(0, 65, 0, 0), null);
        Assert.Empty(f.Active);
        f.Complete();
        Assert.Single(f.Active);
        f.WorldAvailable = false;
        f.Draw();
        int draws = f.Draws;
        f.Binding.Refresh();
        Assert.Empty(f.Active);
        Assert.Equal(draws, f.Draws);
        f.WorldAvailable = true;
        f.Binding.Refresh();
        Assert.Single(f.Active);
        f.Binding.Dispose();
        f.Binding.Dispose();
        Assert.Empty(f.Active);
        Assert.Equal(f.Registers, f.Unregisters);
        Assert.True(f.Mesh.Disposed);
        Assert.Equal(1, f.Mesh.Disposals);
        f.WorkArea.SetVisibility(false);
        f.WorkArea.SetVisibility(true);
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        first.OnRenderFrame(0, EnumRenderStage.OIT);
        Assert.Empty(f.Active);
        Assert.Equal(draws, f.Draws);
        Assert.Equal(1, f.Uploads);
    }

    [Theory]
    [InlineData(-1, 65, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 32768, 0)]
    [InlineData(0, 65, -1)]
    [InlineData(0, 65, 65536)]
    public void Binding_RejectsInvalidCoordinatesWithoutBlockOrChunkReads(int x, int y, int dimension)
    {
        using var f = new Fixture();
        f.Accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns<BlockPos>(p => p.X >= 0);
        f.WorkArea.RestoreEndpoints(new(x, y, 0, dimension), new(x, y, 0, dimension));
        f.Enable();
        Assert.Empty(f.Active);
        Assert.Equal(0, f.Uploads);
    }

    [Fact]
    public void RegistrationFailure_IsReportedOnceWithoutMeshAllocationOrRetryLoop()
    {
        using var f = new Fixture();
        f.Events.Setup(e => e.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.OIT, It.IsAny<string>()))
            .Throws(new InvalidOperationException("Render registration unavailable"));
        f.Complete();
        f.Enable();
        Assert.Empty(f.Active);
        Assert.Equal(0, f.Uploads);
        Assert.Single(f.Notices, code => code.EndsWith("workarea-render-unavailable"));
        f.RunPending();
        f.Binding.Refresh();
        f.WorkArea.SetVisibility(false);
        f.WorkArea.SetVisibility(true);
        Assert.Empty(f.Active);
        f.Events.Verify(e => e.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.OIT,
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void WrongRenderStage_DoesNotAllocateOrDraw()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        Assert.Single(f.Active).OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Single(f.Active).OnRenderFrame(0, EnumRenderStage.Opaque);
        Assert.Equal(0, f.Uploads);
        Assert.Equal(0, f.Draws);
        Assert.Equal(0, f.State.Captures);
    }

    [Fact]
    public void DimensionAndDeath_HideCancelAndRestorePreferenceWithoutChangingEndpoints()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        var original = f.WorkArea.State.Bounds;
        f.WorkArea.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, CreatePlayer());
        Assert.Single(f.Active);
        Assert.True(f.WorkArea.IsEditing);
        f.Player.Entity.Pos.Dimension = 2;
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        Assert.Empty(f.Active);
        Assert.False(f.WorkArea.IsEditing);
        Assert.Equal(original, f.WorkArea.State.Bounds);
        Assert.True(f.WorkArea.State.ShowWorkArea);
        Assert.Single(f.Notices, code => code.EndsWith("workarea-dimension-mismatch"));
        f.Binding.Refresh();
        Assert.Single(f.Notices, code => code.EndsWith("workarea-dimension-mismatch"));
        f.Player.Entity.Pos.Dimension = 0;
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        Assert.Single(f.Active);
        f.WorkArea.BeginEndpointEdit(WorkAreaEndpointRole.End);
        f.Player.Entity.Alive = false;
        f.WorkArea.SetContext(false, false);
        Assert.Empty(f.Active);
        Assert.False(f.WorkArea.IsEditing);
        Assert.Equal(original, f.WorkArea.State.Bounds);
        f.Player.Entity.Alive = true;
        f.WorkArea.SetContext(true, true);
        Assert.Single(f.Active);
        f.Player.Entity.Pos.Dimension = 2;
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        f.WorkArea.TrySetEndpoints(new(10, 65, 20, 2), null, f.Api.Object.World.BlockAccessor);
        Assert.Null(f.WorkArea.State.End);
        Assert.Empty(f.Active);
        f.Player.Entity.Pos.Dimension = 0;
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        Assert.Empty(f.Active);
    }

    [Fact]
    public void NativePlayerAliveAndDimensionAreCheckedAgainBeforeDrawingBetweenTicks()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        f.Player.Entity.Alive = false;
        f.Draw();
        Assert.Equal(0, f.Draws);
        f.Binding.Refresh();
        Assert.Empty(f.Active);
        f.Player.Entity.Alive = true;
        f.Binding.Refresh();
        Assert.Single(f.Active);
        f.Player.Entity.Pos.Dimension = 3;
        f.Draw();
        Assert.Equal(0, f.Draws);
        f.Binding.Refresh();
        Assert.Empty(f.Active);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("native")]
    [InlineData("gpu-memory")]
    [InlineData("mesh")]
    [InlineData("shader")]
    [InlineData("compile")]
    [InlineData("create-shader")]
    [InlineData("register-shader")]
    [InlineData("occluded-depth")]
    [InlineData("occluded-draw")]
    [InlineData("capture")]
    [InlineData("use")]
    [InlineData("uniform")]
    [InlineData("draw")]
    [InlineData("stop")]
    [InlineData("restore")]
    public void ResourceFailures_ReportOncePreserveReadoutAndSafelyRetireRenderer(string fault)
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        var bounds = f.WorkArea.State.Bounds;
        var renderer = Assert.Single(f.Active);
        switch (fault)
        {
            case "upload":
                f.Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>())).Throws(new InvalidOperationException("GPU"));
                break;
            case "native":
                f.Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>())).Throws(new Exception("Native GL check"));
                break;
            case "gpu-memory":
                f.Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>()))
                    .Throws(new OutOfMemoryException("GPU memory"));
                break;
            case "mesh": f.Mesh.Ready = false; break;
            case "shader": f.Shader.SetupGet(s => s.LoadError).Returns(true); break;
            case "compile": f.Shader.Setup(s => s.Compile()).Returns(false); break;
            case "create-shader": f.Shaders.Setup(s => s.NewShaderProgram()).Returns((IShaderProgram)null!); break;
            case "register-shader":
                f.Shaders.Setup(s => s.RegisterFileShaderProgram("vbe-oit-lines", f.Shader.Object))
                    .Throws(new InvalidOperationException("GPU"));
                break;
            case "occluded-depth": f.State.Fault = "occluded-depth"; break;
            case "occluded-draw":
                f.Render.SetupSequence(r => r.RenderMesh(It.IsAny<MeshRef>()))
                    .Pass().Throws(new InvalidOperationException("GPU"));
                break;
            case "capture": f.State.Fault = "capture"; break;
            case "use": f.Shader.Setup(s => s.Use()).Throws(new InvalidOperationException("GPU")); break;
            case "uniform":
                f.Shader.Setup(s => s.Uniform("colorIn", 1f, 0.75f, 0.15f, 1f))
                    .Throws(new InvalidOperationException("GPU"));
                break;
            case "draw":
                f.Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>())).Throws(new InvalidOperationException("GPU"));
                break;
            case "stop": f.Shader.Setup(s => s.Stop()).Throws(new InvalidOperationException("GPU")); break;
            case "restore": f.State.Fault = "restore"; break;
        }
        renderer.OnRenderFrame(0, EnumRenderStage.OIT);
        Assert.Single(f.Notices, code => code.EndsWith("workarea-render-unavailable"));
        Assert.Single(f.Pending);
        // A callback must not remove itself from the native live indexed render list.
        Assert.Single(f.Active);
        Assert.Equal(bounds, f.WorkArea.State.Bounds);
        Assert.True(f.WorkArea.State.ShowWorkArea);
        if (fault is "use" or "uniform" or "draw" or "stop" or "restore" or "occluded-depth" or "occluded-draw")
        {
            Assert.Equal(1, f.State.Restores);
            f.PreviousShader.Verify(s => s.Use(), Times.Once);
        }
        f.RunPending();
        Assert.Empty(f.Active);
        f.WorkArea.SetVisibility(false);
        f.WorkArea.SetVisibility(true);
        f.Binding.Refresh();
        renderer.OnRenderFrame(0, EnumRenderStage.OIT);
        Assert.Single(f.Notices, code => code.EndsWith("workarea-render-unavailable"));
        Assert.Empty(f.Active);
        f.Api.Verify(a => a.Logger.Error("[WorkArea] Wireframe unavailable for this session: {0}",
            It.IsAny<object[]>()), Times.Once);
        f.Binding.Dispose();
        Assert.Equal(f.Uploads > 0 ? 1 : 0, f.Mesh.Disposals);
        f.Shader.Verify(s => s.Dispose(), fault == "create-shader" ? Times.Never : Times.Once);
    }

    [Fact]
    public void Draw_UsesComplementaryDepthTestsAndExactOpacitiesWithoutChangingOitBlend()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        f.Draw();
        f.Draw();
        Assert.Equal(1, f.Uploads);
        Assert.Equal(4, f.Draws);
        Assert.Equal(2, f.State.Captures);
        Assert.Equal(2, f.State.Restores);
        Assert.Equal(new[] { "capture", "previous-stop", "depth-test", "depth-mask",
            "line-width", "use", "visible-depth", "draw", "occluded-depth", "draw", "stop", "restore", "previous-use" },
            f.Sequence.Take(13));
        f.Shader.Verify(s => s.Uniform("colorIn", 1f, 0.75f, 0.15f, 1f), Times.Exactly(2));
        f.Shader.Verify(s => s.Uniform("colorIn", 1f, 0.75f, 0.15f, 0.3f), Times.Exactly(2));
        f.Shader.Verify(s => s.UniformMatrix("projectionMatrix", f.Projection), Times.Exactly(2));
        f.Render.Verify(r => r.GlToggleBlend(It.IsAny<bool>(), It.IsAny<EnumBlendMode>()), Times.Never);
        f.Render.Verify(r => r.GLDepthMask(false), Times.Exactly(2));
        f.Render.VerifySet(r => r.LineWidth = 1.6f, Times.Exactly(2));
        f.Accessor.Verify(a => a.IsValidPos(It.IsAny<BlockPos>()), Times.Exactly(4));
        Assert.Equal(5.004f, f.Model![0]);
    }

    [Fact]
    public void OitShader_IsOwnedReusedAndRecreatedAfterNativeReload()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        f.Draw();
        f.Draw();
        f.Shaders.Verify(s => s.NewShaderProgram(), Times.Once);
        f.Shader.VerifySet(s => s.AssetDomain = "vanillabuildingexpanded", Times.Once);
        f.Shader.VerifySet(s => s.Oit = true, Times.Once);
        f.Shaders.Verify(s => s.RegisterFileShaderProgram("vbe-oit-lines", f.Shader.Object), Times.Once);
        f.Shader.Verify(s => s.Compile(), Times.Once);
        f.Shaders.Verify(s => s.GetProgram(It.IsAny<int>()), Times.Never);
        var replacement = new Mock<IShaderProgram>();
        replacement.Setup(s => s.Compile()).Returns(true);
        f.Shader.SetupGet(s => s.Disposed).Returns(true);
        f.Shaders.Setup(s => s.NewShaderProgram()).Returns(replacement.Object);
        f.Draw();
        f.Shaders.Verify(s => s.NewShaderProgram(), Times.Exactly(2));
        f.Shaders.Verify(s => s.RegisterFileShaderProgram("vbe-oit-lines", replacement.Object), Times.Once);
        replacement.VerifySet(s => s.Oit = true, Times.Once);
        replacement.Verify(s => s.Use(), Times.Once);
        Assert.Equal(1, f.Uploads);
        f.Binding.Dispose();
        f.Binding.Dispose();
        replacement.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public void PendingFailureRetirementAndOldCallbacksAreHarmlessAfterWorldDeparture()
    {
        using var f = new Fixture();
        f.Complete();
        f.Enable();
        var old = Assert.Single(f.Active);
        f.State.Fault = "capture";
        f.Draw();
        f.Binding.Dispose();
        f.RunPending();
        int registers = f.Registers;
        f.WorkArea.RestoreEndpoints(new(0, 65, 0, 1), new(1, 65, 1, 1));
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        old.OnRenderFrame(0, EnumRenderStage.OIT);
        Assert.Empty(f.Active);
        Assert.Equal(registers, f.Registers);
        f.Events.VerifyRemove(e => e.PlayerDimensionChanged -= It.IsAny<PlayerCommonDelegate>(), Times.Once);
        using var fresh = new Fixture();
        Assert.True(fresh.WorkArea.State.ShowWorkArea);
        Assert.False(fresh.WorkArea.State.IsBuildModeEnabled);
        Assert.Null(fresh.WorkArea.State.Bounds);
        Assert.Empty(fresh.Active);
        fresh.Complete();
        fresh.Enable();
        fresh.Draw();
        Assert.NotSame(old, Assert.Single(fresh.Active));
        Assert.NotSame(f.Mesh, fresh.Mesh);
    }

    internal static ClientPlayer CreatePlayer()
    {
        var game = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var data = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        SetField(player, "game", game);
        SetField(player, "worlddata", data);
        SetField(game, "player", player);
        data.EntityPlayer = new EntityPlayer();
        return player;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(target, value);

    internal sealed class FakeState(List<string>? sequence = null) : IOitLineRenderState
    {
        public int Captures;
        public int Restores;
        public string? Fault;

        public void Capture()
        {
            Captures++;
            sequence?.Add("capture");
            if (Fault == "capture") throw new InvalidOperationException("GPU state capture");
        }

        public void Restore()
        {
            Restores++;
            sequence?.Add("restore");
            if (Fault == "restore") throw new InvalidOperationException("GPU state restore");
        }

        public void SetOccludedDepthTest(bool occluded)
        {
            sequence?.Add(occluded ? "occluded-depth" : "visible-depth");
            if (occluded && Fault == "occluded-depth")
                throw new InvalidOperationException("GPU depth comparison");
        }
    }

    private sealed class TestMesh : MeshRef
    {
        public int Disposals;
        public bool Ready = true;
        public override bool Initialized => Ready;
        public override void Dispose() { Disposals++; base.Dispose(); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string previousLocale = Lang.CurrentLocale;
        private readonly string locale = $"vbe-workarea-render-tests-{Guid.NewGuid():N}";
        public readonly Mock<ICoreClientAPI> Api = TestHelpers.CreateMockCapi();
        public readonly Mock<IClientWorldAccessor> World = new();
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly Mock<IBlockAccessor> Accessor = new(MockBehavior.Strict);
        public readonly Mock<IRenderAPI> Render = new();
        public readonly Mock<IShaderAPI> Shaders = new();
        public readonly Mock<IShaderProgram> Shader = new();
        public readonly Mock<IShaderProgram> PreviousShader = new();
        public readonly ClientPlayer Player;
        public readonly WorkAreaController WorkArea = new();
        public readonly WorkAreaRenderBinding Binding;
        public readonly TestMesh Mesh = new();
        public readonly HashSet<IRenderer> Active = [];
        public readonly List<Action> Pending = [];
        public readonly List<string> Notices = [];
        public readonly List<string> Sequence = [];
        public readonly FakeState State;
        public readonly float[] Projection = new Matrixf().Values;
        public float[]? Model;
        public bool WorldAvailable = true;
        public int Registers;
        public int Unregisters;
        public int Uploads;
        public int Draws;

        public Fixture()
        {
            var translations = new Mock<ITranslationService>();
            translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(true);
            translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key);
            Lang.AvailableLanguages.Add(locale, translations.Object);
            Lang.ChangeLanguage(locale);
            State = new FakeState(Sequence);
            Player = CreatePlayer();
            World.Setup(w => w.Player).Returns(Player);
            World.Setup(w => w.BlockAccessor).Returns(Accessor.Object);
            Accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(true);
            Api.Setup(a => a.World).Returns(() => WorldAvailable ? World.Object : null!);
            Api.Setup(a => a.Event).Returns(Events.Object);
            Api.Setup(a => a.Render).Returns(Render.Object);
            Api.Setup(a => a.Shader).Returns(Shaders.Object);
            Api.Setup(a => a.TriggerIngameError(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<object, string, string>((_, code, _) => Notices.Add(code));
            Events.Setup(e => e.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), It.IsAny<string>()))
                .Callback<IRenderer, EnumRenderStage, string>((renderer, stage, _) =>
                {
                    Assert.Equal(EnumRenderStage.OIT, stage);
                    Assert.InRange(renderer.RenderOrder, 0.5, 0.6);
                    Assert.True(Active.Add(renderer));
                    Registers++;
                });
            Events.Setup(e => e.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()))
                .Callback<IRenderer, EnumRenderStage>((renderer, stage) =>
                {
                    Assert.Equal(EnumRenderStage.OIT, stage);
                    Assert.True(Active.Remove(renderer));
                    Unregisters++;
                });
            Events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => Pending.Add(action));
            Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>()))
                .Returns(() => { Uploads++; return Mesh; });
            Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>()))
                .Callback<MeshRef>(mesh => { Assert.Same(Mesh, mesh); Draws++; Sequence.Add("draw"); });
            Render.SetupGet(r => r.CameraMatrixOriginf).Returns(new Matrixf().Values);
            Render.SetupGet(r => r.CurrentProjectionMatrix).Returns(Projection);
            Render.SetupGet(r => r.CurrentActiveShader).Returns(PreviousShader.Object);
            Render.Setup(r => r.GLEnableDepthTest()).Callback(() => Sequence.Add("depth-test"));
            Render.Setup(r => r.GLDepthMask(false)).Callback(() => Sequence.Add("depth-mask"));
            Render.SetupSet(r => r.LineWidth = 1.6f).Callback(() => Sequence.Add("line-width"));
            Shaders.Setup(s => s.NewShaderProgram()).Returns(Shader.Object);
            Shader.Setup(s => s.Compile()).Returns(true);
            Shader.Setup(s => s.UniformMatrix("modelViewMatrix", It.IsAny<float[]>()))
                .Callback<string, float[]>((_, matrix) => Model = (float[])matrix.Clone());
            Shader.Setup(s => s.Use()).Callback(() => Sequence.Add("use"));
            Shader.Setup(s => s.Stop()).Callback(() => Sequence.Add("stop"));
            PreviousShader.Setup(s => s.Stop()).Callback(() => Sequence.Add("previous-stop"));
            PreviousShader.Setup(s => s.Use()).Callback(() => Sequence.Add("previous-use"));
            Binding = new WorkAreaRenderBinding(Api.Object, WorkArea, State);
        }

        public void Complete() => WorkArea.RestoreEndpoints(new(10, 65, 20, 0), new(14, 67, 23, 0));
        public void Enable() { WorkArea.SetContext(true, true); WorkArea.SetVisibility(true); }
        public void Draw()
        {
            foreach (var renderer in Active.ToArray()) renderer.OnRenderFrame(0, EnumRenderStage.OIT);
        }
        public void RunPending()
        {
            foreach (var action in Pending.ToArray()) action();
            Pending.Clear();
        }
        public void Dispose()
        {
            Binding.Dispose();
            Lang.ChangeLanguage(previousLocale);
            Lang.AvailableLanguages.Remove(locale);
        }
    }
}
