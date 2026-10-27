using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed partial class BuildModeHudControlsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void GenericListBoxSnapshotsTypedItemsAndSelectsDistinctRowsWithTheSameLabel(float scale)
    {
        using var f = new Fixture(scale);
        var source = new List<ListBoxTestItem> { new(10, "Same"), new(20, "Same"), new(30, "<Other> & item") };
        var selected = new List<int>();
        var list = new GuiElementListBox<ListBoxTestItem>(f.Api.Object, source, item => item.Label,
            item => selected.Add(item.Key), ElementBounds.Fixed(0, 0, 180, 0),
            selectedIndex: 0, maximumVisibleRows: 2, rowHeight: 26, emptyText: "No choices");
        source.Clear();
        using var composer = f.Composer();
        var table = new GuiElementTable(f.Api.Object, ElementBounds.Fixed(0, 0, 0, 0));
        table.AddRowCells([new TableCell(list, "list", input: TableInput.Pointer, cancelInput: list.CancelPress)]);
        Assert.True(composer.TryAddTable(table, "table"));
        composer.Compose();
        Assert.Equal(3, list.Items.Count);
        Assert.Equal("<Other> & item", list.Items[2].Label);
        Assert.Equal(180, list.Bounds.fixedWidth);
        Assert.Equal(52, list.Bounds.fixedHeight);
        Assert.Equal(26, list.RowHeight);
        Assert.Equal(2, list.VisibleRows);
        Assert.False(list.Focusable);
        int x = (int)list.Bounds.renderX + 5;
        int y = (int)(list.Bounds.renderY + 1.5 * list.RowHeight * scale);
        var press = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        table.OnMouseDown(f.Api.Object, press);
        Assert.True(press.Handled);
        Assert.Empty(selected);
        table.OnMouseUp(f.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal([20], selected);
        table.OnMouseDown(f.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        table.OnMouseUp(f.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.Equal([20], selected);
        f.MouseX = x;
        f.MouseY = y;
        var wheel = new MouseWheelEventArgs { delta = -1, deltaPrecise = -1 };
        table.OnMouseWheel(f.Api.Object, wheel);
        Assert.True(wheel.IsHandled);
        Assert.Equal(1, list.FirstVisible);
        table.OnMouseDown(f.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        table.OnMouseUp(f.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal([20, 30], selected);
        composer.Dispose();
        Assert.All(f.Uploads, upload => Assert.True(upload.Texture.Disposed));
    }

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 1)]
    public void GenericListBoxUsesCallerViewportAndOptionalSelectionReveal(bool reveal, int expectedFirst)
    {
        using var f = new Fixture();
        using var list = new GuiElementListBox<int>(f.Api.Object, Enumerable.Range(0, 8),
            item => "Choice " + item, _ => { }, ElementBounds.Fixed(7, 9, 240, 0),
            selectedIndex: 7, maximumVisibleRows: 4, rowHeight: 30, firstVisible: 1, revealSelection: reveal);
        Assert.Equal(4, list.VisibleRows);
        Assert.Equal(120, list.Bounds.fixedHeight);
        Assert.Equal(240, list.Bounds.fixedWidth);
        Assert.Equal(7, list.Bounds.fixedX);
        Assert.Equal(9, list.Bounds.fixedY);
        Assert.Equal(expectedFirst, list.FirstVisible);
        Assert.Equal(7, list.SelectedIndex);
    }

    [Fact]
    public void GenericListBoxSupportsEmptyStringsWithoutWorkAreaLocalizationOrSelection()
    {
        using var f = new Fixture();
        using var list = new GuiElementListBox<string>(f.Api.Object, [], text => text,
            _ => Assert.Fail("An empty list cannot select an item."),
            ElementBounds.Fixed(0, 0, 180, 0).WithEmptyParent(), rowHeight: 24, emptyText: "No available choices");
        using var composer = f.Composer();
        composer.AddInteractiveElement(list, "list").Compose();
        Assert.Empty(list.Items);
        Assert.Equal(-1, list.SelectedIndex);
        Assert.False(list.Enabled);
        Assert.Equal(1, list.VisibleRows);
        Assert.Equal(24, list.Bounds.fixedHeight);
        list.OnMouseDown(f.Api.Object, new MouseEvent(5, 5, EnumMouseButton.Left, 0));
        var release = new MouseEvent(5, 5, EnumMouseButton.Left, 0);
        list.OnMouseUp(f.Api.Object, release);
        Assert.True(release.Handled);
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Theory]
    [InlineData(0, 22, -1)]
    [InlineData(-1, 22, -1)]
    [InlineData(3, 0, -1)]
    [InlineData(3, -1, -1)]
    [InlineData(3, 22, -2)]
    [InlineData(3, 22, 1)]
    public void GenericListBoxRejectsInvalidViewportAndSelectionArguments(int rows, int height, int selected)
    {
        using var f = new Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => new GuiElementListBox<string>(f.Api.Object,
            ["Item"], text => text, _ => { }, ElementBounds.Fixed(0, 0, 180, 0),
            selectedIndex: selected, maximumVisibleRows: rows, rowHeight: height));
    }

    private sealed record ListBoxTestItem(int Key, string Label);
}
