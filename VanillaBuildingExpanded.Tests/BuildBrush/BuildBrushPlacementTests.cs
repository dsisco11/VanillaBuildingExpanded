using System.Collections.Generic;

using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Integration tests for block placement logic in the BuildBrush system.
/// Verifies that BuildBrushInstance correctly populates ItemStack attributes
/// and CurrentPlacementBlock when orientation changes.
/// 
/// These tests focus on the integration between BuildBrushInstance and
/// BuildBrushOrientationInfo, specifically testing that:
/// 1. ItemStack.Attributes["meshAngle"] is set correctly for IRotatable blocks
/// 2. CurrentPlacementBlock has the correct variant block ID
/// 
/// Note: Unit tests for BlockOrientationResolver and BuildBrushOrientationInfo
/// are in their respective test files.
/// </summary>
public class BuildBrushPlacementTests
{
    #region Test Data

    /// <summary>
    /// Rotation intervals with their expected step counts and angles.
    /// Format: (intervalString, intervalDegrees, expectedStepCount, expectedAngles[])
    /// </summary>
    public static IEnumerable<object[]> RotationIntervalData =>
    [
        ["22.5deg", 22.5f, 16, new float[] { 0f, 22.5f, 45f, 67.5f, 90f, 112.5f, 135f, 157.5f, 180f, 202.5f, 225f, 247.5f, 270f, 292.5f, 315f, 337.5f }],
        ["45deg", 45f, 8, new float[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f }],
        ["90deg", 90f, 4, new float[] { 0f, 90f, 180f, 270f }],
    ];

    /// <summary>
    /// Variant rotation test data.
    /// Format: (variantKey, variantValues[], startBlockId)
    /// </summary>
    public static IEnumerable<object[]> VariantRotationData =>
    [
        ["rot", new[] { "0", "90", "180", "270" }, 100],
        ["horizontalorientation", new[] { "north", "east", "south", "west" }, 200],
        ["orientation", new[] { "up", "down", "horizontal" }, 300],
    ];

    #endregion

    #region Test Helpers

    /// <summary>Checks Create Mock World.</summary>
    private static Mock<IWorldAccessor> CreateMockWorld()
    {
        var mockWorld = new Mock<IWorldAccessor>();
        var mockApi = new Mock<ICoreAPI>();
        var mockClassRegistry = new Mock<IClassRegistryAPI>();
        var mockLogger = new Mock<ILogger>();
        var mockBlockAccessor = new Mock<IBlockAccessor>();

        mockWorld.Setup(w => w.Api).Returns(mockApi.Object);
        mockApi.Setup(a => a.ClassRegistry).Returns(mockClassRegistry.Object);
        mockWorld.Setup(w => w.Logger).Returns(mockLogger.Object);
        mockWorld.Setup(w => w.Side).Returns(EnumAppSide.Server);
        mockWorld.Setup(w => w.BlockAccessor).Returns(mockBlockAccessor.Object);
        mockWorld.Setup(w => w.ClassRegistry).Returns(mockClassRegistry.Object);

        return mockWorld;
    }

    /// <summary>Checks Create Variant Block.</summary>
    private static Block CreateVariantBlock(int blockId, string code, string variantKey, string variantValue)
    {
        var block = new Block
        {
            BlockId = blockId,
            Code = new AssetLocation(code)
        };
        block.VariantStrict[variantKey] = variantValue;
        return block;
    }

    /// <summary>Checks Create Rotatable Block.</summary>
    private static Block CreateRotatableBlock(int blockId, string code, string entityClass, string interval)
    {
        var block = new Block
        {
            BlockId = blockId,
            Code = new AssetLocation(code),
            EntityClass = entityClass,
            Attributes = new JsonObject(new Newtonsoft.Json.Linq.JObject
            {
                ["rotatatableInterval"] = interval
            })
        };
        return block;
    }

    /// <summary>Checks Create Hybrid Block.</summary>
    private static Block CreateHybridBlock(int blockId, string code, string variantKey, string variantValue, string entityClass, string interval)
    {
        var block = new Block
        {
            BlockId = blockId,
            Code = new AssetLocation(code),
            EntityClass = entityClass
        };
        block.VariantStrict[variantKey] = variantValue;
        block.Attributes = new JsonObject(new Newtonsoft.Json.Linq.JObject
        {
            ["rotatatableInterval"] = interval
        });
        return block;
    }

    private static Block[] CreateVariantBlocks(string baseCode, string variantKey, string[] variantValues, int startBlockId)
    {
        var blocks = new Block[variantValues.Length];
        for (int i = 0; i < variantValues.Length; i++)
        {
            blocks[i] = CreateVariantBlock(startBlockId + i, $"{baseCode}-{variantValues[i]}", variantKey, variantValues[i]);
        }
        return blocks;
    }

    private static Block[] CreateHybridVariantBlocks(string baseCode, string variantKey, int variantCount, int startBlockId, string entityClass, string interval)
    {
        var blocks = new Block[variantCount];
        for (int i = 0; i < variantCount; i++)
        {
            blocks[i] = CreateHybridBlock(startBlockId + i, $"{baseCode}-{i}", variantKey, i.ToString(), entityClass, interval);
        }
        return blocks;
    }

    /// <summary>Checks Setup Block Lookup.</summary>
    private static void SetupBlockLookup(Mock<IWorldAccessor> mockWorld, params Block[] blocks)
    {
        foreach (var block in blocks)
        {
            mockWorld.Setup(w => w.GetBlock(block.BlockId)).Returns(block);
        }
    }

    /// <summary>Checks Setup Variant Search.</summary>
    private static void SetupVariantSearch(Mock<IWorldAccessor> mockWorld, Block[] variants)
    {
        mockWorld.Setup(w => w.SearchBlocks(It.IsAny<AssetLocation>())).Returns(variants);
    }

    /// <summary>Checks Setup Rotatable Entity.</summary>
    private static void SetupRotatableEntity(Mock<IWorldAccessor> mockWorld, string entityClass)
    {
        var mockClassRegistry = new Mock<IClassRegistryAPI>();
        mockClassRegistry.Setup(c => c.GetBlockEntity(entityClass)).Returns(typeof(TestRotatableBlockEntity));
        mockClassRegistry.Setup(c => c.CreateBlockEntity(entityClass)).Returns(() => new TestRotatableBlockEntity());

        var mockApi = new Mock<ICoreAPI>();
        mockApi.Setup(a => a.ClassRegistry).Returns(mockClassRegistry.Object);
        mockWorld.Setup(w => w.Api).Returns(mockApi.Object);
        mockWorld.Setup(w => w.ClassRegistry).Returns(mockClassRegistry.Object);
    }

    private class TestRotatableBlockEntity : BlockEntity, IRotatable
    {
        /// <summary>Checks On Transformed.</summary>
        public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
            Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping,
            EnumAxis? flipAxis)
        {
            tree.SetFloat("meshAngle", degreeRotation * GameMath.DEG2RAD);
        }
    }

    #endregion

    #region BuildBrushInstance ItemStack MeshAngle Tests

    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void LanternPlacementRequest_PreservesMaterialLiningAndGlass(EnumAppSide side)
    {
        var world = CreateMockWorld();
        world.Setup(w => w.Side).Returns(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var lanterns = new BlockLantern[2];
        string[] orientations = ["up", "north"];
        for (int i = 0; i < lanterns.Length; i++)
        {
            lanterns[i] = new BlockLantern
            {
                BlockId = 100 + i,
                Code = new AssetLocation($"game:lantern-{orientations[i]}")
            };
            lanterns[i].VariantStrict["orientation"] = orientations[i];
        }
        SetupBlockLookup(world, lanterns);
        SetupVariantSearch(world, lanterns);
        var source = new ItemStack(lanterns[0]);
        source.Attributes.SetString("material", "tinbronze");
        source.Attributes.SetString("lining", "silver");
        source.Attributes.SetString("glass", "quartz");
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(source));
        var brush = inputs.CreateBrush(world.Object);
        brush.OrientationIndex = 1;
        brush.Position = new BlockPos(1, 2, 3);
        var selection = new BlockSelection
        {
            Position = brush.Position.Copy(),
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };
        Assert.True(brush.TryBuildPlacementRequest(selection, out var request));
        Assert.NotNull(request);
        Assert.Equal(lanterns[1], request.PlacementBlock);
        Assert.Same(lanterns[1], request.ItemStack.Block);
        Assert.Equal("tinbronze", request.ItemStack.Attributes.GetString("material"));
        Assert.Equal("silver", request.ItemStack.Attributes.GetString("lining"));
        Assert.Equal("quartz", request.ItemStack.Attributes.GetString("glass"));
        Assert.Same(lanterns[0], source.Block);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceAttributesChanged_WithSameBlockId_RefreshesWithoutResettingOrientation(bool mutateInPlace)
    {
        var world = CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var variants = CreateVariantBlocks("game:lantern", "orientation", ["up", "north"], 100);
        SetupBlockLookup(world, variants);
        SetupVariantSearch(world, variants);
        var source = new ItemStack(variants[0]);
        source.Attributes.SetString("material", "copper");
        source.Attributes.SetString("lining", "gold");
        var details = new TreeAttribute();
        details.SetString("value", "original");
        source.Attributes["details"] = details;
        var slot = new DummySlot(source);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(slot);
        var brush = inputs.CreateBrush(world.Object);
        brush.OrientationIndex = 1;

        if (!mutateInPlace)
            slot.Itemstack = source.Clone();
        slot.Itemstack.Attributes.SetString("material", "brass");
        slot.Itemstack.Attributes.RemoveAttribute("lining");
        slot.Itemstack.Attributes.GetTreeAttribute("details").SetString("value", "updated");

        Assert.True(brush.TryUpdateBlockId());
        Assert.Equal(1, brush.OrientationIndex);
        Assert.NotNull(brush.ItemStack);
        Assert.Same(variants[1], brush.ItemStack.Block);
        Assert.Equal("brass", brush.ItemStack.Attributes.GetString("material"));
        Assert.False(brush.ItemStack.Attributes.HasAttribute("lining"));
        Assert.Equal("updated", brush.ItemStack.Attributes.GetTreeAttribute("details").GetString("value"));
        Assert.False(brush.TryUpdateBlockId());
    }

    [Fact]
    public void PreparedStack_ClonesAllSourceAttributesBeforeApplyingRotation()
    {
        var world = CreateMockWorld();
        var block = CreateRotatableBlock(100, "game:testblock", "TestRotatable", "90deg");
        SetupBlockLookup(world, block);
        SetupRotatableEntity(world, "TestRotatable");
        var source = new ItemStack(block);
        source.Attributes.SetString("type", "oak");
        source.Attributes.SetFloat("meshAngle", -1f);
        var details = new TreeAttribute();
        details.SetString("value", "original");
        source.Attributes["details"] = details;
        var target = new ItemStack(block);
        var rotation = BrushOrientation.Create(block, new BlockOrientationResolver(world.Object), source);

        var prepared = rotation.PrepareItemStackForPlacement(target, rotation.Definitions[1], source);
        Assert.NotNull(prepared.Attributes.GetTreeAttribute("details"));
        prepared.Attributes.GetTreeAttribute("details").SetString("value", "changed");

        Assert.Equal("oak", prepared.Attributes.GetString("type"));
        Assert.Equal(GameMath.PIHALF, prepared.Attributes.GetFloat(rotation.Definitions[1].RotationAttribute), 4);
        Assert.Equal(-1f, source.Attributes.GetFloat("meshAngle"));
        Assert.Equal("original", details.GetString("value"));
        Assert.False(target.Attributes.HasAttribute("type"));
        Assert.False(target.Attributes.HasAttribute("details"));
    }

    /// <summary>
    /// Verifies that after setting OrientationIndex on BuildBrushInstance,
    /// the ItemStack.Attributes["meshAngle"] contains the correct value in radians.
    /// This is critical because TryPlaceBrushBlock should use brush.ItemStack when placing.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationIntervalData))]
    public void BuildBrushInstance_OrientationIndexChange_ItemStackHasMeshAngle(string intervalString, float _, int expectedStepCount, float[] expectedAngles)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var block = CreateRotatableBlock(100, "game:rotatableblock", "TestRotatable", intervalString);
        SetupBlockLookup(mockWorld, block);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Verify rotation is detected
        Assert.NotNull(instance.Rotation);
        Assert.True(instance.Rotation.CanRotate);
        Assert.Equal(expectedStepCount, instance.OrientationCount);

        // Act & Assert - verify each orientation sets correct meshAngle on ItemStack
        for (int i = 0; i < expectedStepCount; i++)
        {
            instance.OrientationIndex = i;

            Assert.NotNull(instance.ItemStack);
            float expectedRadians = expectedAngles[i] * GameMath.DEG2RAD;
            float actualRadians = instance.ItemStack.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);

            Assert.Equal(expectedRadians, actualRadians, precision: 4);
        }
    }

    /// <summary>
    /// Verifies that when a block has both variants and an IRotatable entity, it is treated as Rotatable:
    /// orientation changes update meshAngle (radians) and do not depend on variant swapping.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationIntervalData))]
    public void BuildBrushInstance_VariantPlusRotatableBlock_ItemStackHasMeshAngle(string intervalString, float _, int expectedStepCount, float[] expectedAngles)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();

        // Create multiple variants, all with rotatable interval configured
        var variants = CreateHybridVariantBlocks("game:variantplusrotatable", "rot", variantCount: 4, startBlockId: 100, entityClass: "TestRotatable", interval: intervalString);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        Assert.NotNull(instance.Rotation);
        Assert.True(instance.Rotation.CanRotate);
        Assert.Equal(EBuildBrushRotationMode.Rotatable, instance.Rotation.Mode);
        Assert.Equal(expectedStepCount, instance.OrientationCount);

        // Act & Assert
        for (int i = 0; i < expectedStepCount; i++)
        {
            instance.OrientationIndex = i;

            Assert.NotNull(instance.ItemStack);
            float expectedRadians = expectedAngles[i] * GameMath.DEG2RAD;
            float actualRadians = instance.ItemStack.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);

            Assert.Equal(expectedRadians, actualRadians, precision: 4);
        }
    }

    #endregion

    #region BuildBrushInstance Placement Block Tests

    /// <summary>
    /// Verifies that BuildBrushInstance.CurrentPlacementBlock has the correct block ID
    /// after setting OrientationIndex for variant-based blocks.
    /// </summary>
    [Theory]
    [MemberData(nameof(VariantRotationData))]
    public void BuildBrushInstance_VariantBlock_BlockTransformedHasCorrectId(string variantKey, string[] variantValues, int startBlockId)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var variants = CreateVariantBlocks("game:testblock", variantKey, variantValues, startBlockId);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = startBlockId;

        // Verify rotation is detected
        Assert.NotNull(instance.Rotation);
        Assert.True(instance.Rotation.CanRotate);
        Assert.Equal(variantValues.Length, instance.OrientationCount);

        // Act & Assert - verify each orientation produces correct CurrentPlacementBlock
        for (int i = 0; i < variantValues.Length; i++)
        {
            instance.OrientationIndex = i;

            Assert.NotNull(instance.CurrentPlacementBlock);
            Assert.Equal(startBlockId + i, instance.CurrentPlacementBlock.BlockId);
        }
    }

    /// <summary>
    /// Verifies that when a block has both variants and an IRotatable entity, it is treated as Rotatable:
    /// CurrentPlacementBlock should remain the base block-id while meshAngle changes.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationIntervalData))]
    public void BuildBrushInstance_VariantPlusRotatableBlock_CurrentPlacementBlockStaysSameId(string intervalString, float intervalDegrees, int expectedStepCount, float[] expectedAngles)
    {
        // Arrange
        Assert.Equal(expectedStepCount, expectedAngles.Length);
        if (expectedAngles.Length > 1)
        {
            Assert.Equal(intervalDegrees, expectedAngles[1] - expectedAngles[0], precision: 4);
        }

        var mockWorld = CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();

        var variants = CreateHybridVariantBlocks("game:variantplusrotatable", "rot", variantCount: 4, startBlockId: 100, entityClass: "TestRotatable", interval: intervalString);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        Assert.NotNull(instance.Rotation);
        Assert.Equal(EBuildBrushRotationMode.Rotatable, instance.Rotation.Mode);

        // Act & Assert
        for (int i = 0; i < expectedStepCount; i++)
        {
            instance.OrientationIndex = i;
            Assert.NotNull(instance.CurrentPlacementBlock);
            Assert.Equal(100, instance.CurrentPlacementBlock.BlockId);
        }
    }

    #endregion
}
