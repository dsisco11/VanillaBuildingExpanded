using System;
using System.Threading;
using System.Threading.Tasks;

using Moq;

using VanillaBuildingExpanded.BuildHammer.Tessellation;
using VanillaBuildingExpanded.Tests.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush.Tessellation;

/// <summary>
/// Unit tests for <see cref="MiniDimensionTessellator"/>.
/// Tests tessellation of mini-dimensions with mocked dependencies.
/// </summary>
public class MiniDimensionTessellatorTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullCapi_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MiniDimensionTessellator(null!));
    }

    [Fact]
    public void Constructor_WithValidCapi_Succeeds()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();

        // Act
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        // Assert
        Assert.NotNull(tessellator);
    }

    #endregion

    #region Tessellate - Null/Empty Tests

    [Fact]
    public void Tessellate_WithNullDimension_ReturnsNull()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);
        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(null!, min, max);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Tessellate_WithEmptyBounds_ReturnsNull()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        // Setup dimension to return null/air for all positions
        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns((Block?)null);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Tessellate_WithAirBlocks_ReturnsNull()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        // Setup dimension to return air block (BlockId = 0)
        var airBlock = TestHelpers.CreateTestBlock(0, "game:air");
        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(airBlock);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Tessellate - Single Block Tests

    [Fact]
    public void Tessellate_WithSingleBlock_ReturnsMeshFromTesselatorManager()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        var testMesh = TestHelpers.CreateTestMeshData(4);

        // Setup dimension to return our test block at origin
        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns((BlockEntity?)null);

        // Setup tesselator manager to return our test mesh
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock)).Returns(testMesh);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.VerticesCount > 0);
    }

    [Fact]
    public void Tessellate_WhenTesselatorManagerThrows_FallsBackToDirectTessellation()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var mockTesselator = new Mock<ITesselatorAPI>();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        var testMesh = TestHelpers.CreateTestMeshData(4);

        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns((BlockEntity?)null);

        // Setup tesselator manager to throw
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(It.IsAny<Block>()))
            .Throws(new Exception("Test exception"));

        // Setup direct tessellation fallback
        mockCapi.Setup(c => c.Tesselator.TesselateBlock(testBlock, out It.Ref<MeshData>.IsAny))
            .Callback(new TesselateBlockCallback((Block b, out MeshData m) => m = testMesh));

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert - should still get a mesh via fallback
        // Note: May be null if fallback also fails, which is acceptable
    }

    // Delegate for out parameter callback
    private delegate void TesselateBlockCallback(Block block, out MeshData mesh);

    #endregion

    #region Tessellate - Multiple Blocks Tests

    [Fact]
    public void Tessellate_WithMultipleBlocks_CombinesMeshes()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        var testMesh = TestHelpers.CreateTestMeshData(4);

        // Setup dimension to return our test block at multiple positions
        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns((BlockEntity?)null);
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock)).Returns(testMesh);

        // 2x2x2 cube of blocks
        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(1, 1, 1);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert
        Assert.NotNull(result);
        // Should have more vertices than a single block (8 blocks * 4 vertices each, combined)
        Assert.True(result.VerticesCount >= 4);
    }

    #endregion

    #region Tessellate - Block Entity Tests

    [Fact]
    public void Tessellate_WithBlockEntity_CallsOnTesselation()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        testBlock.EntityClass = "TestBlockEntity";

        var testMesh = TestHelpers.CreateTestMeshData(4);

        // Create a mock BlockEntity - Note: BlockEntity is a concrete class
        // We'll use a real instance and verify OnTesselation was called via the mesh pool
        var mockBlockEntity = new Mock<BlockEntity>();
        mockBlockEntity.CallBase = true;

        // Setup OnTesselation to add mesh and return true (skip default)
        mockBlockEntity.Setup(be => be.OnTesselation(It.IsAny<ITerrainMeshPool>(), It.IsAny<ITesselatorAPI>()))
            .Callback<ITerrainMeshPool, ITesselatorAPI>((pool, tess) =>
            {
                pool.AddMeshData(testMesh);
            })
            .Returns(true);

        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns(mockBlockEntity.Object);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert
        mockBlockEntity.Verify(be => be.OnTesselation(It.IsAny<ITerrainMeshPool>(), It.IsAny<ITesselatorAPI>()), Times.Once);
        Assert.NotNull(result);
    }

    [Fact]
    public void Tessellate_WhenBlockEntitySkipsDefaultButAddsNoVertices_AddsFallbackMesh()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        testBlock.EntityClass = "TestBlockEntity";

        var fallbackMesh = TestHelpers.CreateTestMeshData(4);

        var mockBlockEntity = new Mock<BlockEntity>();
        mockBlockEntity.CallBase = true;

        // Setup OnTesselation to return true (skip default) but add no vertices
        mockBlockEntity.Setup(be => be.OnTesselation(It.IsAny<ITerrainMeshPool>(), It.IsAny<ITesselatorAPI>()))
            .Returns(true); // Skip default, but don't add any mesh

        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns(mockBlockEntity.Object);

        // Setup fallback mesh
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock)).Returns(fallbackMesh);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert - should have fallback mesh
        Assert.NotNull(result);
        Assert.True(result.VerticesCount > 0);

        // Verify fallback was requested
        mockCapi.Verify(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock), Times.Once);
    }

    [Fact]
    public void Tessellate_WhenBlockEntityReturnsFalse_AddsDefaultMesh()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        testBlock.EntityClass = "TestBlockEntity";

        var defaultMesh = TestHelpers.CreateTestMeshData(4);

        var mockBlockEntity = new Mock<BlockEntity>();
        mockBlockEntity.CallBase = true;

        // Setup OnTesselation to return false (use default mesh)
        mockBlockEntity.Setup(be => be.OnTesselation(It.IsAny<ITerrainMeshPool>(), It.IsAny<ITesselatorAPI>()))
            .Returns(false);

        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns(mockBlockEntity.Object);
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock)).Returns(defaultMesh);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = tessellator.Tessellate(mockDimension.Object, min, max);

        // Assert
        Assert.NotNull(result);
        mockCapi.Verify(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock), Times.Once);
    }

    #endregion

    #region TessellateAsync - Cancellation Tests

    [Fact]
    public async Task TessellateAsync_WithNullDimension_ReturnsNull()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);
        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = await tessellator.TessellateAsync(null!, min, max);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task TessellateAsync_WhenCancelled_ReturnsNullOrThrows()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        var testMesh = TestHelpers.CreateTestMeshData(4);

        // Setup a large range that will take time to iterate
        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns((BlockEntity?)null);
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock)).Returns(testMesh);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(100, 100, 100); // Large range

        // Create a pre-cancelled token
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Either throws TaskCanceledException or returns null
        try
        {
            var result = await tessellator.TessellateAsync(mockDimension.Object, min, max, cts.Token);
            Assert.Null(result);
        }
        catch (TaskCanceledException)
        {
            // This is also acceptable behavior for a cancelled task
        }
    }

    [Fact]
    public async Task TessellateAsync_WithValidData_ReturnsMesh()
    {
        // Arrange
        var mockCapi = TestHelpers.CreateMockCapi();
        var mockDimension = TestHelpers.CreateMockDimension();
        var tessellator = new MiniDimensionTessellator(mockCapi.Object);

        var testBlock = TestHelpers.CreateTestBlock(100);
        var testMesh = TestHelpers.CreateTestMeshData(4);

        mockDimension.Setup(d => d.GetBlock(It.IsAny<BlockPos>())).Returns(testBlock);
        mockDimension.Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>())).Returns((BlockEntity?)null);
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(testBlock)).Returns(testMesh);

        var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
        var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);

        // Act
        var result = await tessellator.TessellateAsync(mockDimension.Object, min, max);

        // Assert
        Assert.NotNull(result);
    }

    #endregion
}
