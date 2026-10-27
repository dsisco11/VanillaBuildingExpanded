using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class PositionGizmoTests
{
    internal static Vector256<double> V(double x, double y, double z) => Vector256.Create(x, y, z, 0d);
    internal static GizmoContext Camera(int width = 800, int height = 600, double scale = 1,
        Vector256<double> reference = default, bool grabbed = false, double far = 1000, double[]? view = null) =>
        new(0, new(view ?? Mat4d.Create(), Mat4d.Perspective(new double[16], Math.PI / 2,
            (double)width / height, 0.1, far), reference, width, height, scale, grabbed));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Axis_ChangesOnlySelectedComponentAndPreservesGrabOffset(int component)
    {
        var start = V(-2000000000.25, 30.75, -2000000000.5);
        var rayDirection = component == 2 ? V(0, 1, 0) : V(0, 0, -1);
        var context = new GizmoContext(0);
        using var gizmo = new PositionGizmo(new(start, 0), _ => { }, _ => null);
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
        using var gizmo = new PositionGizmo(new(start, 0), _ => { }, _ => null, edit =>
        {
            double rounded = Math.Round(edit.Candidate.Coordinates[0] - edit.Start.Coordinates[0],
                MidpointRounding.AwayFromZero);
            return new(edit.Start.Coordinates.WithElement(0, edit.Start.Coordinates[0] + rounded), 0);
        });
        var context = new GizmoContext(0);
        var origin = V(-9.25, 4.25, 5);
        Assert.True(gizmo.BeginInteraction(PositionGizmo.X, new(context, new(origin, V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(origin + V(delta, 0, 0), V(0, 0, -1))));
        Assert.Equal(start.WithElement(0, -10 + expected), gizmo.Value.Coordinates);
    }

    [Fact]
    public void DegenerateBeginAndUpdates_ReportReasonsAndRetainLastValidValue()
    {
        using var gizmo = new PositionGizmo(new(V(0, 0, 0), 0), _ => { }, _ => null);
        List<PositionGizmoNotice> notices = [];
        gizmo.NoticeRequested += notices.Add;
        var context = new GizmoContext(0);
        Assert.False(gizmo.BeginInteraction(PositionGizmo.X, new(context, new(V(-10, 0, 0), V(1, 0, 0)))));
        Assert.True(gizmo.BeginInteraction(PositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 10), V(0, 0, -1))));
        var last = gizmo.Value;
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 10), V(1, 0, -0.001))));
        gizmo.UpdateInteraction(new(context, new(V(1000, 0, 10), V(0, 0, -1))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, -10), V(0, 0, -1))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 1000001), V(0, 0, -1))));
        Assert.Equal(last, gizmo.Value);
        Assert.Contains(PositionGizmoNotice.DegenerateAxis, notices);
        Assert.Contains(PositionGizmoNotice.ExcessiveJump, notices);
    }

    [Theory]
    [InlineData(0.0099, false)]
    [InlineData(0.01, false)]
    [InlineData(0.0101, true)]
    public void AxisStart_PreNormalizationSquaredThresholdIsEnforced(double perpendicular, bool accepted)
    {
        double parallel = Math.Sqrt(1 - perpendicular * perpendicular);
        using var gizmo = new PositionGizmo(new(V(0, 0, 0), 0), _ => { }, _ => null);
        var direction = V(parallel, 0, -perpendicular);
        Assert.Equal(accepted, gizmo.BeginInteraction(PositionGizmo.X,
            new(new(0), new(-direction * 10, direction))));
    }

    [Theory]
    [InlineData(0.0099, false)]
    [InlineData(0.01, false)]
    [InlineData(0.0101, true)]
    public void AxisUpdate_DenominatorThresholdRetainsOrPublishes(double perpendicular, bool accepted)
    {
        using var gizmo = new PositionGizmo(new(V(0, 0, 0), 0), _ => { }, _ => null);
        var context = new GizmoContext(0);
        Assert.True(gizmo.BeginInteraction(PositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        var direction = V(Math.Sqrt(1 - perpendicular * perpendicular), 0, -perpendicular);
        gizmo.UpdateInteraction(new(context, new(V(1, 0, 0) - direction * 10, direction)));
        Assert.Equal(accepted ? 1d : 0d, gizmo.Value.Coordinates[0], 10);
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(80.001, false)]
    public void AxisUpdate_JumpThresholdIsMeasuredFromLastAcceptedCoordinate(double jump, bool accepted)
    {
        using var gizmo = new PositionGizmo(new(V(0, 0, 0), 0), _ => { }, _ => null);
        var context = new GizmoContext(0);
        Assert.True(gizmo.BeginInteraction(PositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(jump, 0, 10), V(0, 0, -1))));
        Assert.Equal(accepted ? jump : 0, gizmo.Value.Coordinates[0], 10);
    }

    [Theory]
    [InlineData(100000, true)]
    [InlineData(100000.001, false)]
    public void AxisUpdate_IntersectionDistanceGuardIncludesItsBoundary(double distance, bool accepted)
    {
        using var gizmo = new PositionGizmo(new(V(0, 0, 0), 0), _ => { }, _ => null);
        var context = new GizmoContext(0);
        Assert.True(gizmo.BeginInteraction(PositionGizmo.X, new(context, new(V(0, 0, 10), V(0, 0, -1)))));
        gizmo.UpdateInteraction(new(context, new(V(1, 0, distance), V(0, 0, -1))));
        Assert.Equal(accepted ? 1d : 0d, gizmo.Value.Coordinates[0], 10);
    }

    [Fact]
    public void Center_UsesSuppliedTargetAndPolicyWithoutPlaneFallbackOrDimensionChanges()
    {
        GizmoPosition? target = null;
        bool reject = false;
        using var gizmo = new PositionGizmo(new(V(1, 2, 3), 0), _ => { }, _ => target,
            edit => reject ? null : edit.Candidate);
        var pointer = new GizmoPointer(new(0), new(V(0, 0, 0), V(0, 0, -1)));
        List<PositionGizmoNotice> notices = [];
        gizmo.NoticeRequested += notices.Add;
        Assert.True(gizmo.BeginInteraction(PositionGizmo.Center, pointer));
        gizmo.UpdateInteraction(pointer);
        Assert.Equal(V(1, 2, 3), gizmo.Value.Coordinates);
        target = new(V(-7.25, 900, -20.5), 0);
        gizmo.UpdateInteraction(pointer);
        Assert.Equal(target, gizmo.Value);
        target = new(V(50, 0, -5), 1);
        gizmo.UpdateInteraction(pointer);
        Assert.Equal(V(-7.25, 900, -20.5), gizmo.Value.Coordinates);
        reject = true;
        target = new(V(1, 2, 3), 0);
        gizmo.UpdateInteraction(pointer);
        Assert.Equal(V(-7.25, 900, -20.5), gizmo.Value.Coordinates);
        Assert.Contains(PositionGizmoNotice.MissingTarget, notices);
        Assert.Contains(PositionGizmoNotice.InvalidValue, notices);
        gizmo.CancelInteraction();
        Assert.Equal(V(1, 2, 3), gizmo.Value.Coordinates);
    }

    [Fact]
    public void PolicyCannotModifyUnselectedComponentsAndCallbackCancellationCannotPublish()
    {
        PositionGizmo? instance = null;
        bool cancel = false;
        using var gizmo = new PositionGizmo(new(V(0, 0, 0), 0), _ => { }, _ => null, edit =>
        {
            if (cancel) instance!.CancelInteraction();
            return new(edit.Candidate.Coordinates.WithElement(1, 5), 0);
        });
        instance = gizmo;
        var pointer = new GizmoPointer(new(0), new(V(0, 0, 10), V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(PositionGizmo.X, pointer));
        gizmo.UpdateInteraction(pointer);
        Assert.Equal(V(0, 0, 0), gizmo.Value.Coordinates);
        cancel = true;
        gizmo.UpdateInteraction(pointer);
        Assert.False(gizmo.IsInteracting);
        Assert.Equal(V(0, 0, 0), gizmo.Value.Coordinates);
    }
}
