using System.Runtime.Intrinsics;

using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.PositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class RulerTransactionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreviewCrossing_PreservesRoleAndCommittedPairThenCancelsOrCompletesOnce(bool start)
    {
        var controller = Controller();
        var baseline = controller.State;
        var role = start ? RulerEndpointRole.Start : RulerEndpointRole.End;
        int commits = 0, changes = 0;
        controller.EndpointsCommitted += () => commits++;
        controller.Changed += _ => changes++;
        Assert.True(controller.BeginEndpointEdit(role));
        Assert.False(controller.BeginEndpointEdit(role));
        for (int i = 0; i < 100; i++)
        {
            var target = new RulerEndpoint(-i, 80, -i, 2);
            Assert.True(controller.PreviewEndpoint(target, Accessor().Object));
            Assert.Equal(target, start ? controller.State.Start : controller.State.End);
            Assert.Equal(start ? baseline.End : baseline.Start, start ? controller.State.End : controller.State.Start);
            Assert.Equal((baseline.Start, baseline.End), controller.CommittedEndpoints);
            var bounds = controller.State.Bounds!.Value;
            Assert.Equal((long)Math.Abs((start ? baseline.End : baseline.Start)!.Value.X - target.X) + 1,
                bounds.SizeX);
        }
        Assert.True(changes >= 100);
        Assert.Equal(0, commits);
        controller.CancelEndpointEdit();
        controller.CancelEndpointEdit();
        Assert.Equal(baseline, controller.State);
        Assert.False(controller.CompleteEndpointEdit());
        Assert.True(controller.BeginEndpointEdit(role));
        Assert.True(controller.PreviewEndpoint(new(-20, 70, -30, 2), Accessor().Object));
        Assert.True(controller.CompleteEndpointEdit());
        Assert.False(controller.CompleteEndpointEdit());
        Assert.False(controller.IsEditing);
        Assert.Equal(1, commits);
        Assert.Equal((controller.State.Start, controller.State.End), controller.CommittedEndpoints);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(32768, 2)]
    [InlineData(65, 3)]
    [InlineData(65, -1)]
    [InlineData(65, 65536)]
    public void InvalidPreview_KeepsLastValidValueAndReports(int y, int dimension)
    {
        var controller = Controller();
        Assert.True(controller.BeginEndpointEdit(RulerEndpointRole.Start));
        Assert.True(controller.PreviewEndpoint(new(30, 66, 40, 2), Accessor().Object));
        var before = controller.State;
        List<RulerNotice> notices = [];
        controller.NoticeRequested += notices.Add;
        Assert.False(controller.PreviewEndpoint(new(50, y, 60, dimension), Accessor().Object));
        Assert.False(controller.PreviewEndpoint(new(50, 65, 60, 2), Accessor(false).Object));
        Assert.Equal(before, controller.State);
        Assert.Equal(new[] { RulerNotice.InvalidEndpoint, RulerNotice.InvalidEndpoint }, notices);
    }

    [Fact]
    public void ExternalReplacement_CancelsBeforeApplyingAndCannotBeRolledBackLater()
    {
        var controller = Controller();
        var baseline = controller.State;
        controller.BeginEndpointEdit(RulerEndpointRole.End);
        controller.PreviewEndpoint(new(99, 70, 99, 2), Accessor().Object);
        controller.RestoreEndpoints(controller.State.Start, controller.State.End);
        Assert.True(controller.IsEditing);
        List<RulerState> changes = [];
        int commits = 0;
        controller.Changed += changes.Add;
        controller.EndpointsCommitted += () => commits++;
        var replacement = new RulerEndpoint(-40, 90, -50, 3);
        controller.RestoreEndpoints(replacement, null);
        Assert.Equal(baseline.Start, changes[0].Start);
        Assert.Equal(baseline.End, changes[0].End);
        Assert.Equal(replacement, changes[1].Start);
        Assert.False(controller.IsEditing);
        controller.CancelEndpointEdit();
        Assert.Equal(replacement, controller.State.Start);
        Assert.Null(controller.State.End);
        Assert.Equal(0, commits);
    }

    [Fact]
    public void DirectEdit_ValidatesBeforeCancelAndNeverCommitsAnUnrelatedPreview()
    {
        var controller = Controller();
        var originalStart = controller.State.Start;
        controller.BeginEndpointEdit(RulerEndpointRole.Start);
        controller.PreviewEndpoint(new(99, 80, 99, 2), Accessor().Object);
        var preview = controller.State;
        Assert.False(controller.TrySetEndpoint(RulerEndpointRole.End, new(40, 70, 50, 3), Accessor().Object));
        Assert.True(controller.IsEditing);
        Assert.Equal(preview, controller.State);
        int commits = 0;
        controller.EndpointsCommitted += () => commits++;
        var replacement = new RulerEndpoint(40, 70, 50, 2);
        Assert.True(controller.TrySetEndpoint(RulerEndpointRole.End, replacement, Accessor().Object));
        Assert.Equal(originalStart, controller.State.Start);
        Assert.Equal(replacement, controller.State.End);
        Assert.False(controller.IsEditing);
        Assert.Equal(1, commits);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("death")]
    [InlineData("hidden")]
    [InlineData("legacy")]
    public void ContextOrLegacyPicker_CancelsWithoutCommittingPreview(string reason)
    {
        var controller = Controller();
        var baseline = controller.State;
        controller.BeginEndpointEdit(RulerEndpointRole.Start);
        controller.PreviewEndpoint(new(99, 70, 99, 2), Accessor().Object);
        int commits = 0;
        controller.EndpointsCommitted += () => commits++;
        if (reason == "mode") controller.SetContext(false, true);
        if (reason == "death") controller.SetContext(true, false);
        if (reason == "hidden") controller.SetVisibility(false);
        if (reason == "legacy") controller.TogglePicking(RulerPickMode.PickingEnd);
        Assert.False(controller.IsEditing);
        Assert.Equal((baseline.Start, baseline.End), (controller.State.Start, controller.State.End));
        Assert.Equal(0, commits);
    }

    [Fact]
    public void Begin_RequiresExistingEndpointAndValidContextAndRole()
    {
        var controller = new RulerController();
        Assert.False(controller.BeginEndpointEdit(RulerEndpointRole.Start));
        controller.SetContext(true, true);
        Assert.False(controller.BeginEndpointEdit(RulerEndpointRole.End));
        controller.RestoreEndpoints(new(1, 2, 3, 0), null);
        Assert.False(controller.BeginEndpointEdit(RulerEndpointRole.End));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.BeginEndpointEdit((RulerEndpointRole)99));
        Assert.False(controller.PreviewEndpoint(new(1, 2, 3, 0), Accessor().Object));
        Assert.True(controller.BeginEndpointEdit(RulerEndpointRole.Start));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CellAnchor_RoundTripsExtremeCoordinatesAndDimensionLocalYWithoutLoading(bool start)
    {
        var role = start ? RulerEndpointRole.Start : RulerEndpointRole.End;
        var accessor = Accessor();
        foreach (int x in new[] { int.MinValue, -1, 0, int.MaxValue })
        foreach (int y in new[] { 0, 32767 })
        foreach (int dimension in new[] { 0, 2, 65535 })
        {
            var cell = new RulerEndpoint(x, y, int.MaxValue, dimension);
            var anchor = RulerGizmoCoordinates.ToAnchor(cell, role);
            Assert.Equal(dimension, anchor.Dimension);
            Assert.Equal(y + (start ? 0d : 1d), anchor.Coordinates[1]);
            Assert.True(RulerGizmoCoordinates.TryDecode(anchor, role, accessor.Object, out var decoded));
            Assert.Equal(cell, decoded);
        }
        accessor.Verify(a => a.IsValidPos(It.Is<BlockPos>(p => p.Y == 32767 && p.dimension == 65535)),
            Times.Exactly(4));
        accessor.Verify(a => a.IsValidPos(It.IsAny<BlockPos>()), Times.Exactly(24));
        accessor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 0.49, 0)]
    [InlineData(0, -0.5, -1)]
    [InlineData(1, 0.5, 1)]
    [InlineData(1, -0.49, 0)]
    [InlineData(2, 1.49, 1)]
    [InlineData(2, -1.5, -2)]
    public void AxisPolicy_RoundsDeltaBeforeAddingAndPreservesUntouchedComponents(int component, double delta, double rounded)
    {
        foreach (var role in new[] { RulerEndpointRole.Start, RulerEndpointRole.End })
        {
            var initial = RulerGizmoCoordinates.ToAnchor(new(-2000000000, 65, -20, 2), role);
            var candidate = new GizmoPosition(initial.Coordinates.WithElement(component,
                initial.Coordinates[component] + delta), 2);
            var accepted = RulerGizmoCoordinates.Constrain(new(initial, candidate, new(component + 1)), role, Accessor().Object);
            Assert.NotNull(accepted);
            Assert.Equal(initial.Coordinates.WithElement(component, initial.Coordinates[component] + rounded),
                accepted.Value.Coordinates);
        }
    }

    [Fact]
    public void CenterAndInvalidAnchors_UseCellOffsetsAndRejectRatherThanClampOrInferDimension()
    {
        var role = RulerEndpointRole.End;
        var initial = RulerGizmoCoordinates.ToAnchor(new(-10, 65, -20, 2), role);
        var target = new RulerEndpoint(-40, 32767, -50, 2);
        var anchor = RulerGizmoCoordinates.ToAnchor(target, role);
        Assert.Equal(anchor, RulerGizmoCoordinates.Constrain(new(initial, anchor, PositionGizmo.Center), role, Accessor().Object));
        foreach (var invalid in new[]
        {
            new GizmoPosition(V(-39.5, 32768, -49), 2),
            new GizmoPosition(V(2147483649d, 66, 0), 2),
            new GizmoPosition(V(0, 32769, 0), 2),
            new GizmoPosition(V(0, 66, 0), 3),
            new GizmoPosition(V(double.MaxValue, 66, 0), 2)
        })
            Assert.Null(RulerGizmoCoordinates.Constrain(new(initial, invalid, PositionGizmo.Center), role, Accessor().Object));
        Assert.False(RulerGizmoCoordinates.TryDecode(anchor, role, Accessor(false).Object, out _));
        Assert.Null(RulerGizmoCoordinates.Constrain(new(initial, anchor, PositionGizmo.X), role, Accessor().Object));
    }

    private static RulerController Controller()
    {
        var controller = new RulerController();
        controller.SetContext(true, true);
        controller.RestoreEndpoints(new(10, 65, 20, 2), new(14, 67, 23, 2));
        return controller;
    }

    private static Mock<IBlockAccessor> Accessor(bool valid = true)
    {
        var accessor = new Mock<IBlockAccessor>(MockBehavior.Strict);
        accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(valid);
        return accessor;
    }
}
