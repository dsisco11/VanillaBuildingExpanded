using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class ComposedPositionGizmoTests
{
    internal static ComposedPositionGizmo Create(GizmoPosition initialValue, Action<GizmoHandleDraw> draw,
        Func<PositionGizmoEdit, GizmoPosition?>? constrain = null, GizmoShape marker = GizmoShape.Circle) =>
        new(initialValue, draw, g => new GizmoEditStateTarget(g, () => true, () => { }, () => { }), constrain, marker);

    internal static Vector256<double> V(double x, double y, double z) => Vector256.Create(x, y, z, 0d);
    internal static GizmoContext Camera(int width = 800, int height = 600, double scale = 1,
        Vector256<double> reference = default, bool grabbed = false, double far = 1000, double[]? view = null) =>
        new(0, new(view ?? Mat4d.Create(), Mat4d.Perspective(new double[16], Math.PI / 2,
            (double)width / height, 0.1, far), reference, width, height, scale, grabbed));
    internal static void AssertCoordinates(Vector256<double> expected, Vector256<double> actual, int precision = 8)
    {
        for (int i = 0; i < 3; i++) Assert.Equal(expected[i], actual[i], precision);
        Assert.Equal(0d, actual[3]);
    }

    internal static double[] Yaw(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return [c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1];
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Axis_ChangesOnlySelectedComponentAndPreservesGrabOffset(int component)
    {
        var start = V(-2000000000.25, 30.75, -2000000000.5);
        var rayDirection = component == 2 ? V(0, 1, 0) : V(0, 0, -1);
        var context = new GizmoContext(0);
        using var gizmo = Create(new(start, 0), _ => { });
        var initialHit = start.WithElement(component, start[component] + 0.75);
        var origin = initialHit - rayDirection * 10;
        Assert.True(gizmo.BeginInteraction(new(component + 1), new(context, new(origin, rayDirection))));
        gizmo.UpdateInteraction(new(context, new(origin, rayDirection)));
        Assert.Equal(start, gizmo.Value.Coordinates);
        var moved = origin.WithElement(component, origin[component] - 2.5);
        gizmo.UpdateInteraction(new(context, new(moved, rayDirection)));
        Assert.Equal(start.WithElement(component, start[component] - 2.5), gizmo.Value.Coordinates);
        gizmo.CancelInteraction();
        Assert.Equal(start, gizmo.Value.Coordinates);
    }

    [Theory]
    [InlineData(0.49, 0)]
    [InlineData(-0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, -1)]
    public void SuppliedPolicy_SnapsDeltaBeforePublishingWithoutChangingOtherComponents(double delta, double expected)
    {
        var start = V(-10, 4.25, -5);
        using var gizmo = Create(new(start, 0), _ => { }, edit =>
        {
            double rounded = Math.Round(edit.Candidate.Coordinates[0] - edit.Start.Coordinates[0],
                MidpointRounding.AwayFromZero);
            return new(edit.Start.Coordinates.WithElement(0, edit.Start.Coordinates[0] + rounded), 0);
        });
        var context = new GizmoContext(0);
        var origin = V(-9.25, 4.25, 5);
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.X, new(context, new(origin, V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(origin + V(delta, 0, 0), V(0, 0, -1))));
        Assert.Equal(start.WithElement(0, -10 + expected), gizmo.Value.Coordinates);
    }

    [Fact]
    public void DegenerateBeginAndUpdates_ReportReasonsAndRetainLastValidValue()
    {
        using var gizmo = Create(new(V(0, 0, 0), 0), _ => { });
        List<string> notices = [];
        gizmo.NoticeRequested += notices.Add;
        var context = new GizmoContext(0);
        Assert.False(gizmo.BeginInteraction(ComposedPositionGizmo.X, new(context, new(V(-10, 0, 0), V(1, 0, 0)))));
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 10), V(0, 0, -1))));
        var last = gizmo.Value;
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 10), V(1, 0, -0.001))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 10), V(0, 0, -1))));
        gizmo.UpdateInteraction(new(context, new(V(1000, 0, 10), V(0, 0, -1))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, -10), V(0, 0, -1))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 1000001), V(0, 0, -1))));
        Assert.Equal(last, gizmo.Value);
        Assert.Contains(nameof(PositionGizmoNotice.DegenerateAxis), notices);
        Assert.Contains(nameof(PositionGizmoNotice.ExcessiveJump), notices);
    }

    [Theory]
    [InlineData(0.0099, false)]
    [InlineData(0.01, false)]
    [InlineData(0.0101, true)]
    public void AxisStart_PreNormalizationSquaredThresholdIsEnforced(double perpendicular, bool accepted)
    {
        double parallel = Math.Sqrt(1 - perpendicular * perpendicular);
        using var gizmo = Create(new(V(0, 0, 0), 0), _ => { });
        var direction = V(parallel, 0, -perpendicular);
        Assert.Equal(accepted, gizmo.BeginInteraction(ComposedPositionGizmo.X,
            new(new(0), new(-direction * 10, direction))));
    }

    [Theory]
    [InlineData(0.0099, false)]
    [InlineData(0.01, false)]
    [InlineData(0.0101, true)]
    public void AxisUpdate_DenominatorThresholdRetainsOrPublishes(double perpendicular, bool accepted)
    {
        using var gizmo = Create(new(V(0, 0, 0), 0), _ => { });
        var context = new GizmoContext(0);
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        var direction = V(Math.Sqrt(1 - perpendicular * perpendicular), 0, -perpendicular);
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 0) - direction * 10, direction)));
        Assert.Equal(accepted ? 1d : 0d, gizmo.Value.Coordinates[0], 10);
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(80.001, false)]
    public void AxisUpdate_JumpThresholdIsMeasuredFromLastAcceptedCoordinate(double jump, bool accepted)
    {
        using var gizmo = Create(new(V(0, 0, 0), 0), _ => { });
        var context = new GizmoContext(0);
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(jump, 0, 10), V(0, 0, -1))));
        Assert.Equal(accepted ? jump : 0, gizmo.Value.Coordinates[0], 10);
    }

    [Theory]
    [InlineData(100000, true)]
    [InlineData(100000.001, false)]
    public void AxisUpdate_IntersectionDistanceGuardIncludesItsBoundary(double distance, bool accepted)
    {
        using var gizmo = Create(new(V(0, 0, 0), 0), _ => { });
        var context = new GizmoContext(0);
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, distance), V(0, 0, -1))));
        Assert.Equal(accepted ? 1d : 0d, gizmo.Value.Coordinates[0], 10);
    }

    [Fact]
    public void Center_UsesCapturedViewPlaneAndGrabOffsetWithContinuousCoordinates()
    {
        var context = Camera();
        var start = V(1.25, 2.5, -20);
        using var gizmo = Create(new(start, 0), _ => { });
        var pointer = new GizmoPointer(context, new(V(0, 0, 0), V(2, 3, -20)));
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center, pointer));
        int notifications = 0;
        gizmo.OnValueChanged += _ => notifications++;
        for (int i = 0; i < 120; i++) gizmo.UpdateInteraction(pointer);
        Assert.Equal(start, gizmo.Value.Coordinates);
        Assert.Equal(0, notifications);
        gizmo.UpdateInteraction(new(context, new(V(0, 0, 0), V(4.125, 1.75, -20))));
        AssertCoordinates(start + V(2.125, -1.25, 0), gizmo.Value.Coordinates);
        Assert.Equal(1, notifications);
        gizmo.CancelInteraction();
        Assert.Equal(start, gizmo.Value.Coordinates);
        Assert.Equal(2, notifications);
    }

    [Theory]
    [InlineData(800, 600, 1, 0)]
    [InlineData(801, 601, 2, 0.6)]
    [InlineData(400, 900, 0.75, -0.8)]
    [InlineData(1920, 500, 1.5, 1.1)]
    public void Center_UsesViewNormalNotOffCenterRayAcrossPerspectiveAndGuiScales(
        int width, int height, double scale, double yaw)
    {
        var context = Camera(width, height, scale, view: Yaw(yaw));
        var normal = context.Camera!.ViewDirection;
        var right = V(Math.Cos(yaw), 0, Math.Sin(yaw));
        var anchor = normal * 20 + right * 3;
        using var gizmo = Create(new(anchor, 0), _ => { });
        var initialHit = anchor + right * 0.4 + V(0, 0.3, 0);
        Assert.True(context.Camera.TryProject(initialHit, out var screen));
        var pointer = context.Camera.CreatePointer(context, screen[0], screen[1]);
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center, pointer));
        gizmo.UpdateInteraction(pointer);
        AssertCoordinates(anchor, gizmo.Value.Coordinates);
        var delta = right * 1.125 + V(0, -2.375, 0);
        Assert.True(context.Camera.TryProject(initialHit + delta, out screen));
        gizmo.UpdateInteraction(context.Camera.CreatePointer(context, screen[0], screen[1]));
        AssertCoordinates(anchor + delta, gizmo.Value.Coordinates);
        Assert.Equal(0, Vector256.Dot(gizmo.Value.Coordinates - anchor, normal), 8);
        gizmo.CompleteInteraction();
        AssertCoordinates(anchor + delta, gizmo.Value.Coordinates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Center_CameraTranslationRotationAndOwnerEchoKeepOriginalPlaneAndBaseline(bool grabbed)
    {
        var context = Camera(grabbed: grabbed);
        var anchor = V(0, 0, -20);
        using var gizmo = Create(new(anchor, 0), _ => { });
        var initial = context.Camera!.CreatePointer(context, 400, 300);
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center, initial));
        int changes = 0;
        gizmo.OnValueChanged += value => { changes++; gizmo.Value = value; };
        var movedContext = Camera(reference: V(1, 2, 3), grabbed: grabbed, view: Yaw(0.35));
        var moved = movedContext.Camera!.CreatePointer(movedContext, 480, 250);
        double t = (anchor[2] - moved.Ray.Origin[2]) / moved.Ray.Direction[2];
        var expected = moved.Ray.Origin + moved.Ray.Direction * t;
        gizmo.UpdateInteraction(moved);
        AssertCoordinates(expected, gizmo.Value.Coordinates);
        Assert.Equal(-20d, gizmo.Value.Coordinates[2], 8);
        for (int i = 0; i < 120; i++)
        {
            gizmo.Value = gizmo.Value;
            gizmo.UpdateInteraction(moved);
        }
        Assert.Equal(1, changes);
        gizmo.UpdateInteraction(initial);
        AssertCoordinates(anchor, gizmo.Value.Coordinates);
        Assert.Equal(2, changes);
        Assert.True(gizmo.IsInteracting);
        gizmo.CompleteInteraction();
    }

    [Theory]
    [InlineData(-2000000000.25, 32767.75, 2000000000.5)]
    [InlineData(2000000000.125, -20.5, -2000000000.25)]
    public void Center_DistantCoordinatesPreserveFractionalGrabOffsetAndReturnWithoutAccumulation(
        double x, double y, double z)
    {
        var anchor = V(x, y, z);
        var context = Camera(reference: anchor + V(0, 0, 20));
        using var gizmo = Create(new(anchor, 65535), _ => { });
        var origin = anchor + V(0.375, -0.25, 20);
        var initial = new GizmoPointer(new(65535, context.Camera), new(origin, V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center, initial));
        for (int i = 0; i < 30; i++)
        {
            var delta = V(1.125 + i * 0.125, -2.375, 0);
            gizmo.UpdateInteraction(new(initial.Context, new(origin + delta, V(0, 0, -1))));
            AssertCoordinates(anchor + delta, gizmo.Value.Coordinates, 6);
            gizmo.UpdateInteraction(initial);
            Assert.Equal(anchor, gizmo.Value.Coordinates);
            Assert.Equal(65535, gizmo.Value.Dimension);
        }
    }

    [Theory]
    [InlineData(0.0099, false)]
    [InlineData(0.01, false)]
    [InlineData(0.0101, true)]
    public void CenterStartAndUpdate_EnforceNearParallelThresholdAndRecover(double normalComponent, bool accepted)
    {
        var context = Camera();
        var direction = V(Math.Sqrt(1 - normalComponent * normalComponent), 0, -normalComponent);
        using var gizmo = Create(new(V(0, 0, -10), 0), _ => { });
        var grazing = new GizmoPointer(context, new(V(0, 0, -10) - direction * 10, direction));
        Assert.Equal(accepted, gizmo.BeginInteraction(ComposedPositionGizmo.Center, grazing));
        if (accepted) gizmo.CancelInteraction();
        var initial = new GizmoPointer(context, new(V(0, 0, 0), V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center, initial));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, -10) - direction * 10, direction)));
        Assert.Equal(accepted ? 1d : 0d, gizmo.Value.Coordinates[0], 8);
        gizmo.UpdateInteraction(new(context, new(V(2, 0, 0), V(0, 0, -1))));
        AssertCoordinates(V(2, 0, -10), gizmo.Value.Coordinates);
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(80.001, false)]
    public void Center_JumpGuardUsesLastAcceptedIntersectionAndIncludesBoundary(double jump, bool accepted)
    {
        var context = Camera();
        using var gizmo = Create(new(V(0, 0, -10), 0), _ => { });
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center,
            new(context, new(V(0, 0, 0), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(jump, 0, 0), V(0, 0, -1))));
        Assert.Equal(accepted ? jump : 0, gizmo.Value.Coordinates[0], 8);
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 0), V(0, 0, -1))));
        AssertCoordinates(V(1, 0, -10), gizmo.Value.Coordinates);
    }

    [Theory]
    [InlineData(100000, true)]
    [InlineData(100000.001, false)]
    public void Center_IntersectionDistanceGuardIncludesBoundary(double distance, bool accepted)
    {
        var context = Camera();
        using var gizmo = Create(new(V(0, 0, -10), 0), _ => { });
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center,
            new(context, new(V(0, 0, 0), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, -10 + distance), V(0, 0, -1))));
        Assert.Equal(accepted ? 1d : 0d, gizmo.Value.Coordinates[0], 8);
    }

    [Fact]
    public void Center_InvalidStartsAndContinuingFailuresBoundNoticesThenRecoverEvenOnUnchangedPublish()
    {
        var context = Camera();
        using var gizmo = Create(new(V(0, 0, -10), 0), _ => { });
        List<string> notices = [];
        gizmo.NoticeRequested += notices.Add;
        Assert.False(gizmo.BeginInteraction(ComposedPositionGizmo.Center,
            new(new(0), new(V(0, 0, 0), V(0, 0, -1)))));
        Assert.False(gizmo.BeginInteraction(ComposedPositionGizmo.Center,
            new(context, new(V(0, 0, -20), V(0, 0, -1)))));
        Assert.False(gizmo.BeginInteraction(ComposedPositionGizmo.Center,
            new(context, new(V(0, 0, 0), V(1, 0, 0)))));
        Assert.Equal(3, notices.Count);
        using (var distant = Create(new(V(double.MaxValue, 0, -10), 0), _ => { }))
        {
            distant.NoticeRequested += notices.Add;
            Assert.False(distant.BeginInteraction(ComposedPositionGizmo.Center,
                new(context, new(V(0, 0, 0), V(0, 0, -1)))));
            Assert.False(distant.IsInteracting);
            Assert.Equal(4, notices.Count);
        }
        var initial = new GizmoPointer(context, new(V(0, 0, 0), V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center, initial));
        notices.Clear();
        int changes = 0;
        gizmo.OnValueChanged += _ => changes++;
        var parallel = new GizmoPointer(context, new(V(0, 0, 0), V(1, 0, 0)));
        for (int i = 0; i < 120; i++) gizmo.UpdateInteraction(parallel);
        Assert.Equal(new[] { nameof(PositionGizmoNotice.DegeneratePlane) }, notices);
        gizmo.UpdateInteraction(initial);
        Assert.Equal(0, changes);
        for (int i = 0; i < 120; i++) gizmo.UpdateInteraction(parallel);
        Assert.Equal(2, notices.Count);
        gizmo.UpdateInteraction(initial);
        var extreme = new GizmoPointer(context, new(V(double.MaxValue, double.MaxValue, 0), V(0, 0, -1)));
        for (int i = 0; i < 120; i++) gizmo.UpdateInteraction(extreme);
        Assert.Equal(nameof(PositionGizmoNotice.ExcessiveJump), notices[^1]);
        Assert.Equal(3, notices.Count);
        gizmo.UpdateInteraction(new(context, new(V(0, 0, -20), V(0, 0, -1))));
        gizmo.UpdateInteraction(new(context, new(V(0, 0, double.MaxValue), V(0, 0, -1))));
        AssertCoordinates(V(0, 0, -10), gizmo.Value.Coordinates);
        Assert.True(gizmo.IsInteracting);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoRay(V(double.NaN, 0, 0), V(0, 0, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoRay(default, V(0, double.PositiveInfinity, -1)));
        gizmo.UpdateInteraction(new(context, new(V(2.25, -1.5, 0), V(0, 0, -1))));
        AssertCoordinates(V(2.25, -1.5, -10), gizmo.Value.Coordinates);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Center_RejectedPolicyAndWrongDimensionNeverAdvanceBaselineAndCanCancelFromCallback()
    {
        var context = Camera();
        var anchor = V(0, 0, -10);
        int mode = 0;
        ComposedPositionGizmo? instance = null;
        using var gizmo = Create(new(anchor, 0), _ => { }, edit =>
        {
            if (mode == 3) instance!.CancelInteraction();
            return mode switch { 1 => null, 2 => new(edit.Candidate.Coordinates, 1), _ => edit.Candidate };
        });
        instance = gizmo;
        List<string> notices = [];
        gizmo.NoticeRequested += notices.Add;
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.Center,
            new(context, new(V(0.5, 0.25, 0), V(0, 0, -1)))));
        mode = 1;
        var moved = new GizmoPointer(context, new(V(2.5, 1.25, 0), V(0, 0, -1)));
        for (int i = 0; i < 120; i++) gizmo.UpdateInteraction(moved);
        mode = 2;
        gizmo.UpdateInteraction(moved);
        Assert.Equal(new[] { nameof(PositionGizmoNotice.InvalidValue) }, notices);
        Assert.Equal(anchor, gizmo.Value.Coordinates);
        mode = 0;
        gizmo.UpdateInteraction(moved);
        AssertCoordinates(anchor + V(2, 1, 0), gizmo.Value.Coordinates);
        mode = 1;
        gizmo.UpdateInteraction(moved);
        Assert.Equal(2, notices.Count);
        mode = 3;
        gizmo.UpdateInteraction(moved);
        Assert.False(gizmo.IsInteracting);
        Assert.Equal(anchor, gizmo.Value.Coordinates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlternatingInvalidReasons_EmitOneNoticeUntilSuccessfulUnchangedPublishOrFreshBegin(bool center)
    {
        var context = Camera();
        bool reject = false;
        using var gizmo = Create(new(V(0, 0, -10), 0), _ => { },
            edit => reject ? null : edit.Candidate);
        var handle = center ? ComposedPositionGizmo.Center : ComposedPositionGizmo.X;
        var valid = new GizmoPointer(context, new(V(0, 0, 0), V(0, 0, -1)));
        var degenerate = new GizmoPointer(context, new(V(0, 0, 0), V(1, 0, 0)));
        var jump = new GizmoPointer(context, new(V(1000, 0, 0), V(0, 0, -1)));
        List<string> notices = [];
        gizmo.NoticeRequested += notices.Add;
        Assert.True(gizmo.BeginInteraction(handle, valid));
        for (int i = 0; i < 120; i++)
        {
            gizmo.UpdateInteraction(degenerate);
            gizmo.UpdateInteraction(jump);
            reject = true;
            gizmo.UpdateInteraction(valid);
        }
        Assert.Equal(new[] { center ? nameof(PositionGizmoNotice.DegeneratePlane) : nameof(PositionGizmoNotice.DegenerateAxis) },
            notices);
        Assert.Equal(V(0, 0, -10), gizmo.Value.Coordinates);
        reject = false;
        gizmo.UpdateInteraction(valid);
        gizmo.UpdateInteraction(jump);
        Assert.Equal(2, notices.Count);
        Assert.Equal(nameof(PositionGizmoNotice.ExcessiveJump), notices[1]);
        gizmo.CancelInteraction();
        Assert.True(gizmo.BeginInteraction(handle, valid));
        reject = true;
        gizmo.UpdateInteraction(valid);
        Assert.Equal(3, notices.Count);
        Assert.Equal(nameof(PositionGizmoNotice.InvalidValue), notices[2]);
    }

    [Fact]
    public void PolicyCannotModifyUnselectedComponentsAndCallbackCancellationCannotPublish()
    {
        ComposedPositionGizmo? instance = null;
        bool cancel = false;
        using var gizmo = Create(new(V(0, 0, 0), 0), _ => { }, edit =>
        {
            if (cancel) instance!.CancelInteraction();
            return new(edit.Candidate.Coordinates.WithElement(1, 5), 0);
        });
        instance = gizmo;
        var pointer = new GizmoPointer(new(0), new(V(0, 0, 10), V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(ComposedPositionGizmo.X, pointer));
        gizmo.UpdateInteraction(pointer);
        Assert.Equal(V(0, 0, 0), gizmo.Value.Coordinates);
        cancel = true;
        gizmo.UpdateInteraction(pointer);
        Assert.False(gizmo.IsInteracting);
        Assert.Equal(V(0, 0, 0), gizmo.Value.Coordinates);
    }
}
