using System.Collections.Immutable;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.UI;

internal static class UiPalette
{
    public static readonly ImmutableArray<double> LabelColor = [.. ColorUtil.Hex2Doubles("#B8ADA0")];
    public static readonly ImmutableArray<double> KeybindDividerColor = [.. ColorUtil.Hex2Doubles("#B8ADA0", 0.55)];
    public static readonly ImmutableArray<double> ValueColor = [.. ColorUtil.Hex2Doubles("#F3EBDD")];
    public static readonly ImmutableArray<double> UnavailableColor = [.. ColorUtil.Hex2Doubles("#918A80")];
    public static readonly ImmutableArray<double> PanelBackgroundColor = [.. ColorUtil.Hex2Doubles("#000000", 0.55)];
    public static readonly ImmutableArray<double> TitleOverlayColor = [.. ColorUtil.Hex2Doubles("#000000", 0.25)];
    public static readonly ImmutableArray<double> OpaqueFadeMaskColor = [.. ColorUtil.Hex2Doubles("#000000FF")];
    public static readonly ImmutableArray<double> TransparentFadeMaskColor = [.. ColorUtil.Hex2Doubles("#00000000")];

    public const double IconOpacity = 0.85;
    public const float TitleFontSize = 18;
    public const float TextFontSize = 12;
    public const float KeybindFontSize = TextFontSize * 0.9f;
}
