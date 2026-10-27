using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class WorkAreaControllerTests
{
    [Fact]
    public void NewController_IsInactiveIncompleteAndHidden()
    {
        var controller = new WorkAreaController();
        Assert.Null(controller.State.Start);
        Assert.Null(controller.State.End);
        Assert.Null(controller.State.Bounds);
        Assert.False(controller.State.CanPick);
        Assert.False(controller.State.ShowWorkArea);
        Assert.False(controller.IsEditing);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void DirectEdit_RequiresEnabledModeAndLivingPlayer(bool mode, bool alive)
    {
        var controller = new WorkAreaController();
        controller.SetContext(mode, alive);
        Assert.False(controller.TrySetEndpoints(new(1, 2, 3, 0), null, Accessor()));
        Assert.Null(controller.State.Start);
    }

    [Fact]
    public void Visibility_OnlyNotifiesActualChangesAndDoesNotMutateEndpoints()
    {
        var controller = new WorkAreaController();
        var changes = new List<WorkAreaState>();
        controller.Changed += changes.Add;
        controller.SetVisibility(true);
        controller.SetVisibility(true);
        Assert.Single(changes);
        Assert.True(controller.State.ShowWorkArea);
        Assert.Null(controller.State.Bounds);
        controller.SetContext(false, false);
        Assert.True(controller.State.ShowWorkArea);
        controller.SetVisibility(false);
        Assert.False(controller.State.ShowWorkArea);
    }

    [Theory]
    [InlineData(10, 65, 20, 14, 67, 23, 5, 3, 4)]
    [InlineData(14, 67, 23, 10, 65, 20, 5, 3, 4)]
    [InlineData(-14, 65, -23, -10, 67, -20, 5, 3, 4)]
    [InlineData(14, 65, -23, -10, 67, -20, 25, 3, 4)]
    [InlineData(-4, 7, -9, -4, 7, -9, 1, 1, 1)]
    public void Endpoints_AlwaysMatchInclusiveMinimumAndMaximumCorners(
        int x, int y, int z, int ex, int ey, int ez, long sx, long sy, long sz)
    {
        var controller = new WorkAreaController();
        controller.SetContext(true, true);
        var start = new WorkAreaEndpoint(x, y, z, 2);
        var end = new WorkAreaEndpoint(ex, ey, ez, 2);
        Assert.True(controller.TrySetEndpoints(start, end, Accessor()));
        var bounds = controller.State.Bounds!.Value;
        Assert.Equal(bounds.MinCell, controller.State.Start);
        Assert.Equal(bounds.MaxCell, controller.State.End);
        Assert.Equal((sx, sy, sz), (bounds.SizeX, bounds.SizeY, bounds.SizeZ));
        Assert.Equal(Math.Max(x, ex) + 1d, bounds.RenderMaxExclusiveX);
        Assert.Equal(Math.Max(y, ey) + 1d, bounds.RenderMaxExclusiveY);
        Assert.Equal(Math.Max(z, ez) + 1d, bounds.RenderMaxExclusiveZ);
        Assert.Equal(bounds, new WorkAreaBounds(end, start));
    }

    [Fact]
    public void ExtremeBounds_PromoteArithmeticBeforeSubtractingOrAdding()
    {
        var bounds = new WorkAreaBounds(new(int.MinValue, int.MaxValue, int.MinValue, 0),
            new(int.MaxValue, int.MinValue, int.MaxValue, 0));
        Assert.Equal((4294967296L, 4294967296L, 4294967296L), (bounds.SizeX, bounds.SizeY, bounds.SizeZ));
        Assert.Equal(2147483648d, bounds.RenderMaxExclusiveX);
        Assert.Equal(-2147483648d, bounds.RenderMinX);
    }

    [Fact]
    public void Bounds_RejectCrossDimensionPairsAndIncompleteSnapshots()
    {
        var start = new WorkAreaEndpoint(1, 2, 3, 0);
        var end = new WorkAreaEndpoint(4, 5, 6, 1);
        Assert.Throws<ArgumentException>(() => new WorkAreaBounds(start, end));
        Assert.Null(new WorkAreaState(start, end, false, true, true).Bounds);
        Assert.Null(new WorkAreaState(start, null, false, true, true).Bounds);
    }

    private static IBlockAccessor Accessor()
    {
        var accessor = new Mock<IBlockAccessor>();
        accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(true);
        return accessor.Object;
    }
}
