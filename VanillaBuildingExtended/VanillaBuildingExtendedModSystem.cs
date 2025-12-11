using HarmonyLib;

using VanillaBuildingExtended.BuildHammer;
using VanillaBuildingExtended.Networking;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VanillaBuildingExtended;

public class VanillaBuildingExtendedModSystem : ModSystem
{
    #region Constants
    public static readonly string BuildHammerItemCode = "vanillabuildingextended:buildhammer";
    public static readonly string BuildHammerGuiDialogId = "vanillabuildingextended:buildhammer-gui";
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
            .RegisterChannel(BuildBrushManager.NetworkChannelId)
            .RegisterMessageType(typeof(Packet_SetBuildBrush));
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("BuildHammer", typeof(ItemBuildHammer));

        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
        }
    }
    #endregion
}
