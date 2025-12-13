using System;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildHammer;

public abstract class BuildBrushManager : IDisposable
{
    #region Constants
    internal static readonly string NetworkChannelId = "vanillabuildingextended";
    #endregion

    #region Fields
    protected readonly ICoreAPI coreApi;
    #endregion

    #region Properties
    #endregion

    #region Accessors
    protected ILogger Logger => coreApi.Logger;
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
}
