using Moq;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed class GuiElementCollapseButtonTests
{
    [Theory]
    [InlineData(11)]
    [InlineData(329)]
    public void LeftClick_ActivatesAcrossFullWidthOnlyAfterRelease(int x)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        int clicks = 0;
        using var button = CreateButton(api.Object, () => clicks++);
        var down = new MouseEvent(x, 30, EnumMouseButton.Left, 0);
        button.OnMouseDown(api.Object, down);
        Assert.True(down.Handled);
        Assert.True(button.IsPressed);
        Assert.Equal(0, clicks);

        var up = new MouseEvent(x, 30, EnumMouseButton.Left, 0);
        button.OnMouseUp(api.Object, up);
        Assert.True(up.Handled);
        Assert.False(button.IsPressed);
        Assert.Equal(1, clicks);
        button.OnMouseUp(api.Object, new MouseEvent(x, 30, EnumMouseButton.Left, 0));
        Assert.Equal(1, clicks);
        Assert.False(button.Focusable);
    }

    [Theory]
    [InlineData(9, 30, EnumMouseButton.Left)]
    [InlineData(331, 30, EnumMouseButton.Left)]
    [InlineData(100, 19, EnumMouseButton.Left)]
    [InlineData(100, 45, EnumMouseButton.Left)]
    [InlineData(100, 30, EnumMouseButton.Right)]
    [InlineData(100, 30, EnumMouseButton.Middle)]
    public void OtherInput_PassesThroughWithoutActivating(int x, int y, EnumMouseButton mouseButton)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        int clicks = 0;
        using var button = CreateButton(api.Object, () => clicks++);
        var down = new MouseEvent(x, y, mouseButton, 0);
        var up = new MouseEvent(x, y, mouseButton, 0);
        button.OnMouseDown(api.Object, down);
        button.OnMouseUp(api.Object, up);
        Assert.False(down.Handled);
        Assert.False(up.Handled);
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void DraggingOutside_CancelsActivationAndDoesNotLeavePressedState()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        int clicks = 0;
        using var button = CreateButton(api.Object, () => clicks++);
        button.OnMouseDown(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0));
        var outsideRelease = new MouseEvent(100, 100, EnumMouseButton.Left, 0);
        button.OnMouseUp(api.Object, outsideRelease);
        Assert.True(outsideRelease.Handled);
        Assert.False(button.IsPressed);
        button.OnMouseUp(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0));
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void GrabbedMouseOrHandledEvents_CannotActivateButton()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var input = Mock.Get(api.Object.Input);
        bool grabbed = true;
        input.SetupGet(i => i.MouseGrabbed).Returns(() => grabbed);
        int clicks = 0;
        using var button = CreateButton(api.Object, () => clicks++);
        var down = new MouseEvent(100, 30, EnumMouseButton.Left, 0);
        button.OnMouseDown(api.Object, down);
        Assert.False(down.Handled);
        Assert.False(button.IsPressed);
        grabbed = false;
        button.OnMouseDown(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0) { Handled = true });
        Assert.False(button.IsPressed);
        button.OnMouseDown(api.Object, down);
        button.OnMouseUp(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0) { Handled = true });
        Assert.False(button.IsPressed);
        button.OnMouseDown(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0));
        grabbed = true;
        button.OnMouseUp(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0));
        Assert.False(button.IsPressed);
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Disposal_CancelsPressAndRejectsLateClicks()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        int clicks = 0;
        var button = CreateButton(api.Object, () => clicks++);
        button.OnMouseDown(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0));
        button.Dispose();
        button.Dispose();
        button.OnMouseUp(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0));
        button.OnMouseDown(api.Object, new MouseEvent(100, 30, EnumMouseButton.Left, 0));
        Assert.False(button.IsPressed);
        Assert.Equal(0, clicks);
    }

    private static GuiElementCollapseButton CreateButton(ICoreClientAPI api, Action onClick)
    {
        var bounds = new ElementBounds
        {
            ParentBounds = ElementBounds.Empty,
            absFixedX = 10,
            absFixedY = 20,
            absInnerWidth = 320,
            absInnerHeight = 24
        };
        return new GuiElementCollapseButton(api, bounds, false, onClick);
    }
}
