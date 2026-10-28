using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.ComposedPositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

public sealed class GizmoGeometryTests
{
    [Fact]
    public void AxisFrames_ReadLivePositionAndDirectionWithoutMeshesAndCanBeFrozen()
    {
        var position = new GizmoPosition(V(1, 2, 3), 7);
        var direction = V(2, 0, 0);
        var source = new GizmoAxisFrameSource(() => position, () => direction);
        var context = new GizmoContext(7);
        var frame = source.GetFrame(context);
        Assert.Equal(position.Coordinates, frame.Origin);
        Assert.Equal(V(1, 0, 0), frame.Direction);
        Assert.Null(frame.TangentX);
        var frozen = new GizmoFixedFrameSource(frame);
        position = new(V(4, 5, 6), 7);
        direction = V(0, 5, 0);
        Assert.Equal(V(0, 1, 0), source.GetFrame(context).Direction);
        Assert.Equal(position.Coordinates, source.GetFrame(context).Origin);
        Assert.Equal(frame, frozen.GetFrame(context));
        Assert.Throws<InvalidOperationException>(() => source.GetFrame(new(0)));
        Assert.Throws<InvalidOperationException>(() => frozen.GetFrame(new(0)));
        Assert.Throws<InvalidOperationException>(() => new GizmoFixedFrameSource(default));
        direction = default;
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetFrame(context));
        direction = V(double.NaN, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetFrame(context));
        Assert.Throws<ArgumentNullException>(() => new GizmoAxisFrameSource(null!, () => V(1, 0, 0)));
        Assert.Throws<ArgumentNullException>(() => new GizmoAxisFrameSource(() => position, null!));
    }

    [Fact]
    public void CameraFrames_AreOrthogonalLogicalPlanesEvenOutsideTheVisibleClipInterval()
    {
        var position = new GizmoPosition(V(0, 0, 20), 0);
        var source = new GizmoCameraFrameSource(() => position);
        var initial = Camera();
        var first = source.GetFrame(initial);
        Assert.Equal(initial.Camera!.ViewDirection, first.Direction);
        Assert.Equal(V(1, 0, 0), first.TangentX);
        Assert.Equal(V(0, 1, 0), first.TangentY);
        var frozen = new GizmoFixedFrameSource(first);
        var rotated = Camera(view: Yaw(0.7));
        var current = source.GetFrame(rotated);
        Assert.NotEqual(first.Direction, current.Direction);
        Assert.Equal(0d, Vector256.Dot(current.Direction, current.TangentX!.Value), 10);
        Assert.Equal(0d, Vector256.Dot(current.Direction, current.TangentY!.Value), 10);
        Assert.Equal(0d, Vector256.Dot(current.TangentX.Value, current.TangentY.Value), 10);
        Assert.Equal(first, frozen.GetFrame(rotated));
        Assert.False(source.TryGetFrame(new(0), out _));
        Assert.True(source.TryGetFrame(initial, out var available));
        Assert.Equal(first, available);
        Assert.Throws<InvalidOperationException>(() => source.TryGetFrame(new(1, initial.Camera), out _));
        Assert.Throws<InvalidOperationException>(() => source.GetFrame(new(0)));
        Assert.Throws<InvalidOperationException>(() => source.GetFrame(new(1, initial.Camera)));
        Assert.Throws<ArgumentNullException>(() => new GizmoCameraFrameSource(null!));
    }

    [Theory]
    [InlineData(GizmoShape.Arrow, 1, -0.2, false)]
    [InlineData(GizmoShape.Arrow, 2, -10, false)]
    [InlineData(GizmoShape.Arrow, 1, -200, true)]
    [InlineData(GizmoShape.Circle, 1, -0.2, false)]
    [InlineData(GizmoShape.Circle, 2, -10, false)]
    [InlineData(GizmoShape.Circle, 1, -200, true)]
    [InlineData(GizmoShape.Diamond, 1, -0.2, false)]
    [InlineData(GizmoShape.Diamond, 2, -10, false)]
    [InlineData(GizmoShape.Diamond, 1, -200, true)]
    public void SharedSnapshots_PreserveExistingDimensionsVerticesAndPickingAtLargeCoordinates(
        GizmoShape shape, double scale, double z, bool distant)
    {
        var reference = distant ? V(2000000000.125, 32768, -2000000000.25) : default;
        var context = Camera(scale: scale, reference: reference);
        var anchor = reference + V(0, 0, z);
        using var owner = new GeometryGizmo();
        var (handle, source, target) = Add(owner, -8521, shape, anchor);
        var draw = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        double length = Math.Clamp(64 * scale * -z / 300, 0.125, 8);
        Assert.Same(handle, draw.Geometry.Handle);
        Assert.Same(owner, draw.Geometry.Owner);
        Assert.Same(context, draw.Geometry.Context);
        Assert.Equal(shape, draw.Appearance.Shape);
        Assert.Equal(shape, draw.Geometry.Shape);
        Assert.Equal(draw.Appearance.Size, draw.Geometry.Size);
        double extent = length * (shape == GizmoShape.Arrow ? 1 : 7d / 64);
        Assert.Equal(extent, draw.Geometry.Extent, 8);
        AssertCoordinates(V(extent, 0, 0), draw.Geometry.BasisX, 8);
        AssertCoordinates(shape == GizmoShape.Arrow ? V(0, 0, extent) : V(0, extent, 0),
            draw.Geometry.BasisY, 8);
        AssertCoordinates(shape == GizmoShape.Arrow ? V(0, -extent, 0) : default, draw.Geometry.BasisZ, 8);
        Assert.Equal(shape == GizmoShape.Arrow ? anchor + draw.Geometry.BasisX : anchor, draw.Geometry.LabelAnchor);
        var point = shape == GizmoShape.Arrow ? anchor + V(extent * 0.5, 0, 0) : anchor;
        Assert.True(context.Camera!.TryProject(point, out var screen));
        var pointer = context.Camera.CreatePointer(context, screen[0], screen[1]);
        var hit = Assert.Single(target.HitTest(pointer));
        Assert.Same(handle, hit.Handle);
        Assert.Equal(shape == GizmoShape.Arrow ? 1 : 0, hit.Priority);
        var nearest = shape == GizmoShape.Arrow && distant
            ? anchor + V(extent * 0.8125, 0, extent * 0.09375) : point;
        Assert.Equal(Vector256.Dot(nearest - pointer.Ray.Origin, pointer.Ray.Direction), hit.Distance,
            distant ? 4 : 8);
        foreach (var local in new[] { V(-2, 2, 0), V(2, 2, 0), V(0, -2, 0) })
        {
            Assert.True(context.Camera.TryProject(anchor + local * length, out screen));
            Assert.Empty(target.HitTest(context.Camera.CreatePointer(context, screen[0], screen[1])));
        }
    }

    [Fact]
    public void SharedPrimitives_HaveQualifiedArrowHeadsAndClosedMarkerLoops()
    {
        Assert.Equal(new[]
        {
            V(0, 0, 0), V(1, 0, 0), V(0.8125, 0.09375, 0), V(0.8125, -0.09375, 0),
            V(0.8125, 0, 0.09375), V(0.8125, 0, -0.09375)
        }, GizmoShapes.Vertices(GizmoShape.Arrow).ToArray());
        Assert.Equal(new[] { 0, 1, 1, 2, 1, 3, 1, 4, 1, 5 }, GizmoShapes.Indices(GizmoShape.Arrow).ToArray());
        foreach (var (shape, count) in new[] { (GizmoShape.Circle, 16), (GizmoShape.Diamond, 4) })
        {
            var vertices = GizmoShapes.Vertices(shape).ToArray();
            var indices = GizmoShapes.Indices(shape).ToArray();
            Assert.Equal(count, vertices.Length);
            Assert.Equal(count * 2, indices.Length);
            for (int i = 0; i < count; i++)
            {
                AssertCoordinates(V(Math.Cos(2 * Math.PI * i / count), Math.Sin(2 * Math.PI * i / count), 0),
                    vertices[i]);
                Assert.Equal(i, indices[2 * i]);
                Assert.Equal((i + 1) % count, indices[2 * i + 1]);
            }
        }
    }

    [Fact]
    public void LogicalFrames_CanOrientGeometryWithoutPredefinedAxes()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var frame = new GizmoFrame(V(0, 0, -10), V(1, 0, -1), 0);
        foreach (var shape in new[] { GizmoShape.Arrow, GizmoShape.Circle, GizmoShape.Diamond })
        {
            var (_, source, target) = Add(owner, (int)shape + 560, shape, frame.Origin,
                new GizmoFixedFrameSource(frame));
            var draw = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
            var point = draw.Geometry.Transform(V(0.5, 0, 0));
            Assert.True(context.Camera!.TryProject(point, out var screen));
            Assert.Single(target.HitTest(context.Camera.CreatePointer(context, screen[0], screen[1])));
            point = draw.Geometry.Transform(V(4, 4, 0));
            Assert.True(context.Camera.TryProject(point, out screen));
            Assert.Empty(target.HitTest(context.Camera.CreatePointer(context, screen[0], screen[1])));
            Assert.Equal(frame, draw.Geometry.Frame);
        }
    }

    [Fact]
    public void SpatialCache_ReusesColorAndStyleButRebuildsShapeSizeFrameAndCamera()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var (handle, source, target) = Add(owner, 987654, GizmoShape.Circle, V(0, 0, -10));
        var initial = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        handle.SetAppearance(EAppearenceState.Default, Baseline(GizmoShape.Circle) with
        { Color = new Vector4(0, 1, 0, 0.4f), LineWidth = 3 });
        var color = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        Assert.Same(initial.Geometry, color.Geometry);
        Assert.NotEqual(initial.Appearance.Color, color.Appearance.Color);
        Assert.Equal(3f, color.Appearance.LineWidth);
        handle.SetAppearance(EAppearenceState.Hover, new() { Shape = GizmoShape.Diamond, Size = 14 });
        target.UpdateHover(handle, true);
        var hover = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        Assert.NotSame(initial.Geometry, hover.Geometry);
        Assert.Equal(GizmoShape.Diamond, hover.Geometry.Shape);
        Assert.Equal(initial.Geometry.Extent * 2, hover.Geometry.Extent, 8);
        Assert.Single(target.HitTest(context.Camera!.CreatePointer(context, 412, 300)));
        handle.SetAppearance(EAppearenceState.Selected, new() { Shape = GizmoShape.Arrow, Size = 28 });
        Assert.True(owner.BeginInteraction(handle.Id, context.Camera.CreatePointer(context, 400, 300)));
        target.UpdateInteracting(handle, true);
        var selected = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        Assert.Equal(GizmoShape.Arrow, selected.Geometry.Shape);
        Assert.Equal(28d, selected.Geometry.Size);
        Assert.Equal(color.Appearance.Color, selected.Appearance.Color);
        owner.CancelInteraction();
        target.UpdateInteracting(handle, false);
        target.UpdateHover(handle, false);
        var position = new GizmoPosition(V(1, 0, -10), 0);
        handle.FrameSource = new GizmoCameraFrameSource(() => position);
        var moved = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        Assert.NotSame(hover.Geometry, moved.Geometry);
        Assert.Equal(position.Coordinates, moved.Geometry.Frame.Origin);
        position = new(V(2, 0, -10), 0);
        var movedAgain = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        Assert.NotSame(moved.Geometry, movedAgain.Geometry);
        var nextContext = Camera(scale: 2);
        var next = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(nextContext));
        Assert.NotSame(movedAgain.Geometry, next.Geometry);
        Assert.Equal(movedAgain.Geometry.Extent * 2, next.Geometry.Extent, 8);
    }

    [Fact]
    public void Picking_UsesOnlyThePublishedDrawingContextAndDoesNotRereadTheLiveFrame()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        bool readAllowed = true;
        var frame = new GizmoCameraFrameSource(() => readAllowed
            ? new(V(0, 0, -10), 0) : throw new InvalidOperationException("Picking must not re-read frames."));
        var (handle, source, target) = Add(owner, 114, GizmoShape.Circle, V(0, 0, -10), frame);
        var pointer = context.Camera!.CreatePointer(context, 400, 300);
        Assert.Empty(target.HitTest(pointer));
        source.GetSnapshot(context);
        readAllowed = false;
        Assert.Single(target.HitTest(pointer));
        var otherContext = new GizmoContext(0, context.Camera);
        Assert.Empty(target.HitTest(context.Camera.CreatePointer(otherContext, 400, 300)));
        Assert.Throws<ArgumentException>(() => target.HitTest(default));
        Assert.Throws<InvalidOperationException>(() => target.UpdateInteracting(handle, true));
    }

    [Fact]
    public void Eligibility_DisablesPickingWithoutHidingGeometryAndRejectsRetiredMembers()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var (handle, source, target) = Add(owner, 22, GizmoShape.Circle, V(0, 0, -10));
        var pointer = context.Camera!.CreatePointer(context, 400, 300);
        var draw = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        target.UpdateHover(handle, true);
        handle.Enabled = false;
        Assert.Null(owner.HoveredHandleInstance);
        Assert.NotNull(source.GetSnapshot(context));
        Assert.Empty(target.HitTest(pointer));
        Assert.Throws<InvalidOperationException>(() => target.UpdateHover(handle, true));
        handle.Enabled = true;
        Assert.Same(draw.Geometry, source.GetSnapshot(context)!.Geometry);
        handle.Visible = false;
        Assert.Null(source.GetSnapshot(context));
        Assert.Empty(target.HitTest(pointer));
        handle.Visible = true;
        source.GetSnapshot(context);
        owner.RemoveHandle(handle);
        Assert.Empty(target.HitTest(pointer));
        Assert.Null(source.GetSnapshot(context));
        Assert.Throws<ArgumentException>(() => target.UpdateHover(handle, true));
        handle.Dispose();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-0.01)]
    [InlineData(-1010)]
    public void ClippedSnapshots_HaveNeitherDrawingNorPicking(double z)
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var (_, source, target) = Add(owner, 157, GizmoShape.Circle, V(0, 0, z));
        Assert.Null(source.GetSnapshot(context));
        Assert.Empty(target.HitTest(context.Camera!.CreatePointer(context, 400, 300)));
    }

    [Fact]
    public void PartialClipping_AndMissingOrInvalidFramesCannotPublishInvisibleTargets()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var (handle, source, target) = Add(owner, 59, GizmoShape.Arrow, V(0, 0, -0.15),
            new GizmoAxisFrameSource(() => new(V(0, 0, -0.15), 0), () => V(0, 0, 1)));
        Assert.Null(source.GetSnapshot(context));
        Assert.Empty(target.HitTest(context.Camera!.CreatePointer(context, 400, 300)));
        handle.FrameSource = null;
        Assert.Throws<InvalidOperationException>(() => source.GetSnapshot(context));
        Assert.Null(source.Snapshot);
        handle.FrameSource = new GizmoFixedFrameSource(new(V(0, 0, -10), V(1, 0, 0), 1));
        Assert.Throws<InvalidOperationException>(() => source.GetSnapshot(context));
        handle.FrameSource = new GizmoAxisFrameSource(() => new(V(0, 0, -10), 0), () => V(1, 0, 0));
        Assert.Throws<InvalidOperationException>(() => source.GetSnapshot(new(0)));
        Assert.Null(source.Snapshot);
    }

    [Fact]
    public void DeliberateCustomPaddingAndGeometry_DoNotChangeDrawingOrMeasurementBindings()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var (handle, source, target) = Add(owner, -193, GizmoShape.Circle, V(0, 0, -10));
        var measurement = new Measurement();
        handle.InteractionTarget = measurement;
        var draw = Assert.IsType<GizmoHandleDraw>(source.GetSnapshot(context));
        var pointer = context.Camera!.CreatePointer(context, 420, 300);
        Assert.Empty(target.HitTest(pointer));
        var padded = new GizmoGeometryHitTarget(owner, source, 14);
        handle.HitTarget = padded;
        Assert.Single(padded.HitTest(pointer));
        Assert.Same(draw, source.Snapshot);
        var custom = new GizmoDelegateHitTarget(owner, p =>
            p.Ray.Direction[0] > 0.5 ? [new(owner, handle, 25, -2)] : []);
        handle.HitTarget = custom;
        var customPointer = context.Camera.CreatePointer(context, 700, 300);
        Assert.Empty(target.HitTest(customPointer));
        Assert.Single(custom.HitTest(customPointer));
        Assert.Same(measurement, handle.InteractionTarget);
        Assert.Equal(0, measurement.Preparations);
        Assert.Same(draw.Geometry, source.GetSnapshot(context)!.Geometry);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoGeometryHitTarget(owner, source, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GizmoGeometryHitTarget(owner, source, double.NaN));
    }

    [Fact]
    public void Aggregates_CopyHierarchyAndDispatchArbitraryIdsWithDeterministicDeduplicatedRanks()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var a = Add(owner, -7, GizmoShape.Circle, V(0, 0, -10));
        var b = Add(owner, 134, GizmoShape.Circle, V(0, 0, -10));
        var c = Add(owner, 9000, GizmoShape.Circle, V(0, 0, -10));
        var drawA = Assert.IsType<GizmoHandleDraw>(a.Source.GetSnapshot(context));
        var drawB = Assert.IsType<GizmoHandleDraw>(b.Source.GetSnapshot(context));
        Assert.Equal(drawA.Geometry.Shape, drawB.Geometry.Shape);
        Assert.Equal(drawA.Geometry.BasisX, drawB.Geometry.BasisX);
        List<IGizmoHitTarget> children =
        [
            new GizmoDelegateHitTarget(owner, _ =>
                [new(owner, a.Handle, 1, 1), new(owner, c.Handle, 9), new(owner, b.Handle, 9)])
        ];
        var nested = new GizmoAggregateHitTarget(owner, children);
        children.Clear();
        var aggregate = new GizmoAggregateHitTarget(owner,
            [nested, new GizmoDelegateHitTarget(owner, _ => [new(owner, c.Handle, 8)])]);
        var pointer = context.Camera!.CreatePointer(context, 400, 300);
        Assert.Equal(new[] { 134, 9000, -7 }, nested.HitTest(pointer).Select(h => h.Handle.Id.Value));
        Assert.Equal(new[] { 9000, 134, -7 }, aggregate.HitTest(pointer).Select(h => h.Handle.Id.Value));
        Assert.Throws<NotSupportedException>(() => ((IList<IGizmoHitTarget>)aggregate.Targets).Clear());
        aggregate.UpdateHover(a.Handle, true);
        aggregate.UpdateHover(b.Handle, false);
        Assert.Same(a.Handle, owner.HoveredHandleInstance);
        aggregate.UpdateHover(a.Handle, false);
        Assert.Null(owner.HoveredHandleInstance);
        Assert.True(owner.BeginInteraction(c.Handle.Id, pointer));
        aggregate.UpdateInteracting(c.Handle, true);
        Assert.True(c.Handle.IsSelected);
        owner.CancelInteraction();
        aggregate.UpdateInteracting(c.Handle, false);
    }

    [Fact]
    public void AggregateCallbacks_RecheckEarlierHitsBeforePublishing()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var (handle, _, target) = Add(owner, 3, GizmoShape.Circle, V(0, 0, -10));
        var first = new GizmoDelegateHitTarget(owner, _ => [new(owner, handle, 2)]);
        var disable = new GizmoDelegateHitTarget(owner, _ =>
        {
            handle.Enabled = false;
            return [];
        });
        var aggregate = new GizmoAggregateHitTarget(owner, [first, disable]);
        var pointer = context.Camera!.CreatePointer(context, 400, 300);
        Assert.Empty(aggregate.HitTest(pointer));
        Assert.True(handle.Visible);
        handle.Enabled = true;
        var remove = new GizmoDelegateHitTarget(owner, _ =>
        {
            owner.RemoveHandle(handle);
            return [];
        });
        aggregate = new(owner, [first, remove]);
        Assert.Throws<InvalidOperationException>(() => aggregate.HitTest(pointer));
        handle.Dispose();
    }

    [Fact]
    public void FrameCallbacks_CannotPublishDetachedDisposedOrReconfiguredSnapshots()
    {
        using var owner = new GeometryGizmo();
        var context = Camera();
        var valid = new GizmoFrame(V(0, 0, -10), V(1, 0, 0), 0);
        var (handle, source, _) = Add(owner, 196, GizmoShape.Arrow, valid.Origin);
        handle.FrameSource = new CallbackFrame(() =>
        {
            owner.RemoveHandle(handle);
            return valid;
        });
        Assert.Null(source.GetSnapshot(context));
        owner.AddHandle(handle);
        handle.FrameSource = new CallbackFrame(() =>
        {
            handle.FrameSource = new GizmoFixedFrameSource(valid);
            return valid;
        });
        Assert.Throws<InvalidOperationException>(() => source.GetSnapshot(context));
        Assert.Null(source.Snapshot);
        handle.FrameSource = new CallbackFrame(() => default);
        Assert.Throws<InvalidOperationException>(() => source.GetSnapshot(context));
        handle.FrameSource = new CallbackFrame(() =>
        {
            handle.Dispose();
            return valid;
        });
        Assert.Null(source.GetSnapshot(context));
    }

    [Fact]
    public void Dispatch_RejectsMalformedForeignAliasedAndStaleResults()
    {
        using var owner = new GeometryGizmo();
        using var foreign = new GeometryGizmo();
        var context = Camera();
        var own = Add(owner, 48, GizmoShape.Circle, V(0, 0, -10));
        var other = Add(foreign, 48, GizmoShape.Circle, V(0, 0, -10));
        var pointer = context.Camera!.CreatePointer(context, 400, 300);
        var malformed = new GizmoDelegateHitTarget(owner, _ => [default]);
        var wrongOwner = new GizmoDelegateHitTarget(owner, _ => [new(foreign, other.Handle, 2)]);
        var alias = new Alias(own.Handle.Id, owner);
        var aliased = new GizmoDelegateHitTarget(owner, _ => [new(owner, alias, 2)]);
        Assert.Throws<InvalidOperationException>(() => malformed.HitTest(pointer));
        Assert.Throws<InvalidOperationException>(() => wrongOwner.HitTest(pointer));
        Assert.Throws<InvalidOperationException>(() => aliased.HitTest(pointer));
        Assert.Throws<ArgumentException>(() => own.Target.UpdateHover(alias, true));
        Assert.Throws<ArgumentException>(() => new GizmoAggregateHitTarget(owner, [other.Target]));
        Assert.Throws<ArgumentException>(() => new GizmoGeometryHitTarget(foreign, own.Source));
        var staleHit = new GizmoHandleHit(owner, own.Handle, 2);
        owner.RemoveHandle(own.Handle);
        var stale = new GizmoDelegateHitTarget(owner, _ => [staleHit]);
        Assert.Throws<InvalidOperationException>(() => stale.HitTest(pointer));
        own.Handle.Dispose();
    }

    private static AppearenceProperties Baseline(GizmoShape shape) => new()
    {
        Shape = shape, Size = shape == GizmoShape.Arrow ? 64 : 7,
        Color = Vector4.One, LineWidth = 1.6f
    };

    private static (GizmoHandle Handle, GizmoGeometrySource Source, GizmoGeometryHitTarget Target) Add(
        GeometryGizmo owner, int id, GizmoShape shape, Vector256<double> origin, IGizmoFrameSource? frame = null)
    {
        var handle = new GizmoHandle(new(id), Baseline(shape))
        {
            FrameSource = frame ?? (shape == GizmoShape.Arrow
                ? new GizmoAxisFrameSource(() => new(origin, 0), () => V(1, 0, 0))
                : new GizmoCameraFrameSource(() => new(origin, 0)))
        };
        owner.AddHandle(handle);
        var source = new GizmoGeometrySource(handle);
        var target = new GizmoGeometryHitTarget(owner, source);
        handle.HitTarget = target;
        return (handle, source, target);
    }

    private sealed class GeometryGizmo : Gizmo
    {
        public override int Dimension => 0;
        protected override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer) => [];
        protected override void RenderCore(GizmoContext context) { }
        protected override void UpdateInteractionCore(GizmoPointer pointer) { }
    }

    private sealed record Alias(GizmoHandleId Id, Gizmo? Owner) : IGizmoHandle;

    private sealed class CallbackFrame(Func<GizmoFrame> frame) : IGizmoFrameSource
    {
        public GizmoFrame GetFrame(GizmoContext context) => frame();
    }

    private sealed class Measurement : IGizmoDragTarget
    {
        public int Preparations { get; private set; }
        public bool TryPrepare(GizmoPointer pointer, [NotNullWhen(true)] out IGizmoDragSession? session,
            [NotNullWhen(false)] out string? rejection)
        {
            Preparations++;
            session = null;
            rejection = "Only a measurement configuration identity is needed for picking.";
            return false;
        }
    }
}
