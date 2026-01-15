using System;

using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Server-side brush controller: owns per-player state and side-effectful preview lifecycle.
/// </summary>
public sealed class BuildBrushControllerServer : IDisposable
{
    #region Fields
    private readonly BuildBrushInstance brush;
    #endregion

    #region Properties
    public BuildBrushState State { get; } = new();
    public BuildBrushInstance Brush => brush;
    #endregion

    #region Constructor
    public BuildBrushControllerServer(ICoreServerAPI api, IServerPlayer player)
    {
        brush = new BuildBrushInstance(player, api.World);
        brush.OnActivationChanged += Brush_OnActivationChanged;
        brush.OnDimensionDirty += Brush_OnDimensionDirty;
    }
    #endregion

    #region Public
    public void ApplyState(Packet_SetBuildBrush packet)
    {
        State.Apply(
            packet.isActive,
            packet.orientationIndex,
            selection: null,
            packet.position,
            packet.snapping,
            lastAppliedSeq: packet.seq
        );

        brush.IsActive = State.IsActive;
        brush.Snapping = State.Snapping;
        brush.OrientationIndex = State.OrientationIndex;
        brush.Position = State.Position;
        brush.LastAppliedSeq = State.LastAppliedSeq;
    }

    public void Destroy()
    {
        brush.DestroyDimension();
    }
    #endregion

    #region Events
    private void Brush_OnActivationChanged(object? sender, BrushActivationChangedEventArgs e)
    {
        if (e.IsActive)
        {
            brush.ActivateDimension();
        }
        else
        {
            brush.DeactivateDimension();
        }
    }

    private void Brush_OnDimensionDirty(object? sender, DimensionDirtyEventArgs e)
    {
        if (brush.Entity is null)
            return;

        if (brush.Dimension is not null && brush.Dimension.GetActiveBounds(out BlockPos min, out BlockPos max))
        {
            brush.Entity.SetPreviewBounds(min, max);
        }
        else
        {
            brush.Entity.ClearPreviewBounds();
        }

        brush.Entity.IncrementBrushDirtyCounter();
    }
    #endregion

    #region IDisposable
    public void Dispose()
    {
        brush.OnActivationChanged -= Brush_OnActivationChanged;
        brush.OnDimensionDirty -= Brush_OnDimensionDirty;
    }
    #endregion
}
