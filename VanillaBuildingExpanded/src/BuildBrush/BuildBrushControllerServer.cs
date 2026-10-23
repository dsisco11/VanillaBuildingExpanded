using System;

using VanillaBuildingExpanded.Networking;

using Vintagestory.API.MathTools;

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

    #region Public API
    /// <summary>Owns the supplied session brush and subscribes to preview lifecycle events.</summary>
    public BuildBrushControllerServer(BuildBrushInstance brush)
    {
        this.brush = brush;
        brush.OnActivationChanged += Brush_OnActivationChanged;
        brush.OnDimensionDirty += Brush_OnDimensionDirty;
    }
    /// <summary>Applies accepted network state in the established brush update order.</summary>
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

        // Refresh the held source before applying its orientation, including on immediate entry.
        brush.TryUpdateBlockId();
        brush.IsActive = State.IsActive;
        brush.Snapping = State.Snapping;
        brush.OrientationIndex = State.OrientationIndex;
        if (!State.IsActive)
            brush.ExitBuildMode();
        else
            brush.Position = brush.IsValidPlacementBlock(brush.BlockUntransformed) ? State.Position : null;
        brush.LastAppliedSeq = State.LastAppliedSeq;
    }

    /// <summary>Clears mode on death or respawn while retaining the sequence watermark and preferences.</summary>
    public void ResetBuildMode()
    {
        brush.ExitBuildMode();
        State.Apply(false, brush.OrientationIndex, null, null, brush.Snapping,
            brush.BlockId, brush.LastAppliedSeq);
    }

    /// <summary>Destroys the owned brush's preview resources.</summary>
    public void Destroy()
    {
        brush.DestroyDimension();
    }
    /// <summary>Removes the controller's brush-event subscriptions.</summary>
    public void Dispose()
    {
        brush.OnActivationChanged -= Brush_OnActivationChanged;
        brush.OnDimensionDirty -= Brush_OnDimensionDirty;
    }
    #endregion

    #region Private
    /// <summary>Creates or destroys preview resources when brush activation changes.</summary>
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

    /// <summary>Updates preview bounds and increments the renderer's dirty counter after dimension changes.</summary>
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

}
