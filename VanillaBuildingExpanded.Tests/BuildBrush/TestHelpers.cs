using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Shared test helper methods for BuildBrush-related tests.
/// Provides factory methods for creating common mocks and test objects.
/// </summary>
public static class TestHelpers
{
    #region World and Player Mocks

    /// <summary>
    /// Creates a mock IWorldAccessor configured for testing.
    /// Includes callback registration tracking.
    /// </summary>
    public static Mock<IWorldAccessor> CreateMockWorld(EnumAppSide side = EnumAppSide.Client)
    {
        var mockWorld = new Mock<IWorldAccessor>();
        var mockLogger = new Mock<ILogger>();
        mockWorld.Setup(w => w.Logger).Returns(mockLogger.Object);
        mockWorld.Setup(w => w.Side).Returns(side);

        // Setup callback registration tracking
        var callbackIds = new Dictionary<long, Action<float>>();
        long nextCallbackId = 1;

        mockWorld.Setup(w => w.RegisterCallback(It.IsAny<Action<float>>(), It.IsAny<int>()))
            .Returns((Action<float> callback, int delay) =>
            {
                var id = nextCallbackId++;
                callbackIds[id] = callback;
                return id;
            });

        mockWorld.Setup(w => w.UnregisterCallback(It.IsAny<long>()))
            .Callback((long id) => callbackIds.Remove(id));

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

    #region Server API Mocks

    /// <summary>
    /// Creates a mock ICoreServerAPI with common sub-mocks configured.
    /// Includes entity registry, server interface, and spawn tracking.
    /// </summary>
    public static Mock<ICoreServerAPI> CreateMockSapi()
    {
        var mockSapi = new Mock<ICoreServerAPI>();
        var mockLogger = new Mock<ILogger>();
        var mockServer = new Mock<IServerAPI>();
        var mockClassRegistry = new Mock<IClassRegistryAPI>();
        var mockWorld = new Mock<IServerWorldAccessor>();

        mockSapi.Setup(s => s.Logger).Returns(mockLogger.Object);
        mockSapi.Setup(s => s.Server).Returns(mockServer.Object);
        mockSapi.Setup(s => s.ClassRegistry).Returns(mockClassRegistry.Object);
        mockSapi.Setup(s => s.World).Returns(mockWorld.Object);
        mockSapi.Setup(s => s.Side).Returns(EnumAppSide.Server);

        return mockSapi;
    }

    /// <summary>
    /// Creates a mock IWorldAccessor configured for server-side with ICoreServerAPI accessible via Api property.
    /// </summary>
    public static Mock<IWorldAccessor> CreateMockServerWorld(Mock<ICoreServerAPI>? mockSapi = null)
    {
        mockSapi ??= CreateMockSapi();
        var mockWorld = new Mock<IWorldAccessor>();
        var mockLogger = new Mock<ILogger>();
        var mockBlockAccessor = new Mock<IBlockAccessor>();

        mockWorld.Setup(w => w.Logger).Returns(mockLogger.Object);
        mockWorld.Setup(w => w.Side).Returns(EnumAppSide.Server);
        mockWorld.Setup(w => w.Api).Returns(mockSapi.Object);
        mockWorld.Setup(w => w.BlockAccessor).Returns(mockBlockAccessor.Object);

        // Setup callback registration tracking
        var callbackIds = new Dictionary<long, Action<float>>();
        long nextCallbackId = 1;

        mockWorld.Setup(w => w.RegisterCallback(It.IsAny<Action<float>>(), It.IsAny<int>()))
            .Returns((Action<float> callback, int delay) =>
            {
                var id = nextCallbackId++;
                callbackIds[id] = callback;
                return id;
            });

        mockWorld.Setup(w => w.UnregisterCallback(It.IsAny<long>()))
            .Callback((long id) => callbackIds.Remove(id));

        return mockWorld;
    }

    /// <summary>
    /// Configures the mock server API to return a mock entity when CreateEntity is called.
    /// </summary>
    public static Mock<Entity> SetupEntityCreation(Mock<ICoreServerAPI> mockSapi, string entityClass)
    {
        var mockEntity = new Mock<Entity>();
        mockSapi.Setup(s => s.World.ClassRegistry.CreateEntity(entityClass))
            .Returns(mockEntity.Object);
        return mockEntity;
    }

    /// <summary>
    /// Configures the mock world to track SpawnEntity calls.
    /// Returns a list that captures all spawned entities.
    /// </summary>
    public static List<Entity> SetupSpawnEntityTracking(Mock<IWorldAccessor> mockWorld)
    {
        var spawnedEntities = new List<Entity>();
        mockWorld.Setup(w => w.SpawnEntity(It.IsAny<Entity>()))
            .Callback((Entity e) => spawnedEntities.Add(e));
        return spawnedEntities;
    }

    /// <summary>
    /// Configures the mock block accessor to create mini dimensions.
    /// </summary>
    public static Mock<IMiniDimension> SetupMiniDimensionCreation(Mock<IWorldAccessor> mockWorld)
    {
        var mockDimension = CreateMockDimension();
        var mockBlockAccessor = new Mock<IBlockAccessor>();

        mockBlockAccessor.Setup(ba => ba.CreateMiniDimension(It.IsAny<Vec3d>()))
            .Returns(mockDimension.Object);

        mockWorld.Setup(w => w.BlockAccessor).Returns(mockBlockAccessor.Object);

        return mockDimension;
    }

    /// <summary>
    /// Configures the mock server to handle mini dimension loading.
    /// Returns the dimension ID that will be assigned.
    /// </summary>
    public static void SetupMiniDimensionLoading(Mock<ICoreServerAPI> mockSapi, int dimensionId = 1)
    {
        mockSapi.Setup(s => s.Server.LoadMiniDimension(It.IsAny<IMiniDimension>()))
            .Returns(dimensionId);
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
    public static BrushDimension CreateTestDimension(Mock<IWorldAccessor>? mockWorld = null)
    {
        mockWorld ??= CreateMockWorld();
        return new BrushDimension(mockWorld.Object);
    }

    #endregion
}
