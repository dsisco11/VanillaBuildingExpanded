using System.Collections.Generic;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for the block placement interception system.
/// 
/// The placement flow is:
/// 1. Player right-clicks to place a block
/// 2. Harmony patch intercepts Block.TryPlaceBlock
/// 3. BuildBrushSystem_Server.TryPlaceBrushBlock is called
/// 4. TryPlaceBrushBlock calls brush.CurrentPlacementBlock.DoPlaceBlock with an ItemStack
/// 
/// EXPECTED BEHAVIOR:
/// - For IRotatable blocks, the ItemStack passed to DoPlaceBlock MUST have
///   the correct meshAngle attribute set (in radians)
/// - For variant blocks, CurrentPlacementBlock MUST be the correct variant block
/// 
/// KNOWN BUG:
/// - TryPlaceBrushBlock currently passes the original itemstack from the harmony hook
///   instead of brush.ItemStack which has the meshAngle attribute set
/// </summary>
public class BlockPlacementInterceptTests
{
    #region Test Data

    public static IEnumerable<object[]> RotationAngleData =>
    [
        [0, 0f],
        [1, 90f],
        [2, 180f],
        [3, 270f],
    ];

    #endregion

    #region Test Helpers

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

    private static Block[] CreateVariantBlocks(string baseCode, string variantKey, string[] variantValues, int startBlockId)
    {
        var blocks = new Block[variantValues.Length];
        for (int i = 0; i < variantValues.Length; i++)
        {
            blocks[i] = CreateVariantBlock(startBlockId + i, $"{baseCode}-{variantValues[i]}", variantKey, variantValues[i]);
        }
        return blocks;
    }

    private static void SetupBlockLookup(Mock<IWorldAccessor> mockWorld, params Block[] blocks)
    {
        foreach (var block in blocks)
        {
            mockWorld.Setup(w => w.GetBlock(block.BlockId)).Returns(block);
        }
    }

    private static void SetupVariantSearch(Mock<IWorldAccessor> mockWorld, Block[] variants)
    {
        mockWorld.Setup(w => w.SearchBlocks(It.IsAny<AssetLocation>())).Returns(variants);
    }

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
        public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
            Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping,
            EnumAxis? flipAxis)
        {
            tree.SetFloat("meshAngle", degreeRotation * GameMath.DEG2RAD);
        }
    }

    #endregion

    #region TryPlaceBrushBlock - Expected Behavior Tests

    /// <summary>
    /// Documents the EXPECTED behavior: When placing an IRotatable block with rotation,
    /// the ItemStack passed to DoPlaceBlock MUST have the meshAngle attribute set.
    /// 
    /// This test verifies that BuildBrushInstance.ItemStack has the correct meshAngle,
    /// which is the ItemStack that SHOULD be passed to DoPlaceBlock.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationAngleData))]
    public void TryPlaceBrushBlock_RotatableBlock_ItemStackShouldHaveMeshAngle(int orientationIndex, float expectedAngleDegrees)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var block = CreateRotatableBlock(100, "game:rotatableblock", "TestRotatable", "90deg");
        SetupBlockLookup(mockWorld, block);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100,
            OrientationIndex = orientationIndex
        };

        // Act - Get the ItemStack that SHOULD be passed to DoPlaceBlock
        var expectedItemStack = instance.ItemStack;

        // Assert - The ItemStack should have meshAngle set correctly
        Assert.NotNull(expectedItemStack);
        float expectedRadians = expectedAngleDegrees * GameMath.DEG2RAD;
        float actualRadians = expectedItemStack.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);

        Assert.Equal(expectedRadians, actualRadians, precision: 4);
    }

    /// <summary>
    /// Documents the EXPECTED behavior: When placing a variant block with rotation,
    /// the CurrentPlacementBlock should be the correct variant.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationAngleData))]
    public void TryPlaceBrushBlock_VariantBlock_BlockTransformedShouldBeCorrectVariant(int orientationIndex, float _)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var variants = CreateVariantBlocks("game:stair", "rot", ["0", "90", "180", "270"], 100);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100,
            OrientationIndex = orientationIndex
        };

        // Act - Get the block that SHOULD be placed
        var expectedBlock = instance.CurrentPlacementBlock;

        // Assert - Should be the correct variant
        Assert.NotNull(expectedBlock);
        Assert.Equal(100 + orientationIndex, expectedBlock.BlockId);
    }

    /// <summary>
    /// Documents the EXPECTED behavior: The ItemStack.Block should match CurrentPlacementBlock
    /// to ensure consistency.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationAngleData))]
    public void TryPlaceBrushBlock_ItemStackBlockShouldMatchBlockTransformed(int orientationIndex, float _)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var variants = CreateVariantBlocks("game:stair", "rot", ["0", "90", "180", "270"], 100);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100,
            OrientationIndex = orientationIndex
        };

        // Assert - ItemStack.Block should match CurrentPlacementBlock
        Assert.NotNull(instance.ItemStack);
        Assert.NotNull(instance.CurrentPlacementBlock);
        Assert.Equal(instance.CurrentPlacementBlock.BlockId, instance.ItemStack.Block?.BlockId);
    }

    #endregion

    #region TryPlaceBrushBlock - Bug Documentation Tests

    /// <summary>
    /// This test documents the BUG in the current implementation.
    /// 
    /// TryPlaceBrushBlock receives `itemstack` from the Harmony hook (the player's hotbar item)
    /// and passes it directly to DoPlaceBlock. However, the hotbar itemstack does NOT have
    /// the meshAngle attribute set - only brush.ItemStack has it.
    /// 
    /// THE FIX: TryPlaceBrushBlock should pass brush.ItemStack instead of the itemstack parameter.
    /// </summary>
    [Fact]
    public void TryPlaceBrushBlock_BugDocumentation_HotbarItemStackLacksMeshAngle()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var block = CreateRotatableBlock(100, "game:rotatableblock", "TestRotatable", "90deg");
        SetupBlockLookup(mockWorld, block);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100,
            OrientationIndex = 1 // 90 degrees
        };

        // Simulate the hotbar itemstack (what the harmony hook passes)
        var hotbarItemStack = new ItemStack(block);
        // Note: hotbarItemStack does NOT have meshAngle set - this is the bug!

        // Assert - The hotbar itemstack does NOT have meshAngle
        float hotbarMeshAngle = hotbarItemStack.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);
        Assert.Equal(-999f, hotbarMeshAngle); // Not set!

        // But brush.ItemStack DOES have meshAngle
        float brushMeshAngle = instance.ItemStack!.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);
        float expectedRadians = 90f * GameMath.DEG2RAD;
        Assert.Equal(expectedRadians, brushMeshAngle, precision: 4);

        // THE BUG: TryPlaceBrushBlock passes hotbarItemStack instead of brush.ItemStack
        // THE FIX: Change line 80 in BuildBrushSystem_Server.cs from:
        //   brush.CurrentPlacementBlock.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
        // To:
        //   brush.CurrentPlacementBlock.DoPlaceBlock(world, byPlayer, blockSel, brush.ItemStack);
    }

    /// <summary>
    /// Documents another aspect of the bug: For variant blocks, the hotbar itemstack
    /// has the WRONG block type - it's the original block, not the rotated variant.
    /// </summary>
    [Fact]
    public void TryPlaceBrushBlock_BugDocumentation_HotbarItemStackHasWrongBlock()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var variants = CreateVariantBlocks("game:stair", "rot", ["0", "90", "180", "270"], 100);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100, // Original block (rot=0)
            OrientationIndex = 2 // Rotate to 180 degrees (block ID 102)
        };

        // Simulate the hotbar itemstack (what the harmony hook passes)
        var hotbarItemStack = new ItemStack(variants[0]); // Block ID 100

        // Assert - The hotbar itemstack has the WRONG block (original, not rotated)
        Assert.Equal(100, hotbarItemStack.Block?.BlockId);

        // But brush.ItemStack has the CORRECT block (rotated variant)
        Assert.Equal(102, instance.ItemStack!.Block?.BlockId);
        Assert.Equal(102, instance.CurrentPlacementBlock!.BlockId);

        // THE BUG: TryPlaceBrushBlock passes hotbarItemStack (block 100)
        // but calls DoPlaceBlock on CurrentPlacementBlock (block 102)
        // This creates an inconsistency that may cause issues.
    }

    #endregion

    #region ItemStack Attribute Verification Tests

    /// <summary>
    /// Verifies that brush.ItemStack is properly prepared with all necessary attributes
    /// for block placement at any orientation.
    /// </summary>
    [Theory]
    [MemberData(nameof(RotationAngleData))]
    public void BrushItemStack_AtAnyOrientation_HasAllRequiredAttributes(int orientationIndex, float expectedAngleDegrees)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var block = CreateRotatableBlock(100, "game:rotatableblock", "TestRotatable", "90deg");
        SetupBlockLookup(mockWorld, block);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100,
            OrientationIndex = orientationIndex
        };

        // Assert - brush.ItemStack should be fully prepared for DoPlaceBlock
        var brushItemStack = instance.ItemStack;
        Assert.NotNull(brushItemStack);

        // 1. Should have correct block
        Assert.Equal(100, brushItemStack.Block?.BlockId);

        // 2. Should have meshAngle set
        float expectedRadians = expectedAngleDegrees * GameMath.DEG2RAD;
        float actualRadians = brushItemStack.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);
        Assert.Equal(expectedRadians, actualRadians, precision: 4);

        // 3. Should match CurrentPlacementBlock
        Assert.Equal(instance.CurrentPlacementBlock?.BlockId, brushItemStack.Block?.BlockId);
    }

    /// <summary>
    /// Verifies that the initial orientation (index 0) has meshAngle properly set
    /// immediately after block assignment, without requiring an orientation change.
    /// </summary>
    [Fact]
    public void BrushItemStack_AtInitialOrientation_HasMeshAngleSet()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var block = CreateRotatableBlock(100, "game:rotatableblock", "TestRotatable", "90deg");
        SetupBlockLookup(mockWorld, block);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100
        };
        // Don't change OrientationIndex - test the initial state (index 0)

        // Assert - Even at initial orientation, meshAngle should be set (to 0 radians)
        var brushItemStack = instance.ItemStack;
        Assert.NotNull(brushItemStack);

        float actualRadians = brushItemStack.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);
        Assert.Equal(0f, actualRadians, precision: 4); // 0 degrees = 0 radians
    }

    #endregion
}
