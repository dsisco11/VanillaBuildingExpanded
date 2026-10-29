using System;
using System.Numerics;
using System.Runtime.Intrinsics;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Explicit positioning composition with independently replaceable visual and interaction contracts.</summary>
public sealed class ComposedPositionGizmo : HandleGizmo<GizmoPosition>
{
    /// <summary>Optional conventional identity for the camera-plane control.</summary>
    public static readonly GizmoHandleId Center = new(0);
    /// <summary>Optional conventional identity for the world-X control.</summary>
    public static readonly GizmoHandleId X = new(1);
    /// <summary>Optional conventional identity for the world-Y control.</summary>
    public static readonly GizmoHandleId Y = new(2);
    /// <summary>Optional conventional identity for the world-Z control.</summary>
    public static readonly GizmoHandleId Z = new(3);

    /// <inheritdoc />
    public override int Dimension => Value.Dimension;

    /// <summary>Creates camera-plane and world-axis controls with existing visuals and transaction ownership.</summary>
    /// <param name="initialValue">The initial continuous world position.</param>
    /// <param name="draw">The shared draw/pick snapshot sink.</param>
    /// <param name="editState">Builds the borrowed owner transaction adapter, once for this gizmo.</param>
    /// <param name="constrain">Optional existing position validation/snapping delegate.</param>
    /// <param name="marker">The endpoint circle or whole-area diamond.</param>
    /// <param name="centerId">Optional arbitrary center identity.</param>
    /// <param name="xId">Optional arbitrary X identity.</param>
    /// <param name="yId">Optional arbitrary Y identity.</param>
    /// <param name="zId">Optional arbitrary Z identity.</param>
    /// <exception cref="ArgumentNullException">A required callback or its edit-state result is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The marker is not a circle or diamond.</exception>
    /// <exception cref="ArgumentException">Handle identities are duplicated.</exception>
    public ComposedPositionGizmo(GizmoPosition initialValue, Action<GizmoHandleDraw> draw,
        Func<Gizmo<GizmoPosition>, IGizmoEditStateTarget> editState,
        Func<PositionGizmoEdit, GizmoPosition?>? constrain = null, GizmoShape marker = GizmoShape.Circle,
        GizmoHandleId? centerId = null, GizmoHandleId? xId = null, GizmoHandleId? yId = null,
        GizmoHandleId? zId = null) : base(initialValue, draw)
    {
        ArgumentNullException.ThrowIfNull(editState);
        if (marker is not (GizmoShape.Circle or GizmoShape.Diamond))
            throw new ArgumentOutOfRangeException(nameof(marker));
        GizmoHandleId center = centerId ?? Center, x = xId ?? X,
            y = yId ?? Y, z = zId ?? Z;
        if (center == x || center == y || center == z || x == y || x == z || y == z)
            throw new ArgumentException("Positioning handle identities must be distinct.");
        var edit = editState(this);
        ArgumentNullException.ThrowIfNull(edit);
        var value = new GizmoValueSource<GizmoPosition>(this);
        var plane = new GizmoCameraFrameSource(() => Value);
        var centerHandle = Add(center, marker, 7, new(1, 0.85f, 0.25f, 1), plane);
        centerHandle.InteractionTarget = new GizmoPlaneDragTarget(new HandleFrameSource(centerHandle),
            frame => new GizmoPlanePositionParameterSource(value, frame, center, constrain), edit);
        AddAxis(x, Vector256.Create(1d, 0d, 0d, 0d), new(1, 0.2f, 0.2f, 1));
        AddAxis(y, Vector256.Create(0d, 1d, 0d, 0d), new(0.2f, 1, 0.2f, 1));
        AddAxis(z, Vector256.Create(0d, 0d, 1d, 0d), new(0.25f, 0.5f, 1, 1));

        void AddAxis(GizmoHandleId id, Vector256<double> direction, Vector4 color)
        {
            var frame = new GizmoAxisFrameSource(() => Value, () => direction);
            var handle = Add(id, GizmoShape.Arrow, 32, color, frame);
            handle.InteractionTarget = new GizmoAxisDragTarget(new HandleFrameSource(handle),
                frozen => new GizmoAxisPositionParameterSource(value, frozen, id, constrain), edit);
        }

    }

    /// <summary>Reads the handle's replaceable configuration only at preparation, before the target freezes it.</summary>
    private sealed class HandleFrameSource(GizmoHandle handle) : IGizmoFrameSource
    {
        /// <inheritdoc />
        public GizmoFrame GetFrame(GizmoContext context) => (handle.FrameSource
            ?? throw new InvalidOperationException("Positioning requires a configured frame source.")).GetFrame(context);

        /// <inheritdoc />
        public bool TryGetFrame(GizmoContext context, out GizmoFrame frame) => (handle.FrameSource
            ?? throw new InvalidOperationException("Positioning requires a configured frame source."))
            .TryGetFrame(context, out frame);
    }

    private GizmoHandle Add(GizmoHandleId id, GizmoShape shape, double size, Vector4 color,
        IGizmoFrameSource frame)
    {
        var handle = new GizmoHandle(id, new()
        {
            Shape = shape, Size = size, Color = color, LineWidth = 1.6f,
            LabelTextColor = Vector4.One, LabelBackgroundColor = new(0, 0, 0, 0.65f),
            LabelFontFamily = GuiStyle.StandardFontName, LabelFontSize = 14, LabelBold = true, LabelPadding = 3
        });
        var highlight = new AppearenceProperties { Color = color with { W = 1 }, LineWidth = 2.8f };
        handle.SetAppearance(EAppearenceState.Hover, highlight);
        handle.SetAppearance(EAppearenceState.Selected, highlight);
        handle.FrameSource = frame;
        AddHandle(handle);
        handle.HitTarget = new GizmoGeometryHitTarget(this, GetGeometrySource(handle));
        return handle;
    }
}
