using Moq;
using VanillaBuildingExpanded.BuildHammer;
using VanillaBuildingExpanded.Networking;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Exercises mode transitions, suspended sources, and placement synchronization without equipment.</summary>
public sealed class BuildModeTests
{
    #region Public API

    /// <summary>Targetless transitions survive the actual protobuf serialization contract.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoTargetPacket_RoundTripsModeAndSequence(bool enabled)
    {
        var packet = new Packet_SetBuildBrush { seq = 7, isActive = enabled, orientationIndex = 0,
            position = null, snapping = EBuildBrushSnapping.Horizontal };
        using MemoryStream stream = new();
        ProtoBuf.Serializer.Serialize(stream, packet);
        stream.Position = 0;
        var received = ProtoBuf.Serializer.Deserialize<Packet_SetBuildBrush>(stream);
        Assert.Equal(enabled, received.isActive);
        Assert.Equal(7, received.seq);
        Assert.Null(received.position);
        Assert.Equal(packet.snapping, received.snapping);
    }

    /// <summary>Repeated mode entry and exit create and release the controller's preview resources.</summary>
    [Fact]
    public void ServerModeExit_DestroysPreviewAndAllowsFreshEntry()
    {
        var api = TestHelpers.CreateMockSapi();
        var world = TestHelpers.CreateMockServerWorld(api);
        var dimension = TestHelpers.SetupMiniDimensionCreation(world);
        TestHelpers.SetupMiniDimensionLoading(api);
        dimension.Setup(d => d.CurrentPos).Returns(new Vintagestory.API.Common.Entities.EntityPos());
        var entity = new Mock<BuildBrushEntity> { CallBase = true };
        entity.Setup(e => e.Die(EnumDespawnReason.Removed, null));
        api.Setup(a => a.World.ClassRegistry.CreateEntity(BuildBrushEntity.ClassName)).Returns(entity.Object);
        var brush = TestHelpers.CreateTestInstance(world);
        using BuildBrushControllerServer server = new(brush);

        for (int cycle = 0; cycle < 2; cycle++)
        {
            server.ApplyState(new Packet_SetBuildBrush { seq = cycle * 2 + 1, isActive = true,
                orientationIndex = 0, position = null, snapping = EBuildBrushSnapping.None });
            Assert.NotNull(brush.Dimension);
            Assert.NotNull(brush.Entity);
            Assert.False(entity.Object.WatchedAttributes.GetBool(BuildBrushEntity.BrushVisibleKey));
            server.ApplyState(new Packet_SetBuildBrush { seq = cycle * 2 + 2, isActive = false,
                orientationIndex = 0, position = null, snapping = EBuildBrushSnapping.None });
            Assert.Null(brush.Dimension);
            Assert.Null(brush.Entity);
        }
        entity.Verify(e => e.Die(EnumDespawnReason.Removed, null), Times.Exactly(2));
        dimension.Verify(d => d.UnloadUnusedServerChunks(), Times.Exactly(2));
    }

    /// <summary>Target loss and exit hide the preview, and queued refresh cannot restore an exited target.</summary>
    [Fact]
    public void ExitAndTargetLoss_ClearPreviewVisibilityAndDeferredPlacement()
    {
        var world = TestHelpers.CreateMockWorld();
        Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
        BrushTestInputs inputs = new() { Selection = Selection() };
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
        BuildBrushInstance brush = inputs.CreateBrush(world.Object);
        BuildBrushEntity entity = new();
        brush.AssociateEntity(entity);
        brush.EnterBuildMode(inputs.Selection);
        Assert.True(entity.WatchedAttributes.GetBool(BuildBrushEntity.BrushVisibleKey));

        brush.TryUpdate(null);
        Assert.False(entity.WatchedAttributes.GetBool(BuildBrushEntity.BrushVisibleKey));
        brush.TryUpdate(inputs.Selection);
        Assert.True(entity.WatchedAttributes.GetBool(BuildBrushEntity.BrushVisibleKey));
        Action<float>? pending = null;
        world.Setup(w => w.RegisterCallback(It.IsAny<Action<float>>(), 1))
            .Callback<Action<float>, int>((callback, _) => pending = callback).Returns(42);
        brush.MarkDirty();
        Assert.NotNull(pending);

        brush.ExitBuildMode();
        pending!(0);
        Assert.False(entity.WatchedAttributes.GetBool(BuildBrushEntity.BrushVisibleKey));
        Assert.Null(brush.Position);
        Assert.Null(brush.Selection.Position);
        Assert.False(brush.IsDirty);
    }

    /// <summary>Entry publishes its resolved source/target synchronously; exit clears them before publication.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Toggle_PublishesImmediatelyWithOrWithoutSelection(bool hasSelection)
    {
        var world = TestHelpers.CreateMockWorld();
        Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
        BuildBrushControllerClient client = new(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        var brush = client.GetBrush();
        Assert.False(brush.IsActive);
        brush.Snapping = EBuildBrushSnapping.Horizontal;
        List<(bool Active, BlockPos? Position, int? Block)> publications = [];
        client.ModeChanged += () => publications.Add((client.State.IsActive, client.State.Position?.Copy(), client.State.BlockId));

        Assert.True(client.SetBuildMode(true, hasSelection ? Selection() : null));
        Assert.Single(publications);
        Assert.True(publications[0].Active);
        Assert.Equal(10, publications[0].Block);
        Assert.Equal(hasSelection, publications[0].Position is not null);
        Assert.Equal(!hasSelection, brush.IsDisabled);
        Assert.False(client.SetBuildMode(true, Selection()));
        Assert.Single(publications);

        Assert.True(client.SetBuildMode(false, Selection()));
        Assert.Equal(2, publications.Count);
        Assert.False(publications[1].Active);
        Assert.Null(publications[1].Position);
        Assert.Null(brush.Selection.Position);
        Assert.Null(brush.ItemStack);
        Assert.True(brush.IsDisabled);
        Assert.Equal(EBuildBrushSnapping.Horizontal, brush.Snapping);
        Assert.False(client.SetBuildMode(false, null));
        Assert.True(client.SetBuildMode(true, Selection()));
        Assert.Equal(3, publications.Count);
        Assert.False(brush.IsDisabled);
    }

    /// <summary>Empty slots, ordinary items and missing blocks suspend use without toggling mode.</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("item")]
    [InlineData("missing")]
    public void HotbarChange_SuspendsAndResumesWithoutRotationSupport(string unsupported)
    {
        var world = TestHelpers.CreateMockWorld();
        Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
        Block missing = TestHelpers.CreateAndRegisterBlock(world, 20);
        missing.IsMissing = true;
        ItemSlot slot = new DummySlot(new ItemStack(block));
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(() => slot);
        BuildBrushControllerClient client = new(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        client.SetBuildMode(true, Selection());
        var brush = client.GetBrush();
        Assert.Equal(EBuildBrushRotationMode.None, brush.RotationMode);
        Assert.False(brush.IsDisabled);

        slot = unsupported switch
        {
            "item" => new DummySlot(new ItemStack(new Item { ItemId = 30 })),
            "missing" => new DummySlot(new ItemStack(missing)),
            _ => new DummySlot()
        };
        client.TryUpdate(Selection());
        Assert.True(brush.IsActive);
        Assert.True(brush.IsDisabled);
        Assert.Null(brush.Position);
        Assert.Null(brush.ItemStack);

        slot = new DummySlot(new ItemStack(block));
        client.TryUpdate(Selection());
        Assert.True(brush.IsActive);
        Assert.False(brush.IsDisabled);
        Assert.Equal(10, brush.BlockId);
    }

    /// <summary>Entry and exit reach the server before subsequent native placement; stale packets cannot reactivate mode.</summary>
    [Fact]
    public void ModePackets_ApplyBeforeImmediatePlacementAndRejectStaleReactivation()
    {
        var world = TestHelpers.CreateMockWorld();
        Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
        BuildBrushControllerClient client = new(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        using BuildBrushControllerServer server = new(inputs.CreateBrush(world.Object));
        // Native preview creation has separate lifecycle coverage; this test isolates packet ordering.
        server.Dispose();
        using TestServer system = new();
        system.Sessions.Add(1, server);
        long sequence = 0;
        List<Packet_SetBuildBrush> sent = [];
        client.ModeChanged += () =>
        {
            sent.Add(new Packet_SetBuildBrush { seq = ++sequence, isActive = client.State.IsActive,
                position = client.State.Position, orientationIndex = client.State.OrientationIndex, snapping = client.State.Snapping });
        };

        client.SetBuildMode(true, Selection());
        Assert.False(server.Brush.IsActive);
        var entry = system.ApplyBrushUpdate(1, sent[0], "Tester")!.Value;
        Assert.False(server.Brush.IsDisabled);
        Assert.True(server.Brush.TryBuildPlacementRequest(Selection(), out var request));
        Assert.Equal(10, request!.PlacementBlock.Id);

        client.SetBuildMode(false, null);
        var exit = system.ApplyBrushUpdate(1, sent[1], "Tester")!.Value;
        Assert.True(server.Brush.IsDisabled);
        Assert.Null(server.Brush.Position);
        Assert.False(system.ApplyBrushUpdate(1, sent[0], "Tester")!.Value.Applied);
        Assert.False(server.Brush.IsActive);
        Assert.False(system.TryPlaceBrushBlock(world.Object, null, null, Selection()));
    }

    /// <summary>Lifecycle resets clear placement but preserve ordering and session snapping preferences.</summary>
    [Fact]
    public void DeathAndRespawn_ResetModeWithoutResettingSequence()
    {
        var world = TestHelpers.CreateMockWorld();
        var brush = TestHelpers.CreateTestInstance(world);
        using BuildBrushControllerServer controller = new(brush);
        controller.Dispose();
        controller.ApplyState(new Packet_SetBuildBrush { seq = 9, isActive = true,
            orientationIndex = 0, position = null, snapping = EBuildBrushSnapping.Vertical });
        controller.ResetBuildMode();
        controller.ResetBuildMode();
        Assert.False(brush.IsActive);
        Assert.False(controller.State.IsActive);
        Assert.Null(brush.Position);
        Assert.Equal(9, brush.LastAppliedSeq);
        Assert.Equal(9, controller.State.LastAppliedSeq);
        Assert.Equal(EBuildBrushSnapping.Vertical, brush.Snapping);
    }

    #endregion

    #region Private

    /// <summary>Creates a deterministic target for immediate-entry placement.</summary>
    private static BlockSelection Selection()
    {
        return new BlockSelection { Position = new BlockPos(1, 2, 3), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
    }

    /// <summary>Exposes session registration while retaining production sequence and placement decisions.</summary>
    private sealed class TestServer : BuildBrushSystem_Server, IDisposable
    {
        public Dictionary<int, BuildBrushControllerServer> Sessions => Controllers;
    }

    #endregion
}
