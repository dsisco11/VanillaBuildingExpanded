using HarmonyLib;

using VanillaBuildingExtended.BuildHammer;
using VanillaBuildingExtended.Networking;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
    public static BuildBrushManager_Client buildBrushManager_Client = null!;
    public static BuildBrushManager_Server buildBrushManager_Server = null!;
    #endregion

    #region Lifecycle
    public override void Dispose()
    {
        base.Dispose();
        buildBrushManager_Client?.Dispose();
        buildBrushManager_Client = null!;
        buildBrushManager_Server?.Dispose();
        buildBrushManager_Server = null!;
        harmony?.UnpatchAll(Mod.Info.ModID);
    }

    public override void StartPre(ICoreAPI api)
    {
        api.Network
            .RegisterChannel(BuildBrushManager.NetworkChannelId)
            .RegisterMessageType(typeof(Packet_SetBuildBrush));

        switch (api.Side)
        {
            case EnumAppSide.Client:
                {
                    buildBrushManager_Client = new BuildBrushManager_Client(api as ICoreClientAPI);
                    break;
                }
            case EnumAppSide.Server:
                {
                    buildBrushManager_Server = new BuildBrushManager_Server(api as ICoreServerAPI);
                    break;
                }
        }
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

    public override void StartClientSide(ICoreClientAPI api)
    {
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
    }
    #endregion
}
