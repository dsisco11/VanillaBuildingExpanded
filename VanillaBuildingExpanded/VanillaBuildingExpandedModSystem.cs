using HarmonyLib;

using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded;

/// <summary>Registers build-mode services, preview types and saved-item compatibility.</summary>
public class VanillaBuildingExpandedModSystem : ModSystem
{
    #region Fields
    internal Harmony? harmony;
    #endregion

    #region Public API
    /// <summary>Removes native placement patches owned by this mod.</summary>
    public override void Dispose()
    {
        harmony?.UnpatchAll(Mod.Info.ModID);
    }

    /// <summary>Registers the ordered brush update and acknowledgment channel.</summary>
    public override void StartPre(ICoreAPI api)
    {
        api.Network
            .RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType(typeof(Packet_SetBuildBrush))
            .RegisterMessageType(typeof(Packet_BuildBrushAck));
    }

    /// <summary>Registers collectible compatibility, the preview entity and native placement patches.</summary>
    public override void Start(ICoreAPI api)
    {
        // Preserve the saved collectible class and codes without enabling equipment-driven behavior.
        api.RegisterItemClass("BuildHammer", typeof(ItemBuildHammer));

        // Register entity class (must be in Start, before entity types are loaded)
        api.RegisterEntity(BuildBrushEntity.ClassName, typeof(BuildBrushEntity));

        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
        }
    }
    #endregion
}
