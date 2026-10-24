using Cairo;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Draws a passive, GUI-scaled build information panel.</summary>
internal sealed class BuildModeHud : HudElement, IBuildModeIndicator
{
    private bool disposed;
    private BuildModeInfo info;
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
        if (SingleComposer is null)
        {
            var bounds = ElementBounds.Fixed(0, 0, 320, 130)
                .WithAlignment(EnumDialogArea.LeftTop).WithFixedOffset(12, 12);
            SingleComposer = capi.Gui.CreateCompo("vbe-build-mode", bounds)
                .AddStaticCustomDraw(ElementBounds.Fixed(0, 0, 320, 130), DrawBackground)
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
                .Compose();
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

    internal static double GetFadeOpacity(double progress) => 1 - progress * progress * (3 - 2 * progress);

    private static void DrawBackground(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        ctx.Save();
        try
        {
            ctx.Translate(bounds.drawX, bounds.drawY);
            ctx.Scale(bounds.InnerWidth / 320, bounds.InnerHeight / 130);
            ctx.Rectangle(0, 0, 320, 130);
            ctx.Clip();

            // Mask an isolated group so only the background fades, not the icon or text.
            ctx.PushGroup();
            ctx.SetSourceRGBA([.. UiPalette.PanelBackgroundColor]);
            ctx.Paint();
            ctx.SetSourceRGBA([.. UiPalette.TitleOverlayColor]);
            ctx.Rectangle(0, 0, 320, 48);
            ctx.Fill();

            ctx.Operator = Operator.DestIn;
            var opaque = UiPalette.OpaqueFadeMaskColor;
            var transparent = UiPalette.TransparentFadeMaskColor;
            using var rightFade = new LinearGradient(296, 0, 320, 0) { Extend = Extend.Pad };
            using var bottomFade = new LinearGradient(0, 110, 0, 130) { Extend = Extend.Pad };
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
