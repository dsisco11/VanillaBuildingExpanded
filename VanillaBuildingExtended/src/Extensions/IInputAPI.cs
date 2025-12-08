using Vintagestory.API.Client;

namespace VanillaBuildingExtended;
internal static class IInputAPIExtensions
{
    public static void TryRegisterHotKey(this IInputAPI inputApi, string hotkeyCode, string name, GlKeys key, HotkeyType type = HotkeyType.CharacterControls, bool altPressed = false, bool ctrlPressed = false, bool shiftPressed = false)
    {
        if (!inputApi.HotKeys.ContainsKey(hotkeyCode))
        {
            inputApi.RegisterHotKey(hotkeyCode, name, key, type, altPressed, ctrlPressed, shiftPressed);
        }
    }

    public static void TryRegisterHotKeyFirst(this IInputAPI inputApi, string hotkeyCode, string name, GlKeys key, HotkeyType type = HotkeyType.CharacterControls, bool altPressed = false, bool ctrlPressed = false, bool shiftPressed = false)
    {
        if (!inputApi.HotKeys.ContainsKey(hotkeyCode))
        {
            inputApi.RegisterHotKeyFirst(hotkeyCode, name, key, type, altPressed, ctrlPressed, shiftPressed);
        }
    }
}
