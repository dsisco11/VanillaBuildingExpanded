using System;
using System.Linq;

using Cairo;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Draws a passive, GUI-scaled build information panel.</summary>
internal sealed class BuildModeHud : HudElement, IBuildModeIndicator
{
    private const double KeycapSpacing = 3;
    private const double KeybindDividerPadding = 6;
    private const double KeybindDividerWidth = 1;
    private bool disposed;
    private BuildModeInfo info;
    private BuildModeKeybindHint[] keybindHints = [];
    private float keybindRefreshElapsed;
    private bool RotationUnavailable => info.IsSuspended || info.OrientationCount <= 1;

    #region Public API
    /// <summary>Retains client GUI services; composition is deferred until first activation.</summary>
    public BuildModeHud(ICoreClientAPI api) : base(api)
    {
        api.Settings.Float.AddWatcher("guiScale", OnGuiScaleChanged);
    }

    /// <summary>Never takes focus from gameplay or other dialogs.</summary>
    public override bool Focusable => false;
    /// <summary>Removes the closed HUD from native dialog routing.</summary>
    public override bool UnregisterOnClose => true;
    /// <summary>Leaves mouse input entirely to gameplay and existing dialogs.</summary>
    public override bool ShouldReceiveMouseEvents() => false;
    /// <summary>Leaves keyboard input entirely to gameplay and existing dialogs.</summary>
    public override bool ShouldReceiveKeyboardEvents() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        if (disposed) return;
        if (IsOpened())
        {
            keybindRefreshElapsed += deltaTime;
            if (keybindRefreshElapsed >= 0.5f)
            {
                keybindRefreshElapsed = 0;
                SetVisible(true);
            }
        }
        base.OnRenderGUI(deltaTime);
    }

    public void Update(BuildModeInfo info)
    {
        if (disposed || this.info == info) return;
        bool wasRotationUnavailable = RotationUnavailable;
        this.info = info;
        if (SingleComposer is not null)
        {
            SingleComposer.GetDynamicText("title").SetNewText(GetTitle());
            SingleComposer.GetDynamicText("snapping").SetNewText(GetSnapping());
            var rotation = SingleComposer.GetDynamicText("rotation");
            if (wasRotationUnavailable != RotationUnavailable)
                rotation.Font.WithColor([.. (RotationUnavailable ? UiPalette.UnavailableColor : UiPalette.ValueColor)]);
            rotation.SetNewText(GetRotation(), forceRedraw: wasRotationUnavailable != RotationUnavailable);
        }
    }

    /// <summary>Composes once and opens without focus, or hides while retaining reusable artwork.</summary>
    public void SetVisible(bool visible)
    {
        if (disposed) return;
        if (!visible)
        {
            TryClose();
            return;
        }
        var currentHints = BuildModeKeybinds.Read(capi.Input);
        if (keybindHints.Length != currentHints.Length
            || keybindHints.Where((hint, i) => hint.Action != currentHints[i].Action
                || !hint.Keys.SequenceEqual(currentHints[i].Keys)
                || !hint.AlternateKeys.SequenceEqual(currentHints[i].AlternateKeys)).Any())
        {
            keybindHints = currentHints;
            ClearComposers();
        }
        if (SingleComposer is null)
        {
            using var measureFont = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize);
            using var measureKeyFont = CairoFont.WhiteSmallText().WithFontSize(UiPalette.KeybindFontSize);
            double labelWidth = Math.Ceiling(Math.Max(88,
                keybindHints.Max(hint => measureFont.GetTextExtents(hint.Action).Width / RuntimeEnv.GUIScale)));
            double bindingWidth = Math.Ceiling(Math.Max(200,
                keybindHints.Max(hint => MeasureKeybindWidth(measureKeyFont, hint))));
            double bindingX = 12 + labelWidth + 12;
            double width = Math.Max(320, bindingX + bindingWidth + 24);
            double height = 144 + keybindHints.Length * 20 + 28;
            var bounds = ElementBounds.Fixed(0, 0, width, height)
                .WithAlignment(EnumDialogArea.LeftTop).WithFixedOffset(12, 12);
            var composer = capi.Gui.CreateCompo("vbe-build-mode", bounds)
                .AddStaticCustomDraw(ElementBounds.Fixed(0, 0, width, height),
                    (ctx, surface, backgroundBounds) => DrawBackground(ctx, surface, backgroundBounds, width, height))
                .AddStaticCustomDraw(ElementBounds.Fixed(12, 10, 36, 36), DrawIcon)
                .AddDynamicText(GetTitle(), CairoFont.WhiteSmallText().WithFontSize(UiPalette.TitleFontSize).WithColor([.. UiPalette.ValueColor]),
                    ElementBounds.Fixed(58, 14, 250, 30), "title")
                .AddStaticText(Lang.Get("vanillabuildingexpanded:build-info-snapping-label"),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor]),
                    ElementBounds.Fixed(12, 54, 88, 24))
                .AddDynamicText(GetSnapping(), CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.ValueColor]),
                    ElementBounds.Fixed(108, 54, 200, 24), "snapping")
                .AddStaticText(Lang.Get("vanillabuildingexpanded:build-info-rotation-label"),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor]),
                    ElementBounds.Fixed(12, 78, 88, 24))
                .AddDynamicText(GetRotation(),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. (RotationUnavailable ? UiPalette.UnavailableColor : UiPalette.ValueColor)]),
                    ElementBounds.Fixed(108, 78, 200, 24), "rotation")
                .AddStaticText(Lang.Get("vanillabuildingexpanded:build-info-controls"),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.ValueColor]),
                    ElementBounds.Fixed(12, 116, width - 36, 24));
            for (int i = 0; i < keybindHints.Length; i++)
            {
                var hint = keybindHints[i];
                double y = 144 + i * 20;
                composer
                    .AddStaticText(hint.Action,
                        CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor]),
                        ElementBounds.Fixed(12, y, labelWidth, 20))
                    .AddStaticCustomDraw(ElementBounds.Fixed(bindingX, y, bindingWidth, 20),
                        (ctx, surface, keyBounds) => DrawKeybind(ctx, keyBounds, hint));
            }
            SingleComposer = composer.Compose();
        }
        if (!IsOpened()) TryOpen(false);
    }

    /// <summary>Closes native registration and releases the composer's texture exactly once.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        capi.Settings.Float.RemoveWatcher("guiScale", OnGuiScaleChanged);
        TryClose();
        base.Dispose();
    }
    #endregion

    #region Private
    private string GetTitle() => Lang.Get(info.IsSuspended
        ? "vanillabuildingexpanded:build-info-suspended"
        : "vanillabuildingexpanded:build-info-title");

    private string GetSnapping() => Lang.Get($"vanillabuildingexpanded:build-info-snapping-{info.Snapping.GetCode()}");

    private string GetRotation() => info.IsSuspended
            ? Lang.Get("vanillabuildingexpanded:build-info-unavailable")
            : info.OrientationCount <= 1
                ? Lang.Get("vanillabuildingexpanded:build-info-no-rotation")
                : info.RotationAngleDegrees is float angle
                    ? Lang.Get("vanillabuildingexpanded:build-info-angle", angle)
                    : Lang.Get("vanillabuildingexpanded:build-info-orientation", info.OrientationIndex + 1, info.OrientationCount);

    private static double MeasureKeybindWidth(CairoFont font, BuildModeKeybindHint hint)
    {
        double width = MeasureKeyCombinationWidth(font, hint.Keys.AsSpan());
        if (!hint.AlternateKeys.IsEmpty)
            width += GuiElement.scaled(2 * KeybindDividerPadding + KeybindDividerWidth)
                + MeasureKeyCombinationWidth(font, hint.AlternateKeys.AsSpan());
        return (width + 3) / RuntimeEnv.GUIScale;
    }

    private static double MeasureKeyCombinationWidth(CairoFont font, ReadOnlySpan<string> keys)
    {
        double width = 0;
        foreach (string key in keys)
        {
            if (width > 0) width += GuiElement.scaled(KeycapSpacing);
            width += (int)(font.GetTextExtents(key).Width + GuiElement.scaled(8));
        }
        return width;
    }

    private void DrawKeybind(Context ctx, ElementBounds bounds, BuildModeKeybindHint hint)
    {
        using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.KeybindFontSize).WithColor([.. UiPalette.ValueColor]);
        double textHeight = font.GetFontExtents().Height;
        double[] color = [.. UiPalette.ValueColor];
        double DrawKeys(ReadOnlySpan<string> keys)
        {
            ctx.Save();
            try
            {
                double width = 0;
                foreach (string key in keys)
                {
                    if (width > 0)
                    {
                        double gap = GuiElement.scaled(KeycapSpacing);
                        ctx.Translate(gap, 0);
                        width += gap;
                    }
                    // A translated origin suppresses the native '+' separator without changing the keycap styling.
                    double keyWidth = HotkeyComponent.DrawHotkey(capi, key, 0, 0, ctx, font,
                        (int)textHeight, textHeight, 0, 0, 4, color);
                    ctx.Translate(keyWidth, 0);
                    width += keyWidth;
                }
                return width;
            }
            finally
            {
                ctx.Restore();
            }
        }
        ctx.Save();
        try
        {
            ctx.Translate(bounds.drawX, bounds.drawY);
            font.SetupContext(ctx);
            double x = DrawKeys(hint.Keys.AsSpan());
            if (!hint.AlternateKeys.IsEmpty)
            {
                double padding = GuiElement.scaled(KeybindDividerPadding);
                double dividerWidth = GuiElement.scaled(KeybindDividerWidth);
                double keycapHeight = (int)textHeight;
                ctx.SetSourceRGBA([.. UiPalette.KeybindDividerColor]);
                ctx.Rectangle(x + 1 + padding, 1 + keycapHeight * 0.25, dividerWidth, keycapHeight * 0.5);
                ctx.Fill();
                ctx.Translate(x + 2 * padding + dividerWidth, 0);
                DrawKeys(hint.AlternateKeys.AsSpan());
            }
        }
        finally
        {
            ctx.Restore();
        }
    }

    internal static double GetFadeOpacity(double progress) => 1 - progress * progress * (3 - 2 * progress);

    private static void DrawBackground(Context ctx, ImageSurface surface, ElementBounds bounds, double width, double height)
    {
        ctx.Save();
        try
        {
            ctx.Translate(bounds.drawX, bounds.drawY);
            ctx.Scale(bounds.InnerWidth / width, bounds.InnerHeight / height);
            ctx.Rectangle(0, 0, width, height);
            ctx.Clip();

            // Mask an isolated group so only the background fades, not the icon or text.
            ctx.PushGroup();
            ctx.SetSourceRGBA([.. UiPalette.PanelBackgroundColor]);
            ctx.Paint();
            ctx.SetSourceRGBA([.. UiPalette.TitleOverlayColor]);
            ctx.Rectangle(0, 0, width, 48);
            ctx.Fill();

            ctx.Operator = Operator.DestIn;
            var opaque = UiPalette.OpaqueFadeMaskColor;
            var transparent = UiPalette.TransparentFadeMaskColor;
            using var rightFade = new LinearGradient(width - 24, 0, width, 0) { Extend = Extend.Pad };
            using var bottomFade = new LinearGradient(0, height - 20, 0, height) { Extend = Extend.Pad };
            // Sample smoothstep because Cairo interpolates linearly between gradient stops.
            const int intervals = 16;
            for (int i = 0; i <= intervals; i++)
            {
                double progress = i / (double)intervals;
                double opacity = GetFadeOpacity(progress);
                var mask = new Color(opaque[0], opaque[1], opaque[2],
                    transparent[3] + (opaque[3] - transparent[3]) * opacity);
                rightFade.AddColorStop(progress, mask);
                bottomFade.AddColorStop(progress, mask);
            }
            ctx.SetSource(rightFade);
            ctx.Paint();
            ctx.SetSource(bottomFade);
            ctx.Paint();
            ctx.PopGroupToSource();
            ctx.Operator = Operator.Over;
            ctx.Paint();
        }
        finally
        {
            ctx.Restore();
        }
    }

    /// <summary>Invalidates scaled artwork; hidden HUDs rebuild lazily on their next activation.</summary>
    private void OnGuiScaleChanged(float scale)
    {
        if (disposed) return;
        bool visible = IsOpened();
        ClearComposers();
        if (visible) SetVisible(true);
    }

    /// <summary>Scales the official artwork and applies translucency without changing its source pixels.</summary>
    private void DrawIcon(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        using var icon = GuiElement.getImageSurfaceFromAsset(capi,
            new AssetLocation("vanillabuildingexpanded", "textures/gui/build-mode.png"));
        ctx.Save();
        try
        {
            // Preserve the transparent background and apply opacity to the hammer alone.
            ctx.Translate(bounds.drawX, bounds.drawY);
            ctx.Scale(bounds.InnerWidth / icon.Width, bounds.InnerHeight / icon.Height);
            using var pattern = new SurfacePattern(icon) { Filter = Filter.Best };
            ctx.SetSource(pattern);
            ctx.PaintWithAlpha(UiPalette.IconOpacity);
        }
        finally
        {
            ctx.Restore();
        }
    }
    #endregion
}
