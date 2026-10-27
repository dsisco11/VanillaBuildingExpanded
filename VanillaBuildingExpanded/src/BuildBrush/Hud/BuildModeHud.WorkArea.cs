using System;

using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed partial class BuildModeHud
{
    private readonly WorkAreaController? workArea;
    private readonly CollapsibleSection workAreaSection;
    private GuiElementTable? workAreaTable;
    private bool workAreaDirty;
    private GuiElementTable WorkAreaTable => workAreaTable
        ?? throw new InvalidOperationException("Work area content is only available during HUD composition.");
    private GuiElementTable? ActiveWorkAreaTable => SingleComposer?.GetElement("workarea-table") as GuiElementTable;

    private static string WorkAreaText(string key, params object[] args) =>
        Lang.Get($"vanillabuildingexpanded:build-info-workarea-{key}", args);

    private string WorkAreaSize() => workArea?.State.Bounds is { } bounds
        ? WorkAreaText("size", bounds.SizeX, bounds.SizeY, bounds.SizeZ)
        : WorkAreaText("size", WorkAreaText("unset"), WorkAreaText("unset"), WorkAreaText("unset"));

    private void OnWorkAreaChanged(WorkAreaState _) => workAreaDirty = true;

    private void CancelWorkAreaInput() => ActiveWorkAreaTable?.CancelInput();

    private void ResetHudComposition()
    {
        CancelWorkAreaInput();
        foreach (var section in sections) section.CancelInput();
        ClearComposers();
    }

    public override void OnGuiClosed()
    {
        CancelWorkAreaInput();
        foreach (var section in sections) section.CancelInput();
        base.OnGuiClosed();
    }

    private GuiElementTable CreateWorkAreaTable()
    {
        var table = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
            new TableOptions(rowGap: 6, verticalAlignment: EnumVerticalAlign.Middle));
        bool complete = false;
        try
        {
            using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize);
            double Width(string text) => Math.Ceiling(font.GetTextExtents(text).Width / RuntimeEnv.GUIScale) + 4;
            double height = MeasureTextHeight(font);
            var visibility = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
                new TableOptions(columnGap: 8, verticalAlignment: EnumVerticalAlign.Middle));
            AddNestedRow(table, visibility, "visibility", () =>
            {
                var checkbox = new WorkAreaSwitch(capi, ElementBounds.Fixed(0, 0, 22, 22),
                    value => workArea?.SetVisibility(value)) { On = workArea?.State.ShowWorkArea == true };
                visibility.AddRowCells([new TableCell(checkbox, "show", input: TableInput.Pointer,
                    cancelInput: checkbox.CancelPress),
                    new TableCell(new GuiElementStaticText(capi, WorkAreaText("show"),
                    EnumTextOrientation.Left, ElementBounds.Fixed(0, 0, Width(WorkAreaText("show")), height),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor])), "label")]);
            });

            string size = WorkAreaSize();
            table.AddRowCells([new TableCell(new GuiElementStaticText(capi, size, EnumTextOrientation.Left,
                ElementBounds.Fixed(0, 0, Width(size), height),
                CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.ValueColor])), "size")]);
            complete = true;
            return table;
        }
        finally
        {
            if (!complete) table.Dispose();
        }
    }

    private static void AddNestedRow(GuiElementTable owner, GuiElementTable nested, string key, Action populate)
    {
        bool accepted = false;
        try
        {
            populate();
            nested.Arrange();
            owner.AddRowCells([new TableCell(nested, key, nested.Measure(), TableInput.Pointer, nested.CancelInput)]);
            accepted = true;
        }
        finally
        {
            if (!accepted) nested.Dispose();
        }
    }

    private void ComposeWorkAreaContent(GuiComposer composer, ElementBounds _)
    {
        if (!composer.TryAddTable(WorkAreaTable, "workarea-table"))
            throw new InvalidOperationException("The work area table was unexpectedly skipped during HUD composition.");
    }

    // Native controls activate on mouse-down and have no non-activating cancellation API.
    // Defer their activation to a captured, in-bounds release without changing native artwork.
    private sealed class WorkAreaSwitch : GuiElementSwitch
    {
        private bool pressed;
        public override bool Focusable => false;

        public WorkAreaSwitch(ICoreClientAPI api, ElementBounds bounds, Action<bool> toggle)
            : base(api, toggle, bounds, 22, 3) { }

        public void CancelPress() => pressed = false;

        public override void OnMouseDown(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Handled || api.Input.MouseGrabbed || e.Button != EnumMouseButton.Left
                || !IsPositionInside(e.X, e.Y)) return;
            pressed = true;
            e.Handled = true;
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Button != EnumMouseButton.Left) return;
            bool activate = pressed && !e.Handled && !api.Input.MouseGrabbed && IsPositionInside(e.X, e.Y);
            bool owned = pressed;
            CancelPress();
            if (activate) base.OnMouseDownOnElement(api, e);
            if (owned) e.Handled = true;
        }
    }

}
