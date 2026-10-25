using System;

using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.UI;

internal sealed class GuiElementCollapseButton : GuiElement
{
    private readonly Action onClick;
    private readonly bool collapsed;
    private LoadedTexture normalTexture;
    private LoadedTexture hoverTexture;
    private bool disposed;

    public bool IsPressed { get; private set; }

    public GuiElementCollapseButton(ICoreClientAPI api, ElementBounds bounds, bool collapsed, Action onClick)
        : base(api, bounds)
    {
        this.collapsed = collapsed;
        this.onClick = onClick;
        normalTexture = new LoadedTexture(api);
        hoverTexture = new LoadedTexture(api);
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        ComposeTexture(false, ref normalTexture);
        ComposeTexture(true, ref hoverTexture);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (disposed) return;
        if (api.Input.MouseGrabbed) CancelPress();
        bool hovered = !api.Input.MouseGrabbed && IsPositionInside(api.Input.MouseX, api.Input.MouseY);
        MouseOverCursor = hovered ? "linkselect" : null;
        Render2DTexture((hovered ? hoverTexture : normalTexture).TextureId, Bounds);
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (disposed || args.Handled || api.Input.MouseGrabbed || args.Button != EnumMouseButton.Left
            || !IsPositionInside(args.X, args.Y)) return;
        IsPressed = true;
        args.Handled = true;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (disposed || args.Button != EnumMouseButton.Left) return;
        bool pressed = IsPressed;
        CancelPress();
        if (!pressed || args.Handled || api.Input.MouseGrabbed) return;
        args.Handled = true;
        if (IsPositionInside(args.X, args.Y)) onClick();
    }

    public void CancelPress() => IsPressed = false;

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelPress();
        normalTexture.Dispose();
        hoverTexture.Dispose();
        base.Dispose();
    }

    private void ComposeTexture(bool hovered, ref LoadedTexture texture)
    {
        using var surface = new ImageSurface(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        using var ctx = new Context(surface);
        double width = Bounds.OuterWidth;
        double height = Bounds.OuterHeight;
        if (hovered)
        {
            var tint = UiPalette.SectionHoverColor;
            var clear = UiPalette.TransparentFadeMaskColor;
            var hoverColor = new Color(tint[0], tint[1], tint[2], tint[3]);
            var transparent = new Color(clear[0], clear[1], clear[2], clear[3]);
            using var gradient = new LinearGradient(0, 0, width, 0);
            gradient.AddColorStop(0, transparent);
            gradient.AddColorStop(0.25, hoverColor);
            gradient.AddColorStop(0.75, hoverColor);
            gradient.AddColorStop(1, transparent);
            ctx.SetSource(gradient);
            ctx.Paint();
        }
        double halfWidth = scaled(5);
        double halfHeight = scaled(3);
        double tipY = height / 2 + (collapsed ? halfHeight : -halfHeight);
        double baseY = height / 2 + (collapsed ? -halfHeight : halfHeight);
        ctx.SetSourceRGBA([.. UiPalette.SectionToggleIconColor]);
        ctx.MoveTo(width / 2, tipY);
        ctx.LineTo(width / 2 - halfWidth, baseY);
        ctx.LineTo(width / 2 + halfWidth, baseY);
        ctx.ClosePath();
        ctx.Fill();
        generateTexture(surface, ref texture);
    }
}
