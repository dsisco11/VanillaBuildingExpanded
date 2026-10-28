using Moq;
using VanillaBuildingExpanded.Gizmos;
using VanillaBuildingExpanded.Rendering;
using VanillaBuildingExpanded.Tests.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.ComposedPositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

[Collection("Table composition")]
public sealed partial class GizmoRenderAdapterTests
{
    [Theory]
    [InlineData(GizmoShape.Arrow, 6, 10)]
    [InlineData(GizmoShape.Circle, 16, 32)]
    [InlineData(GizmoShape.Diamond, 4, 8)]
    public void MeshesMatchSharedHitGeometryAndHaveNoWarpFlags(GizmoShape shape, int count, int indices)
    {
        var mesh = GizmoRenderAdapter.CreateMesh(shape);
        Assert.Equal(count, mesh.VerticesCount);
        Assert.Equal(indices, mesh.IndicesCount);
        Assert.Equal(EnumDrawMode.Lines, mesh.mode);
        Assert.All(mesh.Flags, value => Assert.Equal(0, value));
        Assert.All(mesh.Rgba, value => Assert.Equal(255, value));
        var vertices = GizmoShapes.Vertices(shape);
        for (int i = 0; i < count; i++)
            for (int a = 0; a < 3; a++) Assert.Equal((float)vertices[i][a], mesh.xyz[3 * i + a]);
        Assert.Equal(GizmoShapes.Indices(shape).ToArray(), mesh.Indices);
    }

    [Fact]
    public void ModelMatrixSubtractsCameraInDoublesBeforeFloatConversion()
    {
        var reference = V(2000000000.125, 32768, -2000000000.25);
        var camera = Camera(reference: reference).Camera!;
        using var owner = new ComposedPositionGizmo(new(reference + V(0.25, 0.5, -0.75), 0), _ => { },
            g => new GizmoEditStateTarget(g, () => true, () => { }, () => { }));
        var part = owner.GetGeometrySource(owner.Handles[ComposedPositionGizmo.X]).GetSnapshot(new(0, camera))!.Geometry;
        var matrix = GizmoRenderAdapter.ModelView(camera, part);
        Assert.Equal(0.25f, matrix[12]);
        Assert.Equal(0.5f, matrix[13]);
        Assert.Equal(-0.75f, matrix[14]);
        Assert.Equal((float)part.BasisX[0], matrix[0]);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 32767)]
    [InlineData(2, 32768)]
    [InlineData(15, 32768)]
    public void NativeCapturePreservesDimensionAndBoundaryLocalYAcrossProjectionHitsAndDrawing(int dimension, double y)
    {
        float previousScale = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = 1;
            var api = TestHelpers.CreateMockCapi();
            var render = new Mock<IRenderAPI>();
            var world = new Mock<IClientWorldAccessor>();
            var player = WorkAreaRenderingTests.CreatePlayer();
            var input = new Mock<IInputAPI>();
            var entity = player.Entity;
            entity.Pos.Dimension = dimension;
            var reference = V(2000000000.125, y - 0.25, -2000000000.125);
            entity.CameraPos.Set(reference[0], reference[1] + (double)dimension * BlockPos.DimensionBoundary, reference[2]);
            player.OverrideCameraMode = EnumCameraMode.FirstPerson;
            world.SetupGet(w => w.Player).Returns(player);
            api.SetupGet(a => a.World).Returns(world.Object);
            api.SetupGet(a => a.Input).Returns(input.Object);
            api.SetupGet(a => a.Render).Returns(render.Object);
            render.SetupGet(r => r.FrameWidth).Returns(800);
            render.SetupGet(r => r.FrameHeight).Returns(600);
            render.SetupGet(r => r.PerspectiveViewMat).Returns(Mat4d.Translate(new double[16], Mat4d.Create(),
                -entity.CameraPos.X, -entity.CameraPos.Y, -entity.CameraPos.Z));
            render.SetupGet(r => r.PerspectiveProjectionMat).Returns(
                Mat4d.Perspective(new double[16], Math.PI / 2, 800d / 600, 0.1, 1000));

            var context = GizmoCamera.Capture(api.Object);
            var camera = context.Camera!;
            var anchor = reference + V(0, 0.25, -10);
            Assert.Equal(dimension, context.Dimension);
            Assert.Equal(reference, camera.ReferencePosition);
            Assert.Equal(reference, camera.EyePosition);
            Assert.Equal(y, anchor[1]);
            Assert.True(camera.TryProject(anchor, out var screen));
            var pointer = camera.CreatePointer(context, screen[0], screen[1]);
            Assert.True(camera.TryProject(pointer.Ray.Origin + pointer.Ray.Direction * 10, out var roundTrip));
            Assert.Equal(screen[0], roundTrip[0], 3);
            Assert.Equal(screen[1], roundTrip[1], 3);
            Assert.True(camera.TryProject(pointer.FarPoint!.Value, out var far));
            Assert.Equal(1d, far[2], 5);
            List<GizmoHandleDraw> drawing = [];
            using var manager = new GizmoManager();
            var gizmo = new ComposedPositionGizmo(new(anchor, dimension), drawing.Add,
                g => new GizmoEditStateTarget(g, () => true, () => { }, () => { }));
            manager.Add(gizmo);
            manager.Render(context);
            Assert.True(manager.TryBegin(pointer));
            Assert.Equal(ComposedPositionGizmo.Center, manager.CapturedHandle);
            var part = drawing.Single(p => p.Geometry.Handle.Id == ComposedPositionGizmo.Center).Geometry;
            var model = GizmoRenderAdapter.ModelView(camera, part);
            Assert.Equal(0f, model[12]);
            Assert.Equal(0.25f, model[13]);
            Assert.Equal(-10f, model[14]);
            Assert.Equal(dimension, gizmo.Value.Dimension);
            Assert.Equal(y, gizmo.Value.Coordinates[1]);
        }
        finally { RuntimeEnv.GUIScale = previousScale; }
    }

    [Fact]
    public void AdapterCapturesWithoutInstancesAndRegistersOnlyOnExplicitActivation()
    {
        using var f = new Fixture();
        int ready = 0;
        f.Adapter.ContextReady += _ => ready++;
        f.Adapter.OnRenderFrame(0, EnumRenderStage.Opaque);
        Assert.Empty(f.Active);
        Assert.Equal(0, ready);
        f.Adapter.SetActive(true);
        Assert.Equal(2, f.Active.Count);
        Assert.Contains((f.Adapter, EnumRenderStage.OIT), f.Active);
        Assert.Contains((f.Adapter, EnumRenderStage.AfterBlit), f.Active);
        Assert.Equal(0.49, f.Adapter.RenderOrder);
        f.Frame();
        Assert.Equal(1, ready);
        Assert.Same(f.Camera, f.Adapter.Context);
        Assert.Empty(f.Meshes);
        f.Adapter.SetActive(false);
        Assert.Empty(f.Active);
        Assert.Null(f.Adapter.Context);
    }

    [Fact]
    public void DrawingReusesResourcesAndRestoresStateWithoutBroadcastingBlendChanges()
    {
        using var f = new Fixture();
        f.Add();
        f.Add(GizmoShape.Diamond);
        f.Adapter.SetActive(true);
        f.Frame();
        f.Frame();
        Assert.Equal(3, f.Meshes.Count);
        Assert.Equal(32, f.Draws);
        Assert.Equal(16, f.State.Captures);
        Assert.Equal(16, f.State.Restores);
        f.Render.Verify(r => r.GLEnableDepthTest(), Times.Exactly(16));
        f.Render.Verify(r => r.GLDisableDepthTest(), Times.Never);
        f.Render.Verify(r => r.GLDepthMask(false), Times.Exactly(16));
        f.Render.Verify(r => r.GlToggleBlend(It.IsAny<bool>(), It.IsAny<EnumBlendMode>()), Times.Never);
        f.Previous.Verify(p => p.Use(), Times.Exactly(16));
        f.Shader.Verify(p => p.Uniform("colorIn", 1f, 0.2f, 0.2f, 1f), Times.Exactly(4));
        f.Shader.Verify(p => p.Uniform("colorIn", 1f, 0.2f, 0.2f, 0.3f), Times.Exactly(4));
        Assert.Equal(Enumerable.Range(0, 16).SelectMany(_ => new[] { false, true }), f.State.Occluded);
        f.Shader.Verify(p => p.UniformMatrix("modelViewMatrix", It.Is<float[]>(m => m.Length == 16)), Times.Exactly(16));
        f.Adapter.Dispose();
        f.Adapter.Dispose();
        Assert.All(f.Meshes, mesh => Assert.Equal(1, mesh.Disposals));
        f.Shader.Verify(s => s.Dispose(), Times.Once);
        Assert.Empty(f.Active);
    }

    [Fact]
    public void HoverAndCaptureEmphasizeLineWidthWithoutDependingOnColor()
    {
        using var f = new Fixture();
        f.Add();
        f.Adapter.SetActive(true);
        f.Frame();
        var pointer = f.Camera.Camera!.CreatePointer(f.Camera, 400, 300);
        f.Manager.UpdateHover(pointer);
        f.Frame();
        f.Render.VerifySet(r => r.LineWidth = 2.8f, Times.Once);
        Assert.True(f.Manager.TryBegin(pointer));
        f.Frame();
        f.Render.VerifySet(r => r.LineWidth = 2.8f, Times.Exactly(2));
    }

    [Theory]
    [InlineData(GizmoShape.Circle, 0)]
    [InlineData(GizmoShape.Circle, 1)]
    [InlineData(GizmoShape.Circle, 2)]
    [InlineData(GizmoShape.Circle, 3)]
    [InlineData(GizmoShape.Diamond, 0)]
    [InlineData(GizmoShape.Diamond, 1)]
    [InlineData(GizmoShape.Diamond, 2)]
    [InlineData(GizmoShape.Diamond, 3)]
    public void EveryHandle_UsesFullVisibleAndThirtyPercentOccludedOpacityInEveryState(GizmoShape shape, int handle)
    {
        using var f = new Fixture();
        var gizmo = f.Add(shape);
        f.Adapter.SetActive(true);
        f.Frame();
        var baseline = f.Colors.ToArray();
        AssertColors();
        gizmo.HoveredHandle = new(handle);
        f.Colors.Clear();
        f.Frame();
        AssertColors();
        gizmo.HoveredHandle = null;
        var pointer = new GizmoPointer(f.Camera, new(V(10, 10, 0), V(-10, -10, -10)));
        Assert.True(gizmo.BeginInteraction(new(handle), pointer));
        f.Colors.Clear();
        f.Frame();
        AssertColors();
        gizmo.CancelInteraction();
        f.Colors.Clear();
        f.Frame();
        AssertColors();

        void AssertColors()
        {
            Assert.Equal(8, f.Colors.Count);
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal(baseline[i].Red, f.Colors[i].Red);
                Assert.Equal(baseline[i].Green, f.Colors[i].Green);
                Assert.Equal(baseline[i].Blue, f.Colors[i].Blue);
                Assert.Equal(i % 2 == 0 ? 1f : 0.3f, f.Colors[i].Alpha);
            }
        }
    }

    [Fact]
    public void OitShaderIsSharedAcrossHandlesAndRecreatedAfterNativeReload()
    {
        using var f = new Fixture();
        f.Add();
        f.Add(GizmoShape.Diamond);
        f.Adapter.SetActive(true);
        f.Frame();
        f.Frame();
        f.Shaders.Verify(s => s.NewShaderProgram(), Times.Once);
        f.Shaders.Verify(s => s.RegisterFileShaderProgram("vbe-oit-lines", f.Shader.Object), Times.Once);
        f.Shader.VerifySet(s => s.AssetDomain = "vanillabuildingexpanded", Times.Once);
        f.Shader.VerifySet(s => s.Oit = true, Times.Once);
        f.Shader.Verify(s => s.Compile(), Times.Once);
        f.Shaders.Verify(s => s.GetProgram((int)EnumShaderProgram.Wireframe), Times.Never);
        f.Shaders.Verify(s => s.NewShader(It.IsAny<EnumShaderType>()), Times.Never);
        var replacement = new Mock<IShaderProgram>();
        replacement.Setup(s => s.Compile()).Returns(true);
        f.Shader.SetupGet(s => s.Disposed).Returns(true);
        f.Shaders.Setup(s => s.NewShaderProgram()).Returns(replacement.Object);
        f.Frame();
        f.Shaders.Verify(s => s.NewShaderProgram(), Times.Exactly(2));
        f.Shaders.Verify(s => s.RegisterFileShaderProgram("vbe-oit-lines", replacement.Object), Times.Once);
        replacement.VerifySet(s => s.Oit = true, Times.Once);
        replacement.Verify(s => s.Use(), Times.Exactly(8));
        f.Adapter.Dispose();
        replacement.Verify(s => s.Dispose(), Times.Once);
        f.Shader.Verify(s => s.Dispose(), Times.Never);
    }

    [Fact]
    public void ShaderCompileFailureIsReportedAndRetiresInteractiveHandles()
    {
        using var f = new Fixture();
        f.Add();
        f.Shader.Setup(s => s.Compile()).Returns(false);
        f.Adapter.SetActive(true);
        f.Frame();
        Assert.Equal(1, f.Notices);
        Assert.Null(f.Adapter.Context);
        Assert.Equal(0, f.Draws);
        f.Flush();
        Assert.Empty(f.Active);
        Assert.False(f.Manager.TryBegin(f.Camera.Camera!.CreatePointer(f.Camera, 400, 300)));
        f.Adapter.Dispose();
        f.Shader.Verify(s => s.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("use")]
    [InlineData("draw")]
    [InlineData("restore")]
    public void RenderFailureRestoresStateCancelsAndRetiresInvisibleTargetsOutsideCallback(string fault)
    {
        using var f = new Fixture();
        var gizmo = f.Add();
        f.Adapter.SetActive(true);
        f.Frame();
        var pointer = f.Camera.Camera!.CreatePointer(f.Camera, 400, 300);
        Assert.True(f.Manager.TryBegin(pointer));
        f.Manager.Update(new(f.Camera, new(V(0, 0, 0), V(1, 0, -10))));
        if (fault == "upload")
        {
            f.Add(GizmoShape.Diamond);
            f.Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>())).Throws(new InvalidOperationException("GPU"));
        }
        if (fault == "use") f.Shader.Setup(s => s.Use()).Throws(new InvalidOperationException("GPU"));
        if (fault == "draw") f.Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>())).Throws(new InvalidOperationException("GPU"));
        if (fault == "restore") f.State.FailRestore = true;
        f.Frame();
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.Equal(V(0, 0, -10), gizmo.Value.Coordinates);
        Assert.False(f.Manager.TryBegin(pointer));
        Assert.Null(f.Adapter.Context);
        Assert.Equal(2, f.Active.Count);
        Assert.NotEmpty(f.Pending);
        f.Flush();
        Assert.Empty(f.Active);
        Assert.Equal(1, f.Notices);
        Assert.Equal(f.State.Captures, f.State.Restores);
        f.Api.Verify(a => a.Logger.Error("[Gizmo] In-world controls unavailable for this session: {0}",
            It.IsAny<object[]>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackMembershipChangesAreDeferredAndCannotRepublishStaleHitEligibility(bool dispose)
    {
        using var f = new Fixture();
        f.Add();
        f.Adapter.SetActive(true);
        f.Frame();
        f.Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>())).Callback(() =>
        {
            if (dispose) f.Adapter.Dispose(); else f.Adapter.SetActive(false);
        });
        f.Frame();
        Assert.Equal(2, f.Active.Count);
        Assert.Null(f.Adapter.Context);
        Assert.False(f.Manager.TryBegin(f.Camera.Camera!.CreatePointer(f.Camera, 400, 300)));
        Assert.All(f.Meshes, mesh => Assert.Equal(0, mesh.Disposals));
        f.Flush();
        Assert.Empty(f.Active);
        if (dispose) Assert.All(f.Meshes, mesh => Assert.Equal(1, mesh.Disposals));
    }

    private sealed class FakeState : IOitLineRenderState
    {
        public int Captures, Restores;
        public bool FailRestore;
        public readonly List<bool> Occluded = [];
        public void Capture() => Captures++;
        public void SetOccludedDepthTest(bool occluded) => Occluded.Add(occluded);
        public void Restore()
        {
            Restores++;
            if (FailRestore) throw new InvalidOperationException("GPU");
        }
    }

    private sealed class TestMesh : MeshRef
    {
        public int Disposals;
        public override bool Initialized => true;
        public override void Dispose() { Disposals++; base.Dispose(); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string previousLocale = Lang.CurrentLocale;
        private readonly string locale = $"vbe-gizmo-tests-{Guid.NewGuid():N}";
        public readonly Mock<ICoreClientAPI> Api = TestHelpers.CreateMockCapi();
        public readonly Mock<IRenderAPI> Render = new();
        public readonly Mock<IShaderAPI> Shaders = new();
        public readonly Mock<IShaderProgram> Shader = new();
        public readonly Mock<IShaderProgram> LabelShader = new();
        public readonly Mock<IShaderProgram> Previous = new();
        public readonly Mock<IShader> Vertex = new();
        public readonly Mock<IShader> Fragment = new();
        public readonly List<(float Red, float Green, float Blue, float Alpha)> Colors = [];
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly GizmoManager Manager = new();
        public readonly FakeState State = new();
        public GizmoContext Camera = ComposedPositionGizmoTests.Camera();
        public readonly List<(IRenderer, EnumRenderStage)> Active = [];
        public readonly List<Action> Pending = [];
        public readonly List<TestMesh> Meshes = [];
        public readonly GizmoRenderAdapter Adapter;
        public int Draws, Notices;
        public int LabelUploads;
        public readonly List<int> DeletedTextures = [];
        public readonly List<(float X, float Y, float Width, float Height)> LabelRects = [];

        public Fixture()
        {
            var translations = new Mock<ITranslationService>();
            translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(true);
            translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key);
            Lang.AvailableLanguages.Add(locale, translations.Object);
            Lang.ChangeLanguage(locale);
            Api.SetupGet(a => a.Render).Returns(Render.Object);
            Api.SetupGet(a => a.Shader).Returns(Shaders.Object);
            Api.SetupGet(a => a.Event).Returns(Events.Object);
            Shaders.Setup(s => s.GetProgram((int)EnumShaderProgram.Wireframe)).Returns(Shader.Object);
            int programCount = 0;
            Shaders.Setup(s => s.NewShaderProgram()).Returns(() => programCount++ == 0 ? Shader.Object : LabelShader.Object);
            LabelShader.Setup(s => s.Compile()).Returns(true);
            var gui = new Mock<IGuiAPI>();
            Api.Setup(a => a.Gui).Returns(gui.Object);
            gui.Setup(g => g.LoadCairoTexture(It.IsAny<Cairo.ImageSurface>(), It.IsAny<bool>()))
                .Returns(() => 100 + ++LabelUploads);
            gui.Setup(g => g.DeleteTexture(It.IsAny<int>())).Callback<int>(id => DeletedTextures.Add(id));
            LabelShader.Setup(s => s.Uniform("screenRect", It.IsAny<float>(), It.IsAny<float>(),
                It.IsAny<float>(), It.IsAny<float>()))
                .Callback<string, float, float, float, float>((_, x, y, width, height) =>
                    LabelRects.Add((x, y, width, height)));
            Vertex.SetupProperty(s => s.Code);
            Fragment.SetupProperty(s => s.Code);
            Shaders.Setup(s => s.NewShader(EnumShaderType.VertexShader)).Returns(Vertex.Object);
            Shaders.Setup(s => s.NewShader(EnumShaderType.FragmentShader)).Returns(Fragment.Object);
            Shader.Setup(s => s.Compile()).Returns(true);
            Shader.Setup(s => s.Uniform("colorIn", It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
                .Callback<string, float, float, float, float>((_, red, green, blue, alpha) =>
                    Colors.Add((red, green, blue, alpha)));
            Render.SetupGet(r => r.CurrentActiveShader).Returns(Previous.Object);
            Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
            {
                var mesh = new TestMesh();
                Meshes.Add(mesh);
                return mesh;
            });
            Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>())).Callback(() => Draws++);
            Events.Setup(e => e.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), It.IsAny<string>()))
                .Callback<IRenderer, EnumRenderStage, string>((renderer, stage, _) => Active.Add((renderer, stage)));
            Events.Setup(e => e.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()))
                .Callback<IRenderer, EnumRenderStage>((renderer, stage) => Active.Remove((renderer, stage)));
            Events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => Pending.Add(action));
            Api.Setup(a => a.TriggerIngameError(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => Notices++);
            Adapter = new(Api.Object, Manager, () => Camera, State, State);
        }

        public ComposedPositionGizmo Add(GizmoShape shape = GizmoShape.Circle)
        {
            var gizmo = new ComposedPositionGizmo(new(V(0, 0, -10), 0), Adapter.Draw,
                g => new GizmoEditStateTarget(g, () => true, () => { }, () => { }), marker: shape);
            Manager.Add(gizmo);
            return gizmo;
        }

        public void Frame()
        {
            Adapter.OnRenderFrame(0, EnumRenderStage.OIT);
            Adapter.OnRenderFrame(0, EnumRenderStage.AfterBlit);
        }

        public void Flush()
        {
            foreach (var action in Pending.ToArray()) action();
            Pending.Clear();
        }

        public void Dispose()
        {
            State.FailRestore = false;
            Adapter.Dispose();
            Flush();
            Manager.Dispose();
            Lang.ChangeLanguage(previousLocale);
            Lang.AvailableLanguages.Remove(locale);
        }
    }
}
