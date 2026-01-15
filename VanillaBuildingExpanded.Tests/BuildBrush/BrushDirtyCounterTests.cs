using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public class BrushDirtyCounterTests
{
    [Fact]
    public void DirtyCounter_Increments_When_DimensionDirty_Event_Raised()
    {
        // Arrange
        var mockServerApi = TestHelpers.CreateMockSapi();
        var mockWorld = TestHelpers.CreateMockServerWorld(mockServerApi);
        var mockPlayer = new Mock<IServerPlayer>();
        var mockInventoryManager = new Mock<IPlayerInventoryManager>();
        mockInventoryManager.Setup(m => m.ActiveHotbarSlot).Returns(new DummySlot((ItemStack?)null));

        mockPlayer.Setup(p => p.PlayerUID).Returns("uid-test");
        mockPlayer.Setup(p => p.ClientId).Returns(1);
        mockPlayer.Setup(p => p.PlayerName).Returns("Tester");
        mockPlayer.Setup(p => p.InventoryManager).Returns(mockInventoryManager.Object);

        var controller = new BuildBrushControllerServer(mockServerApi.Object, mockPlayer.Object);
        var brush = controller.Brush;

        var entity = new BuildBrushEntity();
        SetPrivateField(brush, "_entity", entity);

        int before = entity.GetBrushDirtyCounter();

        // Invoke the controller's dirty handler directly to simulate orientation-driven preview change
        var method = typeof(BuildBrushControllerServer)
            .GetMethod("Brush_OnDimensionDirty", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        method!.Invoke(controller, new object?[] { brush, new DimensionDirtyEventArgs("test") });

        // Assert
        int after = entity.GetBrushDirtyCounter();
        Assert.True(after > before);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
