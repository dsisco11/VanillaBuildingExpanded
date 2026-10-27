using System;
using System.Collections.Generic;
using System.Linq;

using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.UI;

/// <summary>Non-focusing, single-selection list over a snapshot of caller-provided items.</summary>
/// <typeparam name="T">The type of item displayed and returned to the selection callback.</typeparam>
/// <remarks>
/// Selection commits on a matching left-button press and release. Scrolling cancels pending row selection.
/// The control owns its viewport texture and scrollbar; callers supply its width and position.
/// </remarks>
internal class GuiElementListBox<T> : GuiElementControl
{
    /// <summary>Width of the native scrollbar in unscaled GUI units.</summary>
    private const int ScrollbarWidth = 6;

    /// <summary>Projects each item to literal display text.</summary>
    private readonly System.Func<T, string> itemText;

    /// <summary>Receives the newly selected item after a completed selection gesture.</summary>
    private readonly Action<T> selected;

    /// <summary>Optionally receives the first visible item index whenever it changes after initialization.</summary>
    private readonly Action<int>? scrolled;

    /// <summary>Text displayed in the disabled row when the item snapshot is empty.</summary>
    private readonly string emptyText;

    /// <summary>Maximum number of rows displayed in the viewport.</summary>
    private readonly int maximumVisibleRows;

    /// <summary>Owned native scrollbar used when the items exceed the viewport capacity.</summary>
    private readonly GuiElementCompactScrollbar scrollbar;

    /// <summary>Owned texture containing the visible rows and their current highlights.</summary>
    private LoadedTexture texture;

    /// <summary>Whether the viewport texture needs rebuilding before rendering.</summary>
    private bool dirty = true;

    /// <summary>Whether owned resources have already been released.</summary>
    private bool disposed;

    /// <summary>Whether the control owns an unfinished left-button gesture.</summary>
    private bool pressed;

    /// <summary>Item index where the current gesture began, or -1 when no row can be selected.</summary>
    private int pressedIndex = -1;

    /// <summary>Currently hovered item index, or -1 when no item is hovered.</summary>
    private int hoveredIndex = -1;

    /// <summary>Gets the read-only snapshot of the supplied item sequence.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Gets the selected item index, or -1 when no item is selected.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Gets the height of each row in unscaled GUI units.</summary>
    public int RowHeight { get; }

    /// <summary>Gets the index of the first item displayed in the viewport.</summary>
    public int FirstVisible { get; private set; }

    /// <summary>Gets the viewport row count, including one placeholder row for an empty list.</summary>
    public int VisibleRows => Math.Clamp(Items.Count, 1, maximumVisibleRows);

    /// <summary>Gets whether the item count requires scrolling.</summary>
    private bool Scrollable => Items.Count > VisibleRows;

    /// <summary>Gets false because this pointer-operated control never takes keyboard focus.</summary>
    public override bool Focusable => false;

    /// <summary>Sizes the supplied bounds to the visible rows; the caller supplies width and position.</summary>
    /// <param name="api">Client API used for rendering, input, and native GUI resources.</param>
    /// <param name="items">Item sequence to snapshot for the lifetime of the control.</param>
    /// <param name="itemText">Function producing literal display text for each item.</param>
    /// <param name="selected">Callback invoked when a gesture changes the selected item.</param>
    /// <param name="bounds">Control bounds whose fixed height is replaced by the viewport height.</param>
    /// <param name="selectedIndex">Initial selected index, or -1 for no selection.</param>
    /// <param name="maximumVisibleRows">Positive maximum number of viewport rows.</param>
    /// <param name="rowHeight">Positive row height in unscaled GUI units.</param>
    /// <param name="emptyText">Placeholder text displayed when there are no items.</param>
    /// <param name="firstVisible">Initial scroll index, clamped to the available viewport range.</param>
    /// <param name="revealSelection">Whether to adjust the initial scroll index to show the selected item.</param>
    /// <param name="scrolled">Optional callback receiving subsequent changes to the first visible index.</param>
    /// <exception cref="ArgumentNullException">
    /// An item sequence, text projection, selection callback, bounds, or empty-state text is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The row count or height is not positive, or the selected index is neither -1 nor a valid item index.
    /// </exception>
    public GuiElementListBox(ICoreClientAPI api, IEnumerable<T> items, System.Func<T, string> itemText,
        Action<T> selected, ElementBounds bounds, int selectedIndex = -1, int maximumVisibleRows = 3,
        int rowHeight = 22, string emptyText = "", int firstVisible = 0, bool revealSelection = true,
        Action<int>? scrolled = null) : base(api, bounds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(itemText);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(emptyText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumVisibleRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowHeight);
        Items = Array.AsReadOnly(items.ToArray());
        if (selectedIndex < -1 || selectedIndex >= Items.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        SelectedIndex = selectedIndex;
        RowHeight = rowHeight;
        this.maximumVisibleRows = maximumVisibleRows;
        this.itemText = itemText;
        this.selected = selected;
        this.scrolled = scrolled;
        this.emptyText = emptyText;
        enabled = Items.Count != 0;
        Bounds.fixedHeight = VisibleRows * (double)RowHeight;
        FirstVisible = Math.Clamp(firstVisible, 0, Math.Max(0, Items.Count - VisibleRows));
        if (revealSelection && selectedIndex >= 0)
            FirstVisible = Math.Clamp(FirstVisible, Math.Max(0, selectedIndex - VisibleRows + 1), selectedIndex);
        texture = new LoadedTexture(api);
        scrollbar = new GuiElementCompactScrollbar(api,
            offset => SetFirstVisible((int)Math.Round(offset / scaled(RowHeight)), false),
            ElementBounds.Fixed(0, 0, ScrollbarWidth, Bounds.fixedHeight));
    }

    /// <summary>Calculates viewport bounds, configures scrolling, and builds the initial row texture.</summary>
    /// <param name="ctx">Composer drawing context; rows are rendered into a separate owned texture.</param>
    /// <param name="surface">Composer surface; rows are rendered into a separate owned texture.</param>
    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        if (Scrollable)
        {
            scrollbar.Bounds = ElementBounds.Fixed(Bounds.fixedWidth - ScrollbarWidth, 0,
                ScrollbarWidth, Bounds.fixedHeight).WithParent(Bounds);
            scrollbar.Bounds.CalcWorldBounds();
            int first = FirstVisible;
            scrollbar.SetHeights((float)Bounds.InnerHeight, (float)scaled(Items.Count * (double)RowHeight));
            SetFirstVisible(first, true);
        }
        ComposeTexture();
    }

    /// <summary>Updates hover state and draws the viewport texture and any required scrollbar.</summary>
    /// <param name="deltaTime">Elapsed frame time in seconds, forwarded to the native scrollbar.</param>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (disposed) return;
        if (!Enabled || api.Input.MouseGrabbed) CancelPress();
        SetHover(!Enabled || api.Input.MouseGrabbed ? -1 : IndexAt(api.Input.MouseX, api.Input.MouseY));
        if (dirty) ComposeTexture();
        Render2DTexture(texture.TextureId, Bounds);
        if (Scrollable) scrollbar.RenderInteractiveElements(deltaTime);
    }

    /// <summary>Captures an eligible left-button gesture and forwards scrollbar presses when needed.</summary>
    /// <param name="api">Client API supplying mouse-grab state.</param>
    /// <param name="e">Pointer event marked handled when the control captures the gesture.</param>
    public override void OnMouseDown(ICoreClientAPI api, MouseEvent e)
    {
        if (disposed || e.Handled || api.Input.MouseGrabbed || e.Button != EnumMouseButton.Left
            || !IsPositionInside(e.X, e.Y)) return;
        pressed = true;
        pressedIndex = Enabled ? IndexAt(e.X, e.Y) : -1;
        if (Enabled && Scrollable && scrollbar.IsPositionInside(e.X, e.Y)) scrollbar.OnMouseDown(api, e);
        e.Handled = true;
    }

    /// <summary>Ends a left-button gesture and selects a different item only when release matches the pressed row.</summary>
    /// <param name="api">Client API supplying mouse-grab state.</param>
    /// <param name="e">Pointer event marked handled when the control owned the gesture.</param>
    public override void OnMouseUp(ICoreClientAPI api, MouseEvent e)
    {
        if (disposed || e.Button != EnumMouseButton.Left) return;
        bool owned = pressed;
        int index = IndexAt(e.X, e.Y);
        bool choose = owned && Enabled && !e.Handled && !api.Input.MouseGrabbed && index >= 0 && index == pressedIndex;
        CancelPress();
        if (owned) e.Handled = true;
        if (!choose || index == SelectedIndex) return;
        SelectedIndex = index;
        dirty = true;
        selected(Items[index]);
    }

    /// <summary>Updates scrollbar dragging and row hover, canceling interaction when input is unavailable.</summary>
    /// <param name="api">Client API supplying mouse-grab state and native scrollbar input.</param>
    /// <param name="e">Pointer movement event with current screen coordinates.</param>
    public override void OnMouseMove(ICoreClientAPI api, MouseEvent e)
    {
        if (disposed || e.Handled) return;
        if (!Enabled || api.Input.MouseGrabbed) { CancelPress(); SetHover(-1); return; }
        if (Scrollable) scrollbar.OnMouseMove(api, e);
        SetHover(IndexAt(e.X, e.Y));
    }

    /// <summary>Consumes eligible wheel input inside the control and scrolls one row in the wheel direction.</summary>
    /// <param name="api">Client API supplying pointer coordinates and mouse-grab state.</param>
    /// <param name="e">Wheel event marked handled when accepted, even when scrolling is unnecessary.</param>
    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs e)
    {
        if (disposed || !Enabled || e.IsHandled || api.Input.MouseGrabbed || !IsPositionInside(api.Input.MouseX, api.Input.MouseY))
            return;
        float delta = e.deltaPrecise != 0 ? e.deltaPrecise : e.delta;
        if (Scrollable && delta != 0) SetFirstVisible(FirstVisible + (delta > 0 ? -1 : 1), true);
        e.SetHandled();
    }

    /// <summary>Cancels pending row selection and scrollbar dragging without changing the selected item.</summary>
    public void CancelPress()
    {
        pressed = false;
        pressedIndex = -1;
        scrollbar.mouseDownOnScrollbarHandle = false;
    }

    /// <summary>Finds the visible item under a screen position, excluding the scrollbar and empty rows.</summary>
    /// <param name="x">Horizontal screen coordinate in pixels.</param>
    /// <param name="y">Vertical screen coordinate in pixels.</param>
    /// <returns>The item index, or -1 when the position does not identify a visible item.</returns>
    private int IndexAt(int x, int y)
    {
        if (!Bounds.Initialized || !IsPositionInside(x, y)
            || (Scrollable && x >= Bounds.renderX + Bounds.OuterWidth - scaled(ScrollbarWidth))) return -1;
        int row = (int)((y - Bounds.renderY) / scaled(RowHeight));
        int index = FirstVisible + row;
        return row >= 0 && row < VisibleRows && index < Items.Count ? index : -1;
    }

    /// <summary>Clamps the scroll index, cancels pending row selection on change, and notifies the caller.</summary>
    /// <param name="first">Requested first visible item index.</param>
    /// <param name="synchronizeScrollbar">Whether to update the native scrollbar position to match.</param>
    private void SetFirstVisible(int first, bool synchronizeScrollbar)
    {
        first = Math.Clamp(first, 0, Math.Max(0, Items.Count - VisibleRows));
        if (FirstVisible != first)
        {
            FirstVisible = first;
            pressedIndex = -1;
            dirty = true;
            scrolled?.Invoke(first);
        }
        if (synchronizeScrollbar && Scrollable)
            scrollbar.CurrentYPosition = (float)scaled(FirstVisible * (double)RowHeight);
    }

    /// <summary>Updates the hovered row, invalidates its visual state when changed, and selects the cursor.</summary>
    /// <param name="index">Hovered item index, or -1 to clear hover.</param>
    private void SetHover(int index)
    {
        if (hoveredIndex != index) { hoveredIndex = index; dirty = true; }
        MouseOverCursor = index >= 0 ? "linkselect" : null;
    }

    /// <summary>Rebuilds the clipped viewport texture with visible labels, selection, and hover highlights.</summary>
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
            bool active = index < Items.Count && index == SelectedIndex;
            double y = row * rowHeight;
            if (active || index == hoveredIndex)
            {
                var tint = active ? UiPalette.SectionTitleColor : UiPalette.SectionHoverColor;
                ctx.SetSourceRGBA(tint[0], tint[1], tint[2], active ? 0.25 : tint[3]);
                ctx.Rectangle(0, y, width, rowHeight);
                ctx.Fill();
            }
            var color = Items.Count == 0 ? UiPalette.UnavailableColor
                : active ? UiPalette.SectionTitleColor : UiPalette.ValueColor;
            ctx.SetSourceRGBA([.. color]);
            ctx.MoveTo(scaled(6), y + (rowHeight - ctx.FontExtents.Height) / 2 + ctx.FontExtents.Ascent);
            ctx.ShowText(Items.Count == 0 ? emptyText : itemText(Items[index]));
        }
        generateTexture(surface, ref texture);
        dirty = false;
    }

    /// <summary>Cancels interaction and releases the viewport texture, scrollbar, and base resources once.</summary>
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
