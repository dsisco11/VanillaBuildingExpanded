using HarmonyLib;

using VanillaBuildingExtended.BuildHammer;
using VanillaBuildingExtended.Networking;
using VanillaBuildingExtended.src.BuildHammer;

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
    private BuildPreviewRenderer previewRenderer;
    private BuildHammerInputHandling inputHandler;
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
        this.previewRenderer = new BuildPreviewRenderer(api);
        this.inputHandler = new BuildHammerInputHandling(api);
        api.Event.RegisterRenderer(previewRenderer, EnumRenderStage.Opaque, "build_preview");

        api.Input.InWorldAction += (EnumEntityAction action, bool on, ref EnumHandling handled) => {
            inputHandler.InWorldAction(api, action, on, ref handled);
        };

        api.Input.RegisterHotKey("vbe.RotateBuildCursorForward", Lang.Get("vbe-hotkey-rotate-build-cursor--forward"), GlKeys.R, HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler("vbe.RotateBuildCursorForward", this.inputHandler.Input_RotateBuildCursor_Forward);

        api.Input.RegisterHotKey("vbe.RotateBuildCursorBackward", Lang.Get("vbe-hotkey-rotate-build-cursor--backward"), GlKeys.R, HotkeyType.CharacterControls, shiftPressed: true);
        api.Input.SetHotKeyHandler("vbe.RotateBuildCursorBackward", this.inputHandler.Input_RotateBuildCursor_Backward);

        // Looks like for mouse inputs we have to do things manually via ScreenManager's hotkey manager, because the devs didn't care to expose this function on the IInputAPI interface...
        api.Input.RegisterHotKeyFirst("vbe.CycleSnappingMode_Forward", Lang.Get("vbe-hotkey-cycle-snapping-mode--forward"), (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle), HotkeyType.MouseModifiers, shiftPressed: false);
        //ScreenManager.hotkeyManager.RegisterHotKey("vbe.CycleSnappingMode_Forward", Lang.Get("vbe-hotkey-cycle-snapping-mode--forward"), KeyCombination.MouseStart + (int)EnumMouseButton.Middle, shiftPressed: false, type: HotkeyType.MouseModifiers, insertFirst: true);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Forward", this.inputHandler.Input_CycleSnappingMode_Forward);

        api.Input.RegisterHotKeyFirst("vbe.CycleSnappingMode_Backward", Lang.Get("vbe-hotkey-cycle-snapping-mode--backward"), (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle), HotkeyType.MouseModifiers, shiftPressed: true);
        //ScreenManager.hotkeyManager.RegisterHotKey("vbe.CycleSnappingMode_Backward", Lang.Get("vbe-hotkey-cycle-snapping-mode--backward"), KeyCombination.MouseStart + (int)EnumMouseButton.Middle, shiftPressed: true, type: HotkeyType.MouseModifiers, insertFirst: true);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Backward", this.inputHandler.Input_CycleSnappingMode_Forward);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        buildBrushManager = new BuildBrushManager_Server(api);
    }
    #endregion
}
