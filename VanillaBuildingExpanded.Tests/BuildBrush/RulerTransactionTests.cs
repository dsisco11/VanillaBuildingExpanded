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
    [Fact]
    public void WholeRulerTranslation_PreviewsFromOriginalPairAtomicallyAndPreservesSize()
    {
        var controller = Controller();
        var original = controller.State;
        var size = original.Bounds!.Value;
        var accessor = Accessor().Object;
        int commits = 0;
        controller.EndpointsCommitted += () => commits++;
        Assert.True(controller.BeginTranslation());
        Assert.False(controller.BeginTranslation());
        Assert.False(controller.BeginEndpointEdit(RulerEndpointRole.Start));
        Assert.False(controller.PreviewEndpoint(new(0, 65, 0, 2), accessor));
        foreach (var (x, y, z) in new[] { (3L, 2L, -5L), (-20L, -10L, 100L), (0L, 0L, 0L) })
        {
            Assert.True(controller.PreviewTranslation(x, y, z, accessor));
            Assert.Equal(original.Start!.Value with
                { X = original.Start.Value.X + (int)x, Y = original.Start.Value.Y + (int)y,
                    Z = original.Start.Value.Z + (int)z }, controller.State.Start);
            Assert.Equal(original.End!.Value with
                { X = original.End.Value.X + (int)x, Y = original.End.Value.Y + (int)y,
                    Z = original.End.Value.Z + (int)z }, controller.State.End);
            var bounds = controller.State.Bounds!.Value;
            Assert.Equal((size.SizeX, size.SizeY, size.SizeZ), (bounds.SizeX, bounds.SizeY, bounds.SizeZ));
            Assert.Equal((original.Start, original.End), controller.CommittedEndpoints);
            Assert.Equal(0, commits);
        }
        Assert.True(controller.PreviewTranslation(2, 1, 3, accessor));
        var preview = controller.State;
        foreach (var (x, y, z) in new[] { (long.MaxValue, 0L, 0L), (0L, -1000L, 0L),
            (0L, 32768L, 0L), (0L, 0L, long.MinValue) })
        {
            Assert.False(controller.PreviewTranslation(x, y, z, accessor));
            Assert.Equal(preview, controller.State);
        }
        controller.CancelEndpointEdit();
        Assert.Equal(original, controller.State);
        Assert.Equal(0, commits);
        Assert.True(controller.BeginTranslation());
        Assert.True(controller.PreviewTranslation(-5, 2, -3, accessor));
        Assert.True(controller.CompleteEndpointEdit());
        Assert.False(controller.CompleteEndpointEdit());
        Assert.Equal(1, commits);
        Assert.False(controller.PreviewTranslation(0, 0, 0, accessor));
    }

    [Fact]
    public void WholeRulerTranslation_RequiresCompletePairAndAllowsWideDeltaWithoutIntegerOverflow()
    {
        var controller = Controller();
        controller.RestoreEndpoints(new(int.MinValue, 32766, int.MaxValue - 1, 65535),
            new(int.MinValue + 1, 32767, int.MaxValue, 65535));
        var original = controller.State;
        Assert.True(controller.BeginTranslation());
        Assert.True(controller.PreviewTranslation(uint.MaxValue - 1L, 0, -uint.MaxValue + 1L, Accessor().Object));
        Assert.Equal(new RulerEndpoint(int.MaxValue - 1, 32766, int.MinValue, 65535), controller.State.Start);
        Assert.Equal(new RulerEndpoint(int.MaxValue, 32767, int.MinValue + 1, 65535), controller.State.End);
        var valid = controller.State;
        Assert.False(controller.PreviewTranslation(uint.MaxValue, 0, 0, Accessor().Object));
        Assert.False(controller.PreviewTranslation(0, 1, 0, Accessor().Object));
        Assert.Equal(valid, controller.State);
        controller.RestoreEndpoints(original.Start, null);
        Assert.False(controller.IsEditing);
        Assert.False(controller.BeginTranslation());
        Assert.True(controller.BeginEndpointEdit(RulerEndpointRole.Start));
        Assert.False(controller.PreviewTranslation(0, 0, 0, Accessor().Object));
    }

    [Theory]
    [InlineData(0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(-0.49, 0)]
    [InlineData(-0.5, -1)]
    public void WholeRulerConstraint_SnapsMidpointDeltasAndRejectsMovesThatInvalidateEitherCorner(double step, int snapped)
    {
        var bounds = new RulerBounds(new(-21, 65, -200, 2), new(30, 75, -180, 2));
        var center = RulerGizmoCoordinates.ToCenter(bounds);
        Assert.Equal(new GizmoPosition(V(5, 70.5, -189.5), 2), center);
        var candidate = new GizmoPosition(center.Coordinates + V(step, -step, step), 2);
        var constrained = RulerGizmoCoordinates.ConstrainTranslation(new(center, candidate, PositionGizmo.Center),
            bounds, Accessor().Object);
        Assert.Equal(new GizmoPosition(center.Coordinates + V(snapped, -snapped, snapped), 2), constrained);
        Assert.Null(RulerGizmoCoordinates.ConstrainTranslation(new(center, candidate, PositionGizmo.X),
            bounds, Accessor().Object));
        foreach (var invalid in new[] { new GizmoPosition(center.Coordinates + V(0, -66, 0), 2),
            new(center.Coordinates + V(0, 32768 - 75, 0), 2), new(center.Coordinates, 3),
            new(center.Coordinates + V(int.MaxValue, 0, 0), 2) })
            Assert.Null(RulerGizmoCoordinates.ConstrainTranslation(new(center, invalid, PositionGizmo.Center),
                bounds, Accessor().Object));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreviewCrossing_NormalizesCornersAndPreservesCommittedPairThenCancelsOrCompletesOnce(bool start)
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
            var expected = new RulerBounds(target, (start ? baseline.End : baseline.Start)!.Value);
            Assert.Equal(expected.MinCell, controller.State.Start);
            Assert.Equal(expected.MaxCell, controller.State.End);
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
    [InlineData(true)]
    [InlineData(false)]
    public void CrossingAndReturning_UsesFixedOppositeCornerRatherThanPreviousNormalizedPreview(bool start)
    {
        var controller = Controller();
        var baseline = controller.State;
        var role = start ? RulerEndpointRole.Start : RulerEndpointRole.End;
        Assert.True(controller.BeginEndpointEdit(role));
        Assert.True(controller.PreviewEndpoint(new(-50, 90, 100, 2), Accessor().Object));
        Assert.True(controller.PreviewEndpoint((start ? baseline.Start : baseline.End)!.Value, Accessor().Object));
        Assert.Equal(baseline, controller.State);
        Assert.True(controller.IsEditing);
        Assert.True(controller.CompleteEndpointEdit());
        Assert.Equal(baseline, controller.State);
    }

    [Fact]
    public void Restore_NormalizesMixedAndReversedLegacyCornersWithoutCommitting()
    {
        var controller = new RulerController();
        int commits = 0;
        controller.EndpointsCommitted += () => commits++;
        controller.RestoreEndpoints(new(30, 60, -10, 2), new(-20, 80, -40, 2));
        Assert.Equal(new RulerEndpoint(-20, 60, -40, 2), controller.State.Start);
        Assert.Equal(new RulerEndpoint(30, 80, -10, 2), controller.State.End);
        Assert.Equal(0, commits);
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
    public void ContextLoss_CancelsWithoutCommittingPreview(string reason)
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
            new GizmoPosition(V(2147483649d, 66, 0), 2),
            new GizmoPosition(V(0, 32769, 0), 2),
            new GizmoPosition(V(0, 66, 0), 3),
            new GizmoPosition(V(double.MaxValue, 66, 0), 2)
        })
            Assert.Null(RulerGizmoCoordinates.Constrain(new(initial, invalid, PositionGizmo.Center), role, Accessor().Object));
        Assert.False(RulerGizmoCoordinates.TryDecode(anchor, role, Accessor(false).Object, out _));
        Assert.Null(RulerGizmoCoordinates.Constrain(new(initial, anchor, PositionGizmo.X), role, Accessor().Object));
    }

    [Theory]
    [InlineData(true, 0.49, -0.49, 0.49, 0, 0, 0)]
    [InlineData(false, 0.5, -0.5, 1.5, 1, -1, 2)]
    [InlineData(true, -0.5, 0.5, -1.5, -1, 1, -2)]
    [InlineData(false, -1.49, 1.49, -0.49, -1, 1, 0)]
    public void CenterPolicy_RoundsEveryWorldDeltaAwayFromZeroBeforeDecoding(
        bool start, double dx, double dy, double dz, int x, int y, int z)
    {
        var role = start ? RulerEndpointRole.Start : RulerEndpointRole.End;
        var initial = RulerGizmoCoordinates.ToAnchor(new(-2000000000, 65, 2000000000, 65535), role);
        var candidate = new GizmoPosition(initial.Coordinates + V(dx, dy, dz), 65535);
        var accessor = Accessor();
        var accepted = RulerGizmoCoordinates.Constrain(new(initial, candidate, PositionGizmo.Center), role, accessor.Object);
        Assert.NotNull(accepted);
        Assert.Equal(initial.Coordinates + V(x, y, z), accepted.Value.Coordinates);
        Assert.True(RulerGizmoCoordinates.TryDecode(accepted.Value, role, accessor.Object, out var cell));
        Assert.Equal(new RulerEndpoint(-2000000000 + x, 65 + y, 2000000000 + z, 65535), cell);
        Assert.False(RulerGizmoCoordinates.TryDecode(candidate, role, accessor.Object, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CenterPolicy_ObliquePlaneSnapsWorldCoordinatesWithoutRebasingOnEcho(bool start)
    {
        var role = start ? RulerEndpointRole.Start : RulerEndpointRole.End;
        var initial = RulerGizmoCoordinates.ToAnchor(new(-10, 65, -20, 2), role);
        double yaw = Math.PI / 4;
        var context = new GizmoContext(2, Camera(reference: initial.Coordinates + V(-10, 0, 10),
            view: Yaw(yaw)).Camera);
        using var gizmo = new PositionGizmo(initial, _ => { },
            edit => RulerGizmoCoordinates.Constrain(edit, role, Accessor().Object));
        var origin = initial.Coordinates - context.Camera!.ViewDirection * 20;
        var right = V(Math.Cos(yaw), 0, Math.Sin(yaw));
        var pointer = new GizmoPointer(context, new(origin, context.Camera.ViewDirection));
        Assert.True(gizmo.BeginInteraction(PositionGizmo.Center, pointer));
        int changes = 0;
        gizmo.OnValueChanged += value => { changes++; gizmo.Value = value; };
        foreach (double amount in new[] { 0.49, 0.51, 0.52, 1.49, 1.51, 0.49, -0.51, 0 })
        {
            var delta = right * (amount / right[0]) + V(0, -amount, 0);
            gizmo.UpdateInteraction(new(context, new(origin + delta, context.Camera.ViewDirection)));
            var expected = V(Math.Round(amount, MidpointRounding.AwayFromZero),
                Math.Round(-amount, MidpointRounding.AwayFromZero),
                Math.Round(delta[2], MidpointRounding.AwayFromZero));
            AssertCoordinates(initial.Coordinates + expected, gizmo.Value.Coordinates);
            Assert.True(gizmo.IsInteracting);
        }
        Assert.Equal(initial, gizmo.Value);
        Assert.InRange(changes, 4, 8);
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
