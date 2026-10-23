using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Checks live brush inputs, deferred refresh and controller ownership without player proxies.</summary>
public sealed class BrushInputContractTests
{
    #region Public API
    #region Inventory and selection

    /// <summary>A newly active slot supplies the next source item and receives the placement dirty marker.</summary>
    [Fact]
    public void ActiveSlotChangedAfterConstruction_UsesNewItemAndMarksOnlyNewSlot()
    {
        var world = TestHelpers.CreateMockWorld();
        Block first = TestHelpers.CreateAndRegisterBlock(world, 10);
        Block second = TestHelpers.CreateAndRegisterBlock(world, 20);
        ItemSlot firstSlot = new DummySlot(new ItemStack(first));
        ItemSlot secondSlot = new DummySlot(new ItemStack(second));
        int firstDirty = 0;
        int secondDirty = 0;
        firstSlot.MarkedDirty += () => { firstDirty++; return true; };
        secondSlot.MarkedDirty += () => { secondDirty++; return true; };
        ItemSlot activeSlot = firstSlot;
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(() => activeSlot);
        BuildBrushInstance brush = inputs.CreateBrush(world.Object);
        Assert.Equal(10, brush.BlockId);

        activeSlot = secondSlot;
        brush.OnBlockPlacedServer();

        Assert.Equal(20, brush.BlockId);
        Assert.Equal(0, firstDirty);
        Assert.Equal(1, secondDirty);
        Assert.Same(second, brush.ItemStack!.Block);
    }

    /// <summary>Explicit absence clears position/validity rather than consulting the deferred selection getter.</summary>
    [Fact]
    public void TryUpdate_ExplicitSelectionAndNull_DoNotUseDeferredFallback()
    {
        var world = TestHelpers.CreateMockWorld();
        BrushTestInputs inputs = new() { Selection = SelectionAt(90) };
        BuildBrushInstance brush = inputs.CreateBrush(world.Object);
        BlockSelection explicitSelection = SelectionAt(12);

        brush.TryUpdate(explicitSelection, force: true);
        Assert.Equal(explicitSelection.Position, brush.Position);
        Assert.True(brush.IsValidPlacement);

        Assert.False(brush.TryUpdate(null));
        Assert.Null(brush.Position);
        Assert.False(brush.IsValidPlacement);
    }

    /// <summary>A deferred callback reads the later selection and unregisters before running validation.</summary>
    [Fact]
    public void DirtyRefresh_ReadsSelectionAtExecutionTime()
    {
        var world = TestHelpers.CreateMockWorld();
        BrushTestInputs inputs = new() { Selection = SelectionAt(10) };
        Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
        BuildBrushInstance brush = inputs.CreateBrush(world.Object);
        brush.EnterBuildMode(inputs.Selection);
        Action<float>? pending = null;
        world.Setup(w => w.RegisterCallback(It.IsAny<Action<float>>(), 1))
            .Callback<Action<float>, int>((callback, _) => pending = callback).Returns(42);
        brush.MarkDirty();
        Assert.NotNull(pending);

        inputs.Selection = SelectionAt(30);
        bool unregisteredFirst = false;
        world.Setup(w => w.UnregisterCallback(42)).Callback(() => unregisteredFirst = true);
        brush.OnPositionChanged += (_, _) => Assert.True(unregisteredFirst);
        pending!(0);

        Assert.Equal(inputs.Selection.Position, brush.Position);
        Assert.False(brush.IsDirty);
        world.Verify(w => w.UnregisterCallback(42), Times.Once);
    }

    /// <summary>Forced updates still rerun validity checks when position/snapping have not changed.</summary>
    [Fact]
    public void ForcedUpdate_RevalidatesUnchangedSelection()
    {
        var world = TestHelpers.CreateMockWorld();
        BrushTestInputs inputs = new();
        Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
        BuildBrushInstance brush = inputs.CreateBrush(world.Object);
        BlockSelection selection = SelectionAt(10);
        // Seed the same resolved position so the unforced update can take the unchanged-state path.
        brush.Snapping = EBuildBrushSnapping.Horizontal;
        brush.TryUpdate(selection, force: true);
        inputs.Validator.Invocations.Clear();

        brush.TryUpdate(selection, force: true);

        inputs.Validator.Verify(v => v.CanPlace(block, It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny), Times.Once);
    }

    #endregion

    #region Controllers and preview ownership

    /// <summary>The local controller creates one brush lazily and refreshes it with the explicit post-placement selection.</summary>
    [Fact]
    public void ClientController_IsLazyAndRefreshesExplicitSelection()
    {
        var world = TestHelpers.CreateMockWorld();
        BrushTestInputs inputs = new() { Selection = SelectionAt(90) };
        int created = 0;
        BuildBrushControllerClient controller = new(TestHelpers.CreateMockCapi().Object, () =>
        {
            created++;
            return inputs.CreateBrush(world.Object);
        });
        Assert.Equal(0, created);
        BuildBrushInstance brush = controller.GetBrush();
        Assert.Same(brush, controller.GetBrush());
        Assert.Equal(1, created);

        controller.OnBlockPlacedClient(SelectionAt(25));

        Assert.Equal(new BlockPos(25, 2, 3), brush.Position);
        Assert.Equal(1, created);
    }

    /// <summary>Disposal detaches activation handling from the supplied brush.</summary>
    [Fact]
    public void ServerController_Dispose_UnsubscribesActivation()
    {
        var world = TestHelpers.CreateMockServerWorld();
        BuildBrushInstance brush = TestHelpers.CreateTestInstance(world);
        BuildBrushControllerServer controller = new(brush);
        controller.Dispose();

        brush.IsActive = true;

        world.Verify(w => w.BlockAccessor.CreateMiniDimension(It.IsAny<Vec3d>()), Times.Never);
        Assert.Null(brush.Dimension);
    }

    /// <summary>The injected UID is published on the real preview entity during brush-owned spawning.</summary>
    [Fact]
    public void SpawnEntity_PublishesInjectedOwnerUid()
    {
        var api = TestHelpers.CreateMockSapi();
        var world = TestHelpers.CreateMockServerWorld(api);
        var dimension = TestHelpers.SetupMiniDimensionCreation(world);
        TestHelpers.SetupMiniDimensionLoading(api);
        dimension.Setup(d => d.CurrentPos).Returns(new EntityPos());
        BuildBrushEntity entity = new();
        entity.WatchedAttributes.SetInt("dim", 0);
        api.Setup(a => a.World.ClassRegistry.CreateEntity(BuildBrushEntity.ClassName)).Returns(entity);
        BrushTestInputs inputs = new();
        BuildBrushInstance brush = new(world.Object, inputs.Inventory.Object, "owner-123", "Owner Name",
            inputs.Validator.Object, () => inputs.Selection);
        Assert.True(brush.InitializeDimension());

        Assert.True(brush.SpawnEntity());

        Assert.Equal("owner-123", entity.WatchedAttributes.GetString(BuildBrushEntity.OwnerPlayerUidKey));
        world.Verify(w => w.SpawnEntity(entity), Times.Once);
    }

    #endregion
    #endregion

    #region Private

    /// <summary>Creates a simple hit selection at a known position for input-timing assertions.</summary>
    private static BlockSelection SelectionAt(int x)
    {
        return new BlockSelection
        {
            Position = new BlockPos(x, 2, 3),
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 1, 0.5)
        };
    }

    #endregion
}
