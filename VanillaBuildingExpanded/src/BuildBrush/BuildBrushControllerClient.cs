using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>
/// Client-side brush controller: owns local state + integrates with input/ticks without side-effects in the core state.
/// </summary>
public sealed class BuildBrushControllerClient
{
    #region Fields
    private readonly ICoreClientAPI api;
    private readonly Func<BuildBrushInstance> createBrush;
    private BuildBrushInstance? brush;
    #endregion

    #region Properties
    public BuildBrushState State { get; } = new();

    /// <summary>Allows brush controls for a supported held block, even before selecting a target.</summary>
    public bool CanUseBrushControls => GetBrush().IsActive
        && GetBrush().IsValidPlacementBlock(GetBrush().BlockUntransformed);

    #endregion

    #region Public API
    #region Construction and brush acquisition

    /// <summary>Retains client services and a factory that creates the local brush only when first needed.</summary>
    public BuildBrushControllerClient(ICoreClientAPI api, Func<BuildBrushInstance> createBrush)
    {
        this.api = api;
        this.createBrush = createBrush;
    }
    #endregion

    #region Brush updates
    /// <summary>Gets the session brush, constructing it once through the game-facing factory.</summary>
    public BuildBrushInstance GetBrush()
    {
        brush ??= createBrush();
        return brush;
    }

    /// <summary>Updates brush placement from the caller's current selection.</summary>
    public bool TryUpdate(BlockSelection? selection)
    {
        var localBrush = GetBrush();
        localBrush.TryUpdateBlockId();
        // A suspended brush must not retain a held modifier when the player resumes building.
        if (!CanUseBrushControls)
            localBrush.Snapping &= ~EBuildBrushSnapping.ApplyFaceNormalOffset;
        return localBrush.TryUpdate(CanUseBrushControls ? selection : null);
    }

    internal BrushSnappingPresentation? GetSnappingPresentation(BlockSelection? selection, bool supportedGeometry) =>
        GetBrush().GetSnappingPresentation(selection, supportedGeometry);

    /// <summary>Rechecks the resolved preview placement against the supplied selection.</summary>
    public void TryUpdatePlacementValidity(BlockSelection selection)
    {
        GetBrush().TryUpdatePlacementValidity(selection);
    }

    /// <summary>Cycles the local brush orientation in the requested direction.</summary>
    public bool RotateCursor(EModeCycleDirection direction)
    {
        var localBrush = GetBrush();
        if (!CanUseBrushControls || !localBrush.CycleOrientation(direction))
            return false;

        api.World.PlaySound(new SoundAttributes(new AssetLocation("game:sounds/menubutton"), false));
        return true;
    }

    /// <summary>Cycles snapping flags and displays the resulting mode.</summary>
    public bool CycleSnappingMode(EModeCycleDirection direction)
    {
        if (!CanUseBrushControls)
            return false;

        var localBrush = GetBrush();
        int d = (int)direction;
        EBuildBrushSnapping modeSearchFilter = EBuildBrushSnapping.None | EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical;
        int snappingModeIndex = BuildBrushInstance.BrushSnappingModes.IndexOf(localBrush.Snapping & modeSearchFilter);
        snappingModeIndex = (snappingModeIndex + d + BuildBrushInstance.BrushSnappingModes.Length) % BuildBrushInstance.BrushSnappingModes.Length;
        // Cycling preferences must not release a face-offset key that is still held.
        localBrush.Snapping = BuildBrushInstance.BrushSnappingModes[snappingModeIndex]
            | (localBrush.Snapping & EBuildBrushSnapping.ApplyFaceNormalOffset);
        DisplaySnappingModeNotice(localBrush);
        return true;
    }

    /// <summary>Applies a held face offset only while usable; releases always clear any previous offset.</summary>
    public bool SetFaceOffset(bool held)
    {
        var localBrush = GetBrush();
        bool usable = CanUseBrushControls;
        if (held && usable)
            localBrush.Snapping |= EBuildBrushSnapping.ApplyFaceNormalOffset;
        else
            localBrush.Snapping &= ~EBuildBrushSnapping.ApplyFaceNormalOffset;
        return usable;
    }

    #endregion

    #region Mode and placement events

    /// <summary>Raised synchronously after refreshed mode state is ready to send.</summary>
    public event Action? ModeChanged;

    /// <summary>Changes mode and publishes the transition immediately, even without a target.</summary>
    public bool SetBuildMode(bool enabled, BlockSelection? selection)
    {
        var localBrush = GetBrush();
        if (localBrush.IsActive == enabled)
            return false;

        // Complete local entry/cleanup before notifying the transport boundary.
        if (enabled)
            localBrush.EnterBuildMode(selection);
        else
            localBrush.ExitBuildMode();

        SyncStateFromBrush();
        ModeChanged?.Invoke();
        return true;
    }

    /// <summary>Refreshes the active item and placement after prediction or a block-change notification.</summary>
    public void OnBlockPlacedClient(BlockSelection? selection)
    {
        var localBrush = GetBrush();
        localBrush.OnBlockPlacedServer();
        localBrush.TryUpdate(selection);
    }

    #endregion

    #region State publication

    /// <summary>Copies current brush values into the state used for outgoing updates.</summary>
    public void SyncStateFromBrush()
    {
        var localBrush = GetBrush();
        State.Apply(
            localBrush.IsActive,
            localBrush.OrientationIndex,
            localBrush.Selection,
            localBrush.Position,
            localBrush.Snapping,
            localBrush.BlockId
        );
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Shows the localized snapping-mode notice when mod information is available.</summary>
    private void DisplaySnappingModeNotice(BuildBrushInstance localBrush)
    {
        ModInfo? modInfo = api.ModLoader?.GetModSystem<VanillaBuildingExpandedModSystem>()?.Mod.Info;
        if (modInfo is null)
        {
            return;
        }

        api.TriggerIngameError(
            this,
            $"{modInfo.ModID}:brush-snapping-mode-changed",
            Lang.Get($"{modInfo.ModID}:brush-snapping-mode-changed-{localBrush.Snapping.GetCode()}")
        );
    }
    #endregion
}
