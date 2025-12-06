using VanillaBuildingExtended.BuildHammer;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VanillaBuildingExtended.src.BuildHammer;
using Vintagestory.API.Config;

namespace VanillaBuildingExtended;

public class VanillaBuildingExtendedModSystem : ModSystem
{
    private BuildPreviewRenderer previewRenderer;
    private BuildHammerInputHandling inputHandler;
    public override double ExecuteOrder()
    {
        return 1;// execute after all the blocks JSON defs are loaded, but before they are finalized, so we can inject our own stuff into the JSON defs.
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("BuildHammer", typeof(ItemBuildHammer));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
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

        api.Input.RegisterHotKey("vbe.CycleSnappingMode_Forward", Lang.Get("vbe-hotkey-cycle-snapping-mode--forward"), GlKeys.V, HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Forward", this.inputHandler.Input_CycleSnappingMode_Forward);

        api.Input.RegisterHotKey("vbe.CycleSnappingMode_Backward", Lang.Get("vbe-hotkey-cycle-snapping-mode--backward"), GlKeys.V, HotkeyType.CharacterControls, shiftPressed: true);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Backward", this.inputHandler.Input_CycleSnappingMode_Backward);

        // Looks like for mouse inputs we have to do things manually via ScreenManager's hotkey manager, because the devs didn't care to expose this function on the IInputAPI interface...
        //ScreenManager.hotkeyManager.RegisterHotKey("vbe.RotateBuildCursor", Lang.Get("vbe-hotkey-rotate-build-cursor"), KeyCombination.MouseStart + (int)EnumMouseButton.Middle, shiftPressed: true, type: HotkeyType.MouseModifiers, insertFirst: true);
        //api.Input.SetHotKeyHandler("vbe.RotateBuildCursor", this.inputHandler.Input_RotateBuildCursor);
    }
}
