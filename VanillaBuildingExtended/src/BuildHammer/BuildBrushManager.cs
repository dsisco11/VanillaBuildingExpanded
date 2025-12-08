using System;
using System.Collections.Generic;

using Vintagestory.API.Common;

namespace VanillaBuildingExtended.BuildHammer;

public abstract class BuildBrushManager : IDisposable
{
    #region Constants
    internal static readonly string NetworkChannelId = "vanillabuildingextended";
    #endregion

    #region Fields
    protected readonly ICoreAPI coreApi;
    #endregion

    #region Properties
    public readonly Dictionary<int, BuildBrushInstance> Brushes = [];
    #endregion

    #region Lifecycle
    public BuildBrushManager(ICoreAPI api)
    {
        this.coreApi = api;
    }
    #endregion

    public abstract BuildBrushInstance? GetBrush(in IPlayer? player);

    public virtual void Dispose()
    {
    }

    /// <summary>
    /// Checks if the player is currently holding a build hammer in their offhand.
    /// </summary>
    public bool HasHammer(IPlayer player)
    {
        return player?.InventoryManager?.OffhandHotbarSlot?.Itemstack?.Collectible is ItemBuildHammer;
    }
}
