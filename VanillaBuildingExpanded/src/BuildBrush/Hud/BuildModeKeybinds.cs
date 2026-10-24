using System;
using System.Collections.Immutable;

using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal readonly record struct BuildModeKeybindHint(
    string Action, ImmutableArray<string> Keys, ImmutableArray<string> AlternateKeys);

internal static class BuildModeKeybinds
{
    private static readonly ImmutableArray<(string Label, string Code, string? AlternateCode)> Controls = [
        ("build-info-toggle-action", "vbe.ToggleBuildMode", null),
        ("build-info-rotate-action", "vbe.RotateBuildCursor_Forward", "vbe.RotateBuildCursor_Backward"),
        ("build-info-snapping-action", "vbe.CycleSnappingMode_Forward", "vbe.CycleSnappingMode_Backward"),
        ("build-info-offset-action", "vbe.ToggleFaceOffsetPlacement", null)
    ];

    public static BuildModeKeybindHint[] Read(IInputAPI input)
    {
        var hints = new BuildModeKeybindHint[Controls.Length + 1];
        for (int i = 0; i < Controls.Length; i++)
        {
            var control = Controls[i];
            hints[i] = new BuildModeKeybindHint(
                Lang.Get($"vanillabuildingexpanded:{control.Label}"),
                ReadKeys(input, control.Code),
                control.AlternateCode is string alternateCode ? ReadKeys(input, alternateCode) : []);
        }
        hints[^1] = new BuildModeKeybindHint(
            Lang.Get("vanillabuildingexpanded:build-info-wheel-action"),
            [GlKeyNames.ToString(GlKeys.LShift), Lang.Get("vanillabuildingexpanded:build-info-mouse-wheel")], []);
        return hints;
    }

    private static ImmutableArray<string> ReadKeys(IInputAPI input, string code)
    {
        var hotkey = input.GetHotKeyByCode(code)
            ?? throw new InvalidOperationException($"Build-mode hotkey '{code}' is not registered.");
        return GetKeys(hotkey.CurrentMapping);
    }

    public static ImmutableArray<string> GetKeys(KeyCombination? mapping)
    {
        if (mapping is null || mapping.KeyCode <= (int)GlKeys.Unknown)
            return [Lang.Get("vanillabuildingexpanded:build-info-unbound")];

        var keys = ImmutableArray.CreateBuilder<string>();
        if (mapping.Ctrl) keys.Add("Ctrl");
        if (mapping.Alt) keys.Add("Alt");
        if (mapping.Shift) keys.Add("Shift");
        keys.Add(mapping.PrimaryAsString());
        if (mapping.SecondKeyCode is > 0) keys.Add(mapping.SecondaryAsString());
        return keys.ToImmutable();
    }
}
