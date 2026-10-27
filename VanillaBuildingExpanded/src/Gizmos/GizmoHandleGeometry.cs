using System;
using System.Runtime.Intrinsics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Immutable spatial data shared by drawing and default picking for one actual owned handle.</summary>
public sealed class GizmoHandleGeometry
{
    private readonly Vector256<double>[] screenVertices;
    private readonly Vector256<double> screenOrigin;

    /// <summary>Gets the owner at snapshot construction, not a later transfer destination.</summary>
    public Gizmo Owner { get; }
    /// <summary>Gets the persistent handle whose shape was resolved.</summary>
    public GizmoHandle Handle { get; }
    /// <summary>Gets the exact shared camera/dimension context.</summary>
    public GizmoContext Context { get; }
    /// <summary>Gets the validated logical frame used to derive this geometry.</summary>
    public GizmoFrame Frame { get; }
    /// <summary>Gets the resolved primitive shape.</summary>
    public GizmoShape Shape { get; }
    /// <summary>Gets the nominal logical-pixel length or radius.</summary>
    public double Size { get; }
    /// <summary>Gets the resolved world-space length or radius.</summary>
    public double Extent { get; }
    /// <summary>Gets the scaled primitive X basis.</summary>
    public Vector256<double> BasisX { get; }
    /// <summary>Gets the scaled primitive Y basis.</summary>
    public Vector256<double> BasisY { get; }
    /// <summary>Gets the scaled primitive Z basis.</summary>
    public Vector256<double> BasisZ { get; }
    /// <summary>Gets the label anchor: arrow tip or marker origin.</summary>
    public Vector256<double> LabelAnchor => Shape == GizmoShape.Arrow
        ? Frame.Origin + BasisX : Frame.Origin;

    private GizmoHandleGeometry(Gizmo owner, GizmoHandle handle, GizmoContext context, GizmoFrame frame,
        GizmoShape shape, double size, double extent, Vector256<double> x, Vector256<double> y,
        Vector256<double> z, Vector256<double>[] screenVertices, Vector256<double> screenOrigin)
    {
        Owner = owner;
        Handle = handle;
        Context = context;
        Frame = frame;
        Shape = shape;
        Size = size;
        Extent = extent;
        BasisX = x;
        BasisY = y;
        BasisZ = z;
        this.screenVertices = screenVertices;
        this.screenOrigin = screenOrigin;
    }

    /// <summary>Transforms a shared primitive vertex using double-precision local-world coordinates.</summary>
    /// <param name="point">A primitive-space vertex.</param>
    /// <returns>The world-space point drawn and picked by this snapshot.</returns>
    public Vector256<double> Transform(Vector256<double> point) =>
        Frame.Origin + BasisX * point[0] + BasisY * point[1] + BasisZ * point[2];

    /// <summary>Builds only fully visible geometry; clipped shapes have neither drawing nor picking data.</summary>
    internal static GizmoHandleGeometry? Create(Gizmo owner, GizmoHandle handle, GizmoContext context,
        GizmoFrame frame, ResolvedAppearenceProperties appearance)
    {
        var camera = context.Camera ?? throw new InvalidOperationException("Handle geometry requires a perspective camera.");
        if (!camera.TryProject(frame.Origin, out var origin)) return null;
        var (_, _, units) = camera.Billboard(frame.Origin);
        double extent = Math.Clamp(64 * camera.GuiScale * units, 0.125, 8) * (appearance.Size / 64);
        if (!double.IsFinite(extent) || extent <= 0)
            throw new InvalidOperationException("The resolved size cannot produce a finite positive world extent.");
        var reference = Math.Abs(frame.Direction[0]) < 0.9
            ? Vector256.Create(1d, 0d, 0d, 0d) : Vector256.Create(0d, 1d, 0d, 0d);
        var tangent = frame.TangentX ?? GizmoFrame.Cross(frame.Direction, reference);
        tangent /= Math.Sqrt(Vector256.Dot(tangent, tangent));
        var other = frame.TangentY ?? GizmoFrame.Cross(frame.Direction, tangent);
        var x = (appearance.Shape == GizmoShape.Arrow ? frame.Direction : tangent) * extent;
        var y = (appearance.Shape == GizmoShape.Arrow ? tangent : other) * extent;
        var z = appearance.Shape == GizmoShape.Arrow ? other * extent : Vector256<double>.Zero;
        var vertices = GizmoShapes.Vertices(appearance.Shape);
        var projected = new Vector256<double>[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var point = frame.Origin + x * vertices[i][0] + y * vertices[i][1] + z * vertices[i][2];
            if (!camera.TryProject(point, out projected[i])) return null;
        }
        return new(owner, handle, context, frame, appearance.Shape, appearance.Size, extent,
            x, y, z, projected, origin);
    }

    /// <summary>Picks the same projected primitive with explicit framebuffer-pixel padding.</summary>
    internal double? HitDistance(GizmoPointer pointer, double padding)
    {
        var camera = Context.Camera!;
        var probe = pointer.FarPoint is { } far
            ? pointer.Ray.Origin + (far - pointer.Ray.Origin) * 0.5
            : pointer.Ray.Origin + pointer.Ray.Direction;
        if (!camera.TryProject(probe, out var screen)) return null;
        var indices = GizmoShapes.Indices(Shape);
        var vertices = GizmoShapes.Vertices(Shape);
        bool filled = Shape != GizmoShape.Arrow && Contains(screen);
        if (Shape == GizmoShape.Circle)
        {
            // Preserve the existing round marker's analytic tolerance when it is screen-facing.
            camera.TryProject(Frame.Origin + BasisX, out var edgeX);
            camera.TryProject(Frame.Origin + BasisY, out var edgeY);
            double radius = GizmoScreenPicking.Distance2D(screenOrigin, edgeX);
            double otherRadius = GizmoScreenPicking.Distance2D(screenOrigin, edgeY);
            if (Math.Abs(Vector256.Dot(Frame.Direction, camera.ViewDirection)) > 1 - 1e-8
                && Math.Abs(radius - otherRadius) <= 1e-8)
                filled = GizmoScreenPicking.Distance2D(screen, screenOrigin) <= radius + padding;
        }
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < indices.Length; i += 2)
        {
            int ai = indices[i], bi = indices[i + 1];
            var a = screenVertices[ai];
            var b = screenVertices[bi];
            if (GizmoScreenPicking.SegmentDistance(screen, a, b, out double t) > padding) continue;
            if (Shape != GizmoShape.Arrow) { filled = true; break; }
            double worldT = t * a[3] / ((1 - t) * b[3] + t * a[3]);
            var worldA = Transform(vertices[ai]);
            var worldB = Transform(vertices[bi]);
            double distance = Vector256.Dot(worldA + (worldB - worldA) * worldT - pointer.Ray.Origin,
                pointer.Ray.Direction);
            if (distance > 0) nearest = Math.Min(nearest, distance);
        }
        if (Shape != GizmoShape.Arrow && filled)
            nearest = Vector256.Dot(Frame.Origin - pointer.Ray.Origin, pointer.Ray.Direction);
        return double.IsFinite(nearest) && nearest > 0 ? nearest : null;
    }

    /// <summary>Tests the interior of a projected planar marker, including tilted planes.</summary>
    private bool Contains(Vector256<double> point)
    {
        bool inside = false;
        for (int i = 0, j = screenVertices.Length - 1; i < screenVertices.Length; j = i++)
        {
            var a = screenVertices[i];
            var b = screenVertices[j];
            if ((a[1] > point[1]) != (b[1] > point[1])
                && point[0] < (b[0] - a[0]) * (point[1] - a[1]) / (b[1] - a[1]) + a[0])
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>Immutable appearance plus reusable spatial data, ready for a renderer and its default hit target.</summary>
public sealed record GizmoHandleDraw
{
    /// <summary>Gets the exact geometry to draw and pick.</summary>
    public GizmoHandleGeometry Geometry { get; }
    /// <summary>Gets the complete appearance resolved with that geometry's shape and size.</summary>
    public ResolvedAppearenceProperties Appearance { get; }

    internal GizmoHandleDraw(GizmoHandleGeometry geometry, ResolvedAppearenceProperties appearance)
    {
        Geometry = geometry;
        Appearance = appearance;
    }
}

/// <summary>Publishes per-frame draw snapshots; color/style updates reuse immutable spatial data.</summary>
public sealed class GizmoGeometrySource
{
    private (Gizmo Owner, GizmoContext Context, GizmoFrame Frame, GizmoShape Shape, double Size)? key;
    private GizmoHandleGeometry? geometry;

    /// <summary>Gets the borrowed persistent handle.</summary>
    public GizmoHandle Handle { get; }
    /// <summary>Gets the last published drawing snapshot, or null when omitted.</summary>
    public GizmoHandleDraw? Snapshot { get; private set; }

    /// <summary>Creates a bounded, single-handle spatial cache without subscriptions or native resources.</summary>
    /// <param name="handle">The handle whose frame and appearance are resolved during drawing.</param>
    /// <exception cref="ArgumentNullException">The handle is null.</exception>
    public GizmoGeometrySource(GizmoHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        Handle = handle;
    }

    /// <summary>Resolves and publishes geometry for drawing; disabled but visible handles still produce shapes.</summary>
    /// <param name="context">The exact perspective context also required for picking.</param>
    /// <returns>A draw snapshot, or null for detached, hidden, disposed, ineligible-owner or clipped shapes.</returns>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="InvalidOperationException">Required frame/camera data is absent or invalid.</exception>
    public GizmoHandleDraw? GetSnapshot(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Snapshot = null;
        var owner = Handle.Owner;
        if (Handle.IsDisposed || !Handle.Visible || owner is null || !owner.IsEligible(context.Dimension)) return null;
        var source = Handle.FrameSource ?? throw new InvalidOperationException("Visible handles require a frame source.");
        var frame = source.GetFrame(context);
        frame.Validate(context);
        if (Handle.IsDisposed || !Handle.Visible || !ReferenceEquals(Handle.Owner, owner)
            || !owner.IsEligible(context.Dimension)) return null;
        if (!ReferenceEquals(source, Handle.FrameSource))
            throw new InvalidOperationException("The frame source changed while resolving geometry.");
        var appearance = Handle.Appearance;
        if (key is not { } previous || !ReferenceEquals(previous.Owner, owner)
            || !ReferenceEquals(previous.Context, context) || previous.Frame != frame
            || previous.Shape != appearance.Shape || previous.Size != appearance.Size)
        {
            geometry = GizmoHandleGeometry.Create(owner, Handle, context, frame, appearance);
            key = (owner, context, frame, appearance.Shape, appearance.Size);
        }
        return Snapshot = geometry is null ? null : new(geometry, appearance);
    }
}
