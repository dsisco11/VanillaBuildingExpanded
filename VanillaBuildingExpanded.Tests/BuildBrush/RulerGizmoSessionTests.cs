using System.Runtime.Intrinsics;

using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

[Collection("Table composition")]
public sealed class RulerGizmoSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedBothEndpointDrags_PreviewCrossingAlwaysNormalizesBothCorners(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        f.H.Enable();
        f.H.Frame();
        f.H.Target = new(0, 65, -200, 0);
        Assert.True(f.H.Down(100, 100).Handled);
        Assert.Null(f.H.Session.Manager.CapturedGizmo);
        f.H.Up(100, 100);
        Assert.Equal(1, f.H.Commits);
        Assert.Equal(f.H.Ruler.State.Start, f.H.Ruler.State.End);
        Assert.Equal(2, f.H.Session.Manager.Count);
        Assert.Equal((1L, 1L, 1L), f.H.Size);
        f.H.Ruler.RestoreEndpoints(new(-30, 65, -200, 0), new(30, 65, -200, 0));
        for (int i = 0; i < 12; i++)
        {
            bool start = i % 2 == 0;
            f.H.Frame();
            var instance = start ? f.H.Session.Start! : f.H.Session.End!;
            var baseline = instance.Value;
            var opposite = (start ? f.H.Ruler.State.End : f.H.Ruler.State.Start)!.Value;
            f.H.Aim(instance.Value.Coordinates);
            Assert.True(f.H.Down().Handled);
            Assert.Same(instance, f.H.Session.Manager.CapturedGizmo);
            f.H.Target = new(start ? 80 + i : -80 - i, 65 + i % 3, -180 - i, 0);
            f.H.Frame();
            var expected = new RulerBounds(f.H.Target.Value, opposite);
            Assert.Equal(expected.MinCell, f.H.Ruler.State.Start);
            Assert.Equal(expected.MaxCell, f.H.Ruler.State.End);
            Assert.Same(instance, f.H.Session.Manager.CapturedGizmo);
            f.H.AssertLiveBox();
            Assert.Equal(i + 1, f.H.Commits);
            Assert.NotEqual(baseline, instance.Value);
            Assert.True(f.H.Up().Handled);
            Assert.False(f.H.Ruler.IsEditing);
            Assert.Equal(i + 2, f.H.Commits);
            f.H.AssertLiveBox();
            Assert.Equal(RulerGizmoCoordinates.ToAnchor(start ? expected.MinCell : expected.MaxCell,
                start ? RulerEndpointRole.Start : RulerEndpointRole.End), instance.Value);
            Assert.Equal(RulerGizmoCoordinates.ToAnchor(expected.MinCell, RulerEndpointRole.Start),
                f.H.Session.Start!.Value);
            Assert.Equal(RulerGizmoCoordinates.ToAnchor(expected.MaxCell, RulerEndpointRole.End),
                f.H.Session.End!.Value);
        }
        Assert.Empty(f.H.Network.Invocations);
    }

    [Fact]
    public void CoincidentProjectedCenters_ChooseNearestDeterministicallyAndKeepDistinctArtwork()
    {
        using var f = new Fixture();
        var h = f.H;
        var cell = new RulerEndpoint(0, 65, -200, 0);
        h.Ruler.RestoreEndpoints(cell, cell);
        h.Enable();
        for (int i = 0; i < 3; i++)
        {
            h.Frame();
            h.Down();
            Assert.Same(h.Session.End, h.Session.Manager.CapturedGizmo);
            h.Down(button: EnumMouseButton.Right);
            h.Up(button: EnumMouseButton.Right);
            h.Up();
        }
        Assert.NotEqual(h.Session.Start!.Value, h.Session.End!.Value);
        Assert.Equal(3, h.Meshes.Count);
        Assert.Equal(0, h.Commits);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CrossingAndReturning_KeepsCaptureAndBothCanonicalAnchorsThenRollsBack(bool grabbed, bool start)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        var first = new RulerEndpoint(-30, 65, -200, 0);
        var last = new RulerEndpoint(30, 75, -180, 0);
        h.Ruler.RestoreEndpoints(first, last);
        h.Enable();
        h.Frame();
        var instance = start ? h.Session.Start! : h.Session.End!;
        h.Aim(instance.Value.Coordinates);
        h.Down();
        foreach (var target in new[] { new RulerEndpoint(start ? 70 : -70, start ? 80 : 60, -220, 0),
            start ? first : last, new(-10, 70, -190, 0) })
        {
            h.Target = target;
            h.Frame();
            h.Session.Refresh();
            Assert.Same(instance, h.Session.Manager.CapturedGizmo);
            var expected = new RulerBounds(target, start ? last : first);
            Assert.Equal(expected.MinCell, h.Ruler.State.Start);
            Assert.Equal(expected.MaxCell, h.Ruler.State.End);
            Assert.Equal(RulerGizmoCoordinates.ToAnchor(expected.MinCell, RulerEndpointRole.Start), h.Session.Start!.Value);
            Assert.Equal(RulerGizmoCoordinates.ToAnchor(expected.MaxCell, RulerEndpointRole.End), h.Session.End!.Value);
            h.AssertLiveBox();
            Assert.Equal(0, h.Commits);
        }
        h.Key();
        h.Up();
        Assert.Equal(first, h.Ruler.State.Start);
        Assert.Equal(last, h.Ruler.State.End);
        Assert.Equal(0, h.Commits);
        h.AssertLiveBox();
    }

    [Fact]
    public void AxisDrag_UsesActualAdapterAndSnapsGrabOffsetWithNoTerrainQuery()
    {
        using var f = new Fixture();
        var h = f.H;
        h.Ruler.RestoreEndpoints(new(0, 65, -20, 0), new(10, 65, -20, 0));
        h.Enable();
        h.Frame();
        var anchor = h.Session.Start!.Value.Coordinates;
        var geometry = PositionGizmoGeometry.Create(h.Session.Renderer.Context!.Camera!, anchor, GizmoShape.Circle)!;
        h.Aim(anchor + Vector256.Create(geometry.AxisLength * 0.6, 0, 0, 0));
        h.Down();
        Assert.Equal(PositionGizmo.X, h.Session.Manager.CapturedHandle);
        int originalX = h.X;
        h.X += 17;
        h.Up();
        Assert.Equal(new RulerEndpoint(1, 65, -20, 0), h.Ruler.State.Start);
        Assert.Equal(1, h.Commits);
        Assert.Null(h.LastRay);
        Assert.True(h.X > originalX);
    }

    [Fact]
    public void EndBoundaryAnchor_DecodesLocalYWithoutChangingDimensionAtNativeBoundary()
    {
        using var f = new Fixture();
        var h = f.H;
        h.Player.Entity.Pos.Dimension = 65535;
        h.Reference = Vector256.Create(0d, 32767d, 0d, 0d);
        var first = new RulerEndpoint(-20, 32767, -200, 65535);
        h.Ruler.RestoreEndpoints(first, new(20, 32767, -200, 65535));
        h.Enable();
        h.Frame();
        Assert.Equal(32768, h.Session.End!.Value.Coordinates[1]);
        h.Aim(h.Session.End.Value.Coordinates);
        h.Down();
        Assert.Same(h.Session.End, h.Session.Manager.CapturedGizmo);
        h.Target = new(25, 32767, -220, 65535);
        h.Frame();
        h.Up();
        var expected = new RulerBounds(first, h.Target.Value);
        Assert.Equal(expected.MaxCell, h.Ruler.State.End);
        Assert.Equal(expected.MinCell, h.Ruler.State.Start);
        Assert.Equal(1, h.Commits);
        Assert.True(h.LastRay!.origin.Y > 2147400000);
    }

    [Fact]
    public void CancelThenRetry_UsesFreshPressAndCommitsOnlyTheRetry()
    {
        using var f = new Fixture();
        var h = f.H;
        var baseline = new RulerEndpoint(-30, 65, -200, 0);
        h.Ruler.RestoreEndpoints(baseline, new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.Target = new(-70, 70, -250, 0);
        h.Frame();
        h.AssertLiveBox();
        h.Key();
        h.Up();
        Assert.Equal(baseline, h.Ruler.State.Start);
        Assert.Equal(0, h.Commits);
        h.AssertLiveBox();
        h.Frame();
        h.Aim(h.Session.Start.Value.Coordinates);
        h.Down();
        Assert.Same(h.Session.Start, h.Session.Manager.CapturedGizmo);
        h.Frame();
        h.Up();
        Assert.Equal(new RulerBounds(h.Target!.Value, new(30, 65, -200, 0)).MinCell, h.Ruler.State.Start);
        Assert.Equal(1, h.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderCallbackRetirement_DefersBoxMembershipAndMeshDisposalAlongsideGizmos(bool dispose)
    {
        using var f = new Fixture();
        var h = f.H;
        h.Ruler.RestoreEndpoints(new(-30, 65, -200, 0), new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.Target = new(-70, 70, -250, 0);
        h.Frame();
        var active = h.Active.ToArray();
        if (dispose)
            h.Session.Renderer.ContextReady += _ =>
            {
                h.Session.Dispose();
                h.Box.Dispose();
                Assert.Equal(active, h.Active);
                Assert.All(h.Meshes, mesh => Assert.Equal(0, mesh.Disposals));
            };
        else h.Player.Entity.Alive = false;
        h.Session.Renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        Assert.Equal(active, h.Active);
        Assert.False(h.Ruler.IsEditing);
        Assert.Null(h.Session.Renderer.Context);
        h.RunPending();
        Assert.Empty(h.Active);
        Assert.Equal(new RulerEndpoint(-30, 65, -200, 0), h.Ruler.State.Start);
        Assert.Equal(0, h.Commits);
    }

    [Theory]
    [InlineData("right")]
    [InlineData("escape")]
    [InlineData("focus")]
    [InlineData("dialog")]
    [InlineData("mode")]
    [InlineData("death")]
    [InlineData("dimension")]
    [InlineData("hidden")]
    [InlineData("camera")]
    [InlineData("world")]
    [InlineData("departure")]
    [InlineData("render")]
    [InlineData("input")]
    public void Cancellation_RollsBackBeforeRetirementWithoutCommitAndDetachesOnDispose(string reason)
    {
        using var f = new Fixture();
        var h = f.H;
        h.Ruler.RestoreEndpoints(new(0, 65, -200, 0), new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        var baseline = h.Ruler.State;
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.Target = new(-50, 70, -250, 0);
        h.Frame();
        Assert.NotEqual(baseline.Start, h.Ruler.State.Start);
        switch (reason)
        {
            case "right": h.Down(button: EnumMouseButton.Right); break;
            case "escape": h.Key(); break;
            case "focus": h.Focus.ForEach(callback => callback(false)); break;
            case "dialog": h.Dialogs.Add(new TestDialog(h.Api.Object)); break;
            case "mode": h.Ruler.SetContext(false, true); break;
            case "death": h.Player.Entity.Alive = false; break;
            case "dimension": h.Player.Entity.Pos.Dimension++; break;
            case "hidden": h.Ruler.SetVisibility(false); break;
            case "camera": h.Grabbed = true; break;
            case "world": h.WorldAvailable = false; break;
            case "departure": h.Events.Raise(e => e.LeaveWorld += null!); break;
            case "render": h.Shader.SetupGet(s => s.LoadError).Returns(true); h.Frame(); break;
            case "input": h.ThrowTarget = true; h.Frame(); break;
        }
        if (reason != "departure") h.Session.Refresh();
        Assert.False(h.Ruler.IsEditing);
        Assert.Null(h.Session.Manager.CapturedGizmo);
        Assert.Equal((baseline.Start, baseline.End), (h.Ruler.State.Start, h.Ruler.State.End));
        Assert.Equal(0, h.Commits);
        h.Session.Dispose();
        h.Session.Dispose();
        h.RunPending();
        Assert.Empty(h.Focus);
        Assert.DoesNotContain(h.Active, pair => ReferenceEquals(pair.Item1, h.Session.Renderer));
        Assert.All(h.Meshes, mesh => Assert.Equal(1, mesh.Disposals));
        h.Frame();
        Assert.Equal(0, h.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialCreation_HandlePrecedenceAndExplicitCancellationAndDimensionIsolation(bool hasStart)
    {
        using var f = new Fixture();
        var h = f.H;
        var existing = new RulerEndpoint(0, 65, -200, 0);
        h.Ruler.RestoreEndpoints(hasStart ? existing : null, hasStart ? null : existing);
        h.Enable();
        h.Frame();
        Assert.True(h.Session.IsPending);
        Assert.Equal(1, h.Session.Manager.Count);
        Assert.DoesNotContain(h.Active, pair => !ReferenceEquals(pair.Item1, h.Session.Renderer));
        var instance = hasStart ? h.Session.Start! : h.Session.End!;
        h.Aim(instance.Value.Coordinates);
        h.Down();
        Assert.Same(instance, h.Session.Manager.CapturedGizmo);
        h.Down(button: EnumMouseButton.Right);
        h.Up(button: EnumMouseButton.Right);
        h.Up();
        Assert.False(h.Session.IsPending);
        h.Frame();
        Assert.False(h.Session.IsPending);
        h.Ruler.SetVisibility(false);
        h.Ruler.SetVisibility(true);
        h.Frame();
        Assert.True(h.Session.IsPending);
        h.Target = null;
        Assert.True(h.Down(20, 20).Handled);
        h.Up(20, 20);
        Assert.True(h.Session.IsPending);
        Assert.Equal(0, h.Commits);
        h.Target = new(-10, 70, -300, 0);
        h.Down(20, 20);
        h.Up(20, 20);
        var expected = new RulerBounds(existing, h.Target.Value);
        Assert.Equal(expected.MinCell, h.Ruler.State.Start);
        Assert.Equal(expected.MaxCell, h.Ruler.State.End);
        Assert.Equal(1, h.Commits);
        h.Player.Entity.Pos.Dimension = 2;
        h.Session.Refresh();
        h.Frame();
        Assert.False(h.Session.IsPending);
        Assert.False(h.Session.Start!.Visible);
        Assert.False(h.Session.End!.Visible);
        h.Ruler.SetVisibility(false);
        h.Ruler.SetVisibility(true);
        h.Frame();
        Assert.False(h.Down(20, 20).Handled);
        Assert.Equal(1, h.Commits);
        Assert.Equal(0, h.Ruler.State.Start!.Value.Dimension);
    }

    [Fact]
    public void ContextualCreationRetirement_ReoffersOnlyAfterFreshContextAndReleasedButtons()
    {
        using var f = new Fixture();
        var h = f.H;
        h.Enable();
        h.Frame();
        Assert.True(h.Session.IsPending);
        h.Key();
        h.Frame();
        Assert.False(h.Session.IsPending);
        h.Ruler.SetContext(false, true);
        h.Ruler.SetContext(true, true);
        h.Frame();
        Assert.True(h.Session.IsPending);
        h.Focus.ForEach(callback => callback(false));
        Assert.False(h.Session.IsPending);
        h.Raw.Left = true;
        h.Focus.ForEach(callback => callback(true));
        h.Frame();
        Assert.False(h.Session.IsPending);
        Assert.False(h.Down().Handled);
        h.Up();
        h.Frame();
        Assert.True(h.Session.IsPending);
    }

    [Fact]
    public void ClientRouter_SuppressesBothActionFamiliesBeforeBrushAndRetiresOnFreshPress()
    {
        using var f = new Fixture();
        var h = f.H;
        var owner = new BuildBrushSystem_Client();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic;
        typeof(BuildBrushSystem_Client).GetField("rulerGizmos", flags)!.SetValue(owner, h.Session);
        var route = typeof(BuildBrushSystem_Client).GetMethod("InWorldAction", flags)!;
        h.Enable();
        h.Frame();
        h.Target = new(0, 65, -200, 0);
        h.Down();
        foreach (bool on in new[] { true, false })
        foreach (var action in new[] { EnumEntityAction.LeftMouseDown, EnumEntityAction.InWorldLeftMouseDown })
        {
            object[] args = [action, on, EnumHandling.PassThrough];
            route.Invoke(owner, args);
            Assert.Equal(EnumHandling.PreventSubsequent, args[2]);
        }
        h.Up();
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.Down(button: EnumMouseButton.Right);
        h.Up(button: EnumMouseButton.Right);
        h.Up();
        foreach (var action in new[] { EnumEntityAction.LeftMouseDown, EnumEntityAction.InWorldLeftMouseDown,
            EnumEntityAction.RightMouseDown, EnumEntityAction.InWorldRightMouseDown })
        {
            object[] args = [action, true, EnumHandling.PassThrough];
            route.Invoke(owner, args);
            Assert.Equal(EnumHandling.PreventSubsequent, args[2]);
        }
        h.Down(20, 20);
        object[] ordinary = [EnumEntityAction.InWorldLeftMouseDown, true, EnumHandling.PassThrough];
        route.Invoke(owner, ordinary);
        Assert.Equal(EnumHandling.PassThrough, ordinary[2]);
        Assert.Empty(h.Network.Invocations);
    }

    [Fact]
    public void AuthoritativeReplacement_CancelsBeforeAssigningAndEqualEchoKeepsTransaction()
    {
        using var f = new Fixture();
        var h = f.H;
        h.Ruler.RestoreEndpoints(new(0, 65, -200, 0), new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        var start = h.Session.Start!;
        int notifications = 0;
        start.OnValueChanged += _ => notifications++;
        h.Aim(start.Value.Coordinates);
        h.Down();
        h.Target = new(-40, 60, -220, 0);
        h.Frame();
        h.Ruler.RestoreEndpoints(h.Ruler.State.Start, h.Ruler.State.End);
        Assert.Same(start, h.Session.Manager.CapturedGizmo);
        Assert.True(h.Ruler.IsEditing);
        Assert.Equal(1, notifications);
        var replacement = new RulerEndpoint(-50, 60, -300, 0);
        h.Ruler.RestoreEndpoints(replacement, h.Ruler.State.End);
        Assert.Null(h.Session.Manager.CapturedGizmo);
        Assert.False(h.Ruler.IsEditing);
        Assert.Equal(replacement, h.Ruler.State.Start);
        Assert.Equal(RulerGizmoCoordinates.ToAnchor(replacement, RulerEndpointRole.Start), start.Value);
        Assert.Equal(3, notifications);
        h.Up();
        Assert.Equal(0, h.Commits);
    }

    [Fact]
    public void TerrainResolver_UsesFiniteVisualSegmentAndPreservesOcclusionClipAndLoadedChecks()
    {
        using var f = new Fixture();
        var h = f.H;
        var context = h.Capture();
        var pointer = context.Camera!.CreatePointer(context, 100, 150);
        var target = new RulerTerrainTarget(h.Api.Object);
        h.Target = new(-20, 65, -200, 0);
        Assert.Equal(h.Target, target.Resolve(pointer));
        var ray = h.LastRay!;
        Assert.Equal(pointer.Ray.Origin[0], ray.origin.X, 10);
        Assert.Equal(pointer.FarPoint!.Value[2], ray.origin.Z + ray.dir.Z, 8);
        Assert.True(ray.Length > 1000);
        Assert.False(h.EntityFilter!(h.Player.Entity));
        Assert.True(h.EntityFilter(new EntityPlayer { EntityId = 999 }));
        Assert.False(h.EntityFilter(new NonInteractableEntity { EntityId = 999 }));
        var position = new BlockPos(0, 65, -200, 0);
        Assert.True(h.BlockFilter!(position, new Block { RenderPass = EnumChunkRenderPass.Opaque }));
        Assert.False(h.BlockFilter(position, new Block { RenderPass = EnumChunkRenderPass.Meta }));
        Assert.True(h.BlockFilter(position, new SelectableMetaBlock { RenderPass = EnumChunkRenderPass.Meta }));
        h.Api.Setup(a => a.Settings.Bool["renderMetaBlocks"]).Returns(true);
        target.Resolve(pointer);
        Assert.True(h.BlockFilter!(position, new Block { RenderPass = EnumChunkRenderPass.Meta }));
        h.EntityOcclusion = true;
        Assert.Null(target.Resolve(pointer));
        h.EntityOcclusion = false;
        h.Loaded = false;
        Assert.Null(target.Resolve(pointer));
        h.Loaded = true;
        h.Hit = new(0, 0, -1001);
        Assert.Null(target.Resolve(pointer));
        h.Hit = new(double.NaN, 0, 0);
        Assert.Null(target.Resolve(pointer));
        h.Hit = new(0, 0, 0);
        h.Target = new(0, 65, -1000, 0);
        Assert.Equal(h.Target, target.Resolve(pointer));
        h.Target = new(0, 65, 1, 0);
        Assert.Null(target.Resolve(pointer));
    }

    private sealed class TestDialog(ICoreClientAPI api) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => "";
    }

    private sealed class NonInteractableEntity : Entity
    {
        public override bool IsInteractable => false;
    }

    private sealed class SelectableMetaBlock : Block, IMetaBlock
    {
        public bool IsSelectable(BlockPos pos) => true;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string oldLocale = Lang.CurrentLocale;
        private readonly string locale = $"vbe-ruler-gizmos-{Guid.NewGuid():N}";
        public Harness H { get; }
        public Fixture(bool grabbed = false)
        {
            var translations = new Mock<ITranslationService>();
            translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns(true);
            translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("ruler notice");
            Lang.AvailableLanguages.Add(locale, translations.Object);
            Lang.ChangeLanguage(locale);
            H = new(TestHelpers.CreateMockCapi(), new(), new(), new(), grabbed);
        }
        public void Dispose()
        {
            H.Dispose();
            Lang.ChangeLanguage(oldLocale);
            Lang.AvailableLanguages.Remove(locale);
        }
    }

    internal sealed class Harness : IDisposable
    {
        public readonly Mock<ICoreClientAPI> Api;
        public readonly Mock<IClientWorldAccessor> World;
        public readonly Mock<IBlockAccessor> Accessor;
        public readonly RulerController Ruler;
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly Mock<IInputAPI> Input = new();
        public readonly Mock<IRenderAPI> Render = new();
        public readonly Mock<IShaderProgram> Shader = new();
        public readonly Mock<IClientNetworkAPI> Network = new(MockBehavior.Strict);
        public readonly ClientPlayer Player = RulerRenderingTests.CreatePlayer();
        public readonly MouseButtonState Raw = new(), WorldButtons = new();
        public readonly List<Action<bool>> Focus = [];
        public readonly List<GuiDialog> Dialogs = [];
        public readonly List<(IRenderer, EnumRenderStage)> Active = [];
        public readonly List<TestMesh> Meshes = [];
        public readonly List<Action> Pending = [];
        public readonly RulerGizmoSession Session;
        public readonly RulerRenderBinding Box;
        public bool Grabbed, WorldAvailable = true, Loaded = true, EntityOcclusion, ThrowTarget;
        public RulerEndpoint? Target;
        public Vec3d Hit = new(0, 0, 0);
        public Ray? LastRay;
        public float[]? LastModel;
        public EntityFilter? EntityFilter;
        public BlockFilter? BlockFilter;
        public Vector256<double> Reference = Vector256.Create(0d, 65d, 0d, 0d);
        public int X = 400, Y = 300, Commits;
        public (long, long, long) Size => Ruler.State.Bounds is { } b ? (b.SizeX, b.SizeY, b.SizeZ) : default;

        public Harness(Mock<ICoreClientAPI> api, Mock<IClientWorldAccessor> world,
            Mock<IBlockAccessor> accessor, RulerController ruler, bool grabbed = false)
        {
            Api = api; World = world; Accessor = accessor; Ruler = ruler; Grabbed = grabbed;
            Player.OverrideCameraMode = EnumCameraMode.FirstPerson;
            Api.Setup(a => a.World).Returns(() => WorldAvailable ? world.Object : null!);
            world.Setup(w => w.Player).Returns(Player);
            world.Setup(w => w.BlockAccessor).Returns(accessor.Object);
            accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(true);
            var chunk = new Mock<IWorldChunk>();
            accessor.Setup(a => a.GetChunkAtBlockPos(It.IsAny<BlockPos>())).Returns(() => Loaded ? chunk.Object : null!);
            world.Setup(w => w.RayTraceForSelection(It.IsAny<Ray>(), ref It.Ref<BlockSelection?>.IsAny,
                ref It.Ref<EntitySelection?>.IsAny, It.IsAny<BlockFilter>(), It.IsAny<EntityFilter>()))
                .Callback((Ray ray, ref BlockSelection? block, ref EntitySelection? entity,
                    BlockFilter bfilter, EntityFilter efilter) =>
                {
                    if (ThrowTarget) throw new InvalidOperationException("target failure");
                    LastRay = ray;
                    EntityFilter = efilter;
                    BlockFilter = bfilter;
                    entity = EntityOcclusion ? new EntitySelection() : null!;
                    block = Target is { } cell ? new BlockSelection
                    {
                        Position = new(cell.X, cell.Y, cell.Z, cell.Dimension), HitPosition = Hit.Clone()
                    } : null!;
                });
            Api.Setup(a => a.Event).Returns(Events.Object);
            Api.Setup(a => a.Input).Returns(Input.Object);
            Api.Setup(a => a.Render).Returns(Render.Object);
            Api.Setup(a => a.Network).Returns(Network.Object);
            Api.Setup(a => a.IsGamePaused).Returns(false);
            Api.Setup(a => a.Settings.Bool["renderMetaBlocks"]).Returns(false);
            Api.Setup(a => a.Gui.OpenedGuis).Returns(Dialogs);
            Input.Setup(i => i.MouseGrabbed).Returns(() => Grabbed);
            Input.Setup(i => i.MouseButton).Returns(Raw);
            Input.Setup(i => i.InWorldMouseButton).Returns(WorldButtons);
            Input.Setup(i => i.KeyboardKeyStateRaw).Returns(new bool[256]);
            Render.Setup(r => r.FrameWidth).Returns(800);
            Render.Setup(r => r.FrameHeight).Returns(600);
            Render.Setup(r => r.CameraMatrixOriginf).Returns(new Matrixf().Values);
            Api.Setup(a => a.Shader.GetProgram((int)EnumShaderProgram.Wireframe)).Returns(Shader.Object);
            Shader.Setup(s => s.UniformMatrix("modelViewMatrix", It.IsAny<float[]>()))
                .Callback<string, float[]>((_, matrix) => LastModel = (float[])matrix.Clone());
            Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
            {
                var mesh = new TestMesh(); Meshes.Add(mesh); return mesh;
            });
            Events.Setup(e => e.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), It.IsAny<string>()))
                .Callback<IRenderer, EnumRenderStage, string>((renderer, stage, _) =>
                {
                    Assert.DoesNotContain((renderer, stage), Active);
                    Active.Add((renderer, stage));
                });
            Events.Setup(e => e.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()))
                .Callback<IRenderer, EnumRenderStage>((renderer, stage) => Assert.True(Active.Remove((renderer, stage))));
            Events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => Pending.Add(action));
            Ruler.EndpointsCommitted += OnCommit;
            Box = new(api.Object, ruler, new RulerRenderingTests.FakeState(),
                () => Session?.Renderer.IsInCallback == true);
            Session = new(api.Object, ruler, Focus, Capture, new RulerRenderingTests.FakeState());
        }

        public GizmoContext Capture() => new(Player.Entity.Pos.Dimension,
            new GizmoCamera(Mat4d.Create(), Mat4d.Perspective(new double[16], Math.PI / 2, 4d / 3, 0.1, 1000),
                Reference, 800, 600, mouseGrabbed: Grabbed, cameraMode: Player.CameraMode));
        public void Enable() { Ruler.SetContext(true, true); Ruler.SetVisibility(true); }
        public void Frame()
        {
            Session.Renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
            Session.Renderer.OnRenderFrame(0, EnumRenderStage.AfterBlit);
            RunPending();
        }
        public void RunPending()
        {
            foreach (var action in Pending.ToArray()) { Pending.Remove(action); action(); }
        }
        public void AssertLiveBox()
        {
            var start = Ruler.State.Start!.Value;
            var end = Ruler.State.End!.Value;
            var expectedSize = (Math.Abs((long)start.X - end.X) + 1,
                Math.Abs((long)start.Y - end.Y) + 1, Math.Abs((long)start.Z - end.Z) + 1);
            Assert.Equal(expectedSize, Size);
            Player.Entity.CameraPos.Set(Reference[0],
                Reference[1] + (double)Player.Entity.Pos.Dimension * BlockPos.DimensionBoundary, Reference[2]);
            LastModel = null;
            var box = Assert.Single(Active, pair => pair.Item1 is RulerRenderer
                && pair.Item2 == EnumRenderStage.Opaque).Item1;
            box.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.NotNull(LastModel);
            double expansion = RulerRenderer.VisualExpansion;
            Assert.Equal((float)(expectedSize.Item1 + 2 * expansion), LastModel[0]);
            Assert.Equal((float)(expectedSize.Item2 + 2 * expansion), LastModel[5]);
            Assert.Equal((float)(expectedSize.Item3 + 2 * expansion), LastModel[10]);
            Assert.Equal((float)(Math.Min(start.X, end.X) - Reference[0] - expansion), LastModel[12]);
            Assert.Equal((float)(Math.Min(start.Y, end.Y) - Reference[1] - expansion), LastModel[13]);
            Assert.Equal((float)(Math.Min(start.Z, end.Z) - Reference[2] - expansion), LastModel[14]);
        }
        public void Aim(Vector256<double> point)
        {
            if (Grabbed)
            {
                Reference = Vector256.Create(point[0], point[1], point[2] + 200, 0d);
                Frame();
                X = 400; Y = 300;
            }
            else
            {
                Assert.True(Session.Renderer.Context!.Camera!.TryProject(point, out var screen));
                X = (int)Math.Round(screen[0]); Y = (int)Math.Round(screen[1]);
            }
        }
        public MouseEvent Down(int? x = null, int? y = null, EnumMouseButton button = EnumMouseButton.Left)
        {
            if (button == EnumMouseButton.Left) Raw.Left = true; else Raw.Right = true;
            var e = new MouseEvent(x ?? X, y ?? Y, button, 0);
            Events.Raise(ev => ev.MouseDown += null!, e);
            return e;
        }
        public MouseEvent Up(int? x = null, int? y = null, EnumMouseButton button = EnumMouseButton.Left)
        {
            if (button == EnumMouseButton.Left) Raw.Left = false; else Raw.Right = false;
            var e = new MouseEvent(x ?? X, y ?? Y, button, 0);
            Events.Raise(ev => ev.MouseUp += null!, e);
            return e;
        }
        public void Key() => Events.Raise(e => e.KeyDown += null!, new KeyEvent { KeyCode = (int)GlKeys.Escape });
        private void OnCommit() => Commits++;
        public void Dispose()
        {
            Session.Dispose(); Box.Dispose(); RunPending();
            Ruler.EndpointsCommitted -= OnCommit;
        }
    }

    internal sealed class TestMesh : MeshRef
    {
        public int Disposals;
        public override bool Initialized => true;
        public override void Dispose() { Disposals++; base.Dispose(); }
    }
}
