using HarmonyLib;

using VanillaBuildingExpanded.BuildHammer;
using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded;

public class VanillaBuildingExpandedModSystem : ModSystem
{
    #region Constants
    public static readonly string BuildHammerItemCode = "vanillabuildingexpanded:buildhammer";
    public static readonly string BuildHammerGuiDialogId = "vanillabuildingexpanded:buildhammer-gui";
    #endregion

    #region Fields
    internal Harmony? harmony;
    #endregion

    #region Lifecycle
    public override void Dispose()
    {
        harmony?.UnpatchAll(Mod.Info.ModID);
    }

    public override void StartPre(ICoreAPI api)
    {
        api.Network
            .RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType(typeof(Packet_SetBuildBrush))
            .RegisterMessageType(typeof(Packet_BuildBrushAck));
    }

    public override void Start(ICoreAPI api)
    {
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
