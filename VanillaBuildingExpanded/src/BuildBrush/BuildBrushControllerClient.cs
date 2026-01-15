using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Client-side brush controller: owns local state + integrates with input/ticks without side-effects in the core state.
/// </summary>
public sealed class BuildBrushControllerClient
{
    #region Fields
    private readonly ICoreClientAPI api;
    private BuildBrushInstance? brush;
    #endregion

    #region Properties
    public BuildBrushState State { get; } = new();
    #endregion

    #region Constructor
    public BuildBrushControllerClient(ICoreClientAPI api)
    {
        this.api = api;
    }
    #endregion

    #region Public
    public BuildBrushInstance GetBrush(IPlayer player)
    {
        brush ??= new BuildBrushInstance(player, api.World);
        return brush;
    }

    public bool TryUpdate(BlockSelection? selection)
    {
        return GetBrush(api.World.Player).TryUpdate(selection);
    }

    public void TryUpdatePlacementValidity(BlockSelection selection)
    {
        GetBrush(api.World.Player).TryUpdatePlacementValidity(selection);
    }

    public bool RotateCursor(EModeCycleDirection direction)
    {
        var localBrush = GetBrush(api.World.Player);
        return localBrush.CycleOrientation(direction);
    }

    public void CycleSnappingMode(EModeCycleDirection direction)
    {
        var localBrush = GetBrush(api.World.Player);
        int d = (int)direction;
        EBuildBrushSnapping modeSearchFilter = EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical;
        int snappingModeIndex = BuildBrushInstance.BrushSnappingModes.IndexOf(localBrush.Snapping & modeSearchFilter);
        snappingModeIndex = (snappingModeIndex + d + BuildBrushInstance.BrushSnappingModes.Length) % BuildBrushInstance.BrushSnappingModes.Length;
        localBrush.Snapping = BuildBrushInstance.BrushSnappingModes[snappingModeIndex];
        DisplaySnappingModeNotice(localBrush);
    }

    public void OnEquipped()
    {
        var localBrush = GetBrush(api.World.Player);
        localBrush.OnEquipped();
        DisplaySnappingModeNotice(localBrush);
    }

    public void OnUnequipped()
    {
        var localBrush = GetBrush(api.World.Player);
        localBrush.OnUnequipped();
    }

    public void OnBlockPlacedClient()
    {
        var localBrush = GetBrush(api.World.Player);
        localBrush.OnBlockPlacedServer();
        localBrush.TryUpdate();
    }

    public void SyncStateFromBrush()
    {
        var localBrush = GetBrush(api.World.Player);
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

    #region Private
    private void DisplaySnappingModeNotice(BuildBrushInstance localBrush)
    {
        ModInfo? modInfo = api.ModLoader.GetModSystem<VanillaBuildingExpandedModSystem>()?.Mod.Info;
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
