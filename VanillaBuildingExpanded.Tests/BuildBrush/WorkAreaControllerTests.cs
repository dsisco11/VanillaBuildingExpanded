using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class WorkAreaControllerTests
{
    [Fact]
    public void NamedAreas_SelectRenameAndToggleIndependentlyWithoutMutatingOtherBounds()
    {
        var controller = new WorkAreaController();
        controller.SetContext(true, true);
        var first = controller.SelectedId!.Value;
        controller.RenameArea(first, " Workshop ");
        controller.TrySetEndpoints(new(1, 2, 3, 0), new(4, 5, 6, 0), Accessor());
        controller.SetVisibility(true);
        var original = Assert.Single(controller.Areas);
        var second = controller.CreateArea("Garden");
        Assert.NotEqual(first, second);
        Assert.Equal(second, controller.SelectedId);
        Assert.Null(controller.State.Bounds);
        Assert.True(controller.State.ShowWorkArea);
        controller.TrySetEndpoints(new(20, 30, 40, 2), null, Accessor());
        controller.SetVisibility(false);
        controller.SelectArea(first);
        Assert.Equal(original.Start, controller.State.Start);
        Assert.Equal(original.End, controller.State.End);
        Assert.True(controller.State.ShowWorkArea);
        Assert.Equal("Workshop", controller.Areas[0].Name);
        controller.SetVisibility(second, true);
        Assert.Equal(first, controller.SelectedId);
        Assert.True(controller.Areas[1].IsVisible);
        controller.SetVisibility(first, false);
        Assert.False(controller.State.ShowWorkArea);
        Assert.True(controller.Areas[1].IsVisible);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A\nB")]
    [InlineData("A\tB")]
    [InlineData("A\0B")]
    public void Names_RejectEmptyAndControlCharactersWithoutChangingTheCollection(string name)
    {
        var controller = new WorkAreaController();
        Assert.Equal(WorkAreaNameError.Invalid, controller.ValidateName(name));
        Assert.Throws<ArgumentException>(() => controller.CreateArea(name));
        Assert.Single(controller.Areas);
    }

    [Fact]
    public void Names_TrimPreserveUnicodeAndRequireCaseInsensitiveUniquenessAndBoundedLength()
    {
        var controller = new WorkAreaController();
        var first = controller.SelectedId!.Value;
        string unicode = "Garden " + char.ConvertFromUtf32(0x1F33B);
        controller.RenameArea(first, unicode);
        Assert.Equal(unicode, controller.Areas[0].Name);
        Assert.Equal(WorkAreaNameError.Duplicate, controller.ValidateName(" " + unicode.ToUpperInvariant() + " "));
        Assert.Equal(WorkAreaNameError.None, controller.ValidateName(unicode, first));
        Assert.Throws<ArgumentException>(() => controller.CreateArea(new string('x', 65)));
        var second = controller.CreateArea(new string('x', 64));
        Assert.Throws<ArgumentException>(() => controller.RenameArea(second, unicode));
        Assert.Throws<KeyNotFoundException>(() => controller.SelectArea(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => controller.DeleteArea(Guid.NewGuid()));
    }

    [Fact]
    public void SelectingOrDeletingDuringPreviewRollsBackOriginalAreaAndNeverCommitsItsPreview()
    {
        var controller = new WorkAreaController();
        controller.SetContext(true, true);
        var first = controller.SelectedId!.Value;
        controller.TrySetEndpoints(new(1, 2, 3, 0), new(4, 5, 6, 0), Accessor());
        var second = controller.CreateArea("Second");
        controller.SelectArea(first);
        Assert.True(controller.BeginTranslation());
        Assert.True(controller.PreviewTranslation(10, 10, 10, Accessor()));
        Assert.Equal(1, controller.CommittedAreas.First().Start!.Value.X);
        controller.RenameArea(second, "Other");
        Assert.True(controller.IsEditing);
        Assert.Equal(1, controller.CommittedAreas.First().Start!.Value.X);
        int commits = 0;
        controller.EndpointsCommitted += () => commits++;
        controller.SelectArea(second);
        Assert.False(controller.IsEditing);
        Assert.Equal(1, controller.Areas[0].Start!.Value.X);
        Assert.Null(controller.State.Start);
        Assert.Equal(0, commits);
        controller.SelectArea(first);
        controller.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        controller.PreviewEndpoint(new(-20, 2, 3, 0), Accessor());
        controller.DeleteArea(first);
        Assert.Equal(second, controller.SelectedId);
        Assert.Null(controller.State.Start);
        Assert.False(controller.IsEditing);
        Assert.Equal(0, commits);
    }

    [Fact]
    public void DeletingLastAreaLeavesAnEmptyNonEditableCollectionAndCreatingAnAreaRestoresEditing()
    {
        var controller = new WorkAreaController();
        controller.SetContext(true, true);
        controller.DeleteArea(controller.SelectedId!.Value);
        Assert.Empty(controller.Areas);
        Assert.Null(controller.SelectedId);
        Assert.False(controller.State.CanPick);
        Assert.False(controller.TrySetEndpoints(new(1, 2, 3, 0), null, Accessor()));
        controller.SetVisibility(true);
        Assert.False(controller.State.ShowWorkArea);
        var id = controller.CreateArea("New");
        Assert.Equal(id, controller.SelectedId);
        Assert.True(controller.State.CanPick);
        Assert.True(controller.State.ShowWorkArea);
    }

    [Fact]
    public void RestoredAreasNormalizeAllCornersAndStartHiddenWithoutEditingOrSaving()
    {
        var controller = new WorkAreaController();
        int commits = 0;
        controller.AreasCommitted += () => commits++;
        var id = Guid.NewGuid();
        controller.RestoreAreas([new(id, "Home", new(10, 1, 30, 0), new(5, 2, 20, 0), true),
            new(Guid.NewGuid(), "Field", null, new(1, 2, 3, 2), true)]);
        Assert.Equal(id, controller.SelectedId);
        Assert.Equal(new WorkAreaEndpoint(5, 1, 20, 0), controller.State.Start);
        Assert.All(controller.Areas, area => Assert.False(area.IsVisible));
        Assert.False(controller.IsEditing);
        Assert.Equal(0, commits);
        Assert.Throws<ArgumentException>(() => controller.RestoreAreas([controller.Areas[0], controller.Areas[0]]));
        Assert.Equal(2, controller.Areas.Count);
    }

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
