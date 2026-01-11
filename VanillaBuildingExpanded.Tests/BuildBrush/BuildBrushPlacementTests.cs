using System.Collections.Generic;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

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

    /// <summary>
    /// Hybrid rotation test data combining variants and mesh angles.
    /// Format: (variantCount, intervalString, testOrientationIndex, expectedVariantIndex, expectedMeshAngleDegrees)
    /// </summary>
    public static IEnumerable<object[]> HybridRotationData =>
    [
        // 4 variants × 90° interval = 1 step per variant = 4 total orientations
        [4, "90deg", 0, 0, 0f],
        [4, "90deg", 1, 1, 0f],
        [4, "90deg", 2, 2, 0f],
        [4, "90deg", 3, 3, 0f],
        // 4 variants × 45° interval = 2 steps per variant = 8 total orientations
        [4, "45deg", 0, 0, 0f],
        [4, "45deg", 1, 0, 45f],
        [4, "45deg", 2, 1, 0f],
        [4, "45deg", 3, 1, 45f],
        [4, "45deg", 4, 2, 0f],
        [4, "45deg", 5, 2, 45f],
        // 4 variants × 22.5° interval = 4 steps per variant = 16 total orientations
        [4, "22.5deg", 0, 0, 0f],
        [4, "22.5deg", 1, 0, 22.5f],
        [4, "22.5deg", 2, 0, 45f],
        [4, "22.5deg", 3, 0, 67.5f],
        [4, "22.5deg", 4, 1, 0f],
        [4, "22.5deg", 8, 2, 0f],
        [4, "22.5deg", 12, 3, 0f],
        // 2 variants × 45° interval = 4 steps per variant = 8 total orientations
        [2, "45deg", 0, 0, 0f],
        [2, "45deg", 1, 0, 45f],
        [2, "45deg", 2, 0, 90f],
        [2, "45deg", 3, 0, 135f],
        [2, "45deg", 4, 1, 0f],
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

    #region BuildBrushInstance ItemStack MeshAngle Tests

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
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var block = CreateRotatableBlock(100, "game:rotatableblock", "TestRotatable", intervalString);
        SetupBlockLookup(mockWorld, block);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100
        };

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
    /// Verifies that after setting OrientationIndex on BuildBrushInstance for a hybrid block,
    /// the ItemStack.Attributes["meshAngle"] contains the correct value in radians.
    /// </summary>
    [Theory]
    [MemberData(nameof(HybridRotationData))]
    public void BuildBrushInstance_HybridBlock_ItemStackHasMeshAngle(
        int variantCount, string intervalString, int testOrientationIndex,
        int expectedVariantIndex, float expectedMeshAngleDegrees)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var variants = CreateHybridVariantBlocks("game:hybridblock", "rot", variantCount, 100, "TestRotatable", intervalString);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100
        };

        // Verify rotation is detected
        Assert.NotNull(instance.Rotation);
        Assert.True(instance.Rotation.CanRotate);

        // Act
        instance.OrientationIndex = testOrientationIndex;

        // Assert - ItemStack should have correct meshAngle
        Assert.NotNull(instance.ItemStack);
        float expectedRadians = expectedMeshAngleDegrees * GameMath.DEG2RAD;
        float actualRadians = instance.ItemStack.Attributes.GetFloat(instance?.Rotation?.RotationAttribute, -999f);

        Assert.Equal(expectedRadians, actualRadians, precision: 4);
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
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var variants = CreateVariantBlocks("game:testblock", variantKey, variantValues, startBlockId);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = startBlockId
        };

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
    /// Verifies that BuildBrushInstance.CurrentPlacementBlock has the correct block ID
    /// after setting OrientationIndex for hybrid blocks.
    /// </summary>
    [Theory]
    [MemberData(nameof(HybridRotationData))]
    public void BuildBrushInstance_HybridBlock_BlockTransformedHasCorrectId(
        int variantCount, string intervalString, int testOrientationIndex,
        int expectedVariantIndex, float expectedMeshAngleDegrees)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var variants = CreateHybridVariantBlocks("game:hybridblock", "rot", variantCount, 100, "TestRotatable", intervalString);
        SetupBlockLookup(mockWorld, variants);
        SetupVariantSearch(mockWorld, variants);
        SetupRotatableEntity(mockWorld, "TestRotatable");

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object)
        {
            BlockId = 100
        };

        // Verify rotation is detected
        Assert.NotNull(instance.Rotation);
        Assert.True(instance.Rotation.CanRotate);

        // Act
        instance.OrientationIndex = testOrientationIndex;

        // Assert - CurrentPlacementBlock should be the correct variant
        Assert.NotNull(instance.CurrentPlacementBlock);
        Assert.Equal(100 + expectedVariantIndex, instance.CurrentPlacementBlock.BlockId);
    }

    #endregion
}
