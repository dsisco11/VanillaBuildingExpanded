using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Unit tests for <see cref="BlockOrientationResolver"/>.
/// Verifies correct rotation mode detection and orientation definition computation
/// for all rotation modes: None, VariantBased, Rotatable, and Hybrid.
/// </summary>
public class BlockOrientationResolverTests
{
    #region Test Helpers

    /// <summary>
    /// Creates a mock world with configurable block lookups.
    /// </summary>
    private static Mock<IWorldAccessor> CreateMockWorld()
    {
        var mockWorld = new Mock<IWorldAccessor>();
        var mockApi = new Mock<ICoreAPI>();
        var mockClassRegistry = new Mock<IClassRegistryAPI>();

        mockWorld.Setup(w => w.Api).Returns(mockApi.Object);
        mockApi.Setup(a => a.ClassRegistry).Returns(mockClassRegistry.Object);

        return mockWorld;
    }

    /// <summary>
    /// Creates a simple block with no rotation support.
    /// Uses VariantStrict which is the writable dictionary that Variant wraps.
    /// </summary>
    private static Block CreateSimpleBlock(int blockId, string code = "game:simpleblock")
    {
        var block = new Block
        {
            BlockId = blockId,
            Code = new AssetLocation(code)
        };
        // VariantStrict is empty by default, Variant wraps it as read-only
        return block;
    }

    /// <summary>
    /// Creates a block with variant-based rotation support.
    /// </summary>
    private static Block CreateVariantBlock(int blockId, string code, string variantKey, string variantValue)
    {
        var block = new Block
        {
            BlockId = blockId,
            Code = new AssetLocation(code)
        };
        // Add orientation variant to VariantStrict (Variant is a read-only wrapper)
        block.VariantStrict[variantKey] = variantValue;
        return block;
    }

    /// <summary>
    /// Creates a block with IRotatable entity support.
    /// </summary>
    private static Block CreateRotatableBlock(int blockId, string code, string entityClass)
    {
        var block = new Block
        {
            BlockId = blockId,
            Code = new AssetLocation(code),
            EntityClass = entityClass
        };
        return block;
    }

    /// <summary>
    /// Creates a block with rotatable interval attribute.
    /// </summary>
    private static Block CreateRotatableBlockWithInterval(int blockId, string code, string entityClass, string interval)
    {
        var block = CreateRotatableBlock(blockId, code, entityClass);
        block.Attributes = new JsonObject(new Newtonsoft.Json.Linq.JObject
        {
            ["rotatatableInterval"] = interval
        });
        return block;
    }

    /// <summary>
    /// Creates a hybrid block (both variants and IRotatable).
    /// </summary>
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

    /// <summary>
    /// Sets up the mock world to return specific blocks by ID.
    /// </summary>
    private static void SetupBlockLookup(Mock<IWorldAccessor> mockWorld, params Block[] blocks)
    {
        foreach (var block in blocks)
        {
            mockWorld.Setup(w => w.GetBlock(block.BlockId)).Returns(block);
        }
    }

    /// <summary>
    /// Sets up the mock world to return variant blocks when searching.
    /// </summary>
    private static void SetupVariantSearch(Mock<IWorldAccessor> mockWorld, Block[] variants)
    {
        mockWorld.Setup(w => w.SearchBlocks(It.IsAny<AssetLocation>())).Returns(variants);
    }

    /// <summary>
    /// Sets up the class registry to return a type that implements IRotatable.
    /// Also sets up CreateBlockEntity to return an instance.
    /// </summary>
    private static void SetupRotatableEntity(Mock<IWorldAccessor> mockWorld, string entityClass)
    {
        var mockClassRegistry = new Mock<IClassRegistryAPI>();
        mockClassRegistry.Setup(c => c.GetBlockEntity(entityClass)).Returns(typeof(TestRotatableBlockEntity));
        mockClassRegistry.Setup(c => c.CreateBlockEntity(entityClass)).Returns(new TestRotatableBlockEntity());

        var mockApi = new Mock<ICoreAPI>();
        mockApi.Setup(a => a.ClassRegistry).Returns(mockClassRegistry.Object);
        mockWorld.Setup(w => w.Api).Returns(mockApi.Object);
    }

    /// <summary>
    /// Sets up the class registry to return a type that does NOT implement IRotatable.
    /// Also sets up CreateBlockEntity to return an instance.
    /// </summary>
    private static void SetupNonRotatableEntity(Mock<IWorldAccessor> mockWorld, string entityClass)
    {
        var mockClassRegistry = new Mock<IClassRegistryAPI>();
        mockClassRegistry.Setup(c => c.GetBlockEntity(entityClass)).Returns(typeof(TestNonRotatableBlockEntity));
        mockClassRegistry.Setup(c => c.CreateBlockEntity(entityClass)).Returns(new TestNonRotatableBlockEntity());

        var mockApi = new Mock<ICoreAPI>();
        mockApi.Setup(a => a.ClassRegistry).Returns(mockClassRegistry.Object);
        mockWorld.Setup(w => w.Api).Returns(mockApi.Object);
    }

    // Test stub classes for IRotatable detection
    private class TestRotatableBlockEntity : BlockEntity, IRotatable
    {
        public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
            Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping,
            EnumAxis? flipAxis)
        { }
    }

    private class TestNonRotatableBlockEntity : BlockEntity { }

    #endregion

    #region GetRotationMode Tests - None

    [Fact]
    public void GetRotationMode_SimpleBlock_ReturnsNone()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateSimpleBlock(100);
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.None, mode);
    }

    [Fact]
    public void GetRotationMode_NullBlock_ReturnsNone()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(null!);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.None, mode);
    }

    [Fact]
    public void GetRotationMode_BlockWithNonRotatableEntity_ReturnsNone()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateRotatableBlock(100, "game:staticblock", "NonRotatableEntity");
        SetupNonRotatableEntity(mockWorld, "NonRotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.None, mode);
    }

    #endregion

    #region GetRotationMode Tests - VariantBased

    [Theory]
    [InlineData("rot")]
    [InlineData("rotation")]
    [InlineData("horizontalorientation")]
    [InlineData("orientation")]
    [InlineData("v")]
    [InlineData("side")]
    public void GetRotationMode_BlockWithOrientationVariant_ReturnsVariantBased(string variantKey)
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateVariantBlock(100, "game:variantblock", variantKey, "north");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.VariantBased, mode);
    }

    [Fact]
    public void GetRotationMode_BlockWithNonOrientationVariant_ReturnsNone()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateVariantBlock(100, "game:colorblock", "color", "red");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.None, mode);
    }

    #endregion

    #region GetRotationMode Tests - Rotatable

    [Fact]
    public void GetRotationMode_BlockWithRotatableEntity_ReturnsRotatable()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateRotatableBlockWithInterval(100, "game:rotatableblock", "RotatableEntity", "90deg");
        SetupRotatableEntity(mockWorld, "RotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.Rotatable, mode);
    }

    [Fact]
    public void GetRotationMode_BlockWithEmptyEntityClass_ReturnsNone()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateSimpleBlock(100);
        block.EntityClass = "";
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.None, mode);
    }

    #endregion

    #region GetRotationMode Tests - Hybrid

    [Fact]
    public void GetRotationMode_BlockWithBothVariantAndRotatable_ReturnsHybrid()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateHybridBlock(100, "game:hybridblock", "rot", "north", "RotatableEntity", "90deg");
        SetupRotatableEntity(mockWorld, "RotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.Hybrid, mode);
    }

    #endregion

    #region GetRotationMode Tests - Guard Tests

    [Fact]
    public void GetRotationMode_SingleVariantWithMultipleAngles_ReturnsRotatable_NotHybrid()
    {
        // Arrange - Block with rotatable entity but only one variant (no orientation variants)
        var mockWorld = CreateMockWorld();
        var block = CreateRotatableBlockWithInterval(100, "game:rotatableonly", "RotatableEntity", "45deg");
        SetupRotatableEntity(mockWorld, "RotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert - Should be Rotatable, NOT Hybrid (no variant keys)
        Assert.Equal(EBuildBrushRotationMode.Rotatable, mode);
    }

    [Fact]
    public void GetRotationMode_MultipleVariantsWithNoRotatableEntity_ReturnsVariantBased_NotHybrid()
    {
        // Arrange - Block with orientation variant but no rotatable entity
        var mockWorld = CreateMockWorld();
        var block = CreateVariantBlock(100, "game:variantonly", "rot", "north");
        // NOT setting up rotatable entity - no EntityClass
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert - Should be VariantBased, NOT Hybrid (no IRotatable)
        Assert.Equal(EBuildBrushRotationMode.VariantBased, mode);
    }

    [Fact]
    public void GetRotationMode_MultipleVariantsWithNonRotatableEntity_ReturnsVariantBased_NotHybrid()
    {
        // Arrange - Block with orientation variant AND entity class, but entity doesn't implement IRotatable
        var mockWorld = CreateMockWorld();
        var block = CreateVariantBlock(100, "game:variantonly", "rot", "north");
        block.EntityClass = "NonRotatableEntity";
        SetupNonRotatableEntity(mockWorld, "NonRotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var mode = resolver.GetRotationMode(block);

        // Assert - Should be VariantBased, NOT Hybrid (entity doesn't implement IRotatable)
        Assert.Equal(EBuildBrushRotationMode.VariantBased, mode);
    }

    #endregion

    #region GetOrientations Tests - None Mode

    [Fact]
    public void GetOrientations_NonexistentBlock_ReturnsSingleDefinition()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        mockWorld.Setup(w => w.GetBlock(999)).Returns((Block)null!);
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(999);

        // Assert
        Assert.Single(orientations);
        Assert.Equal(999, orientations[0].BlockId);
        Assert.Equal(0f, orientations[0].MeshAngleDegrees);
    }

    [Fact]
    public void GetOrientations_SimpleBlock_ReturnsSingleDefinitionWithZeroAngle()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateSimpleBlock(100);
        SetupBlockLookup(mockWorld, block);
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(100);

        // Assert
        Assert.Single(orientations);
        Assert.Equal(100, orientations[0].BlockId);
        Assert.Equal(0f, orientations[0].MeshAngleDegrees);
    }

    #endregion

    #region GetOrientations Tests - VariantBased Mode

    [Fact]
    public void GetOrientations_VariantBlock_ReturnsOneDefinitionPerVariant()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var blockNorth = CreateVariantBlock(100, "game:stair-north", "rot", "north");
        var blockEast = CreateVariantBlock(101, "game:stair-east", "rot", "east");
        var blockSouth = CreateVariantBlock(102, "game:stair-south", "rot", "south");
        var blockWest = CreateVariantBlock(103, "game:stair-west", "rot", "west");

        SetupBlockLookup(mockWorld, blockNorth, blockEast, blockSouth, blockWest);
        SetupVariantSearch(mockWorld, [blockNorth, blockEast, blockSouth, blockWest]);
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(100);

        // Assert
        Assert.Equal(4, orientations.Length);
        Assert.Equal(100, orientations[0].BlockId);
        Assert.Equal(101, orientations[1].BlockId);
        Assert.Equal(102, orientations[2].BlockId);
        Assert.Equal(103, orientations[3].BlockId);

        // All variants should have 0° mesh angle
        Assert.All(orientations, o => Assert.Equal(0f, o.MeshAngleDegrees));
    }

    [Fact]
    public void GetOrientations_VariantBlock_NoVariantsFound_ReturnsSingleDefinition()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateVariantBlock(100, "game:orphanblock", "rot", "north");
        SetupBlockLookup(mockWorld, block);
        SetupVariantSearch(mockWorld, []); // No variants found
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(100);

        // Assert
        Assert.Single(orientations);
        Assert.Equal(100, orientations[0].BlockId);
    }

    #endregion

    #region GetOrientations Tests - Rotatable Mode

    [Fact]
    public void GetOrientations_RotatableBlock_NoInterval_ReturnsSingleDefinition()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateRotatableBlock(100, "game:nointerval", "RotatableEntity");
        SetupBlockLookup(mockWorld, block);
        SetupRotatableEntity(mockWorld, "RotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(100);

        // Assert - No interval configured => not rotatable
        Assert.Single(orientations);
        Assert.Equal(100, orientations[0].BlockId);
        Assert.Equal(0f, orientations[0].MeshAngleDegrees);
    }

    #endregion

    #region GetOrientations Tests - Hybrid Mode

    [Fact]
    public void GetOrientations_HybridBlock_UsesMeshAnglesOnly()
    {
        // Arrange
        var mockWorld = CreateMockWorld();

        // Hybrid block with 90° interval - currently uses rotatable rotations (ignores variants)
        var blockNorth = CreateHybridBlock(100, "game:fancy-north", "rot", "north", "RotatableEntity", "90deg");
        var blockEast = CreateHybridBlock(101, "game:fancy-east", "rot", "east", "RotatableEntity", "90deg");
        var blockSouth = CreateHybridBlock(102, "game:fancy-south", "rot", "south", "RotatableEntity", "90deg");
        var blockWest = CreateHybridBlock(103, "game:fancy-west", "rot", "west", "RotatableEntity", "90deg");

        SetupBlockLookup(mockWorld, blockNorth, blockEast, blockSouth, blockWest);
        SetupVariantSearch(mockWorld, [blockNorth, blockEast, blockSouth, blockWest]);
        SetupRotatableEntity(mockWorld, "RotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(100);

        // Assert - Hybrid uses variant slices with mesh-angle substeps
        Assert.Equal(4, orientations.Length);

        Assert.Equal(100, orientations[0].BlockId);
        Assert.Equal(0f, orientations[0].MeshAngleDegrees);

        Assert.Equal(101, orientations[1].BlockId);
        Assert.Equal(0f, orientations[1].MeshAngleDegrees);

        Assert.Equal(102, orientations[2].BlockId);
        Assert.Equal(0f, orientations[2].MeshAngleDegrees);

        Assert.Equal(103, orientations[3].BlockId);
        Assert.Equal(0f, orientations[3].MeshAngleDegrees);
    }

    [Fact]
    public void GetOrientations_HybridBlock_45DegInterval_Returns8Steps()
    {
        // Arrange
        var mockWorld = CreateMockWorld();

        // Hybrid block with 45° interval - currently uses rotatable rotations
        var blockUp = CreateHybridBlock(100, "game:ladder-up", "v", "up", "RotatableEntity", "45deg");
        var blockDown = CreateHybridBlock(101, "game:ladder-down", "v", "down", "RotatableEntity", "45deg");

        SetupBlockLookup(mockWorld, blockUp, blockDown);
        SetupVariantSearch(mockWorld, [blockUp, blockDown]);
        SetupRotatableEntity(mockWorld, "RotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(100);

        // Assert - 2 variants × 45° = 8 total orientations (4 per variant)
        Assert.Equal(8, orientations.Length);

        // First variant slice (0-180)
        Assert.Equal(100, orientations[0].BlockId);
        Assert.Equal(0f, orientations[0].MeshAngleDegrees);
        Assert.Equal(100, orientations[1].BlockId);
        Assert.Equal(45f, orientations[1].MeshAngleDegrees);
        Assert.Equal(100, orientations[2].BlockId);
        Assert.Equal(90f, orientations[2].MeshAngleDegrees);
        Assert.Equal(100, orientations[3].BlockId);
        Assert.Equal(135f, orientations[3].MeshAngleDegrees);

        // Second variant slice (180-360), mesh angles reset relative to slice start
        Assert.Equal(101, orientations[4].BlockId);
        Assert.Equal(0f, orientations[4].MeshAngleDegrees);
        Assert.Equal(101, orientations[5].BlockId);
        Assert.Equal(45f, orientations[5].MeshAngleDegrees);
        Assert.Equal(101, orientations[6].BlockId);
        Assert.Equal(90f, orientations[6].MeshAngleDegrees);
        Assert.Equal(101, orientations[7].BlockId);
        Assert.Equal(135f, orientations[7].MeshAngleDegrees);
    }

    [Fact]
    public void GetOrientations_HybridBlock_NoInterval_FallsBackToVariantOnly()
    {
        // Arrange
        var mockWorld = CreateMockWorld();

        // Variants but no interval - defaults to 90° interval
        var blockNorth = CreateVariantBlock(100, "game:hybrid-north", "rot", "north");
        blockNorth.EntityClass = "RotatableEntity";
        var blockSouth = CreateVariantBlock(101, "game:hybrid-south", "rot", "south");
        blockSouth.EntityClass = "RotatableEntity";

        SetupBlockLookup(mockWorld, blockNorth, blockSouth);
        SetupVariantSearch(mockWorld, [blockNorth, blockSouth]);
        SetupRotatableEntity(mockWorld, "RotatableEntity");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var orientations = resolver.GetOrientations(100);

        // Assert - No interval configured => not hybrid; fall back to variant-only
        Assert.Equal(2, orientations.Length);
        Assert.Equal(100, orientations[0].BlockId);
        Assert.Equal(0f, orientations[0].MeshAngleDegrees);
        Assert.Equal(101, orientations[1].BlockId);
        Assert.Equal(0f, orientations[1].MeshAngleDegrees);
    }

    #endregion

    #region FindIndexForBlockId Tests

    [Fact]
    public void FindIndexForBlockId_ExistingBlockId_ReturnsCorrectIndex()
    {
        // Arrange
        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 90f),
            new BlockOrientationDefinition(102, 180f),
            new BlockOrientationDefinition(103, 270f)
        );

        // Act & Assert
        Assert.Equal(0, BlockOrientationResolver.FindIndexForBlockId(definitions, 100));
        Assert.Equal(1, BlockOrientationResolver.FindIndexForBlockId(definitions, 101));
        Assert.Equal(2, BlockOrientationResolver.FindIndexForBlockId(definitions, 102));
        Assert.Equal(3, BlockOrientationResolver.FindIndexForBlockId(definitions, 103));
    }

    [Fact]
    public void FindIndexForBlockId_NonexistentBlockId_ReturnsZero()
    {
        // Arrange
        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 90f)
        );

        // Act
        var index = BlockOrientationResolver.FindIndexForBlockId(definitions, 999);

        // Assert
        Assert.Equal(0, index);
    }

    [Fact]
    public void FindIndexForBlockId_EmptyDefinitions_ReturnsZero()
    {
        // Arrange
        var definitions = ImmutableArray<BlockOrientationDefinition>.Empty;

        // Act
        var index = BlockOrientationResolver.FindIndexForBlockId(definitions, 100);

        // Assert
        Assert.Equal(0, index);
    }

    #endregion

    #region Caching Tests

    [Fact]
    public void GetOrientations_CalledTwice_ReturnsCachedResult()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateSimpleBlock(100);
        SetupBlockLookup(mockWorld, block);
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var first = resolver.GetOrientations(100);
        var second = resolver.GetOrientations(100);

        // Assert - Should return equal values from cache (ImmutableArray is a value type)
        Assert.Equal(first, second);

        // World.GetBlock should only be called once due to caching
        mockWorld.Verify(w => w.GetBlock(100), Times.Once);
    }

    [Fact]
    public void GetRotationMode_CalledTwice_ReturnsCachedResult()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateVariantBlock(100, "game:cached", "rot", "north");
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Act
        var first = resolver.GetRotationMode(block);
        var second = resolver.GetRotationMode(block);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void ClearCaches_ClearsAllCaches()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var block = CreateSimpleBlock(100);
        SetupBlockLookup(mockWorld, block);
        var resolver = new BlockOrientationResolver(mockWorld.Object);

        // Populate caches
        resolver.GetOrientations(100);
        resolver.GetRotationMode(block);

        // Act
        resolver.ClearCaches();

        // Get orientations again - should call GetBlock again
        resolver.GetOrientations(100);

        // Assert - GetBlock should be called twice (once before clear, once after)
        mockWorld.Verify(w => w.GetBlock(100), Times.Exactly(2));
    }

    #endregion

    #region ResolveBlockType Tests

    [Fact]
    public void ResolveBlockType_NullBlock_ReturnsNull()
    {
        // Act
        var type = BlockOrientationResolver.ResolveBlockType(null);

        // Assert
        Assert.Null(type);
    }

    [Fact]
    public void ResolveBlockType_BlockWithDefaultType_ReturnsDefaultType()
    {
        // Arrange
        var block = CreateSimpleBlock(100);
        block.Attributes = new JsonObject(new Newtonsoft.Json.Linq.JObject
        {
            ["defaultType"] = "oak"
        });

        // Act
        var type = BlockOrientationResolver.ResolveBlockType(block);

        // Assert
        Assert.Equal("oak", type);
    }

    [Fact]
    public void ResolveBlockType_ItemStackOverridesDefault()
    {
        // Arrange
        var block = CreateSimpleBlock(100);
        block.Attributes = new JsonObject(new Newtonsoft.Json.Linq.JObject
        {
            ["defaultType"] = "oak"
        });

        var itemStack = new ItemStack(block);
        itemStack.Attributes.SetString("type", "birch");

        // Act
        var type = BlockOrientationResolver.ResolveBlockType(block, itemStack);

        // Assert
        Assert.Equal("birch", type);
    }

    #endregion
}
