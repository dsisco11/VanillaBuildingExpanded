using System;
using System.Collections.Generic;

using Cairo;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaListBox : GuiElementControl
{
    internal const int RowHeight = 22;
    private const int MaximumRows = 3;
    private const int ScrollbarWidth = 12;
    private readonly Action<Guid> selected;
    private readonly Action<int> scrolled;
    private readonly GuiElementCompactScrollbar scrollbar;
    private LoadedTexture texture;
    private bool dirty = true, disposed, pressed;
    private int pressedIndex = -1, hoveredIndex = -1;
    public IReadOnlyList<NamedWorkArea> Areas { get; }
    public Guid? SelectedId { get; private set; }
    public int FirstVisible { get; private set; }
    public int VisibleRows => Math.Clamp(Areas.Count, 1, MaximumRows);
    private bool Scrollable => Areas.Count > VisibleRows;
    public override bool Focusable => false;

    public WorkAreaListBox(ICoreClientAPI api, NamedWorkArea[] areas, Guid? selectedId, Action<Guid> selected,
        int firstVisible, bool revealSelection, Action<int> scrolled)
        : base(api, ElementBounds.Fixed(0, 0, 280, Math.Clamp(areas.Length, 1, MaximumRows) * RowHeight))
    {
        Areas = Array.AsReadOnly(areas);
        SelectedId = selectedId;
        this.selected = selected;
        this.scrolled = scrolled;
        enabled = areas.Length != 0;
        FirstVisible = Math.Clamp(firstVisible, 0, Math.Max(0, areas.Length - VisibleRows));
        int index = Array.FindIndex(areas, area => area.Id == selectedId);
        if (revealSelection && index >= 0)
            FirstVisible = Math.Clamp(FirstVisible, Math.Max(0, index - VisibleRows + 1), index);
        texture = new LoadedTexture(api);
        scrollbar = new GuiElementCompactScrollbar(api,
            offset => SetFirstVisible((int)Math.Round(offset / scaled(RowHeight)), false),
            ElementBounds.Fixed(0, 0, ScrollbarWidth, Bounds.fixedHeight));
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        if (Scrollable)
        {
            scrollbar.Bounds = ElementBounds.Fixed(Bounds.fixedWidth - ScrollbarWidth, 0,
                ScrollbarWidth, Bounds.fixedHeight).WithParent(Bounds);
            scrollbar.Bounds.CalcWorldBounds();
            int first = FirstVisible;
            scrollbar.SetHeights((float)Bounds.InnerHeight, (float)scaled(Areas.Count * (double)RowHeight));
            SetFirstVisible(first, true);
        }
        ComposeTexture();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (disposed) return;
        if (!Enabled || api.Input.MouseGrabbed) CancelPress();
        SetHover(!Enabled || api.Input.MouseGrabbed ? -1 : IndexAt(api.Input.MouseX, api.Input.MouseY));
        if (dirty) ComposeTexture();
        Render2DTexture(texture.TextureId, Bounds);
        if (Scrollable) scrollbar.RenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent e)
    {
        if (disposed || e.Handled || api.Input.MouseGrabbed || e.Button != EnumMouseButton.Left
            || !IsPositionInside(e.X, e.Y)) return;
        pressed = true;
        pressedIndex = Enabled ? IndexAt(e.X, e.Y) : -1;
        if (Enabled && Scrollable && scrollbar.IsPositionInside(e.X, e.Y)) scrollbar.OnMouseDown(api, e);
        e.Handled = true;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent e)
    {
        if (disposed || e.Button != EnumMouseButton.Left) return;
        bool owned = pressed;
        int index = IndexAt(e.X, e.Y);
        bool choose = owned && Enabled && !e.Handled && !api.Input.MouseGrabbed && index >= 0 && index == pressedIndex;
        CancelPress();
        if (owned) e.Handled = true;
        if (!choose || Areas[index].Id == SelectedId) return;
        SelectedId = Areas[index].Id;
        dirty = true;
        selected(SelectedId.Value);
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent e)
    {
        if (disposed || e.Handled) return;
        if (!Enabled || api.Input.MouseGrabbed) { CancelPress(); SetHover(-1); return; }
        if (Scrollable) scrollbar.OnMouseMove(api, e);
        SetHover(IndexAt(e.X, e.Y));
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs e)
    {
        if (disposed || !Enabled || e.IsHandled || api.Input.MouseGrabbed || !IsPositionInside(api.Input.MouseX, api.Input.MouseY))
            return;
        float delta = e.deltaPrecise != 0 ? e.deltaPrecise : e.delta;
        if (Scrollable && delta != 0) SetFirstVisible(FirstVisible + (delta > 0 ? -1 : 1), true);
        e.SetHandled();
    }

    public void CancelPress()
    {
        pressed = false;
        pressedIndex = -1;
        scrollbar.mouseDownOnScrollbarHandle = false;
    }

    private int IndexAt(int x, int y)
    {
        if (!Bounds.Initialized || !IsPositionInside(x, y)
            || (Scrollable && x >= Bounds.renderX + Bounds.OuterWidth - scaled(ScrollbarWidth))) return -1;
        int row = (int)((y - Bounds.renderY) / scaled(RowHeight));
        int index = FirstVisible + row;
        return row >= 0 && row < VisibleRows && index < Areas.Count ? index : -1;
    }

    private void SetFirstVisible(int first, bool synchronizeScrollbar)
    {
        first = Math.Clamp(first, 0, Math.Max(0, Areas.Count - VisibleRows));
        if (FirstVisible != first)
        {
            FirstVisible = first;
            pressedIndex = -1;
            dirty = true;
            scrolled(first);
        }
        if (synchronizeScrollbar && Scrollable)
            scrollbar.CurrentYPosition = (float)scaled(FirstVisible * (double)RowHeight);
    }

    private void SetHover(int index)
    {
        if (hoveredIndex != index) { hoveredIndex = index; dirty = true; }
        MouseOverCursor = index >= 0 ? "linkselect" : null;
    }

    private void ComposeTexture()
    {
        using var surface = new ImageSurface(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        using var ctx = new Context(surface);
        using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize);
        font.SetupContext(ctx);
        double width = Bounds.OuterWidth - (Scrollable ? scaled(ScrollbarWidth) : 0);
        double rowHeight = scaled(RowHeight);
        ctx.SetSourceRGBA(0, 0, 0, 0.25);
        ctx.Paint();
        ctx.Rectangle(scaled(4), 0, width - scaled(8), Bounds.OuterHeight);
        ctx.Clip();
        for (int row = 0; row < VisibleRows; row++)
        {
            int index = FirstVisible + row;
            bool active = index < Areas.Count && Areas[index].Id == SelectedId;
            double y = row * rowHeight;
            if (active || index == hoveredIndex)
            {
                var tint = active ? UiPalette.SectionTitleColor : UiPalette.SectionHoverColor;
                ctx.SetSourceRGBA(tint[0], tint[1], tint[2], active ? 0.25 : tint[3]);
                ctx.Rectangle(0, y, width, rowHeight);
                ctx.Fill();
            }
            var color = Areas.Count == 0 ? UiPalette.UnavailableColor
                : active ? UiPalette.SectionTitleColor : UiPalette.ValueColor;
            ctx.SetSourceRGBA([.. color]);
            ctx.MoveTo(scaled(6), y + (rowHeight - ctx.FontExtents.Height) / 2 + ctx.FontExtents.Ascent);
            ctx.ShowText(Areas.Count == 0 ? Lang.Get("vanillabuildingexpanded:build-info-workarea-empty") : Areas[index].Name);
        }
        generateTexture(surface, ref texture);
        dirty = false;
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelPress();
        texture.Dispose();
        scrollbar.Dispose();
        base.Dispose();
    }
}
