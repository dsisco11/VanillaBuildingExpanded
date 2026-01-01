using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Shared test helper methods for BuildBrush-related tests.
/// Provides factory methods for creating common mocks and test objects.
/// </summary>
public static class TestHelpers
{
    #region World and Player Mocks

    /// <summary>
    /// Creates a mock IWorldAccessor configured for client-side testing.
    /// </summary>
    public static Mock<IWorldAccessor> CreateMockWorld(EnumAppSide side = EnumAppSide.Client)
    {
        var mockWorld = new Mock<IWorldAccessor>();
        var mockLogger = new Mock<ILogger>();
        mockWorld.Setup(w => w.Logger).Returns(mockLogger.Object);
        mockWorld.Setup(w => w.Side).Returns(side);
        return mockWorld;
    }

    /// <summary>
    /// Creates a mock IPlayer with basic setup.
    /// </summary>
    public static Mock<IPlayer> CreateMockPlayer()
    {
        var mockPlayer = new Mock<IPlayer>();
        var mockInventoryManager = new Mock<IPlayerInventoryManager>();
        mockPlayer.Setup(p => p.InventoryManager).Returns(mockInventoryManager.Object);
        mockPlayer.Setup(p => p.CurrentBlockSelection).Returns((BlockSelection?)null);
        return mockPlayer;
    }

    #endregion

    #region Client API Mocks

    /// <summary>
    /// Creates a mock ICoreClientAPI with common sub-mocks configured.
    /// </summary>
    public static Mock<ICoreClientAPI> CreateMockCapi()
    {
        var mockCapi = new Mock<ICoreClientAPI>();
        var mockLogger = new Mock<ILogger>();
        var mockTesselator = new Mock<ITesselatorAPI>();
        var mockTesselatorManager = new Mock<ITesselatorManager>();

        mockCapi.Setup(c => c.Logger).Returns(mockLogger.Object);
        mockCapi.Setup(c => c.Tesselator).Returns(mockTesselator.Object);
        mockCapi.Setup(c => c.TesselatorManager).Returns(mockTesselatorManager.Object);
        mockCapi.Setup(c => c.Side).Returns(EnumAppSide.Client);

        return mockCapi;
    }

    /// <summary>
    /// Creates a mock IMiniDimension.
    /// </summary>
    public static Mock<IMiniDimension> CreateMockDimension()
    {
        return new Mock<IMiniDimension>();
    }

    #endregion

    #region Block Creation

    /// <summary>
    /// Creates a real Block instance with the specified BlockId and Code.
    /// </summary>
    public static Block CreateTestBlock(int blockId, string code = "game:testblock")
    {
        var block = new Block();
        block.BlockId = blockId;
        block.Code = new AssetLocation(code);
        return block;
    }

    /// <summary>
    /// Creates a Block and configures the world mock to return it for GetBlock(blockId).
    /// </summary>
    public static Block CreateAndRegisterBlock(Mock<IWorldAccessor> mockWorld, int blockId, string code = "game:testblock")
    {
        var block = CreateTestBlock(blockId, code);
        mockWorld.Setup(w => w.GetBlock(blockId)).Returns(block);
        return block;
    }

    /// <summary>
    /// Creates a Block with tessellation mock configured.
    /// </summary>
    public static Block CreateMockBlockWithMesh(Mock<ICoreClientAPI> mockCapi, int blockId, MeshData? mesh = null, string code = "game:testblock")
    {
        var block = CreateTestBlock(blockId, code);
        mesh ??= CreateTestMeshData();
        mockCapi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(block)).Returns(mesh);
        return block;
    }

    #endregion

    #region Mesh Data Creation

    /// <summary>
    /// Creates a simple MeshData instance for testing.
    /// </summary>
    /// <param name="vertexCount">Number of vertices (should be divisible by 4 for quads).</param>
    public static MeshData CreateTestMeshData(int vertexCount = 4)
    {
        var mesh = new MeshData(vertexCount, vertexCount / 2 * 3);
        mesh.xyz = new float[vertexCount * 3];
        mesh.Uv = new float[vertexCount * 2];
        mesh.Rgba = new byte[vertexCount * 4];
        mesh.Indices = new int[vertexCount / 2 * 3];
        mesh.VerticesCount = vertexCount;
        mesh.IndicesCount = vertexCount / 2 * 3;
        return mesh;
    }

    /// <summary>
    /// Creates a MeshData representing a simple cube.
    /// </summary>
    public static MeshData CreateTestCubeMesh()
    {
        // 24 vertices for a cube (6 faces * 4 vertices each)
        return CreateTestMeshData(24);
    }

    #endregion

    #region BlockPos Helpers

    /// <summary>
    /// Creates a BlockPos in mini-dimension coordinate space.
    /// </summary>
    public static BlockPos CreateMiniDimensionPos(int x, int y, int z)
    {
        return new BlockPos(x, y, z, Vintagestory.API.Config.Dimensions.MiniDimensions);
    }

    #endregion

    #region Instance Creation

    /// <summary>
    /// Creates a BuildBrushInstance with default mocks.
    /// </summary>
    public static BuildBrushInstance CreateTestInstance(Mock<IWorldAccessor>? mockWorld = null, Mock<IPlayer>? mockPlayer = null)
    {
        mockWorld ??= CreateMockWorld();
        mockPlayer ??= CreateMockPlayer();
        return new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
    }

    /// <summary>
    /// Creates a BuildBrushDimension with default mock world.
    /// Note: Won't be fully initialized without server API.
    /// </summary>
    public static BuildBrushDimension CreateTestDimension(Mock<IWorldAccessor>? mockWorld = null)
    {
        mockWorld ??= CreateMockWorld();
        return new BuildBrushDimension(mockWorld.Object);
    }

    #endregion
}
