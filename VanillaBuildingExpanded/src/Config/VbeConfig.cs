using System;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.Config;

public sealed class VbeConfig
{
    #region Constants
    public const string ConfigFileName = "vanillabuildingexpanded.json";
    private const string ObjectCacheKey = "vanillabuildingexpanded:config";
    #endregion

    #region Build Brush Debugging
    public bool BuildBrushDebugLogging { get; set; } = false;
    public bool BuildBrushDebugHud { get; set; } = false;

    /// <summary>
    /// If true, client-side placement waits until the server acknowledges the latest brush state.
    /// Default is false to preserve existing behavior.
    /// </summary>
    public bool BuildBrushGatePlacementOnAck { get; set; } = false;
    #endregion

    #region Public
    public static VbeConfig Get(in ICoreAPI api)
    {
        if (api.ObjectCache.TryGetValue(ObjectCacheKey, out var cached) && cached is VbeConfig config)
        {
            return config;
        }

        VbeConfig loaded;
        try
        {
            loaded = api.LoadModConfig<VbeConfig>(ConfigFileName) ?? new VbeConfig();
        }
        catch
        {
            loaded = new VbeConfig();
        }

        try
        {
            api.StoreModConfig(loaded, ConfigFileName);
        }
        catch
        {
            // Intentionally ignored. Config should never break mod startup.
        }

        api.ObjectCache[ObjectCacheKey] = loaded;
        return loaded;
    }

    public static bool TryGet(in ICoreAPI api, out VbeConfig config)
    {
        try
        {
            config = Get(api);
            return true;
        }
        catch
        {
            config = new VbeConfig();
            return false;
        }
    }
    #endregion
}
