using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class RulerControllerTests
{
    [Fact]
    public void NewController_IsInactiveIncompleteAndHidden()
    {
        var controller = new RulerController();

        Assert.Null(controller.State.Start);
        Assert.Null(controller.State.End);
        Assert.Null(controller.State.Bounds);
        Assert.Equal(RulerPickMode.Idle, controller.State.PickMode);
        Assert.False(controller.State.IsBuildModeEnabled);
        Assert.False(controller.State.IsPlayerAlive);
        Assert.False(controller.State.CanPick);
        Assert.False(controller.State.ShowRuler);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Arming_RequiresEnabledModeAndLivingPlayer(bool mode, bool alive)
    {
        var controller = new RulerController();
        controller.SetContext(mode, alive);
        List<RulerState> changes = [];
        List<RulerNotice> notices = [];
        controller.Changed += changes.Add;
        controller.NoticeRequested += notices.Add;

        Assert.False(controller.TogglePicking(RulerPickMode.PickingStart));
        Assert.False(controller.TogglePicking(RulerPickMode.PickingEnd));
        Assert.False(controller.TrySelect(Position(1, 2, 3), true));
        Assert.Equal(RulerPickMode.Idle, controller.State.PickMode);
        Assert.Empty(changes);
        Assert.Empty(notices);
    }

    [Fact]
    public void Arming_SwitchesEndpointsAndSameButtonCancelsWithoutEditingEndpoints()
    {
        var controller = CompleteRuler();
        var start = controller.State.Start;
        var end = controller.State.End;
        List<RulerNotice> notices = [];
        controller.NoticeRequested += notices.Add;

        Assert.True(controller.TogglePicking(RulerPickMode.PickingStart));
        Assert.Equal(RulerPickMode.PickingStart, controller.State.PickMode);
        Assert.True(controller.TogglePicking(RulerPickMode.PickingEnd));
        Assert.Equal(RulerPickMode.PickingEnd, controller.State.PickMode);
        Assert.True(controller.TogglePicking(RulerPickMode.PickingEnd));
        Assert.Equal(RulerPickMode.Idle, controller.State.PickMode);
        Assert.Equal(start, controller.State.Start);
        Assert.Equal(end, controller.State.End);
        Assert.Equal(new[] { RulerNotice.SelectStart, RulerNotice.SelectEnd }, notices);
    }

    [Theory]
    [InlineData((int)RulerPickMode.Idle)]
    [InlineData(123)]
    public void Arming_RejectsInvalidModesWithoutMutation(int mode)
    {
        var controller = ActiveController();
        var before = controller.State;

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.TogglePicking((RulerPickMode)mode));
        Assert.Equal(before, controller.State);
    }

    [Theory]
    [InlineData((int)RulerPickMode.PickingStart)]
    [InlineData((int)RulerPickMode.PickingEnd)]
    public void Selection_EitherEndpointCanBeFirstAndIncompleteHasNoBounds(int modeValue)
    {
        var mode = (RulerPickMode)modeValue;
        var controller = ActiveController();
        List<RulerState> changes = [];
        controller.TogglePicking(mode);
        controller.Changed += changes.Add;

        Assert.True(controller.TrySelect(Position(10, 65, 20, 2), true));
        var endpoint = new RulerEndpoint(10, 65, 20, 2);
        Assert.Equal(mode == RulerPickMode.PickingStart ? endpoint : (RulerEndpoint?)null,
            controller.State.Start);
        Assert.Equal(mode == RulerPickMode.PickingEnd ? endpoint : (RulerEndpoint?)null,
            controller.State.End);
        Assert.Null(controller.State.Bounds);
        Assert.Equal(RulerPickMode.Idle, controller.State.PickMode);
        Assert.Equal(controller.State, Assert.Single(changes));
    }

    [Theory]
    [InlineData((int)RulerPickMode.PickingStart)]
    [InlineData((int)RulerPickMode.PickingEnd)]
    public void Selection_ReplacesOnlyRequestedEndpointAndCopiesLocalCoordinates(int modeValue)
    {
        var mode = (RulerPickMode)modeValue;
        var controller = CompleteRuler(dimension: 2);
        var before = controller.State;
        var position = Position(-10, 70, -20, 2);
        controller.TogglePicking(mode);

        Assert.True(controller.TrySelect(position, true));
        var snapshot = controller.State;
        position.X = 999;
        position.Y = 999;
        position.Z = 999;
        position.dimension = 999;

        Assert.Equal(snapshot, controller.State);
        Assert.Equal(mode == RulerPickMode.PickingStart ? new RulerEndpoint(-10, 70, -20, 2) : before.Start,
            snapshot.Start);
        Assert.Equal(mode == RulerPickMode.PickingEnd ? new RulerEndpoint(-10, 70, -20, 2) : before.End,
            snapshot.End);
        Assert.Equal(2, snapshot.Bounds!.Value.MinCell.Dimension);
        Assert.Equal(mode == RulerPickMode.PickingStart ? 67 : 65, snapshot.Bounds.Value.MinCell.Y);
        Assert.Equal(70, snapshot.Bounds.Value.MaxCell.Y);
    }

    [Theory]
    [InlineData((int)RulerPickMode.PickingStart, true, true)]
    [InlineData((int)RulerPickMode.PickingEnd, true, false)]
    [InlineData((int)RulerPickMode.PickingStart, false, false)]
    [InlineData((int)RulerPickMode.PickingEnd, false, false)]
    public void InvalidTarget_KeepsPriorEndpointsAndArmedModeWithFeedback(
        int modeValue, bool missingPosition, bool validPosition)
    {
        var mode = (RulerPickMode)modeValue;
        var controller = CompleteRuler();
        controller.TogglePicking(mode);
        var before = controller.State;
        List<RulerState> changes = [];
        List<RulerNotice> notices = [];
        controller.Changed += changes.Add;
        controller.NoticeRequested += notices.Add;

        Assert.False(controller.TrySelect(missingPosition ? null : Position(1, 2, 3), validPosition));
        Assert.Equal(before, controller.State);
        Assert.Empty(changes);
        Assert.Equal(RulerNotice.AimAtBlock, Assert.Single(notices));
    }

    [Fact]
    public void IdleSelection_DoesNotEditNotifyOrRepeatSuccessfulSelection()
    {
        var controller = CompleteRuler();
        var before = controller.State;
        List<RulerState> changes = [];
        List<RulerNotice> notices = [];
        controller.Changed += changes.Add;
        controller.NoticeRequested += notices.Add;

        Assert.False(controller.TrySelect(Position(99, 99, 99), true));
        Assert.False(controller.TrySelect(null, false));
        Assert.Equal(before, controller.State);
        Assert.Empty(changes);
        Assert.Empty(notices);
    }

    [Theory]
    [InlineData((int)RulerPickMode.PickingStart)]
    [InlineData((int)RulerPickMode.PickingEnd)]
    public void CrossDimensionSelection_PublishesOneCoherentPartialResetAndFeedback(int modeValue)
    {
        var mode = (RulerPickMode)modeValue;
        var controller = CompleteRuler();
        controller.TogglePicking(mode);
        List<RulerState> changes = [];
        List<RulerNotice> notices = [];
        controller.Changed += state =>
        {
            Assert.Equal(state, controller.State);
            Assert.Null(state.Bounds);
            Assert.Equal(RulerPickMode.Idle, state.PickMode);
            changes.Add(state);
        };
        controller.NoticeRequested += notice =>
        {
            Assert.Single(changes);
            notices.Add(notice);
        };

        Assert.True(controller.TrySelect(Position(9, 80, 7, 3), true));
        var next = Assert.Single(changes);
        var endpoint = new RulerEndpoint(9, 80, 7, 3);
        Assert.Equal(mode == RulerPickMode.PickingStart ? endpoint : (RulerEndpoint?)null, next.Start);
        Assert.Equal(mode == RulerPickMode.PickingEnd ? endpoint : (RulerEndpoint?)null, next.End);
        Assert.Equal(RulerNotice.SetOtherEndpoint, Assert.Single(notices));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ContextLoss_CancelsPickingButRetainsEndpointsAndVisibility(bool mode, bool alive)
    {
        var controller = CompleteRuler();
        controller.SetVisibility(true);
        controller.TogglePicking(RulerPickMode.PickingEnd);
        var before = controller.State;
        List<RulerState> changes = [];
        controller.Changed += changes.Add;

        controller.SetContext(mode, alive);
        Assert.Equal(RulerPickMode.Idle, controller.State.PickMode);
        Assert.Equal(before.Start, controller.State.Start);
        Assert.Equal(before.End, controller.State.End);
        Assert.True(controller.State.ShowRuler);
        Assert.False(controller.State.CanPick);
        Assert.Single(changes);
        Assert.False(controller.TrySelect(Position(90, 90, 90), true));
        controller.SetContext(true, true);
        Assert.Equal(RulerPickMode.Idle, controller.State.PickMode);
        Assert.True(controller.State.CanPick);
        Assert.True(controller.State.ShowRuler);
        Assert.Equal(before.Bounds, controller.State.Bounds);
    }

    [Fact]
    public void Cancellation_IsIdempotentAndDoesNotClearRulerOrVisibility()
    {
        var controller = CompleteRuler();
        controller.SetVisibility(true);
        var before = controller.State;
        controller.TogglePicking(RulerPickMode.PickingStart);
        List<RulerState> changes = [];
        controller.Changed += changes.Add;

        controller.CancelPicking();
        controller.CancelPicking();
        Assert.Equal(before, controller.State);
        Assert.Equal(before, Assert.Single(changes));
    }

    [Fact]
    public void Visibility_CanBeEnabledWhileIncompleteAndSurvivesPickingChanges()
    {
        var controller = ActiveController();
        List<RulerState> changes = [];
        List<RulerNotice> notices = [];
        controller.Changed += changes.Add;
        controller.NoticeRequested += notices.Add;

        controller.SetVisibility(true);
        controller.SetVisibility(true);
        Assert.True(controller.State.ShowRuler);
        Assert.Null(controller.State.Bounds);
        Assert.Single(changes);
        Assert.Equal(RulerNotice.SetBothEndpoints, Assert.Single(notices));
        controller.TogglePicking(RulerPickMode.PickingStart);
        controller.CancelPicking();
        Assert.True(controller.State.ShowRuler);
        controller.SetVisibility(false);
        Assert.False(controller.State.ShowRuler);
    }

    [Fact]
    public void RepeatedContextAndCompleteVisibility_OnlyNotifyActualStateChanges()
    {
        var controller = CompleteRuler();
        List<RulerState> changes = [];
        List<RulerNotice> notices = [];
        controller.Changed += changes.Add;
        controller.NoticeRequested += notices.Add;

        controller.SetContext(true, true);
        controller.SetVisibility(false);
        controller.CancelPicking();
        Assert.Empty(changes);
        controller.SetVisibility(true);
        controller.SetVisibility(true);
        Assert.Single(changes);
        Assert.Empty(notices);
    }

    [Fact]
    public void ExampleBounds_AreInclusiveWithExactRenderExtents()
    {
        var controller = CompleteRuler();
        var bounds = controller.State.Bounds!.Value;

        Assert.Equal(new RulerEndpoint(10, 65, 20, 0), bounds.MinCell);
        Assert.Equal(new RulerEndpoint(14, 67, 23, 0), bounds.MaxCell);
        Assert.Equal((5L, 3L, 4L), (bounds.SizeX, bounds.SizeY, bounds.SizeZ));
        Assert.Equal((10d, 65d, 20d), (bounds.RenderMinX, bounds.RenderMinY, bounds.RenderMinZ));
        Assert.Equal((15d, 68d, 24d),
            (bounds.RenderMaxExclusiveX, bounds.RenderMaxExclusiveY, bounds.RenderMaxExclusiveZ));
    }

    [Fact]
    public void SameCellBounds_EncloseOneWholeBlock()
    {
        var point = new RulerEndpoint(-4, 7, -9, 2);
        var bounds = new RulerBounds(point, point);

        Assert.Equal(point, bounds.MinCell);
        Assert.Equal(point, bounds.MaxCell);
        Assert.Equal((1L, 1L, 1L), (bounds.SizeX, bounds.SizeY, bounds.SizeZ));
        Assert.Equal((-4d, 7d, -9d), (bounds.RenderMinX, bounds.RenderMinY, bounds.RenderMinZ));
        Assert.Equal((-3d, 8d, -8d),
            (bounds.RenderMaxExclusiveX, bounds.RenderMaxExclusiveY, bounds.RenderMaxExclusiveZ));
    }

    [Fact]
    public void ReversedAndNegativeCoordinates_NormalizePerAxisWithoutSwappingOrigin()
    {
        var controller = ActiveController();
        controller.TogglePicking(RulerPickMode.PickingStart);
        controller.TrySelect(Position(14, -5, -23, 4), true);
        controller.TogglePicking(RulerPickMode.PickingEnd);
        controller.TrySelect(Position(-10, 7, -20, 4), true);
        var bounds = controller.State.Bounds!.Value;

        Assert.Equal(new RulerEndpoint(14, -5, -23, 4), controller.State.Start);
        Assert.Equal(new RulerEndpoint(-10, 7, -20, 4), controller.State.End);
        Assert.Equal(new RulerEndpoint(-10, -5, -23, 4), bounds.MinCell);
        Assert.Equal(new RulerEndpoint(14, 7, -20, 4), bounds.MaxCell);
        Assert.Equal((25L, 13L, 4L), (bounds.SizeX, bounds.SizeY, bounds.SizeZ));
        Assert.Equal(bounds, new RulerBounds(controller.State.End!.Value, controller.State.Start!.Value));
    }

    [Fact]
    public void ExtremeCoordinates_PromoteArithmeticBeforeSubtractingOrAdding()
    {
        var bounds = new RulerBounds(
            new RulerEndpoint(int.MinValue, int.MaxValue, int.MinValue, 0),
            new RulerEndpoint(int.MaxValue, int.MinValue, int.MaxValue, 0));

        Assert.Equal((4294967296L, 4294967296L, 4294967296L), (bounds.SizeX, bounds.SizeY, bounds.SizeZ));
        Assert.Equal((-2147483648d, -2147483648d, -2147483648d),
            (bounds.RenderMinX, bounds.RenderMinY, bounds.RenderMinZ));
        Assert.Equal((2147483648d, 2147483648d, 2147483648d),
            (bounds.RenderMaxExclusiveX, bounds.RenderMaxExclusiveY, bounds.RenderMaxExclusiveZ));
    }

    [Fact]
    public void Bounds_RejectCrossDimensionPairsAndIncompleteSnapshotsDoNotConstructThem()
    {
        var start = new RulerEndpoint(1, 2, 3, 0);
        var end = new RulerEndpoint(4, 5, 6, 1);
        Assert.Throws<ArgumentException>(() => new RulerBounds(start, end));
        var state = new RulerState(start, end, RulerPickMode.Idle, false, true, true);
        Assert.Null(state.Bounds);
    }

    private static BlockPos Position(int x, int y, int z, int dimension = 0) => new(x, y, z, dimension);

    private static RulerController ActiveController()
    {
        var controller = new RulerController();
        controller.SetContext(true, true);
        return controller;
    }

    private static RulerController CompleteRuler(int dimension = 0)
    {
        var controller = ActiveController();
        controller.TogglePicking(RulerPickMode.PickingStart);
        controller.TrySelect(Position(10, 65, 20, dimension), true);
        controller.TogglePicking(RulerPickMode.PickingEnd);
        controller.TrySelect(Position(14, 67, 23, dimension), true);
        return controller;
    }
}
