using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildHammer;
using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public class BrushSyncTests
{
    [Fact]
    public void Server_Ignores_Older_Seq_Packets()
    {
        // Arrange
        var mockServerApi = TestHelpers.CreateMockSapi();
        var mockWorld = TestHelpers.CreateMockServerWorld(mockServerApi);
        var mockChannel = new Mock<IServerNetworkChannel>();
        var mockPlayer = new Mock<IServerPlayer>();
        var mockInventoryManager = new Mock<IPlayerInventoryManager>();
        mockInventoryManager.Setup(m => m.ActiveHotbarSlot).Returns(new DummySlot((ItemStack?)null));

        mockPlayer.Setup(p => p.ClientId).Returns(1);
        mockPlayer.Setup(p => p.PlayerName).Returns("Tester");
        mockPlayer.Setup(p => p.PlayerUID).Returns("uid-test");
        mockPlayer.Setup(p => p.InventoryManager).Returns(mockInventoryManager.Object);

        var system = new BuildBrushSystem_Server();

        SetPrivateField(system, "api", mockServerApi.Object);
        SetPrivateField(system, "serverChannel", mockChannel.Object);

        var controllers = new Dictionary<int, BuildBrushControllerServer>
        {
            [1] = new BuildBrushControllerServer(mockServerApi.Object, mockPlayer.Object)
        };
        SetPrivateField(system, "Controllers", controllers);

        var lastAppliedMap = new Dictionary<int, long> { [1] = 5 };
        SetPrivateField(system, "lastAppliedSeqByClientId", lastAppliedMap);

        var handler = typeof(BuildBrushSystem_Server)
            .GetMethod("HandlePacket_SetBuildBrush", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handler);

        var packet = new Packet_SetBuildBrush
        {
            seq = 4,
            isActive = true,
            orientationIndex = 0,
            position = new BlockPos(1, 2, 3),
            snapping = EBuildBrushSnapping.None
        };

        // Act
        handler!.Invoke(system, new object[] { mockPlayer.Object, packet });

        // Assert
        mockChannel.Verify(c => c.SendPacket(It.IsAny<Packet_BuildBrushAck>(), mockPlayer.Object), Times.AtLeastOnce);
        Assert.Equal(5, lastAppliedMap[1]);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
