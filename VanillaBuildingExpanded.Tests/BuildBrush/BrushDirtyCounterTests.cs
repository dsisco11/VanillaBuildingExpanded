using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Checks preview dirty-event handling without a server player dependency.</summary>
public class BrushDirtyCounterTests
{
    #region Public API
    /// <summary>Verifies that a dimension-dirty notification increments the preview entity counter.</summary>
    [Fact]
    public void DirtyCounter_Increments_When_DimensionDirty_Event_Raised()
    {
        // Arrange
        var mockServerApi = TestHelpers.CreateMockSapi();
        var mockWorld = TestHelpers.CreateMockServerWorld(mockServerApi);
        using var controller = new BuildBrushControllerServer(TestHelpers.CreateTestInstance(mockWorld));
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

    #endregion

    #region Private
    /// <summary>Sets the retained preview entity field needed by the dirty-event test.</summary>
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
    #endregion
}
