using System.Numerics;
using System.Reflection;

using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

[Collection("Native snapping capture")]
public sealed class BrushSnappingRenderBindingTests
{
    [Fact]
    public void NativeCaptureRequestDoesNotInvalidateItselfThroughSynchronousBlockChanged()
    {
        using var f = new Fixture(captureNative: true);
        f.Geometry.Blocks.Setup(b => b.MarkBlockDirty(It.IsAny<BlockPos>(), (IPlayer)null!))
            .Callback<BlockPos, IPlayer>((position, _) =>
                f.Events.Raise(e => e.BlockChanged += null!, position, (Block)null!));
        f.Draw();
        f.Draw();
        Assert.Empty(f.Pending);
        f.Geometry.Blocks.Verify(b => b.MarkBlockDirty(It.IsAny<BlockPos>(), (IPlayer)null!), Times.Once);
        f.Events.Raise(e => e.BlockChanged += null!, f.Geometry.Position, f.Geometry.Block);
        f.Draw();
        f.Draw();
        Assert.Empty(f.Pending);
        f.Geometry.Blocks.Verify(b => b.MarkBlockDirty(It.IsAny<BlockPos>(), (IPlayer)null!), Times.Exactly(2));
    }

    [Fact]
    public void NativeVerticalSlab_ReachesShaderDrawingAndRefreshesCapturedSnowGeometry()
    {
        using var f = new Fixture();
        Block slab = f.Geometry.UseNativeSlab();
        slab.BlockId = 20;
        f.Selection!.Block = slab;
        f.Resolve();
        int validations = f.ValidationCount;
        f.Draw();
        Assert.Equal(1, f.Draws);
        Assert.Equal(1, f.Geometry.Uploads);
        f.Program.Verify(p => p.Compile(), Times.Once);
        f.Geometry.Blocks.Setup(x => x.GetBlock(f.Geometry.Position.AddCopy(BlockFacing.SOUTH)))
            .Returns(new Block { BlockMaterial = EnumBlockMaterial.Snow });
        f.Geometry.Borrowed = BrushTargetMeshTests.CreateCuboid(System.Numerics.Vector3.Zero, System.Numerics.Vector3.One);
        f.Draw();
        Assert.Equal(2, f.Draws);
        f.Geometry.Blocks.Setup(x => x.GetBlock(f.Geometry.Position.AddCopy(BlockFacing.SOUTH))).Returns(new Block());
        f.Draw();
        Assert.Equal(3, f.Draws);
        Assert.Equal(2, f.Geometry.Uploads);
        Assert.Equal(validations, f.ValidationCount);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("face")]
    [InlineData("hit")]
    [InlineData("mode")]
    [InlineData("item")]
    [InlineData("attributes")]
    [InlineData("slot")]
    [InlineData("count")]
    [InlineData("lost")]
    [InlineData("dirty")]
    [InlineData("disabled")]
    [InlineData("death")]
    [InlineData("world")]
    [InlineData("dimension")]
    public void LiveContextChangesSuppressImmediatelyWithoutDrawingOrValidatingStaleResults(string change)
    {
        using var f = new Fixture();
        f.Draw();
        Assert.Equal(1, f.Draws);
        int validations = f.ValidationCount;
        switch (change)
        {
            case "target": f.Selection!.Position.X++; break;
            case "face": f.Selection!.Face = BlockFacing.SOUTH; break;
            case "hit": f.Selection!.HitPosition.X = 0.1; break;
            case "mode": f.Brush.Snapping = EBuildBrushSnapping.Vertical; break;
            case "item": f.Slot.Itemstack = new ItemStack(new Item { ItemId = 20 }); break;
            case "attributes": f.Slot.Itemstack!.Attributes.SetString("lining", "new"); break;
            case "slot": f.SlotNumber++; break;
            case "count": f.Slot.Itemstack!.StackSize = 0; break;
            case "lost": f.Selection = null; break;
            case "dirty": f.Brush.MarkDirty(); break;
            case "disabled": f.Client.SetBuildMode(false, null); break;
            case "death": f.Entity.Alive = false; break;
            case "world": f.Geometry.Api.SetupGet(a => a.World).Returns(new Mock<IClientWorldAccessor>().Object); break;
            case "dimension": f.Entity.Pos.Dimension = 1; break;
        }
        f.Draw();
        f.Draw();
        Assert.Equal(1, f.Draws);
        Assert.Equal(validations, f.ValidationCount);
        Assert.Equal(1, f.Geometry.Uploads);
        Assert.Equal(0, f.Geometry.Uploaded[0].DisposeCount);
    }

    [Fact]
    public void ResolvedTargetsAndIndependentFaceDirectionsRefreshWithoutRebuildingGeometry()
    {
        using var f = new Fixture();
        for (int i = 0; i < 12; i++)
        {
            f.Selection = f.CreateSelection();
            f.Selection.Position.X += i;
            f.Selection.Face = BlockFacing.ALLFACES[i % 6];
            f.Selection.HitPosition = f.Selection.Face.Axis switch
            {
                EnumAxis.X => new Vec3d(f.Selection.Face.Negative ? 0 : 1, 0.9, 0.1),
                EnumAxis.Y => new Vec3d(0.9, f.Selection.Face.Negative ? 0 : 1, 0.1),
                _ => new Vec3d(0.9, 0.1, f.Selection.Face.Negative ? 0 : 1)
            };
            f.Brush.Snapping = EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical;
            int before = f.Draws;
            if (i != 0) { f.Draw(); Assert.Equal(before, f.Draws); }
            f.Resolve();
            int validations = f.ValidationCount;
            f.Draw();
            Assert.Equal(before + 1, f.Draws);
            Assert.Equal(validations, f.ValidationCount);
            Assert.NotEqual(f.Selection.Position, f.Brush.Position);
            var expected = new Matrixf().Translate(f.Selection.Position.X, f.Selection.Position.Y, f.Selection.Position.Z);
            Assert.Equal(expected.Values, f.LastModel);
        }
        Assert.Equal(1, f.Geometry.Uploads);
        Assert.Single(f.Active);
    }

    [Fact]
    public void SmoothedSelectionRendersEveryFrameBetweenPlacementUpdatesWithoutValidationOrReupload()
    {
        using var f = new Fixture();
        int validations = f.ValidationCount;
        for (int frame = 0; frame < 120; frame++)
        {
            double bob = 0.04 * Math.Sin((frame + 1) * 0.8) * Math.Exp(-frame / 30.0);
            f.Entity.CameraPos.Y = bob;
            f.Selection!.HitPosition.X = 0.9 + bob / 2;
            f.Selection.HitPosition.Y = 0.5 + bob;
            f.Draw();
            Assert.Equal(frame + 1, f.Draws);
            Assert.Equal(validations, f.ValidationCount);
            Assert.Equal(1, f.Geometry.Uploads);
            Assert.Equal((float)(f.Selection.Position.Y - bob), f.LastModel![13]);
        }
        f.Selection!.HitPosition.X = 0.1;
        f.Draw();
        Assert.Equal(120, f.Draws);
        f.Selection.HitPosition.X = 0.9;
        f.Draw();
        Assert.Equal(121, f.Draws);
        Assert.Equal(validations, f.ValidationCount);
    }

    [Fact]
    public void SuspensionResumptionAndModeTogglesRetainValidGeometryButNotOutcomes()
    {
        using var f = new Fixture();
        f.Draw();
        for (int i = 0; i < 3; i++)
        {
            int before = f.Draws;
            f.Slot.Itemstack = new ItemStack(new Item { ItemId = 20 });
            f.Resolve();
            f.Draw();
            Assert.Equal(before, f.Draws);
            f.Slot.Itemstack = new ItemStack(f.Geometry.Block);
            f.Draw();
            Assert.Equal(before, f.Draws);
            f.Resolve();
            f.Draw();
            Assert.Equal(++before, f.Draws);
            f.Client.SetBuildMode(false, null);
            f.Draw();
            Assert.Equal(before, f.Draws);
            f.Client.SetBuildMode(true, f.Selection);
            f.Draw();
            Assert.Equal(before + 1, f.Draws);
        }
        Assert.Equal(1, f.Geometry.Uploads);
        Assert.Equal(0, f.Geometry.Uploaded[0].DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidityChangesDrawResolvedSnappingInRedOrCyanWithoutValidationOrReupload(bool initiallyValid)
    {
        using var f = new Fixture();
        bool valid = initiallyValid;
        f.Geometry.Inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(),
            ref It.Ref<string>.IsAny)).Returns(() => valid);
        var colors = new List<(float R, float G, float B, float A)>();
        f.Program.SetupSet(p => p.ColorIn = It.IsAny<Vector4>())
            .Callback<Vector4>(color => colors.Add((color.X, color.Y, color.Z, color.W)));
        f.Selection!.HitPosition.X = 0.1;
        f.Resolve();
        for (int phase = 0; phase < 3; phase++)
        {
            if (phase != 0)
            {
                valid = !valid;
                int before = f.ValidationCount;
                f.Client.TryUpdatePlacementValidity(f.Selection!);
                Assert.Equal(before + 1, f.ValidationCount);
            }
            int validations = f.ValidationCount;
            for (int frame = 0; frame < 10; frame++)
            {
                f.Selection!.HitPosition.X = 0.1 + 0.02 * Math.Sin(frame);
                f.Draw();
                Assert.Equal(phase * 10 + frame + 1, f.Draws);
                Assert.Equal(valid ? (0.2f, 0.85f, 1f, 0.7f) : (1f, 0f, 0f, 0.7f), colors.Last());
                Assert.Equal(validations, f.ValidationCount);
                Assert.Equal(valid, f.Brush.IsValidPlacement);
                Assert.Equal(1, f.Geometry.Uploads);
                Assert.Equal(0, f.Geometry.Uploaded[0].DisposeCount);
            }
        }
        f.Program.VerifySet(p => p.ResolvedDirections = new Vector2(-1f, 0f), Times.Exactly(30));
        f.Program.VerifySet(p => p.ResolvedDirections = new Vector2(1f, 0f), Times.Never);
        f.Program.Verify(p => p.Compile(), Times.Once);
    }

    [Theory]
    [InlineData("shapes")]
    [InlineData("textures")]
    [InlineData("atlas")]
    [InlineData("shader")]
    [InlineData("target-edit")]
    public void RelevantChangesRetireOnlyOwnedMeshAndRebuildOnce(string change)
    {
        using var f = new Fixture();
        var borrowedPositions = f.Geometry.Borrowed.xyz;
        f.Draw();
        f.RaiseReload(change);
        Assert.Equal(1, f.Geometry.Uploaded[0].DisposeCount);
        Assert.Equal(borrowedPositions, f.Geometry.Borrowed.xyz);
        f.Draw();
        f.Draw();
        Assert.Equal(2, f.Geometry.Uploads);
        Assert.Equal(3, f.Draws);
        f.Program.Verify(p => p.Compile(), Times.Once);
        f.Factory.Verify(create => create(), change == "shader" ? Times.Exactly(2) : Times.Once());
        f.Program.Verify(p => p.Dispose(), change == "shader" ? Times.Once() : Times.Never());
    }

    [Fact]
    public void UnrelatedWorldEditsAndBlockMetadataDoNotDiscardCapturedGeometry()
    {
        using var f = new Fixture();
        f.Draw();
        f.Events.Raise(e => e.BlockChanged += null!, new BlockPos(90, 90, 90), f.Geometry.Block);
        f.Draw();
        Assert.Equal(1, f.Geometry.Uploads);
        f.Geometry.Block.RandomizeRotations = true;
        f.Draw();
        Assert.Equal(3, f.Draws);
        f.Geometry.Block.RandomizeRotations = false;
        f.Draw();
        Assert.Equal(4, f.Draws);
        Assert.Equal(1, f.Geometry.Uploads);
    }

    [Fact]
    public void RejectedSnapsTurnRedAndDisabledSnapsClearWithoutASecondRuleCall()
    {
        using var f = new Fixture();
        f.Draw();
        f.Geometry.Inputs.Validator.Setup(v => v.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(),
            ref It.Ref<string>.IsAny)).Returns(false);
        f.Resolve();
        int validations = f.ValidationCount;
        f.Draw();
        Assert.Equal(validations, f.ValidationCount);
        Assert.Equal(2, f.Draws);
        f.Program.VerifySet(p => p.ColorIn = new Vector4(1f, 0f, 0f, 0.7f), Times.Once);
        f.Geometry.Inputs.Validator.Setup(v => v.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(),
            ref It.Ref<string>.IsAny)).Returns(true);
        f.Brush.Snapping = EBuildBrushSnapping.None;
        f.Resolve();
        f.Draw();
        Assert.Equal(2, f.Draws);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposalAndWorldLeaveReleaseSubscriptionsAndResourcesExactlyOnce(bool leave)
    {
        using var f = new Fixture();
        f.Draw();
        if (leave) f.Events.Raise(e => e.LeaveWorld += null!);
        else f.Binding.Dispose();
        f.Binding.Dispose();
        Assert.Empty(f.Active);
        Assert.Equal(1, f.Geometry.Uploaded[0].DisposeCount);
        f.Program.Verify(p => p.Dispose(), Times.Once);
        f.Events.VerifyRemove(e => e.BlockChanged -= It.IsAny<BlockChangedDelegate>(), Times.Once);
        f.Events.VerifyRemove(e => e.ReloadShapes -= It.IsAny<Action>(), Times.Once);
        f.Events.VerifyRemove(e => e.ReloadTextures -= It.IsAny<Action>(), Times.Once);
        f.Events.VerifyRemove(e => e.BlockTexturesLoaded -= It.IsAny<Action>(), Times.Once);
        f.Events.VerifyRemove(e => e.ReloadShader -= It.IsAny<ActionBoolReturn>(), Times.Once);
        f.Events.VerifyRemove(e => e.LeaveWorld -= It.IsAny<Action>(), Times.Once);
        f.RaiseReload("shader");
        f.Draw();
        Assert.Equal(1, f.Draws);
        Assert.NotEmpty(f.Geometry.Borrowed.xyz);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("shader")]
    [InlineData("shapes")]
    public void ReentrantLifecycleEventsDeferResourceRetirementUntilOutsideTheCallback(string change)
    {
        using var f = new Fixture();
        f.OnDraw = () =>
        {
            f.OnDraw = null;
            if (change == "leave") f.Events.Raise(e => e.LeaveWorld += null!);
            else f.RaiseReload(change);
            Assert.Equal(0, f.Geometry.Uploaded[0].DisposeCount);
            Assert.Single(f.Active);
        };
        f.Draw();
        Assert.Single(f.Pending);
        f.Drain();
        Assert.Equal(1, f.Geometry.Uploaded[0].DisposeCount);
        if (change == "leave") Assert.Empty(f.Active);
        else
        {
            Assert.Single(f.Active);
            f.Draw();
            Assert.Equal(2, f.Geometry.Uploads);
        }
    }

    [Fact]
    public void ShaderFailureLeavesBrushPlacementUsableAndReloadRecoversWithFreshOwnedGeometry()
    {
        using var f = new Fixture();
        f.Program.Setup(p => p.Compile()).Returns(false);
        f.Draw();
        Assert.Equal(0, f.Draws);
        f.Selection!.Position.X++;
        f.Resolve();
        Assert.True(f.Brush.IsValidPlacement);
        Assert.NotNull(f.Brush.Position);
        f.Draw();
        f.Program.Verify(p => p.Compile(), Times.Once);
        var replacement = new Mock<IBrushSnappingShader>();
        replacement.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(replacement.Object);
        Assert.True(f.ReloadShader!());
        f.Draw();
        Assert.Equal(1, f.Draws);
        f.Geometry.Api.Verify(a => a.Logger.Error(
            "[BuildBrush] Snapping indicator unavailable until shader reload: {0}", It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public void DifferentSupportedTargetGeometryReplacesOnlyTheOwnedCacheEntry()
    {
        using var f = new Fixture();
        f.Draw();
        var target = new Block
        {
            BlockId = 11, Code = new AssetLocation("game:other-target"),
            Shape = f.Geometry.Block.Shape, DrawType = f.Geometry.Block.DrawType
        };
        f.Geometry.WorldBlock = target;
        f.Selection!.Position.X++;
        f.Selection.Block = target;
        f.Draw();
        Assert.Equal(1, f.Draws);
        f.Resolve();
        f.Draw();
        Assert.Equal(2, f.Draws);
        Assert.Equal(2, f.Geometry.Uploads);
        Assert.Equal(1, f.Geometry.Uploaded[0].DisposeCount);
        Assert.NotEmpty(f.Geometry.Borrowed.xyz);
    }

    [Fact]
    public void FailedReloadReturnsFailureAndRecoveryDoesNotReuseRetiredGeometry()
    {
        using var f = new Fixture();
        f.Draw();
        var broken = new Mock<IBrushSnappingShader>();
        broken.Setup(p => p.Compile()).Returns(false);
        f.Factory.Setup(create => create()).Returns(broken.Object);
        Assert.False(f.ReloadShader!());
        f.Program.Verify(p => p.Dispose(), Times.Once);
        broken.Verify(p => p.Dispose(), Times.Once);
        Assert.Equal(1, f.Geometry.Uploaded[0].DisposeCount);
        f.Resolve();
        Assert.True(f.Brush.IsValidPlacement);
        f.Draw();
        Assert.Equal(1, f.Draws);
        var recovered = new Mock<IBrushSnappingShader>();
        recovered.Setup(p => p.Compile()).Returns(true);
        f.Factory.Setup(create => create()).Returns(recovered.Object);
        Assert.True(f.ReloadShader!());
        f.Draw();
        Assert.Equal(2, f.Draws);
        Assert.Equal(2, f.Geometry.Uploads);
    }

    [Fact]
    public void DrawingFailureRestoresStateAndDoesNotInterruptPlacementOrReloadRecovery()
    {
        using var f = new Fixture();
        f.OnDraw = () => throw new InvalidOperationException("GPU draw");
        f.Draw();
        Assert.Equal(1, f.Captures);
        Assert.Equal(1, f.Restores);
        f.Selection!.Position.X++;
        f.Resolve();
        Assert.True(f.Brush.IsValidPlacement);
        f.Draw();
        Assert.Equal(1, f.Draws);
        f.OnDraw = null;
        Assert.True(f.ReloadShader!());
        f.Draw();
        Assert.Equal(2, f.Draws);
        Assert.Equal(f.Captures, f.Restores);
    }

    [Fact]
    public void RegistrationFailureIsExplicitAndCanRecoverAtShaderReload()
    {
        using var f = new Fixture(registerFailure: true);
        Assert.Empty(f.Active);
        Assert.True(f.Brush.IsActive);
        Assert.True(f.Brush.IsValidPlacement);
        f.FailRegistration = false;
        Assert.True(f.ReloadShader!());
        Assert.Single(f.Active);
        f.Draw();
        Assert.Equal(1, f.Draws);
    }

    [Fact]
    public void ClientSystemOwnsOneSessionBindingAndRetiresItOnDeparture()
    {
        using var f = new Fixture();
        f.Binding.Dispose();
        f.Client.SetBuildMode(false, null);
        var system = new BuildBrushSystem_Client();
        SetField(system, "api", f.Geometry.Api.Object);
        SetField(system, "controller", f.Client);
        SetField(system, "workArea", new WorkAreaController());
        Invoke(system, "Event_PlayerJoin", f.Player);
        var binding = GetField(system, "snappingRendering");
        Assert.IsType<BrushSnappingRenderBinding>(binding);
        Invoke(system, "Event_PlayerJoin", f.Player);
        Assert.Same(binding, GetField(system, "snappingRendering"));
        Assert.Single(f.Active);
        Invoke(system, "Event_PlayerDeath", f.Player);
        Assert.False(f.Brush.IsActive);
        Invoke(system, "Event_LeaveWorld");
        Assert.Null(GetField(system, "snappingRendering"));
        Assert.Empty(f.Active);
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance)!.SetValue(target, value);
    private static object? GetField(object target, string name) =>
        target.GetType().GetField(name, PrivateInstance)!.GetValue(target);
    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, PrivateInstance)!.Invoke(target, args);

    private sealed class Fixture : IDisposable
    {
        public readonly BrushTargetMeshTests.Fixture Geometry = new();
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly ClientPlayer Player = WorkAreaRenderingTests.CreatePlayer();
        public EntityPlayer Entity => Player.Entity;
        public readonly Mock<IShaderAPI> Shaders = new();
        public readonly Mock<IBrushSnappingShader> Program = new();
        public readonly Mock<Func<IBrushSnappingShader>> Factory = new();
        public readonly HashSet<IRenderer> Active = [];
        public readonly Queue<Action> Pending = new();
        public readonly DummySlot Slot;
        public readonly BuildBrushControllerClient Client;
        public readonly BrushSnappingRenderBinding Binding;
        public BuildBrushInstance Brush => Client.GetBrush();
        public BlockSelection? Selection
        {
            get => Entity.BlockSelection;
            set => Entity.BlockSelection = value!;
        }
        public int SlotNumber;
        public int Draws;
        public int Captures, Restores;
        public float[]? LastModel;
        public Action? OnDraw;
        public bool FailRegistration;
        public ActionBoolReturn? ReloadShader;
        public int ValidationCount => Geometry.Inputs.Validator.Invocations.Count(i => i.Method.Name == "CanPlace");

        public Fixture(bool registerFailure = false, bool captureNative = false)
        {
            FailRegistration = registerFailure;
            var world = Mock.Get(Geometry.Api.Object.World);
            world.SetupGet(w => w.Side).Returns(EnumAppSide.Client);
            world.SetupGet(w => w.Logger).Returns(Geometry.Api.Object.Logger);
            world.SetupGet(w => w.Player).Returns(Player);
            world.Setup(w => w.GetBlock(10)).Returns(Geometry.Block);
            Entity.Alive = true;
            Geometry.Api.SetupGet(a => a.Event).Returns(Events.Object);
            Geometry.Api.SetupGet(a => a.Shader).Returns(Shaders.Object);
            Geometry.Render.SetupGet(r => r.CameraMatrixOriginf).Returns(new Matrixf().Values);
            Geometry.Render.SetupGet(r => r.CurrentProjectionMatrix).Returns(new Matrixf().Values);
            Geometry.Render.Setup(r => r.RenderMesh(It.IsAny<MeshRef>())).Callback(() => { Draws++; OnDraw?.Invoke(); });
            Program.Setup(p => p.Compile()).Returns(true);
            Program.SetupSet(p => p.ModelViewMatrix = It.IsAny<float[]>())
                .Callback<float[]>(matrix => LastModel = (float[])matrix.Clone());
            int programCount = 0;
            Factory.Setup(create => create()).Returns(() =>
            {
                if (programCount++ == 0) return Program.Object;
                var replacement = new Mock<IBrushSnappingShader>();
                replacement.Setup(p => p.Compile()).Returns(true);
                return replacement.Object;
            });
            Events.Setup(e => e.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), "vbe-snapping"))
                .Callback<IRenderer, EnumRenderStage, string>((renderer, stage, _) =>
                {
                    Assert.Equal(EnumRenderStage.OIT, stage);
                    Assert.Equal(0.52, renderer.RenderOrder);
                    if (FailRegistration) throw new InvalidOperationException("GPU registration");
                    Assert.True(Active.Add(renderer));
                });
            Events.Setup(e => e.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.OIT))
                .Callback<IRenderer, EnumRenderStage>((renderer, _) =>
                {
                    Assert.False(((BrushSnappingRenderer)renderer).IsInCallback);
                    Assert.True(Active.Remove(renderer));
                });
            Events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => Pending.Enqueue(action));
            Events.SetupAdd(e => e.ReloadShader += It.IsAny<ActionBoolReturn>())
                .Callback<ActionBoolReturn>(handler => ReloadShader += handler);
            Events.SetupRemove(e => e.ReloadShader -= It.IsAny<ActionBoolReturn>())
                .Callback<ActionBoolReturn>(handler => ReloadShader -= handler);
            Slot = new DummySlot(new ItemStack(Geometry.Block));
            Geometry.Inputs.Inventory.SetupGet(i => i.ActiveHotbarSlot).Returns(Slot);
            Geometry.Inputs.Inventory.SetupGet(i => i.ActiveHotbarSlotNumber).Returns(() => SlotNumber);
            Selection = CreateSelection();
            Client = new BuildBrushControllerClient(Geometry.Api.Object, () => Geometry.Inputs.CreateBrush(world.Object));
            Brush.Snapping = EBuildBrushSnapping.Horizontal;
            Client.SetBuildMode(true, Selection);
            Assert.Same(Selection, ((IPlayer)Player).CurrentBlockSelection);
            Assert.True(Brush.IsValidPlacement);
            Assert.False(Brush.IsDirty);
            Assert.NotNull(Client.GetSnappingPresentation(Selection, true));
            Binding = new BrushSnappingRenderBinding(Geometry.Api.Object, Client, new State(this),
                captureNative ? null : Geometry.GetCapturedMesh, Factory.Object);
        }

        public BlockSelection CreateSelection() => new()
        {
            Position = Geometry.Position.Copy(), Block = Geometry.Block, Face = BlockFacing.NORTH,
            HitPosition = new Vec3d(0.9, 0.5, 0)
        };
        public void Resolve()
        {
            Geometry.Inputs.Selection = Selection;
            Client.TryUpdate(Selection);
        }
        public void Draw() => Binding.Renderer.OnRenderFrame(0, EnumRenderStage.OIT);
        public void Drain() { while (Pending.TryDequeue(out var task)) task(); }
        public void RaiseReload(string kind)
        {
            switch (kind)
            {
                case "shapes": Events.Raise(e => e.ReloadShapes += null!); break;
                case "textures": Events.Raise(e => e.ReloadTextures += null!); break;
                case "atlas": Events.Raise(e => e.BlockTexturesLoaded += null!); break;
                case "shader": ReloadShader?.Invoke(); break;
                case "target-edit": Events.Raise(e => e.BlockChanged += null!, Selection!.Position, Geometry.Block); break;
            }
        }
        public void Dispose()
        {
            Binding.Dispose();
            Drain();
            Geometry.Dispose();
        }
        private sealed class State(Fixture fixture) : IBrushOverlayRenderState
        {
            public void Capture() => fixture.Captures++;
            public void Apply() { }
            public void Restore() => fixture.Restores++;
        }
    }
}
