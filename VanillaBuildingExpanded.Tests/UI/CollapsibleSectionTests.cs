using VanillaBuildingExpanded.UI;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed class CollapsibleSectionTests
{
    [Fact]
    public void Toggle_IsDeferredAndDoesNotChangeOtherSections()
    {
        var first = CreateSection("first", 100);
        var second = CreateSection("second", 60);
        first.RequestToggle();
        second.RequestToggle();
        Assert.False(first.IsCollapsed);
        Assert.False(second.IsCollapsed);

        Assert.True(first.ApplyPendingToggle());
        Assert.True(first.IsCollapsed);
        Assert.False(second.IsCollapsed);
        Assert.True(second.ApplyPendingToggle());
        Assert.True(second.IsCollapsed);
        Assert.False(first.ApplyPendingToggle());
        Assert.True(first.IsCollapsed);
    }

    [Fact]
    public void PairedClicksBeforeRecomposition_CancelEachOther()
    {
        var section = CreateSection("controls", 100);
        section.RequestToggle();
        section.RequestToggle();
        Assert.False(section.ApplyPendingToggle());
        Assert.False(section.IsCollapsed);
    }

    [Fact]
    public void Collapsing_RemovesOnlyContentHeightAndPreservesExpandedWidth()
    {
        double contentHeight = 100;
        double contentWidth = 300;
        var first = new CollapsibleSection("first", "first-title", () => contentWidth, _ => contentHeight, (_, _) => { });
        var second = CreateSection("second", 60);
        double width = first.MeasureWidth();
        double expandedFirstHeight = first.MeasureHeight(width);
        double secondHeight = second.MeasureHeight(width);
        first.RequestToggle();
        first.ApplyPendingToggle();
        double collapsedFirstHeight = first.MeasureHeight(width);
        Assert.Equal(100, expandedFirstHeight - collapsedFirstHeight);
        Assert.Equal(width, first.MeasureWidth());
        Assert.Equal(secondHeight, second.MeasureHeight(width));

        contentHeight = 140;
        contentWidth = 360;
        Assert.Equal(width + 60, first.MeasureWidth());
        Assert.Equal(collapsedFirstHeight, first.MeasureHeight(width));
        first.RequestToggle();
        first.ApplyPendingToggle();
        Assert.Equal(collapsedFirstHeight + contentHeight, first.MeasureHeight(width));
    }

    [Fact]
    public void Measurement_UsesAvailableContentWidthForReflow()
    {
        double measuredContentWidth = 0;
        var section = new CollapsibleSection("wrapped", "wrapped-title", () => 300,
            width =>
            {
                measuredContentWidth = width;
                return width < 400 ? 80 : 40;
            }, (_, _) => { });
        double preferredWidth = section.MeasureWidth();
        double narrowHeight = section.MeasureHeight(preferredWidth);
        Assert.Equal(300, measuredContentWidth);
        double wideHeight = section.MeasureHeight(preferredWidth + 100);
        Assert.Equal(400, measuredContentWidth);
        Assert.Equal(40, narrowHeight - wideHeight);
    }

    [Fact]
    public void CancelInput_DiscardsPendingToggleWithoutResettingCollapsedState()
    {
        var section = CreateSection("controls", 100);
        section.RequestToggle();
        section.CancelInput();
        Assert.False(section.ApplyPendingToggle());
        Assert.False(section.IsCollapsed);

        section.RequestToggle();
        section.ApplyPendingToggle();
        section.RequestToggle();
        section.CancelInput();
        Assert.False(section.ApplyPendingToggle());
        Assert.True(section.IsCollapsed);
    }

    private static CollapsibleSection CreateSection(string id, double contentHeight) =>
        new(id, $"{id}-title", () => 300, _ => contentHeight, (_, _) => { });
}
