using System.Collections.Immutable;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.UI;

internal static class UiPalette
{
    public static readonly ImmutableArray<double> LabelColor = [.. ColorUtil.Hex2Doubles("#B8ADA0")];
    public static readonly ImmutableArray<double> KeybindDividerColor = [.. ColorUtil.Hex2Doubles("#B8ADA0", 0.55)];
    public static readonly ImmutableArray<double> SectionDividerColor = [.. ColorUtil.Hex2Doubles("#D4C8B6", 0.35)];
    public static readonly ImmutableArray<double> SectionTitleColor = [.. ColorUtil.Hex2Doubles("#D8BE8A")];
    public static readonly ImmutableArray<double> SectionHoverColor = [.. ColorUtil.Hex2Doubles("#A0A0A0", 0.22)];
    public static readonly ImmutableArray<double> SectionToggleIconColor = [.. ColorUtil.Hex2Doubles("#A65F26")];
    public static readonly ImmutableArray<double> ValueColor = [.. ColorUtil.Hex2Doubles("#F3EBDD")];
    public static readonly ImmutableArray<double> UnavailableColor = [.. ColorUtil.Hex2Doubles("#918A80")];
    public static readonly ImmutableArray<double> RoomTagColor = [.. ColorUtil.Hex2Doubles("#639ED9")];
    public static readonly ImmutableArray<double> ShelterTagColor = [.. ColorUtil.Hex2Doubles("#D1A85E")];
    public static readonly ImmutableArray<double> CellarTagColor = [.. ColorUtil.Hex2Doubles("#B082CF")];
    public static readonly ImmutableArray<double> GreenhouseTagColor = [.. ColorUtil.Hex2Doubles("#66BA73")];
    public static readonly ImmutableArray<double> PanelBackgroundColor = [.. ColorUtil.Hex2Doubles("#000000", 0.55)];
    public static readonly ImmutableArray<double> TitleOverlayColor = [.. ColorUtil.Hex2Doubles("#000000", 0.25)];
    public static readonly ImmutableArray<double> OpaqueFadeMaskColor = [.. ColorUtil.Hex2Doubles("#000000FF")];
    public static readonly ImmutableArray<double> TransparentFadeMaskColor = [.. ColorUtil.Hex2Doubles("#00000000")];

    public const double IconOpacity = 0.85;
    public const float TitleFontSize = 18;
    public const float TextFontSize = 12;
    public const float KeybindFontSize = TextFontSize * 0.9f;
}
