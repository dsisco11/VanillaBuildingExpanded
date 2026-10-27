using System.Runtime.Intrinsics;

using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.PositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

[Collection("Table composition")]
public sealed class WorkAreaGizmoSessionTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void WholeBoxGizmo_RepeatedActualDragsTranslateBothCornersWithoutResizingOrTargeting(bool grabbed, int handle)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        h.WorkArea.RestoreEndpoints(new(-20, 60, -220, 0), new(20, 80, -180, 0));
        double c = Math.Cos(0.45), s = Math.Sin(0.45);
        h.View = Mat4d.Multiply(new double[16], Yaw(0.65),
            [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1]);
        h.ReticleDistance = 30;
        h.Enable();
        for (int repeat = 0; repeat < 3; repeat++)
        {
            var before = h.WorkArea.State;
            var bounds = before.Bounds!.Value;
            var center = WorkAreaGizmoCoordinates.ToCenter(bounds);
            h.Reference = center.Coordinates - h.Capture().Camera!.ViewDirection * h.ReticleDistance;
            h.Frame();
            Assert.Equal(3, h.Session.Manager.Count);
            Assert.Equal(center, h.Session.Center!.Value);
            var geometry = PositionGizmoGeometry.Create(h.Session.Renderer.Context!.Camera!, center.Coordinates,
                GizmoShape.Circle)!;
            var axis = handle == 0 ? V(1, 0, 0) : V(0, 0, 0).WithElement(handle - 1, 1);
            var grab = center.Coordinates + (handle == 0 ? V(0, 0, 0) : axis * (geometry.AxisLength * 0.7));
            h.Aim(grab);
            Assert.True(h.Down().Handled);
            Assert.Same(h.Session.Center, h.Session.Manager.CapturedGizmo);
            Assert.Equal(new GizmoHandleId(handle), h.Session.Manager.CapturedHandle);
            for (int frame = 0; frame < 120; frame++) h.Frame();
            Assert.Equal(before, h.WorkArea.State);
            foreach (int amount in new[] { 1, 3, -2, 0, 2 })
            {
                var delta = handle == 0 ? h.Capture().Camera!.Billboard(center.Coordinates).Right * amount
                    : axis * amount;
                h.Aim(grab + delta);
                h.Move(h.X, h.Y);
                h.Frame();
                var first = h.WorkArea.State.Start!.Value;
                var last = h.WorkArea.State.End!.Value;
                Assert.Equal((int)Math.Round(delta[0], MidpointRounding.AwayFromZero),
                    first.X - before.Start!.Value.X);
                Assert.Equal((int)Math.Round(delta[1], MidpointRounding.AwayFromZero),
                    first.Y - before.Start.Value.Y);
                Assert.Equal((int)Math.Round(delta[2], MidpointRounding.AwayFromZero),
                    first.Z - before.Start.Value.Z);
                Assert.Equal(first.X - before.Start!.Value.X, last.X - before.End!.Value.X);
                Assert.Equal(first.Y - before.Start.Value.Y, last.Y - before.End.Value.Y);
                Assert.Equal(first.Z - before.Start.Value.Z, last.Z - before.End.Value.Z);
                Assert.Equal((bounds.SizeX, bounds.SizeY, bounds.SizeZ), h.Size);
                Assert.Equal(WorkAreaGizmoCoordinates.ToCenter(h.WorkArea.State.Bounds!.Value), h.Session.Center.Value);
                Assert.Same(h.Session.Center, h.Session.Manager.CapturedGizmo);
                Assert.Equal(repeat, h.Commits);
                h.AssertLiveBox();
                h.AssertOwnedActions();
            }
            Assert.NotEqual(before.Start, h.WorkArea.State.Start);
            Assert.True(h.Up().Handled);
            Assert.Equal(repeat + 1, h.Commits);
            h.AssertNoTargeting();
        }
    }

    [Theory]
    [InlineData(false, "escape")]
    [InlineData(true, "right")]
    [InlineData(false, "hidden")]
    [InlineData(true, "dimension")]
    [InlineData(false, "focus")]
    [InlineData(true, "departure")]
    public void WholeBoxGizmo_CancelAndContextLossRestoreBothCorners(bool grabbed, string reason)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        h.WorkArea.RestoreEndpoints(new(-30, 65, -200, 0), new(30, 75, -200, 0));
        h.Enable();
        h.Frame();
        var before = h.WorkArea.State;
        var center = h.Session.Center!;
        h.Aim(center.Value.Coordinates);
        Assert.True(h.Down().Handled);
        Assert.Same(center, h.Session.Manager.CapturedGizmo);
        h.Aim(center.Value.Coordinates + V(4, 2, 0));
        h.Move(h.X, h.Y);
        h.Frame();
        Assert.NotEqual(before.Start, h.WorkArea.State.Start);
        switch (reason)
        {
            case "escape": h.Key(); break;
            case "right": h.Down(button: EnumMouseButton.Right); break;
            case "hidden": h.WorkArea.SetVisibility(false); break;
            case "dimension": h.Player.Entity.Pos.Dimension++; h.Session.Refresh(); break;
            case "focus": h.Focus.ForEach(callback => callback(false)); break;
            case "departure": h.Events.Raise(e => e.LeaveWorld += null!); break;
        }
        Assert.Equal(before.Start, h.WorkArea.State.Start);
        Assert.Equal(before.End, h.WorkArea.State.End);
        Assert.False(h.WorkArea.IsEditing);
        Assert.Null(h.Session.Manager.CapturedGizmo);
        Assert.Equal(0, h.Commits);
        h.Up();
    }

    [Fact]
    public void WholeBoxGizmo_ExistsOnlyForCompleteSelectionAndTracksEndpointEditsAndVisibility()
    {
        using var f = new Fixture();
        var h = f.H;
        h.Enable();
        h.Frame();
        Assert.Null(h.Session.Center);
        var first = new WorkAreaEndpoint(-30, 65, -200, 0);
        h.WorkArea.RestoreEndpoints(first, null);
        h.Frame();
        Assert.Null(h.Session.Center);
        h.WorkArea.RestoreEndpoints(first, new(30, 75, -180, 0));
        h.Frame();
        var center = h.Session.Center!;
        Assert.Equal(WorkAreaGizmoCoordinates.ToCenter(h.WorkArea.State.Bounds!.Value), center.Value);
        h.WorkArea.TrySetEndpoint(WorkAreaEndpointRole.End, new(50, 80, -160, 0), h.Accessor.Object);
        Assert.Same(center, h.Session.Center);
        Assert.Equal(WorkAreaGizmoCoordinates.ToCenter(h.WorkArea.State.Bounds!.Value), center.Value);
        h.WorkArea.SetVisibility(false);
        Assert.False(center.Visible);
        h.WorkArea.SetVisibility(true);
        Assert.True(center.Visible);
        h.WorkArea.RestoreEndpoints(first, null);
        Assert.Null(h.Session.Center);
        Assert.True(center.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeBoxGizmo_InvalidCornerRetainsEntireBoxAndRecoversAtLargeCoordinates(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        var first = new WorkAreaEndpoint(int.MaxValue - 2, 0, -200, 65535);
        var last = new WorkAreaEndpoint(int.MaxValue, 32767, -200, 65535);
        h.Player.Entity.Pos.Dimension = 65535;
        h.WorkArea.RestoreEndpoints(first, last);
        var center = WorkAreaGizmoCoordinates.ToCenter(h.WorkArea.State.Bounds!.Value);
        h.Reference = center.Coordinates + V(0, 0, 200);
        h.Enable();
        h.Frame();
        h.Aim(center.Coordinates);
        h.Down();
        Assert.Same(h.Session.Center, h.Session.Manager.CapturedGizmo);
        foreach (var invalid in new[] { V(1, 0, 0), V(0, 1, 0), V(0, -1, 0) })
        {
            h.Aim(center.Coordinates + invalid);
            h.Move(h.X, h.Y);
            for (int frame = 0; frame < 120; frame++) h.Frame();
            Assert.Equal(first, h.WorkArea.State.Start);
            Assert.Equal(last, h.WorkArea.State.End);
            Assert.Same(h.Session.Center, h.Session.Manager.CapturedGizmo);
            h.AssertLiveBox();
        }
        h.Api.Verify(api => api.TriggerIngameError(It.IsAny<object>(),
            "vanillabuildingexpanded:workarea-invalid-endpoint", It.IsAny<string>()), Times.Once);
        h.Aim(center.Coordinates + V(-2, 0, 0));
        h.Move(h.X, h.Y);
        h.Frame();
        Assert.Equal(first with { X = first.X - 2 }, h.WorkArea.State.Start);
        Assert.Equal(last with { X = last.X - 2 }, h.WorkArea.State.End);
        h.Up();
        Assert.Equal(1, h.Commits);
        h.AssertNoTargeting();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedBothEndpointDrags_PreviewCrossingAlwaysNormalizesBothCorners(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        f.H.Enable();
        f.H.Frame();
        f.H.Target = new(0, 65, -200, 0);
        Assert.True(f.H.Down(100, 100).Handled);
        Assert.Null(f.H.Session.Manager.CapturedGizmo);
        f.H.Up(100, 100);
        Assert.Equal(1, f.H.Commits);
        Assert.Equal(f.H.WorkArea.State.Start, f.H.WorkArea.State.End);
        Assert.Equal(3, f.H.Session.Manager.Count);
        Assert.Equal((1L, 1L, 1L), f.H.Size);
        f.H.WorkArea.RestoreEndpoints(new(-30, 65, -200, 0), new(30, 65, -200, 0));
        for (int i = 0; i < 12; i++)
        {
            bool start = i % 2 == 0;
            f.H.Frame();
            var instance = start ? f.H.Session.Start! : f.H.Session.End!;
            var baseline = instance.Value;
            var opposite = (start ? f.H.WorkArea.State.End : f.H.WorkArea.State.Start)!.Value;
            f.H.Aim(instance.Value.Coordinates);
            Assert.True(f.H.Down().Handled);
            Assert.Same(instance, f.H.Session.Manager.CapturedGizmo);
            var target = new WorkAreaEndpoint(start ? 80 + i : -80 - i, 65 + i % 3, -200, 0);
            f.H.MoveCenterTo(target);
            f.H.Frame();
            var expected = new WorkAreaBounds(target, opposite);
            Assert.Equal(expected.MinCell, f.H.WorkArea.State.Start);
            Assert.Equal(expected.MaxCell, f.H.WorkArea.State.End);
            Assert.Same(instance, f.H.Session.Manager.CapturedGizmo);
            f.H.AssertLiveBox();
            Assert.Equal(i + 1, f.H.Commits);
            Assert.NotEqual(baseline, instance.Value);
            Assert.True(f.H.Up().Handled);
            Assert.False(f.H.WorkArea.IsEditing);
            Assert.Equal(i + 2, f.H.Commits);
            f.H.AssertLiveBox();
            Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(start ? expected.MinCell : expected.MaxCell,
                start ? WorkAreaEndpointRole.Start : WorkAreaEndpointRole.End), instance.Value);
            Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(expected.MinCell, WorkAreaEndpointRole.Start),
                f.H.Session.Start!.Value);
            Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(expected.MaxCell, WorkAreaEndpointRole.End),
                f.H.Session.End!.Value);
        }
        Assert.Empty(f.H.Network.Invocations);
    }

    [Fact]
    public void CoincidentProjectedCenters_ChooseNearestDeterministicallyAndKeepDistinctArtwork()
    {
        using var f = new Fixture();
        var h = f.H;
        var cell = new WorkAreaEndpoint(0, 65, -200, 0);
        h.WorkArea.RestoreEndpoints(cell, cell);
        h.Enable();
        for (int i = 0; i < 3; i++)
        {
            h.Frame();
            h.Down();
            Assert.Same(h.Session.End, h.Session.Manager.CapturedGizmo);
            h.Down(button: EnumMouseButton.Right);
            h.Up(button: EnumMouseButton.Right);
            h.Up();
        }
        Assert.NotEqual(h.Session.Start!.Value, h.Session.End!.Value);
        Assert.Equal(3, h.Meshes.Count);
        Assert.Equal(0, h.Commits);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CrossingAndReturning_KeepsCaptureAndBothCanonicalAnchorsThenRollsBack(bool grabbed, bool start)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        var first = new WorkAreaEndpoint(-30, 65, -200, 0);
        var last = new WorkAreaEndpoint(30, 75, -200, 0);
        h.WorkArea.RestoreEndpoints(first, last);
        h.Enable();
        h.Frame();
        var instance = start ? h.Session.Start! : h.Session.End!;
        h.Aim(instance.Value.Coordinates);
        h.Down();
        foreach (var target in new[] { new WorkAreaEndpoint(start ? 70 : -70, start ? 80 : 60, -200, 0),
            start ? first : last, new(-10, 70, -200, 0) })
        {
            h.MoveCenterTo(target);
            h.Frame();
            h.Session.Refresh();
            Assert.Same(instance, h.Session.Manager.CapturedGizmo);
            var expected = new WorkAreaBounds(target, start ? last : first);
            Assert.Equal(expected.MinCell, h.WorkArea.State.Start);
            Assert.Equal(expected.MaxCell, h.WorkArea.State.End);
            Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(expected.MinCell, WorkAreaEndpointRole.Start), h.Session.Start!.Value);
            Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(expected.MaxCell, WorkAreaEndpointRole.End), h.Session.End!.Value);
            h.AssertLiveBox();
            Assert.Equal(0, h.Commits);
        }
        h.Key();
        h.Up();
        Assert.Equal(first, h.WorkArea.State.Start);
        Assert.Equal(last, h.WorkArea.State.End);
        Assert.Equal(0, h.Commits);
        h.AssertLiveBox();
    }

    [Fact]
    public void AxisDrag_UsesActualAdapterAndSnapsGrabOffsetWithNoTerrainQuery()
    {
        using var f = new Fixture();
        var h = f.H;
        h.WorkArea.RestoreEndpoints(new(0, 65, -20, 0), new(10, 65, -20, 0));
        h.Enable();
        h.Frame();
        var anchor = h.Session.Start!.Value.Coordinates;
        var geometry = PositionGizmoGeometry.Create(h.Session.Renderer.Context!.Camera!, anchor, GizmoShape.Circle)!;
        h.Aim(anchor + Vector256.Create(geometry.AxisLength * 0.6, 0, 0, 0));
        h.Down();
        Assert.Equal(PositionGizmo.X, h.Session.Manager.CapturedHandle);
        int originalX = h.X;
        h.Move(h.X + 17, h.Y);
        h.Up();
        Assert.Equal(new WorkAreaEndpoint(1, 65, -20, 0), h.WorkArea.State.Start);
        Assert.Equal(1, h.Commits);
        Assert.Null(h.LastRay);
        Assert.True(h.X > originalX);
    }

    [Theory]
    [InlineData(false, "sky")]
    [InlineData(true, "sky")]
    [InlineData(false, "entity")]
    [InlineData(true, "entity")]
    [InlineData(false, "unloaded")]
    [InlineData(true, "unloaded")]
    public void ExistingCenters_StationaryHoldAndRepeatedMotionNeverUseSelectionOrTerrain(bool grabbed, string scene)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        var first = new WorkAreaEndpoint(-30, 65, -200, 0);
        var last = new WorkAreaEndpoint(30, 75, -200, 0);
        h.WorkArea.RestoreEndpoints(first, last);
        h.Loaded = scene != "unloaded";
        h.EntityOcclusion = scene == "entity";
        h.Target = null;
        h.Enable();
        for (int drag = 0; drag < 6; drag++)
        {
            bool start = drag % 2 == 0;
            h.Frame();
            var instance = start ? h.Session.Start! : h.Session.End!;
            var cell = (start ? h.WorkArea.State.Start : h.WorkArea.State.End)!.Value;
            var opposite = (start ? h.WorkArea.State.End : h.WorkArea.State.Start)!.Value;
            var baseline = h.WorkArea.State;
            h.Aim(instance.Value.Coordinates);
            Assert.True(h.Down().Handled);
            Assert.Equal(PositionGizmo.Center, h.Session.Manager.CapturedHandle);
            for (int frame = 0; frame < 120; frame++) h.Frame();
            Assert.Equal(baseline, h.WorkArea.State);
            Assert.Equal(drag, h.Commits);
            foreach (int dx in new[] { -3, 5, -2, 0, 4 })
            {
                var target = cell with { X = cell.X + dx, Y = cell.Y + 2 };
                h.MoveCenterTo(target);
                h.Frame();
                var expected = new WorkAreaBounds(target, opposite);
                Assert.Equal(expected.MinCell, h.WorkArea.State.Start);
                Assert.Equal(expected.MaxCell, h.WorkArea.State.End);
                Assert.Same(instance, h.Session.Manager.CapturedGizmo);
                h.AssertLiveBox();
                h.AssertOwnedActions();
            }
            Assert.True(h.Up().Handled);
            Assert.Equal(drag + 1, h.Commits);
            h.AssertNoTargeting();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllAxes_BothRolesRepeatedActualPointerMotionPreservesGrabOffsetAndNeverTargetsTerrain(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        h.WorkArea.RestoreEndpoints(new(0, 65, -20, 0), new(10, 75, -10, 0));
        double c = Math.Cos(0.45), s = Math.Sin(0.45);
        h.View = Mat4d.Multiply(new double[16], Yaw(0.65),
            [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1]);
        h.Loaded = false;
        h.EntityOcclusion = true;
        h.ReticleDistance = 20;
        h.Enable();
        int commits = 0;
        for (int repeat = 0; repeat < 2; repeat++)
        foreach (bool start in new[] { true, false })
        for (int component = 0; component < 3; component++)
        {
            var role = start ? WorkAreaEndpointRole.Start : WorkAreaEndpointRole.End;
            var cell = (start ? h.WorkArea.State.Start : h.WorkArea.State.End)!.Value;
            var opposite = (start ? h.WorkArea.State.End : h.WorkArea.State.Start)!.Value;
            var anchor = WorkAreaGizmoCoordinates.ToAnchor(cell, role).Coordinates;
            h.Reference = anchor - h.Capture().Camera!.ViewDirection * 20;
            h.Frame();
            var instance = start ? h.Session.Start! : h.Session.End!;
            var geometry = PositionGizmoGeometry.Create(h.Session.Renderer.Context!.Camera!, anchor,
                start ? GizmoShape.Circle : GizmoShape.Diamond)!;
            var axis = V(0, 0, 0).WithElement(component, 1);
            var grab = anchor + axis * (geometry.AxisLength * 0.7);
            h.Aim(grab);
            Assert.True(h.Down().Handled);
            Assert.Same(instance, h.Session.Manager.CapturedGizmo);
            Assert.Equal(new GizmoHandleId(component + 1), h.Session.Manager.CapturedHandle);
            for (int frame = 0; frame < 120; frame++) h.Frame();
            Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(cell, role), instance.Value);
            foreach (double delta in new[] { 0.4, 0.6, 1.4, 2.1, -1.2, 0d, 2.1 })
            {
                h.Aim(grab + axis * delta);
                h.Move(h.X, h.Y);
                h.Frame();
                var expectedAnchor = anchor + axis * Math.Round(delta, MidpointRounding.AwayFromZero);
                AssertCoordinates(expectedAnchor, instance.Value.Coordinates);
                Assert.Equal(0, instance.Value.Dimension);
                Assert.True(WorkAreaGizmoCoordinates.TryDecode(new(expectedAnchor, 0), role,
                    h.Accessor.Object, out var expectedCell));
                var bounds = new WorkAreaBounds(expectedCell, opposite);
                Assert.Equal(bounds.MinCell, h.WorkArea.State.Start);
                Assert.Equal(bounds.MaxCell, h.WorkArea.State.End);
                Assert.Equal(commits, h.Commits);
                h.AssertLiveBox();
                h.AssertOwnedActions();
            }
            Assert.True(h.Up().Handled);
            Assert.Equal(++commits, h.Commits);
            h.AssertNoTargeting();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyCreation_OnlyFreshExplicitAttemptsQueryTerrainAndSuccessfulGestureDoesNotStartDrag(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        h.Enable();
        h.Frame();
        Assert.True(h.Session.IsPending);
        for (int frame = 0; frame < 120; frame++) h.Frame();
        Assert.Equal(0, h.SelectionCalls);
        Assert.True(h.Down(20, 20).Handled);
        Assert.Equal(1, h.SelectionCalls);
        for (int frame = 0; frame < 120; frame++)
        {
            h.Frame();
            h.Down(20, 20);
            h.AssertOwnedActions();
        }
        Assert.Equal(1, h.SelectionCalls);
        Assert.True(h.Session.IsPending);
        Assert.Null(h.Session.Manager.CapturedGizmo);
        h.Up(20, 20);
        h.Target = new(0, 65, -200, 0);
        h.Loaded = false;
        h.Down(20, 20);
        h.Up(20, 20);
        Assert.Equal(2, h.SelectionCalls);
        Assert.True(h.Session.IsPending);
        h.Loaded = true;
        h.EntityOcclusion = true;
        h.Down(20, 20);
        h.Up(20, 20);
        Assert.Equal(3, h.SelectionCalls);
        Assert.True(h.Session.IsPending);
        h.EntityOcclusion = false;
        h.Down(20, 20);
        Assert.Equal(4, h.SelectionCalls);
        Assert.False(h.Session.IsPending);
        Assert.Equal(1, h.Commits);
        Assert.Equal(h.Target, h.WorkArea.State.Start);
        Assert.Equal(h.Target, h.WorkArea.State.End);
        for (int frame = 0; frame < 120; frame++) h.Frame();
        Assert.Null(h.Session.Manager.CapturedGizmo);
        h.Up(20, 20);
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        Assert.NotNull(h.Session.Manager.CapturedGizmo);
        for (int frame = 0; frame < 120; frame++) h.Frame();
        Assert.Equal(4, h.SelectionCalls);
        h.Key();
        h.Up();
        Assert.Equal(1, h.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndBoundaryAnchor_DecodesLocalYWithoutChangingDimensionAtNativeBoundary(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        h.Player.Entity.Pos.Dimension = 65535;
        h.Reference = Vector256.Create(0d, 32767d, 0d, 0d);
        var first = new WorkAreaEndpoint(-20, 32767, -200, 65535);
        h.WorkArea.RestoreEndpoints(first, new(20, 32767, -200, 65535));
        h.Enable();
        h.Frame();
        Assert.Equal(32768, h.Session.End!.Value.Coordinates[1]);
        h.Aim(h.Session.End.Value.Coordinates);
        h.Down();
        Assert.Same(h.Session.End, h.Session.Manager.CapturedGizmo);
        var target = new WorkAreaEndpoint(25, 32767, -200, 65535);
        h.MoveCenterTo(target);
        h.Frame();
        h.Up();
        var expected = new WorkAreaBounds(first, target);
        Assert.Equal(expected.MaxCell, h.WorkArea.State.End);
        Assert.Equal(expected.MinCell, h.WorkArea.State.Start);
        Assert.Equal(1, h.Commits);
        Assert.Null(h.LastRay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Center_InvalidCoordinateIntervalRetainsLastPreviewBoundsAndRecoversBeforeRollback(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        var h = f.H;
        var first = new WorkAreaEndpoint(-30, 65, -200, 0);
        var last = new WorkAreaEndpoint(30, 75, -200, 0);
        h.WorkArea.RestoreEndpoints(first, last);
        h.Enable();
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        var valid = new WorkAreaEndpoint(-35, 70, -200, 0);
        h.MoveCenterTo(valid);
        h.Frame();
        var preview = h.WorkArea.State;
        h.MoveCenterTo(new(-50, -20, -200, 0));
        for (int frame = 0; frame < 120; frame++) h.Frame();
        Assert.Equal(preview, h.WorkArea.State);
        Assert.True(h.WorkArea.IsEditing);
        h.AssertLiveBox();
        h.Api.Verify(api => api.TriggerIngameError(It.IsAny<object>(),
            "vanillabuildingexpanded:workarea-invalid-endpoint", It.IsAny<string>()), Times.Once);
        h.MoveCenterTo(valid);
        h.Frame();
        h.MoveCenterTo(new(-50, -20, -200, 0));
        for (int frame = 0; frame < 120; frame++) h.Frame();
        h.Api.Verify(api => api.TriggerIngameError(It.IsAny<object>(),
            "vanillabuildingexpanded:workarea-invalid-endpoint", It.IsAny<string>()), Times.Exactly(2));
        h.MoveCenterTo(new(-40, 72, -200, 0));
        h.Frame();
        Assert.Equal(new WorkAreaEndpoint(-40, 72, -200, 0), h.WorkArea.State.Start);
        h.Key();
        h.Up();
        Assert.Equal(first, h.WorkArea.State.Start);
        Assert.Equal(last, h.WorkArea.State.End);
        Assert.Equal(0, h.Commits);
        h.AssertNoTargeting();
    }

    [Fact]
    public void CancelThenRetry_UsesFreshPressAndCommitsOnlyTheRetry()
    {
        using var f = new Fixture();
        var h = f.H;
        var baseline = new WorkAreaEndpoint(-30, 65, -200, 0);
        h.WorkArea.RestoreEndpoints(baseline, new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        var target = new WorkAreaEndpoint(-70, 70, -200, 0);
        h.MoveCenterTo(target);
        h.Frame();
        h.AssertLiveBox();
        h.Key();
        h.Up();
        Assert.Equal(baseline, h.WorkArea.State.Start);
        Assert.Equal(0, h.Commits);
        h.AssertLiveBox();
        h.Frame();
        h.Aim(h.Session.Start.Value.Coordinates);
        h.Down();
        Assert.Same(h.Session.Start, h.Session.Manager.CapturedGizmo);
        h.MoveCenterTo(target);
        h.Frame();
        h.Up();
        Assert.Equal(new WorkAreaBounds(target, new(30, 65, -200, 0)).MinCell, h.WorkArea.State.Start);
        Assert.Equal(1, h.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderCallbackRetirement_DefersBoxMembershipAndMeshDisposalAlongsideGizmos(bool dispose)
    {
        using var f = new Fixture();
        var h = f.H;
        h.WorkArea.RestoreEndpoints(new(-30, 65, -200, 0), new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.MoveCenterTo(new(-70, 70, -200, 0));
        h.Frame();
        var active = h.Active.ToArray();
        if (dispose)
            h.Session.Renderer.ContextReady += _ =>
            {
                h.Session.Dispose();
                h.Box.Dispose();
                Assert.Equal(active, h.Active);
                Assert.All(h.Meshes, mesh => Assert.Equal(0, mesh.Disposals));
            };
        else h.Player.Entity.Alive = false;
        h.Session.Renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        Assert.Equal(active, h.Active);
        Assert.False(h.WorkArea.IsEditing);
        Assert.Null(h.Session.Renderer.Context);
        h.RunPending();
        Assert.Empty(h.Active);
        Assert.Equal(new WorkAreaEndpoint(-30, 65, -200, 0), h.WorkArea.State.Start);
        Assert.Equal(0, h.Commits);
    }

    [Theory]
    [InlineData("right")]
    [InlineData("escape")]
    [InlineData("focus")]
    [InlineData("dialog")]
    [InlineData("mode")]
    [InlineData("death")]
    [InlineData("dimension")]
    [InlineData("hidden")]
    [InlineData("camera")]
    [InlineData("world")]
    [InlineData("departure")]
    [InlineData("render")]
    [InlineData("input")]
    public void Cancellation_RollsBackBeforeRetirementWithoutCommitAndDetachesOnDispose(string reason)
    {
        using var f = new Fixture();
        var h = f.H;
        h.WorkArea.RestoreEndpoints(new(0, 65, -200, 0), new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        var baseline = h.WorkArea.State;
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.MoveCenterTo(new(-50, 70, -200, 0));
        h.Frame();
        Assert.NotEqual(baseline.Start, h.WorkArea.State.Start);
        switch (reason)
        {
            case "right": h.Down(button: EnumMouseButton.Right); break;
            case "escape": h.Key(); break;
            case "focus": h.Focus.ForEach(callback => callback(false)); break;
            case "dialog": h.Dialogs.Add(new TestDialog(h.Api.Object)); break;
            case "mode": h.WorkArea.SetContext(false, true); break;
            case "death": h.Player.Entity.Alive = false; break;
            case "dimension": h.Player.Entity.Pos.Dimension++; break;
            case "hidden": h.WorkArea.SetVisibility(false); break;
            case "camera": h.Grabbed = true; break;
            case "world": h.WorldAvailable = false; break;
            case "departure": h.Events.Raise(e => e.LeaveWorld += null!); break;
            case "render": h.Shader.SetupGet(s => s.LoadError).Returns(true); h.Frame(); break;
            case "input":
                bool failNextChange = true;
                h.Session.Start!.OnValueChanged += _ =>
                {
                    if (!failNextChange) return;
                    failNextChange = false;
                    throw new InvalidOperationException("input callback failure");
                };
                h.MoveCenterTo(new(-60, 72, -200, 0));
                h.Frame();
                break;
        }
        if (reason != "departure") h.Session.Refresh();
        Assert.False(h.WorkArea.IsEditing);
        Assert.Null(h.Session.Manager.CapturedGizmo);
        Assert.Equal((baseline.Start, baseline.End), (h.WorkArea.State.Start, h.WorkArea.State.End));
        Assert.Equal(0, h.Commits);
        h.Session.Dispose();
        h.Session.Dispose();
        h.RunPending();
        Assert.Empty(h.Focus);
        Assert.DoesNotContain(h.Active, pair => ReferenceEquals(pair.Item1, h.Session.Renderer));
        Assert.All(h.Meshes, mesh => Assert.Equal(1, mesh.Disposals));
        h.Frame();
        Assert.Equal(0, h.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialCreation_HandlePrecedenceAndExplicitCancellationAndDimensionIsolation(bool hasStart)
    {
        using var f = new Fixture();
        var h = f.H;
        var existing = new WorkAreaEndpoint(0, 65, -200, 0);
        h.WorkArea.RestoreEndpoints(hasStart ? existing : null, hasStart ? null : existing);
        h.Enable();
        h.Frame();
        Assert.True(h.Session.IsPending);
        Assert.Equal(1, h.Session.Manager.Count);
        Assert.DoesNotContain(h.Active, pair => !ReferenceEquals(pair.Item1, h.Session.Renderer));
        var instance = hasStart ? h.Session.Start! : h.Session.End!;
        h.Aim(instance.Value.Coordinates);
        h.Down();
        Assert.Same(instance, h.Session.Manager.CapturedGizmo);
        Assert.Equal(0, h.SelectionCalls);
        h.Down(button: EnumMouseButton.Right);
        h.Up(button: EnumMouseButton.Right);
        h.Up();
        Assert.False(h.Session.IsPending);
        h.Frame();
        Assert.False(h.Session.IsPending);
        h.WorkArea.SetVisibility(false);
        h.WorkArea.SetVisibility(true);
        h.Frame();
        Assert.True(h.Session.IsPending);
        h.Target = null;
        Assert.True(h.Down(20, 20).Handled);
        h.Up(20, 20);
        Assert.True(h.Session.IsPending);
        Assert.Equal(0, h.Commits);
        Assert.Equal(1, h.SelectionCalls);
        h.Target = new(-10, 70, -300, 0);
        h.Down(20, 20);
        h.Up(20, 20);
        var expected = new WorkAreaBounds(existing, h.Target.Value);
        Assert.Equal(expected.MinCell, h.WorkArea.State.Start);
        Assert.Equal(expected.MaxCell, h.WorkArea.State.End);
        Assert.Equal(1, h.Commits);
        Assert.Equal(2, h.SelectionCalls);
        h.Player.Entity.Pos.Dimension = 2;
        h.Session.Refresh();
        h.Frame();
        Assert.False(h.Session.IsPending);
        Assert.False(h.Session.Start!.Visible);
        Assert.False(h.Session.End!.Visible);
        h.WorkArea.SetVisibility(false);
        h.WorkArea.SetVisibility(true);
        h.Frame();
        Assert.False(h.Down(20, 20).Handled);
        Assert.Equal(1, h.Commits);
        Assert.Equal(2, h.SelectionCalls);
        Assert.Equal(0, h.WorkArea.State.Start!.Value.Dimension);
    }

    [Fact]
    public void ContextualCreationRetirement_ReoffersOnlyAfterFreshContextAndReleasedButtons()
    {
        using var f = new Fixture();
        var h = f.H;
        h.Enable();
        h.Frame();
        Assert.True(h.Session.IsPending);
        h.Key();
        h.Frame();
        Assert.False(h.Session.IsPending);
        h.WorkArea.SetContext(false, true);
        h.WorkArea.SetContext(true, true);
        h.Frame();
        Assert.True(h.Session.IsPending);
        h.Focus.ForEach(callback => callback(false));
        Assert.False(h.Session.IsPending);
        h.Raw.Left = true;
        h.Focus.ForEach(callback => callback(true));
        h.Frame();
        Assert.False(h.Session.IsPending);
        Assert.False(h.Down().Handled);
        h.Up();
        h.Frame();
        Assert.True(h.Session.IsPending);
    }

    [Fact]
    public void ClientRouter_SuppressesBothActionFamiliesBeforeBrushAndRetiresOnFreshPress()
    {
        using var f = new Fixture();
        var h = f.H;
        var owner = new BuildBrushSystem_Client();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic;
        typeof(BuildBrushSystem_Client).GetField("workAreaGizmos", flags)!.SetValue(owner, h.Session);
        var route = typeof(BuildBrushSystem_Client).GetMethod("InWorldAction", flags)!;
        h.Enable();
        h.Frame();
        h.Target = new(0, 65, -200, 0);
        h.Down();
        foreach (bool on in new[] { true, false })
        foreach (var action in new[] { EnumEntityAction.LeftMouseDown, EnumEntityAction.InWorldLeftMouseDown })
        {
            object[] args = [action, on, EnumHandling.PassThrough];
            route.Invoke(owner, args);
            Assert.Equal(EnumHandling.PreventSubsequent, args[2]);
        }
        h.Up();
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.Down(button: EnumMouseButton.Right);
        h.Up(button: EnumMouseButton.Right);
        h.Up();
        foreach (var action in new[] { EnumEntityAction.LeftMouseDown, EnumEntityAction.InWorldLeftMouseDown,
            EnumEntityAction.RightMouseDown, EnumEntityAction.InWorldRightMouseDown })
        {
            object[] args = [action, true, EnumHandling.PassThrough];
            route.Invoke(owner, args);
            Assert.Equal(EnumHandling.PreventSubsequent, args[2]);
        }
        h.Down(20, 20);
        object[] ordinary = [EnumEntityAction.InWorldLeftMouseDown, true, EnumHandling.PassThrough];
        route.Invoke(owner, ordinary);
        Assert.Equal(EnumHandling.PassThrough, ordinary[2]);
        Assert.Empty(h.Network.Invocations);
    }

    [Fact]
    public void AuthoritativeReplacement_CancelsBeforeAssigningAndEqualEchoKeepsTransaction()
    {
        using var f = new Fixture();
        var h = f.H;
        h.WorkArea.RestoreEndpoints(new(0, 65, -200, 0), new(30, 65, -200, 0));
        h.Enable();
        h.Frame();
        var start = h.Session.Start!;
        int notifications = 0;
        start.OnValueChanged += _ => notifications++;
        h.Aim(start.Value.Coordinates);
        h.Down();
        h.MoveCenterTo(new(-40, 60, -200, 0));
        h.Frame();
        h.WorkArea.RestoreEndpoints(h.WorkArea.State.Start, h.WorkArea.State.End);
        Assert.Same(start, h.Session.Manager.CapturedGizmo);
        Assert.True(h.WorkArea.IsEditing);
        Assert.Equal(1, notifications);
        var replacement = new WorkAreaEndpoint(-50, 60, -300, 0);
        h.WorkArea.RestoreEndpoints(replacement, h.WorkArea.State.End);
        Assert.Null(h.Session.Manager.CapturedGizmo);
        Assert.False(h.WorkArea.IsEditing);
        Assert.Equal(replacement, h.WorkArea.State.Start);
        Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(replacement, WorkAreaEndpointRole.Start), start.Value);
        Assert.Equal(3, notifications);
        h.Up();
        Assert.Equal(0, h.Commits);
    }

    [Fact]
    public void TerrainResolver_UsesFiniteVisualSegmentAndPreservesOcclusionClipAndLoadedChecks()
    {
        using var f = new Fixture();
        var h = f.H;
        var context = h.Capture();
        var pointer = context.Camera!.CreatePointer(context, 100, 150);
        var target = new WorkAreaTerrainTarget(h.Api.Object);
        h.Target = new(-20, 65, -200, 0);
        Assert.Equal(h.Target, target.Resolve(pointer));
        var ray = h.LastRay!;
        Assert.Equal(pointer.Ray.Origin[0], ray.origin.X, 10);
        Assert.Equal(pointer.FarPoint!.Value[2], ray.origin.Z + ray.dir.Z, 8);
        Assert.True(ray.Length > 1000);
        Assert.False(h.EntityFilter!(h.Player.Entity));
        Assert.True(h.EntityFilter(new EntityPlayer { EntityId = 999 }));
        Assert.False(h.EntityFilter(new NonInteractableEntity { EntityId = 999 }));
        var position = new BlockPos(0, 65, -200, 0);
        Assert.True(h.BlockFilter!(position, new Block { RenderPass = EnumChunkRenderPass.Opaque }));
        Assert.False(h.BlockFilter(position, new Block { RenderPass = EnumChunkRenderPass.Meta }));
        Assert.True(h.BlockFilter(position, new SelectableMetaBlock { RenderPass = EnumChunkRenderPass.Meta }));
        h.Api.Setup(a => a.Settings.Bool["renderMetaBlocks"]).Returns(true);
        target.Resolve(pointer);
        Assert.True(h.BlockFilter!(position, new Block { RenderPass = EnumChunkRenderPass.Meta }));
        h.EntityOcclusion = true;
        Assert.Null(target.Resolve(pointer));
        h.EntityOcclusion = false;
        h.Loaded = false;
        Assert.Null(target.Resolve(pointer));
        h.Loaded = true;
        h.Hit = new(0, 0, -1001);
        Assert.Null(target.Resolve(pointer));
        h.Hit = new(double.NaN, 0, 0);
        Assert.Null(target.Resolve(pointer));
        h.Hit = new(0, 0, 0);
        h.Target = new(0, 65, -1000, 0);
        Assert.Equal(h.Target, target.Resolve(pointer));
        h.Target = new(0, 65, 1, 0);
        Assert.Null(target.Resolve(pointer));
    }

    private sealed class TestDialog(ICoreClientAPI api) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => "";
    }

    private sealed class NonInteractableEntity : Entity
    {
        public override bool IsInteractable => false;
    }

    private sealed class SelectableMetaBlock : Block, IMetaBlock
    {
        public bool IsSelectable(BlockPos pos) => true;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string oldLocale = Lang.CurrentLocale;
        private readonly string locale = $"vbe-workarea-gizmos-{Guid.NewGuid():N}";
        public Harness H { get; }
        public Fixture(bool grabbed = false)
        {
            var translations = new Mock<ITranslationService>();
            translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns(true);
            translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("work area notice");
            Lang.AvailableLanguages.Add(locale, translations.Object);
            Lang.ChangeLanguage(locale);
            H = new(TestHelpers.CreateMockCapi(), new(), new(), new(), grabbed);
        }
        public void Dispose()
        {
            H.Dispose();
            Lang.ChangeLanguage(oldLocale);
            Lang.AvailableLanguages.Remove(locale);
        }
    }

    internal sealed class Harness : IDisposable
    {
        public readonly Mock<ICoreClientAPI> Api;
        public readonly Mock<IClientWorldAccessor> World;
        public readonly Mock<IBlockAccessor> Accessor;
        public readonly WorkAreaController WorkArea;
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly Mock<IInputAPI> Input = new();
        public readonly Mock<IRenderAPI> Render = new();
        public readonly Mock<IShaderProgram> Shader = new();
        public readonly Mock<IClientNetworkAPI> Network = new(MockBehavior.Strict);
        public readonly ClientPlayer Player = WorkAreaRenderingTests.CreatePlayer();
        public readonly MouseButtonState Raw = new(), WorldButtons = new();
        public readonly List<Action<bool>> Focus = [];
        public readonly List<GuiDialog> Dialogs = [];
        public readonly List<(IRenderer, EnumRenderStage)> Active = [];
        public readonly List<TestMesh> Meshes = [];
        public readonly List<Action> Pending = [];
        public readonly WorkAreaGizmoSession Session;
        public readonly WorkAreaRenderBinding Box;
        public bool Grabbed, WorldAvailable = true, Loaded = true, EntityOcclusion;
        public WorkAreaEndpoint? Target;
        public Vec3d Hit = new(0, 0, 0);
        public Ray? LastRay;
        public float[]? LastModel;
        public EntityFilter? EntityFilter;
        public BlockFilter? BlockFilter;
        public Vector256<double> Reference = Vector256.Create(0d, 65d, 0d, 0d);
        public int X = 400, Y = 300, Commits, SelectionCalls;
        public double ReticleDistance = 200;
        public double[] View = Mat4d.Create();
        public (long, long, long) Size => WorkArea.State.Bounds is { } b ? (b.SizeX, b.SizeY, b.SizeZ) : default;

        public Harness(Mock<ICoreClientAPI> api, Mock<IClientWorldAccessor> world,
            Mock<IBlockAccessor> accessor, WorkAreaController workArea, bool grabbed = false)
        {
            Api = api; World = world; Accessor = accessor; WorkArea = workArea; Grabbed = grabbed;
            Player.OverrideCameraMode = EnumCameraMode.FirstPerson;
            Api.Setup(a => a.World).Returns(() => WorldAvailable ? world.Object : null!);
            world.Setup(w => w.Player).Returns(Player);
            world.Setup(w => w.BlockAccessor).Returns(accessor.Object);
            accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(true);
            var chunk = new Mock<IWorldChunk>();
            accessor.Setup(a => a.GetChunkAtBlockPos(It.IsAny<BlockPos>())).Returns(() => Loaded ? chunk.Object : null!);
            world.Setup(w => w.RayTraceForSelection(It.IsAny<Ray>(), ref It.Ref<BlockSelection?>.IsAny,
                ref It.Ref<EntitySelection?>.IsAny, It.IsAny<BlockFilter>(), It.IsAny<EntityFilter>()))
                .Callback((Ray ray, ref BlockSelection? block, ref EntitySelection? entity,
                    BlockFilter bfilter, EntityFilter efilter) =>
                {
                    SelectionCalls++;
                    LastRay = ray;
                    EntityFilter = efilter;
                    BlockFilter = bfilter;
                    entity = EntityOcclusion ? new EntitySelection() : null!;
                    block = Target is { } cell ? new BlockSelection
                    {
                        Position = new(cell.X, cell.Y, cell.Z, cell.Dimension), HitPosition = Hit.Clone()
                    } : null!;
                });
            Api.Setup(a => a.Event).Returns(Events.Object);
            Api.Setup(a => a.Input).Returns(Input.Object);
            Api.Setup(a => a.Render).Returns(Render.Object);
            Api.Setup(a => a.Network).Returns(Network.Object);
            Api.Setup(a => a.IsGamePaused).Returns(false);
            Api.Setup(a => a.Settings.Bool["renderMetaBlocks"]).Returns(false);
            Api.Setup(a => a.Gui.OpenedGuis).Returns(Dialogs);
            Input.Setup(i => i.MouseGrabbed).Returns(() => Grabbed);
            Input.Setup(i => i.MouseButton).Returns(Raw);
            Input.Setup(i => i.InWorldMouseButton).Returns(WorldButtons);
            Input.Setup(i => i.KeyboardKeyStateRaw).Returns(new bool[256]);
            Render.Setup(r => r.FrameWidth).Returns(800);
            Render.Setup(r => r.FrameHeight).Returns(600);
            Render.Setup(r => r.CameraMatrixOriginf).Returns(new Matrixf().Values);
            Api.Setup(a => a.Shader.GetProgram((int)EnumShaderProgram.Wireframe)).Returns(Shader.Object);
            Api.Setup(a => a.Shader.NewShaderProgram()).Returns(Shader.Object);
            Api.Setup(a => a.Shader.NewShader(It.IsAny<EnumShaderType>())).Returns(() => Mock.Of<IShader>());
            Api.Setup(a => a.Shader.RegisterMemoryShaderProgram("vbe-gizmo-overlay", Shader.Object)).Returns(49);
            Shader.Setup(s => s.Compile()).Returns(true);
            Shader.Setup(s => s.UniformMatrix("modelViewMatrix", It.IsAny<float[]>()))
                .Callback<string, float[]>((_, matrix) => LastModel = (float[])matrix.Clone());
            Render.Setup(r => r.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
            {
                var mesh = new TestMesh(); Meshes.Add(mesh); return mesh;
            });
            Events.Setup(e => e.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), It.IsAny<string>()))
                .Callback<IRenderer, EnumRenderStage, string>((renderer, stage, _) =>
                {
                    Assert.DoesNotContain((renderer, stage), Active);
                    Active.Add((renderer, stage));
                });
            Events.Setup(e => e.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()))
                .Callback<IRenderer, EnumRenderStage>((renderer, stage) => Assert.True(Active.Remove((renderer, stage))));
            Events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => Pending.Add(action));
            WorkArea.EndpointsCommitted += OnCommit;
            Box = new(api.Object, workArea, new WorkAreaRenderingTests.FakeState(),
                () => Session?.Renderer.IsInCallback == true);
            Session = new(api.Object, workArea, Focus, Capture, new WorkAreaRenderingTests.FakeState());
        }

        public GizmoContext Capture() => new(Player.Entity.Pos.Dimension,
            new GizmoCamera(View, Mat4d.Perspective(new double[16], Math.PI / 2, 4d / 3, 0.1, 1000),
                Reference, 800, 600, mouseGrabbed: Grabbed, cameraMode: Player.CameraMode));
        public void Enable() { WorkArea.SetContext(true, true); WorkArea.SetVisibility(true); }
        public void Frame()
        {
            Session.Renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
            Session.Renderer.OnRenderFrame(0, EnumRenderStage.AfterBlit);
            RunPending();
        }
        public void RunPending()
        {
            foreach (var action in Pending.ToArray()) { Pending.Remove(action); action(); }
        }
        public void AssertLiveBox()
        {
            var start = WorkArea.State.Start!.Value;
            var end = WorkArea.State.End!.Value;
            var expectedSize = (Math.Abs((long)start.X - end.X) + 1,
                Math.Abs((long)start.Y - end.Y) + 1, Math.Abs((long)start.Z - end.Z) + 1);
            Assert.Equal(expectedSize, Size);
            Player.Entity.CameraPos.Set(Reference[0],
                Reference[1] + (double)Player.Entity.Pos.Dimension * BlockPos.DimensionBoundary, Reference[2]);
            LastModel = null;
            var box = Assert.Single(Active, pair => pair.Item1 is WorkAreaRenderer
                && pair.Item2 == EnumRenderStage.Opaque).Item1;
            box.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.NotNull(LastModel);
            double expansion = WorkAreaRenderer.VisualExpansion;
            Assert.Equal((float)(expectedSize.Item1 + 2 * expansion), LastModel[0]);
            Assert.Equal((float)(expectedSize.Item2 + 2 * expansion), LastModel[5]);
            Assert.Equal((float)(expectedSize.Item3 + 2 * expansion), LastModel[10]);
            Assert.Equal((float)(Math.Min(start.X, end.X) - Reference[0] - expansion), LastModel[12]);
            Assert.Equal((float)(Math.Min(start.Y, end.Y) - Reference[1] - expansion), LastModel[13]);
            Assert.Equal((float)(Math.Min(start.Z, end.Z) - Reference[2] - expansion), LastModel[14]);
        }
        public void Aim(Vector256<double> point)
        {
            if (Grabbed)
            {
                Reference = point - Capture().Camera!.ViewDirection * ReticleDistance;
                Frame();
                X = 400; Y = 300;
            }
            else
            {
                Assert.True(Session.Renderer.Context!.Camera!.TryProject(point, out var screen));
                X = (int)Math.Round(screen[0]); Y = (int)Math.Round(screen[1]);
            }
        }
        public void MoveCenterTo(WorkAreaEndpoint cell)
        {
            var captured = Session.Manager.CapturedGizmo;
            Assert.NotNull(captured);
            Assert.Equal(PositionGizmo.Center, Session.Manager.CapturedHandle);
            var role = ReferenceEquals(captured, Session.Start) ? WorkAreaEndpointRole.Start : WorkAreaEndpointRole.End;
            Aim(WorkAreaGizmoCoordinates.ToAnchor(cell, role).Coordinates);
            Move(X, Y);
        }
        public MouseEvent Move(int x, int y)
        {
            X = x; Y = y;
            var e = new MouseEvent(x, y);
            Events.Raise(ev => ev.MouseMove += null!, e);
            return e;
        }
        public void AssertOwnedActions()
        {
            foreach (bool on in new[] { true, false })
            foreach (var action in new[] { EnumEntityAction.LeftMouseDown, EnumEntityAction.InWorldLeftMouseDown })
            {
                var handling = EnumHandling.PassThrough;
                Session.HandleWorldAction(action, on, ref handling);
                Assert.Equal(EnumHandling.PreventSubsequent, handling);
            }
        }
        public void AssertNoTargeting()
        {
            Assert.Equal(0, SelectionCalls);
            Assert.DoesNotContain(World.Invocations, invocation =>
                invocation.Method.Name.Contains("Selection", StringComparison.OrdinalIgnoreCase)
                || invocation.Method.Name.Contains("RayTrace", StringComparison.OrdinalIgnoreCase)
                || invocation.Method.Name.Contains("Raycast", StringComparison.OrdinalIgnoreCase)
                || invocation.Method.Name.Contains("Entit", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(Accessor.Invocations, invocation =>
                invocation.Method.Name is not "IsValidPos");
            Assert.Empty(Network.Invocations);
        }
        public MouseEvent Down(int? x = null, int? y = null, EnumMouseButton button = EnumMouseButton.Left)
        {
            if (button == EnumMouseButton.Left) Raw.Left = true; else Raw.Right = true;
            var e = new MouseEvent(x ?? X, y ?? Y, button, 0);
            Events.Raise(ev => ev.MouseDown += null!, e);
            return e;
        }
        public MouseEvent Up(int? x = null, int? y = null, EnumMouseButton button = EnumMouseButton.Left)
        {
            if (button == EnumMouseButton.Left) Raw.Left = false; else Raw.Right = false;
            var e = new MouseEvent(x ?? X, y ?? Y, button, 0);
            Events.Raise(ev => ev.MouseUp += null!, e);
            return e;
        }
        public void Key() => Events.Raise(e => e.KeyDown += null!, new KeyEvent { KeyCode = (int)GlKeys.Escape });
        private void OnCommit() => Commits++;
        public void Dispose()
        {
            Session.Dispose(); Box.Dispose(); RunPending();
            WorkArea.EndpointsCommitted -= OnCommit;
        }
    }

    internal sealed class TestMesh : MeshRef
    {
        public int Disposals;
        public override bool Initialized => true;
        public override void Dispose() { Disposals++; base.Dispose(); }
    }
}
