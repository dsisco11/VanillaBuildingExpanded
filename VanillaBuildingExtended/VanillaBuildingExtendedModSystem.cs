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

    #region Properties
    public static BuildBrushManager buildBrushManager = null!;
    #endregion

    #region Lifecycle
    public override void Dispose()
    {
        base.Dispose();
        harmony?.UnpatchAll(Mod.Info.ModID);
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("BuildHammer", typeof(ItemBuildHammer));

        api.Network
            .RegisterChannel(BuildBrushManager.NetworkChannelId)
            .RegisterMessageType(typeof(Packet_SetBuildBrush));

        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        buildBrushManager = new BuildBrushManager_Client(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        buildBrushManager = new BuildBrushManager_Server(api);
    }
    #endregion
}
