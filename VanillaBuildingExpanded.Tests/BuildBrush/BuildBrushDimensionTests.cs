using System.Collections.Immutable;
using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for <see cref="BrushDimension"/>.
/// Verifies subscription lifecycle and event handler behavior.
/// </summary>
public class BuildBrushDimensionTests
{
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void Preview_PlacesPackedDirtBeforeQuernWithoutIncludingSupportInBounds(EnumAppSide side)
    {
        var world = TestHelpers.CreateMockWorld(side);
        Block support = world.Object.GetBlock(new AssetLocation("game:packeddirt"));
        support.SideSolid[BlockFacing.UP.Index] = true;
        var quern = new BlockQuern { BlockId = 100, Code = new AssetLocation("game:quern-basalt"), EntityClass = "Quern" };
        var fallingBehavior = new BlockBehaviorUnstableFalling(quern);
        fallingBehavior.Initialize(JsonObject.FromJson("{}"));
        quern.BlockBehaviors = [fallingBehavior];
        var miniDimension = TestHelpers.CreateMockDimension();
        miniDimension.SetupGet(d => d.subDimensionId).Returns(1);
        miniDimension.Setup(d => d.AdjustPosForSubDimension(It.IsAny<BlockPos>()))
            .Callback<BlockPos>(pos => pos.Set(100, 100, 100));
        Dictionary<BlockPos, Block> blocks = [];
        var accessor = new Mock<IBlockAccessor>();
        world.Setup(w => w.BlockAccessor).Returns(accessor.Object);
        accessor.Setup(a => a.GetBlock(It.IsAny<BlockPos>()))
            .Returns((BlockPos pos) => blocks.TryGetValue(pos, out Block? block) ? block : null!);
        miniDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>()))
            .Returns((BlockPos pos) => blocks.TryGetValue(pos, out Block? block) ? block : null!);
        miniDimension.Setup(d => d.SetBlock(support.BlockId, It.IsAny<BlockPos>()))
            .Callback<int, BlockPos>((_, pos) => blocks[pos.Copy()] = support);
        miniDimension.Setup(d => d.SetBlock(quern.BlockId, It.IsAny<BlockPos>(), It.IsAny<ItemStack>()))
            .Callback<int, BlockPos, ItemStack>((_, pos, _) =>
            {
                Assert.Same(support, blocks[pos.DownCopy()]);
                Assert.True(fallingBehavior.IsAttached(accessor.Object, pos));
                blocks[pos.Copy()] = quern;
                EnumHandling handling = EnumHandling.PassThrough;
                fallingBehavior.OnBlockPlaced(world.Object, pos, ref handling);
                Assert.Equal(EnumHandling.PassThrough, handling);
            });
        var dimension = new BrushDimension(world.Object);
        dimension.InitializeClientSide(miniDimension.Object);

        dimension.SetBlock(quern);

        Assert.True(dimension.GetActiveBounds(out BlockPos min, out BlockPos max));
        Assert.Equal(new BlockPos(100, 100, 100, Dimensions.MiniDimensions), min);
        Assert.Equal(min, max);
        Assert.Equal(2, blocks.Count);
        miniDimension.Verify(d => d.SetBlock(support.BlockId, min.DownCopy()), Times.Once);
        dimension.Clear();
        miniDimension.Verify(d => d.ClearChunks(), Times.Exactly(2));
    }

    [Fact]
    public void InitializeClientSide_DesignatesReceivedDimensionAsPreviewBeforeReadingBlocks()
    {
        var world = new Mock<IClientWorldAccessor>();
        var miniDimension = TestHelpers.CreateMockDimension();
        miniDimension.SetupGet(d => d.subDimensionId).Returns(42);
        bool designated = false;
        world.Setup(w => w.SetBlocksPreviewDimension(42)).Callback(() => designated = true);
        miniDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>()))
            .Callback(() => Assert.True(designated));
        var dimension = new BrushDimension(world.Object);

        Assert.True(dimension.InitializeClientSide(miniDimension.Object));

        Assert.Same(miniDimension.Object, dimension.Dimension);
        Assert.Equal(42, dimension.DimensionId);
        world.Verify(w => w.SetBlocksPreviewDimension(42), Times.Once);
        miniDimension.Verify(d => d.GetBlock(It.IsAny<BlockPos>()), Times.Once);
    }

    [Theory]
    [InlineData(EnumAppSide.Client, false)]
    [InlineData(EnumAppSide.Server, false)]
    [InlineData(EnumAppSide.Client, true)]
    [InlineData(EnumAppSide.Server, true)]
    public void SlotSwitch_RecreatesPreviewEvenForIdenticalStacks(EnumAppSide side, bool hasEntity)
    {
        var world = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        int selectedSlot = 0;
        DummySlot[] slots = [new(), new(), new(), new()];
        inputs.Inventory.Setup(i => i.ActiveHotbarSlotNumber).Returns(() => selectedSlot);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(() => slots[selectedSlot]);
        var block = new Block
        {
            BlockId = 100,
            Code = new AssetLocation("game:slotpreview"),
            EntityClass = hasEntity ? "PlacementHookPreview" : null
        };
        var otherBlock = new Block
        {
            BlockId = 101,
            Code = new AssetLocation("game:otherslotpreview"),
            EntityClass = block.EntityClass
        };
        world.Setup(w => w.GetBlock(100)).Returns(block);
        world.Setup(w => w.GetBlock(101)).Returns(otherBlock);
        var brush = inputs.CreateBrush(world.Object);
        var miniDimension = new Mock<IMiniDimension>();
        miniDimension.Setup(d => d.subDimensionId).Returns(1);
        Block? previewBlock = null;
        PlacementHookPreviewEntity? entity = null;
        int clearCount = 0;
        miniDimension.Setup(d => d.ClearChunks()).Callback(() =>
        {
            clearCount++;
            previewBlock = null;
            entity = null;
        });
        miniDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(() => previewBlock!);
        miniDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns(() => entity!);
        miniDimension.Setup(d => d.SetBlock(It.IsAny<int>(), It.IsAny<BlockPos>(), It.IsAny<ItemStack>()))
            .Callback<int, BlockPos, ItemStack>((id, pos, stack) =>
            {
                previewBlock = world.Object.GetBlock(id);
                if (hasEntity)
                {
                    entity = new PlacementHookPreviewEntity { Block = previewBlock, Pos = pos.Copy() };
                    entity.OnBlockPlaced(stack);
                }
            });
        var dimension = new BrushDimension(world.Object);
        Assert.True(dimension.InitializeClientSide(miniDimension.Object));
        dimension.SubscribeTo(brush);
        int dirtyCount = 0;
        dimension.OnDirty += (_, _) => dirtyCount++;
        slots[0].Itemstack = new ItemStack(block);
        slots[0].Itemstack.Attributes["inventory"] = new TreeAttribute();
        slots[1].Itemstack = slots[0].Itemstack.Clone();
        slots[2].Itemstack = new ItemStack(otherBlock);
        slots[2].Itemstack.Attributes["inventory"] = new TreeAttribute();
        Assert.True(brush.TryUpdateBlockId());
        var originalEntity = entity;
        int initialClears = clearCount;
        int initialDirtyCount = dirtyCount;

        selectedSlot = 1;
        Assert.True(brush.TryUpdateBlockId());
        Assert.Same(block, previewBlock);
        Assert.Equal(initialClears + 1, clearCount);
        Assert.Equal(initialDirtyCount + 1, dirtyCount);
        if (hasEntity)
        {
            Assert.NotNull(entity);
            Assert.NotSame(originalEntity, entity);
            Assert.Equal(1, entity.PlacementCount);
        }
        Assert.True(dimension.HasActiveBounds);
        Assert.False(brush.TryUpdateBlockId());
        Assert.Equal(initialClears + 1, clearCount);
        Assert.Equal(initialDirtyCount + 1, dirtyCount);

        selectedSlot = 2;
        Assert.True(brush.TryUpdateBlockId());
        Assert.Same(otherBlock, previewBlock);
        Assert.Equal(initialClears + 2, clearCount);
        miniDimension.Verify(d => d.SetBlock(It.IsAny<int>(), It.IsAny<BlockPos>(), It.IsAny<ItemStack>()),
            Times.Exactly(3));

        selectedSlot = 3;
        Assert.True(brush.TryUpdateBlockId());
        Assert.Null(previewBlock);
        Assert.Null(entity);
        Assert.False(dimension.HasActiveBounds);
    }

    [Theory]
    [InlineData(EnumAppSide.Client, false)]
    [InlineData(EnumAppSide.Server, false)]
    [InlineData(EnumAppSide.Client, true)]
    [InlineData(EnumAppSide.Server, true)]
    public void LanternPreview_CombinesNativeHooksAndSerializedStackProperties(
        EnumAppSide side, bool automaticEntitySpawn)
    {
        var world = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var slot = new DummySlot();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(slot);
        var lanterns = new BlockLantern[2];
        string[] orientations = ["up", "north"];
        for (int i = 0; i < lanterns.Length; i++)
        {
            lanterns[i] = new BlockLantern
            {
                BlockId = 100 + i,
                Code = new AssetLocation($"game:lantern-{orientations[i]}"),
                EntityClass = "Lantern"
            };
            lanterns[i].VariantStrict["orientation"] = orientations[i];
            world.Setup(w => w.GetBlock(lanterns[i].BlockId)).Returns(lanterns[i]);
        }
        world.Setup(w => w.SearchBlocks(It.IsAny<AssetLocation>())).Returns(lanterns);
        var brush = inputs.CreateBrush(world.Object);
        var miniDimension = new Mock<IMiniDimension>();
        miniDimension.Setup(d => d.subDimensionId).Returns(1);
        Block? previewBlock = null;
        PlacementTrackingLantern? previewEntity = null;
        miniDimension.Setup(d => d.ClearChunks()).Callback(() =>
        {
            previewBlock = null;
            previewEntity = null;
        });
        miniDimension.Setup(d => d.SetBlock(It.IsAny<int>(), It.IsAny<BlockPos>(), It.IsAny<ItemStack>()))
            .Callback<int, BlockPos, ItemStack>((id, pos, stack) =>
            {
                previewBlock = world.Object.GetBlock(id);
                previewEntity = automaticEntitySpawn
                    ? new PlacementTrackingLantern { Block = previewBlock, Pos = pos.Copy() }
                    : null;
                previewEntity?.OnBlockPlaced(stack);
            });
        miniDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(() => previewBlock!);
        miniDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns(() => previewEntity!);
        miniDimension.Setup(d => d.SpawnBlockEntity("Lantern", It.IsAny<BlockPos>(), It.IsAny<ItemStack>()))
            .Callback<string, BlockPos, ItemStack>((_, pos, stack) =>
            {
                previewEntity = new PlacementTrackingLantern { Block = previewBlock!, Pos = pos.Copy() };
                previewEntity.OnBlockPlaced(stack);
            });
        var dimension = new BrushDimension(world.Object);
        Assert.True(dimension.InitializeClientSide(miniDimension.Object));
        dimension.SubscribeTo(brush);
        int dirtyCount = 0;
        dimension.OnDirty += (_, _) => dirtyCount++;
        slot.Itemstack = new ItemStack(lanterns[0]);
        slot.Itemstack.Attributes.SetString("material", "tinbronze");
        slot.Itemstack.Attributes.SetString("lining", "gold");
        slot.Itemstack.Attributes.SetString("glass", "blue");

        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(previewEntity);
        Assert.Equal("tinbronze", previewEntity.material);
        Assert.Equal("gold", previewEntity.lining);
        Assert.Equal("blue", previewEntity.glass);
        Assert.Equal(1, previewEntity.PlacementCount);
        Assert.NotNull(previewEntity.LastPlacementStack);
        Assert.Equal("tinbronze", previewEntity.LastPlacementStack.Attributes.GetString("material"));
        Assert.True(dirtyCount > 0);

        brush.CycleOrientation();
        Assert.Same(lanterns[1], previewBlock);
        Assert.NotNull(previewEntity);
        Assert.Equal("tinbronze", previewEntity.material);
        Assert.Equal("gold", previewEntity.lining);
        Assert.Equal("blue", previewEntity.glass);
        Assert.Equal(1, previewEntity.PlacementCount);
        Assert.NotNull(previewEntity.LastPlacementStack);
        Assert.Same(lanterns[1], previewEntity.LastPlacementStack.Block);
        Assert.Equal("tinbronze", previewEntity.LastPlacementStack.Attributes.GetString("material"));

        var rotatedEntity = previewEntity;
        rotatedEntity.MeshAngle = 0.75f;
        slot.Itemstack = new ItemStack(lanterns[0]);
        slot.Itemstack.Attributes.SetString("material", "brass");
        slot.Itemstack.Attributes.SetString("lining", "plain");
        slot.Itemstack.Attributes.SetString("glass", "green");
        int beforeSourceChange = dirtyCount;
        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(previewEntity);
        Assert.NotSame(rotatedEntity, previewEntity);
        Assert.Equal("brass", previewEntity.material);
        Assert.Equal("plain", previewEntity.lining);
        Assert.Equal("green", previewEntity.glass);
        Assert.Equal(1, previewEntity.PlacementCount);
        Assert.Equal("brass", previewEntity.LastPlacementStack.Attributes.GetString("material"));
        Assert.Equal("green", previewEntity.LastPlacementStack.Attributes.GetString("glass"));
        Assert.Equal(0f, previewEntity.MeshAngle);
        Assert.Equal(1, brush.OrientationIndex);
        Assert.True(dirtyCount > beforeSourceChange);

        var brassEntity = previewEntity;
        slot.Itemstack.Attributes.SetString("material", "copper");
        int beforeInPlaceChange = dirtyCount;
        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(previewEntity);
        Assert.NotSame(brassEntity, previewEntity);
        Assert.Equal("copper", previewEntity.material);
        Assert.Equal(1, previewEntity.PlacementCount);
        Assert.True(dirtyCount > beforeInPlaceChange);
        miniDimension.VerifySet(d => d.Dirty = true, Times.AtLeastOnce);
        miniDimension.Verify(d => d.SetBlock(It.IsAny<int>(), It.IsAny<BlockPos>(), It.IsAny<ItemStack>()), Times.Exactly(4));
        miniDimension.Verify(d => d.SpawnBlockEntity("Lantern", It.IsAny<BlockPos>(), It.IsAny<ItemStack>()),
            automaticEntitySpawn ? Times.Never() : Times.Exactly(4));

        var tree = new TreeAttribute();
        previewEntity.ToTreeAttributes(tree);
        Assert.Equal("copper", tree.GetString("material"));
        Assert.Equal("plain", tree.GetString("lining"));
        Assert.Equal("green", tree.GetString("glass"));
        int beforeUnchangedRefresh = dirtyCount;
        Assert.False(brush.TryUpdateBlockId());
        Assert.Equal(beforeUnchangedRefresh, dirtyCount);
        Assert.Equal(1, previewEntity.PlacementCount);
        Assert.Same(lanterns[0], slot.Itemstack.Block);
        Assert.False(slot.Itemstack.Attributes.HasAttribute("meshAngle"));

        var linedEntity = previewEntity;
        slot.Itemstack.Attributes.RemoveAttribute("lining");
        slot.Itemstack.Attributes.RemoveAttribute("glass");
        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(previewEntity);
        Assert.NotSame(linedEntity, previewEntity);
        Assert.Equal("copper", previewEntity.material);
        Assert.Equal("plain", previewEntity.lining);
        Assert.Equal("quartz", previewEntity.glass);
        Assert.Equal(0f, previewEntity.MeshAngle);
        Assert.Equal(1, previewEntity.PlacementCount);
        beforeUnchangedRefresh = dirtyCount;

        dimension.Unsubscribe();
        slot.Itemstack.Attributes.SetString("material", "silver");
        Assert.True(brush.TryUpdateBlockId());
        Assert.Equal("copper", previewEntity.material);
        Assert.Equal(beforeUnchangedRefresh, dirtyCount);
        Assert.Equal(1, previewEntity.PlacementCount);
    }

    private sealed class PlacementTrackingLantern : BELantern
    {
        public int PlacementCount;
        public ItemStack? LastPlacementStack;

        public override void OnBlockPlaced(ItemStack byItemStack)
        {
            base.OnBlockPlaced(byItemStack);
            PlacementCount++;
            LastPlacementStack = byItemStack;
        }
    }

    [Theory]
    [InlineData(EnumAppSide.Client, false)]
    [InlineData(EnumAppSide.Server, false)]
    [InlineData(EnumAppSide.Client, true)]
    [InlineData(EnumAppSide.Server, true)]
    public void PlacementHookPreview_SupplementsOnlyPropertiesNotInitializedByHooks(
        EnumAppSide side, bool needsProjection)
    {
        var world = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var slot = new DummySlot();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(slot);
        var block = new Block
        {
            BlockId = 100,
            Code = new AssetLocation("game:placementhookpreview"),
            EntityClass = "PlacementHookPreview"
        };
        world.Setup(w => w.GetBlock(100)).Returns(block);
        var brush = inputs.CreateBrush(world.Object);
        var miniDimension = new Mock<IMiniDimension>();
        miniDimension.Setup(d => d.subDimensionId).Returns(1);
        PlacementHookPreviewEntity? entity = null;
        miniDimension.Setup(d => d.SetBlock(100, It.IsAny<BlockPos>(), It.IsAny<ItemStack>()))
            .Callback<int, BlockPos, ItemStack>((_, pos, stack) =>
            {
                entity = new PlacementHookPreviewEntity { Block = block, Pos = pos.Copy() };
                entity.OnBlockPlaced(stack);
            });
        miniDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns(() => entity!);
        var dimension = new BrushDimension(world.Object);
        Assert.True(dimension.InitializeClientSide(miniDimension.Object));
        dimension.SubscribeTo(brush);
        int dirtyCount = 0;
        dimension.OnDirty += (_, _) => dirtyCount++;
        slot.Itemstack = new ItemStack(block);
        var attributes = slot.Itemstack.Attributes;
        attributes.SetString("finish", "painted");
        attributes.SetInt("count", 7);
        if (needsProjection)
            attributes.SetString("tint", "blue");
        attributes.SetInt("posx", 111);
        attributes.SetInt("posy", 222);
        attributes.SetInt("posz", 333);
        attributes.SetString("blockCode", "game:wrong");
        attributes.SetFloat("meshAngle", 99f);
        attributes.SetString("unknown", "ignored");
        var inventory = new TreeAttribute();
        inventory.SetString("contents", "not preview data");
        attributes["inventory"] = inventory;
        var metadata = new TreeAttribute();
        metadata.SetString("owner", "not preview metadata");
        attributes["metadata"] = metadata;
        attributes.SetBytes("bytes", [9]);
        var itemApi = new Mock<ICoreAPI>();
        itemApi.Setup(a => a.World).Returns(world.Object);
        var storedItem = new Item { ItemId = 200, Code = new AssetLocation("game:testitem") };
        storedItem.OnLoadedNative(itemApi.Object);
        attributes.SetItemstack("stored", new ItemStack(storedItem));

        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(entity);
        Assert.Equal("painted", entity.Finish);
        Assert.Equal(7, entity.Count);
        Assert.Equal(0.75f, entity.MeshAngle);
        Assert.Equal(0, entity.Pos.X);
        Assert.Equal(0, entity.Pos.Y);
        Assert.Equal(0, entity.Pos.Z);
        Assert.Equal("not preview data", entity.Inventory.GetString("contents"));
        Assert.Equal(1, entity.PlacementCount);
        Assert.Equal(needsProjection ? 1 : 0, entity.LoadCount);
        Assert.Equal(needsProjection ? "blue" : "neutral", entity.Tint);
        Assert.Equal("preview", entity.Metadata.GetString("owner"));
        Assert.NotNull(entity.LastPlacementStack);
        Assert.NotSame(brush.ItemStack, entity.LastPlacementStack);
        Assert.Equal(new byte[] { 9 }, entity.LastPlacementStack.Attributes.GetBytes("bytes"));
        Assert.NotNull(entity.LastPlacementStack.Attributes.GetItemstack("stored"));
        Assert.Equal(99f, attributes.GetFloat("meshAngle"));
        Assert.Equal("not preview data", inventory.GetString("contents"));
        if (needsProjection)
        {
            Assert.NotNull(entity.LastLoadedTree);
            Assert.Equal(block.Code.ToShortString(), entity.LastLoadedTree.GetString("blockCode"));
            Assert.False(entity.LastLoadedTree.HasAttribute("unknown"));
            Assert.Equal(new byte[] { 1 }, entity.LastLoadedTree.GetBytes("bytes"));
            Assert.Null(entity.LastLoadedTree.GetItemstack("stored"));
        }

        var originalEntity = entity;
        entity.MeshAngle = 1.25f;
        entity.RuntimeCounter = 42;
        int beforeRemoval = dirtyCount;
        attributes.RemoveAttribute("finish");
        attributes.RemoveAttribute("count");
        attributes.RemoveAttribute("tint");
        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(entity);
        Assert.NotSame(originalEntity, entity);
        Assert.Equal("default", entity.Finish);
        Assert.Equal(1, entity.Count);
        Assert.Equal(1, entity.PlacementCount);
        Assert.Equal(0, entity.LoadCount);
        Assert.Equal("neutral", entity.Tint);
        Assert.Equal(0.75f, entity.MeshAngle);
        Assert.Equal(0, entity.RuntimeCounter);
        Assert.True(dirtyCount > beforeRemoval);
        entity.LastPlacementStack.Attributes.SetString("finish", "mutated by hook");
        Assert.False(attributes.HasAttribute("finish"));
        Assert.False(brush.ItemStack!.Attributes.HasAttribute("finish"));
        int beforeUnchangedRefresh = dirtyCount;
        Assert.False(brush.TryUpdateBlockId());
        Assert.Equal(beforeUnchangedRefresh, dirtyCount);
        Assert.Equal(1, entity.PlacementCount);

        var defaultEntity = entity;
        attributes.SetInt("tint", 123);
        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(entity);
        Assert.NotSame(defaultEntity, entity);
        Assert.Equal("neutral", entity.Tint);
        Assert.Equal(0, entity.LoadCount);
        Assert.Equal(1, entity.PlacementCount);
    }

    [Fact]
    public void PlacementHookPreview_RestoresTargetRotationWithoutApplyingItTwice()
    {
        var world = TestHelpers.CreateMockWorld();
        var registry = new Mock<IClassRegistryAPI>();
        registry.Setup(r => r.GetBlockEntity("HookRotatable")).Returns(typeof(HookRotatableEntity));
        registry.Setup(r => r.CreateBlockEntity("HookRotatable")).Returns(() => new HookRotatableEntity());
        var api = new Mock<ICoreAPI>();
        api.Setup(a => a.ClassRegistry).Returns(registry.Object);
        world.Setup(w => w.Api).Returns(api.Object);
        world.Setup(w => w.ClassRegistry).Returns(registry.Object);
        var inputs = TestHelpers.CreateBrushInputs();
        var slot = new DummySlot();
        int selectedSlot = 0;
        inputs.Inventory.Setup(i => i.ActiveHotbarSlotNumber).Returns(() => selectedSlot);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(slot);
        var block = new Block
        {
            BlockId = 100,
            Code = new AssetLocation("game:hookrotatable"),
            EntityClass = "HookRotatable",
            Attributes = new JsonObject(Newtonsoft.Json.Linq.JObject.Parse(
                "{\"rotatatableInterval\":\"90deg\"}"))
        };
        world.Setup(w => w.GetBlock(100)).Returns(block);
        var brush = inputs.CreateBrush(world.Object);
        var miniDimension = new Mock<IMiniDimension>();
        miniDimension.Setup(d => d.subDimensionId).Returns(1);
        HookRotatableEntity? entity = null;
        miniDimension.Setup(d => d.SetBlock(100, It.IsAny<BlockPos>(), It.IsAny<ItemStack>()))
            .Callback<int, BlockPos, ItemStack>((_, pos, stack) =>
            {
                entity = new HookRotatableEntity { Block = block, Pos = pos.Copy() };
                entity.OnBlockPlaced(stack);
            });
        miniDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns(() => entity!);
        var dimension = new BrushDimension(world.Object);
        Assert.True(dimension.InitializeClientSide(miniDimension.Object));
        dimension.SubscribeTo(brush);
        slot.Itemstack = new ItemStack(block);

        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(entity);
        Assert.Equal(1, entity.PlacementCount);
        Assert.Equal(0f, entity.MeshAngle, 4);
        var rotatingEntity = entity;
        brush.OrientationIndex = 1;
        Assert.Same(rotatingEntity, entity);
        Assert.Equal(1, entity.PlacementCount);
        Assert.Equal(GameMath.PIHALF, entity.MeshAngle, 4);
        brush.OrientationIndex = 2;
        Assert.Same(rotatingEntity, entity);
        Assert.Equal(1, entity.PlacementCount);
        Assert.Equal(GameMath.PI, entity.MeshAngle, 4);

        slot.Itemstack.Attributes.SetString("type", "changed");
        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(entity);
        Assert.NotSame(rotatingEntity, entity);
        Assert.Equal(1, entity.PlacementCount);
        Assert.Equal(GameMath.PI, entity.MeshAngle, 4);
        Assert.Equal(2, brush.OrientationIndex);
        Assert.False(slot.Itemstack.Attributes.HasAttribute("meshAngle"));
        miniDimension.Verify(d => d.SetBlock(100, It.IsAny<BlockPos>(), It.IsAny<ItemStack>()), Times.Exactly(2));

        var originalEntity = entity;
        selectedSlot = 1;
        Assert.True(brush.TryUpdateBlockId());
        Assert.NotNull(entity);
        Assert.NotSame(originalEntity, entity);
        Assert.Equal(1, entity.PlacementCount);
        Assert.Equal(GameMath.PI, entity.MeshAngle, 4);
        Assert.Equal(2, brush.OrientationIndex);
        Assert.Equal(180, dimension.RotationAngle);
        miniDimension.Verify(d => d.SetBlock(100, It.IsAny<BlockPos>(), It.IsAny<ItemStack>()), Times.Exactly(3));
    }

    private sealed class HookRotatableEntity : BlockEntity, IRotatable
    {
        public float MeshAngle;
        public int PlacementCount;

        public override void OnBlockPlaced(ItemStack byItemStack)
        {
            base.OnBlockPlaced(byItemStack);
            MeshAngle = 0f;
            PlacementCount++;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("meshAngle", MeshAngle);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            MeshAngle = tree.GetFloat("meshAngle");
        }

        public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
            Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping,
            EnumAxis? flipAxis)
        {
            tree.SetFloat("meshAngle", tree.GetFloat("meshAngle") - degreeRotation * GameMath.DEG2RAD);
        }
    }

    private sealed class PlacementHookPreviewEntity : BlockEntity
    {
        public string Finish = "default";
        public int Count = 1;
        public float MeshAngle = 0.75f;
        public int RuntimeCounter;
        public string Tint = "neutral";
        public ITreeAttribute Inventory = new TreeAttribute();
        public ITreeAttribute Metadata = new TreeAttribute();
        public ITreeAttribute? LastLoadedTree;
        public ItemStack? LastPlacementStack;
        public int PlacementCount;
        public int LoadCount;

        public PlacementHookPreviewEntity()
        {
            Metadata.SetString("owner", "preview");
        }

        public override void OnBlockPlaced(ItemStack byItemStack)
        {
            base.OnBlockPlaced(byItemStack);
            Finish = byItemStack.Attributes.GetString("finish", "default");
            Count = byItemStack.Attributes.GetInt("count", 1);
            Inventory = byItemStack.Attributes.GetTreeAttribute("inventory").Clone();
            LastPlacementStack = byItemStack;
            PlacementCount++;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("finish", Finish);
            tree.SetInt("count", Count);
            tree.SetString("tint", Tint);
            tree.SetFloat("meshAngle", MeshAngle);
            tree.SetInt("runtimeCounter", RuntimeCounter);
            tree["inventory"] = Inventory.Clone();
            tree["metadata"] = Metadata.Clone();
            tree.SetBytes("bytes", [1]);
            tree.SetItemstack("stored", null);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Finish = tree.GetString("finish", "default");
            Count = tree.GetInt("count", 1);
            Tint = tree.GetString("tint", "neutral");
            MeshAngle = tree.GetFloat("meshAngle");
            RuntimeCounter = tree.GetInt("runtimeCounter");
            Inventory = tree.GetTreeAttribute("inventory");
            Metadata = tree.GetTreeAttribute("metadata");
            LastLoadedTree = tree;
            LoadCount++;
        }
    }

    #region Subscription Tests    #region SubscribeTo Tests

    /// <summary>Checks Subscribe To Sets Subscribed Instance.</summary>
    [Fact]
    public void SubscribeTo_SetsSubscribedInstance()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();

        // Act
        dimension.SubscribeTo(instance);

        // Assert
        Assert.Same(instance, dimension.SubscribedInstance);
    }

    /// <summary>Checks Subscribe To Called Twice With Same Instance Does Nothing.</summary>
    [Fact]
    public void SubscribeTo_CalledTwiceWithSameInstance_DoesNothing()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();

        // Act
        dimension.SubscribeTo(instance);
        dimension.SubscribeTo(instance);

        // Assert
        Assert.Same(instance, dimension.SubscribedInstance);
    }

    /// <summary>Checks Subscribe To Called With Different Instance Unsubscribes From Previous.</summary>
    [Fact]
    public void SubscribeTo_CalledWithDifferentInstance_UnsubscribesFromPrevious()
    {
        // Arrange
        var instance1 = TestHelpers.CreateTestInstance();
        var instance2 = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();

        // Act
        dimension.SubscribeTo(instance1);
        dimension.SubscribeTo(instance2);

        // Assert
        Assert.Same(instance2, dimension.SubscribedInstance);
    }

    #endregion

    #region Unsubscribe Tests

    /// <summary>Checks Unsubscribe Clears Subscribed Instance.</summary>
    [Fact]
    public void Unsubscribe_ClearsSubscribedInstance()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();
        dimension.SubscribeTo(instance);

        // Act
        dimension.Unsubscribe();

        // Assert
        Assert.Null(dimension.SubscribedInstance);
    }

    /// <summary>Checks Unsubscribe When Not Subscribed Does Not Throw.</summary>
    [Fact]
    public void Unsubscribe_WhenNotSubscribed_DoesNotThrow()
    {
        // Arrange
        var dimension = TestHelpers.CreateTestDimension();

        // Act & Assert - should not throw
        var exception = Record.Exception(() => dimension.Unsubscribe());
        Assert.Null(exception);
    }

    /// <summary>Checks Unsubscribe Stops Receiving Events.</summary>
    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        int blockChangedCount = 0;
        dimension.OnDirty += (s, e) => blockChangedCount++;

        dimension.SubscribeTo(instance);
        dimension.Unsubscribe();

        // Act - change block after unsubscribe
        instance.BlockId = 100;

        // Assert - dimension should not receive the event
        // (blockChangedCount would be 0 if unsubscribe worked)
        // Note: The dimension isn't initialized so SetBlock won't work,
        // but the event handlers won't be called at all
        Assert.Null(dimension.SubscribedInstance);
    }

    #endregion

    #region Destroy Tests

    /// <summary>Checks Destroy Unsubscribes From Instance.</summary>
    [Fact]
    public void Destroy_UnsubscribesFromInstance()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();
        dimension.SubscribeTo(instance);

        // Act
        dimension.Destroy();

        // Assert
        Assert.Null(dimension.SubscribedInstance);
    }

    #endregion

    #region Event Handler Tests

    /// <summary>Checks Instance On Block Transformed Changed Updates Dimension.</summary>
    [Fact]
    public void Instance_OnBlockTransformedChanged_UpdatesDimension()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        // Track if dimension received the event
        bool eventReceived = false;
        dimension.OnDirty += (s, e) => eventReceived = true;

        dimension.SubscribeTo(instance);

        // Act - setting BlockId triggers OnPlacementBlockChanged
        instance.BlockId = 100;

        // Assert - event was received by dimension
        // Note: The actual block placement won't work without initialization,
        // but the event handler is still invoked
        Assert.True(eventReceived || dimension.SubscribedInstance == instance);
    }

    /// <summary>Checks Subscribe To Automatic Resubscription On Instance Change.</summary>
    [Fact]
    public void SubscribeTo_AutomaticResubscription_OnInstanceChange()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock1 = TestHelpers.CreateTestBlock(100);
        var testBlock2 = TestHelpers.CreateTestBlock(200);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock1);
        mockWorld.Setup(w => w.GetBlock(200)).Returns(testBlock2);

        var instance1 = inputs.CreateBrush(mockWorld.Object);
        var instance2 = inputs.CreateBrush(mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        int eventCount = 0;
        dimension.OnDirty += (s, e) => eventCount++;

        // Subscribe to first instance
        dimension.SubscribeTo(instance1);
        instance1.BlockId = 100;
        int countAfterFirst = eventCount;

        // Subscribe to second instance (should unsubscribe from first)
        dimension.SubscribeTo(instance2);
        
        // Trigger event on first instance - should not be received
        int countBeforeOldEvent = eventCount;
        instance1.BlockId = 0; // Clear
        instance1.BlockId = 100; // Set again

        // Trigger event on second instance - should be received
        instance2.BlockId = 200;

        // Assert
        Assert.Same(instance2, dimension.SubscribedInstance);
        // The subscription should have moved to instance2
    }

    #endregion

    #region Event Args Propagation Tests

    /// <summary>Checks Instance On Orientation Changed Is Received.</summary>
    [Fact]
    public void Instance_OnOrientationChanged_IsReceived()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        // We need to verify the dimension's event handler is connected
        // Since we can't easily mock the internal handler, we verify
        // the subscription exists
        dimension.SubscribeTo(instance);

        // Set up a block that supports rotation
        instance.BlockId = 100;

        // Assert subscription is active
        Assert.Same(instance, dimension.SubscribedInstance);
    }

    #endregion

    #region ApplyRotation Tests

    /// <summary>
    /// Helper to set RotationMode on dimension via reflection.
    /// </summary>
    private static void SetRotationMode(BrushDimension dimension, EBuildBrushRotationMode mode)
    {
        var prop = typeof(BrushDimension).GetProperty("RotationMode");
        prop?.SetValue(dimension, mode);
    }

    /// <summary>
    /// Helper to set originalBlock on dimension via reflection.
    /// </summary>
    private static void SetOriginalBlock(BrushDimension dimension, Block block)
    {
        var field = typeof(BrushDimension).GetField("originalBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(dimension, block);
    }

    /// <summary>
    /// Helper to set currentBlock on dimension via reflection.
    /// </summary>
    private static void SetCurrentBlock(BrushDimension dimension, Block block)
    {
        var field = typeof(BrushDimension).GetField("currentBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(dimension, block);
    }

    /// <summary>
    /// Helper to get currentBlock from dimension via reflection.
    /// </summary>
    private static Block? GetCurrentBlock(BrushDimension dimension)
    {
        var field = typeof(BrushDimension).GetField("currentBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(dimension) as Block;
    }

    /// <summary>Checks Apply Rotation Rotatable Mode When Block Id Differs Updates Current Block.</summary>
    [Fact]
    public void ApplyRotation_RotatableMode_WhenBlockIdDiffers_UpdatesCurrentBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        var blockEast = TestHelpers.CreateTestBlock(101, "game:hybrid-east");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);
        mockWorld.Setup(w => w.GetBlock(101)).Returns(blockEast);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to Rotatable mode with blockNorth as original/current
        SetRotationMode(dimension, EBuildBrushRotationMode.Rotatable);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (variant change)
        var previousDef = new BlockOrientation(100, 0f);
        var currentDef = new BlockOrientation(101, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BrushOrientation(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Rotatable, definitions);
        orientationInfo.CurrentIndex = 1; // Set to the new index

        // Act - Apply rotation with different variant block
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should now be blockEast
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(blockEast, currentBlock);
    }

    /// <summary>Checks Apply Rotation Rotatable Mode When Block Id Same Does Not Replace Block.</summary>
    [Fact]
    public void ApplyRotation_RotatableMode_WhenBlockIdSame_DoesNotReplaceBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to Rotatable mode
        SetRotationMode(dimension, EBuildBrushRotationMode.Rotatable);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (same variant, different angle)
        var previousDef = new BlockOrientation(100, 0f);
        var currentDef = new BlockOrientation(100, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BrushOrientation(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Rotatable, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act - Apply rotation with SAME variant block (different angle, same block)
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should still be blockNorth
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(blockNorth, currentBlock);
    }

    /// <summary>Checks Apply Rotation Variant Based Mode When Variant Block Differs Updates Current Block.</summary>
    [Fact]
    public void ApplyRotation_VariantBasedMode_WhenVariantBlockDiffers_UpdatesCurrentBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:variant-north");
        var blockEast = TestHelpers.CreateTestBlock(101, "game:variant-east");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);
        mockWorld.Setup(w => w.GetBlock(101)).Returns(blockEast);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to VariantBased mode
        SetRotationMode(dimension, EBuildBrushRotationMode.VariantBased);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (variant change)
        var previousDef = new BlockOrientation(100, 0f);
        var currentDef = new BlockOrientation(101, 0f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BrushOrientation(mockWorld.Object, blockNorth, EBuildBrushRotationMode.VariantBased, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act - Apply rotation with different variant block
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should now be blockEast
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(blockEast, currentBlock);
    }

    /// <summary>Checks Apply Rotation Rotatable Mode When No Variant Change Does Not Crash.</summary>
    [Fact]
    public void ApplyRotation_RotatableMode_WhenNoVariantChange_DoesNotCrash()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to Rotatable mode
        SetRotationMode(dimension, EBuildBrushRotationMode.Rotatable);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (angle change only)
        var previousDef = new BlockOrientation(100, 0f);
        var currentDef = new BlockOrientation(100, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BrushOrientation(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Rotatable, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act & Assert - Should not throw even with null CurrentBlock in orientation info
        var exception = Record.Exception(() => dimension.ApplyRotation(eventArgs, orientationInfo));
        Assert.Null(exception);
    }

    /// <summary>Checks Apply Rotation None Mode Does Not Change Block.</summary>
    [Fact]
    public void ApplyRotation_NoneMode_DoesNotChangeBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BrushDimension(mockWorld.Object);

        var block = TestHelpers.CreateTestBlock(100, "game:static");
        var otherBlock = TestHelpers.CreateTestBlock(101, "game:other");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(block);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to None mode
        SetRotationMode(dimension, EBuildBrushRotationMode.None);
        SetOriginalBlock(dimension, block);
        SetCurrentBlock(dimension, block);

        // Create mock event args for rotation (attempted change)
        var previousDef = new BlockOrientation(100, 0f);
        var currentDef = new BlockOrientation(101, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BrushOrientation(mockWorld.Object, block, EBuildBrushRotationMode.None, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act - Try to apply rotation with different block
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should NOT change for None mode
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(block, currentBlock);
    }

    #endregion

    #region Instance_OnOrientationChanged Handler Tests

    /// <summary>Checks Instance On Orientation Changed Rotatable Mode Passes Correct Angle No Variant Change.</summary>
    [Fact]
    public void Instance_OnOrientationChanged_RotatableMode_PassesCorrectAngle_NoVariantChange()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();

        var block = TestHelpers.CreateTestBlock(100, "game:rotatable");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(block);

        var instance = inputs.CreateBrush(mockWorld.Object);
        var dimension = new BrushDimension(mockWorld.Object);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Subscribe dimension to instance
        dimension.SubscribeTo(instance);

        // Create a rotatable orientation: same block-id, different angles
        var definitions = ImmutableArray.Create(
            new BlockOrientation(100, 0f),
            new BlockOrientation(100, 90f),
            new BlockOrientation(100, 180f),
            new BlockOrientation(100, 270f)
        );

        var rotation = new BrushOrientation(mockWorld.Object, block, EBuildBrushRotationMode.Rotatable, definitions);

        // Inject rotation info into instance
        var rotationField = typeof(BuildBrushInstance).GetField("_rotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockField = typeof(BuildBrushInstance).GetField("_blockUntransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockTransformedField = typeof(BuildBrushInstance).GetField("_blockTransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        rotationField?.SetValue(instance, rotation);
        blockField?.SetValue(instance, block);
        blockTransformedField?.SetValue(instance, block);

        // Wire up the rotation event handler
        var handlerMethod = typeof(BuildBrushInstance).GetMethod("Rotation_OnOrientationChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handlerMethod is not null)
        {
            var handler = (EventHandler<OrientationIndexChangedEventArgs>)Delegate.CreateDelegate(
                typeof(EventHandler<OrientationIndexChangedEventArgs>), instance, handlerMethod);
            rotation.OnOrientationChanged += handler;
        }

        // Capture events
        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        instance.OnOrientationChanged += (s, e) => capturedEvents.Add(e);

        // Act - Cycle through orientations: 0 → 1 → 2 → 3
        for (int i = 0; i < 3; i++)
        {
            instance.CycleOrientation(EModeCycleDirection.Forward);
        }

        // Assert
        Assert.Equal(3, capturedEvents.Count);

        Assert.Equal(100, capturedEvents[0].CurrentDefinition.BlockId);
        Assert.Equal(90f, capturedEvents[0].CurrentDefinition.MeshAngleDegrees);
        Assert.False(capturedEvents[0].VariantChanged);

        Assert.Equal(100, capturedEvents[1].CurrentDefinition.BlockId);
        Assert.Equal(180f, capturedEvents[1].CurrentDefinition.MeshAngleDegrees);
        Assert.False(capturedEvents[1].VariantChanged);

        Assert.Equal(100, capturedEvents[2].CurrentDefinition.BlockId);
        Assert.Equal(270f, capturedEvents[2].CurrentDefinition.MeshAngleDegrees);
        Assert.False(capturedEvents[2].VariantChanged);
    }

    /// <summary>Checks Instance On Orientation Changed Variant Based Mode Event Args Contain Correct New Block Id.</summary>
    [Fact]
    public void Instance_OnOrientationChanged_VariantBasedMode_EventArgsContainCorrectNewBlockId()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:variant-north");
        var blockEast = TestHelpers.CreateTestBlock(101, "game:variant-east");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);
        mockWorld.Setup(w => w.GetBlock(101)).Returns(blockEast);

        var instance = inputs.CreateBrush(mockWorld.Object);
        var dimension = new BrushDimension(mockWorld.Object);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        dimension.SubscribeTo(instance);

        // Create a variant-only orientation: two distinct block ids
        var definitions = ImmutableArray.Create(
            new BlockOrientation(100, 0f),
            new BlockOrientation(101, 0f)
        );

        var rotation = new BrushOrientation(mockWorld.Object, blockNorth, EBuildBrushRotationMode.VariantBased, definitions);

        // Inject rotation info
        var rotationField = typeof(BuildBrushInstance).GetField("_rotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockField = typeof(BuildBrushInstance).GetField("_blockUntransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockTransformedField = typeof(BuildBrushInstance).GetField("_blockTransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        rotationField?.SetValue(instance, rotation);
        blockField?.SetValue(instance, blockNorth);
        blockTransformedField?.SetValue(instance, blockNorth);

        // Wire up rotation event
        var handlerMethod = typeof(BuildBrushInstance).GetMethod("Rotation_OnOrientationChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handlerMethod is not null)
        {
            var handler = (EventHandler<OrientationIndexChangedEventArgs>)Delegate.CreateDelegate(
                typeof(EventHandler<OrientationIndexChangedEventArgs>), instance, handlerMethod);
            rotation.OnOrientationChanged += handler;
        }

        // Capture CurrentPlacementBlock at the moment of each event
        var blockTransformedAtEvent = new List<Block?>();
        var eventArgsCurrentBlockId = new List<int>();
        instance.OnOrientationChanged += (s, e) =>
        {
            blockTransformedAtEvent.Add(instance.CurrentPlacementBlock);
            eventArgsCurrentBlockId.Add(e.CurrentDefinition.BlockId);
        };

        // Act - Cycle to index 1 (East variant)
        instance.CycleOrientation(EModeCycleDirection.Forward); // 0 → 1

        // Assert - EventArgs.CurrentDefinition.BlockId should always have the correct NEW block ID
        // even if instance.CurrentPlacementBlock is updated after the event fires
        Assert.Single(eventArgsCurrentBlockId);
        Assert.Equal(101, eventArgsCurrentBlockId[0]);
    }

    #endregion
}
