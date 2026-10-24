using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

[CollectionDefinition("Keybind localization", DisableParallelization = true)]
public sealed class KeybindLocalizationCollection { }

[Collection("Keybind localization")]
public sealed class BuildModeKeybindsTests : IDisposable
{
    private const string TestLocale = "vbe-keybind-tests";
    private const string UnboundKey = "vanillabuildingexpanded:build-info-unbound";
    private readonly string previousLocale = Lang.CurrentLocale;

    public BuildModeKeybindsTests()
    {
        var translations = new Mock<ITranslationService>();
        translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns(true);
        translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] _) => key);
        Lang.AvailableLanguages.Add(TestLocale, translations.Object);
        Lang.ChangeLanguage(TestLocale);
    }

    public void Dispose()
    {
        Lang.ChangeLanguage(previousLocale);
        Lang.AvailableLanguages.Remove(TestLocale);
    }

    [Fact]
    public void Read_GroupsCycleBindingsAndReflectsRebindings()
    {
        string[] codes = [
            "vbe.ToggleBuildMode",
            "vbe.RotateBuildCursor_Forward",
            "vbe.RotateBuildCursor_Backward",
            "vbe.CycleSnappingMode_Forward",
            "vbe.CycleSnappingMode_Backward",
            "vbe.ToggleFaceOffsetPlacement"
        ];
        var hotkeys = codes.ToDictionary(code => code, code => new HotKey
        {
            Code = code,
            Name = $"Action: {code}",
            CurrentMapping = new KeyCombination { KeyCode = (int)GlKeys.LShift },
            DefaultMapping = new KeyCombination { KeyCode = (int)GlKeys.Escape }
        });
        var input = new Mock<IInputAPI>();
        input.Setup(i => i.GetHotKeyByCode(It.IsAny<string>())).Returns((string code) => hotkeys[code]);
        hotkeys[codes[2]].CurrentMapping = new KeyCombination { KeyCode = (int)GlKeys.Escape, Shift = true };
        hotkeys[codes[3]].CurrentMapping = new KeyCombination
        {
            KeyCode = KeyCombination.MouseStart + (int)EnumMouseButton.Middle
        };
        hotkeys[codes[4]].CurrentMapping = hotkeys[codes[3]].CurrentMapping.Clone();
        hotkeys[codes[4]].CurrentMapping.Shift = true;

        var hints = BuildModeKeybinds.Read(input.Object);
        Assert.Equal(5, hints.Length);
        Assert.Equal(new[] { "Shift" }, hints[1].Keys);
        Assert.Equal(new[] { "Shift", "Esc" }, hints[1].AlternateKeys);
        string mouseKey = hotkeys[codes[3]].CurrentMapping.PrimaryAsString();
        Assert.Equal(new[] { mouseKey }, hints[2].Keys);
        Assert.Equal(new[] { "Shift", mouseKey }, hints[2].AlternateKeys);
        Assert.Empty(hints[0].AlternateKeys);
        Assert.Empty(hints[3].AlternateKeys);
        Assert.Empty(hints[^1].AlternateKeys);

        hotkeys[codes[1]].CurrentMapping = new KeyCombination { KeyCode = (int)GlKeys.Escape, Ctrl = true };
        var rebound = BuildModeKeybinds.Read(input.Object);
        Assert.Equal(new[] { "Ctrl", "Esc" }, rebound[1].Keys);
        Assert.Equal(hints[^1].Keys.ToArray(), rebound[^1].Keys.ToArray());
        hotkeys[codes[2]].CurrentMapping = new KeyCombination { KeyCode = (int)GlKeys.Escape, Alt = true };
        Assert.Equal(new[] { "Alt", "Esc" }, BuildModeKeybinds.Read(input.Object)[1].AlternateKeys);
        hotkeys[codes[2]].CurrentMapping.KeyCode = -1;
        Assert.Equal(new[] { UnboundKey }, BuildModeKeybinds.Read(input.Object)[1].AlternateKeys);
    }

    [Fact]
    public void GetKeys_PreservesMouseModifiersAndSecondaryButton()
    {
        var mapping = new KeyCombination
        {
            KeyCode = KeyCombination.MouseStart + (int)EnumMouseButton.Middle,
            SecondKeyCode = KeyCombination.MouseStart + (int)EnumMouseButton.Right,
            Ctrl = true,
            Alt = true,
            Shift = true
        };
        Assert.Equal(new[] { "Ctrl", "Alt", "Shift", mapping.PrimaryAsString(), mapping.SecondaryAsString() },
            BuildModeKeybinds.GetKeys(mapping));
        mapping.Ctrl = mapping.Alt = false;
        mapping.SecondKeyCode = null;
        Assert.Equal(new[] { "Shift", mapping.PrimaryAsString() }, BuildModeKeybinds.GetKeys(mapping));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    public void GetKeys_ShowsUnboundForMissingOrClearedKeys(int? keyCode)
    {
        var mapping = keyCode is int code ? new KeyCombination { KeyCode = code } : null;
        Assert.Equal(new[] { UnboundKey }, BuildModeKeybinds.GetKeys(mapping));
    }

    [Fact]
    public void Read_ReportsMissingRegistrationExplicitly()
    {
        var input = new Mock<IInputAPI>();
        var error = Assert.Throws<InvalidOperationException>(() => BuildModeKeybinds.Read(input.Object));
        Assert.Contains("vbe.ToggleBuildMode", error.Message);
    }
}
