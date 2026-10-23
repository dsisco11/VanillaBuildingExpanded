using Cairo;
using Vintagestory.API.Common;
using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Draws a passive, GUI-scaled mod icon for enabled build mode.</summary>
internal sealed class BuildModeHud : HudElement, IBuildModeIndicator
{
    private bool disposed;

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
            // Keep clear of the central crosshair, bottom hotbar and upper-right minimap.
            // Fixed bounds are GUI units; native bounds handle window resizing.
            var bounds = ElementBounds.Fixed(0, 0, 36, 36)
                .WithAlignment(EnumDialogArea.CenterTop).WithFixedOffset(0, 16);
            SingleComposer = capi.Gui.CreateCompo("vbe-build-mode", bounds)
                .AddStaticCustomDraw(ElementBounds.Fixed(0, 0, 36, 36), DrawIcon).Compose();
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
            ctx.PaintWithAlpha(0.85);
        }
        finally
        {
            ctx.Restore();
        }
    }
    #endregion
}
