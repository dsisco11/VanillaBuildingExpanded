using System;

using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed partial class BuildModeHud
{
    private readonly RulerController? ruler;
    private readonly CollapsibleSection rulerSection;
    private GuiElementTable? rulerTable;
    private bool rulerDirty;
    private GuiElementTable RulerTable => rulerTable
        ?? throw new InvalidOperationException("Ruler content is only available during HUD composition.");
    private GuiElementTable? ActiveRulerTable => SingleComposer?.GetElement("ruler-table") as GuiElementTable;

    private static string RulerText(string key, params object[] args) =>
        Lang.Get($"vanillabuildingexpanded:build-info-ruler-{key}", args);

    private string RulerSize() => ruler?.State.Bounds is { } bounds
        ? RulerText("size", bounds.SizeX, bounds.SizeY, bounds.SizeZ)
        : RulerText("size", RulerText("unset"), RulerText("unset"), RulerText("unset"));

    private void OnRulerChanged(RulerState _) => rulerDirty = true;

    private void CancelRulerInput() => ActiveRulerTable?.CancelInput();

    private void ResetHudComposition()
    {
        CancelRulerInput();
        foreach (var section in sections) section.CancelInput();
        ClearComposers();
    }

    public override void OnGuiClosed()
    {
        CancelRulerInput();
        foreach (var section in sections) section.CancelInput();
        base.OnGuiClosed();
    }

    private GuiElementTable CreateRulerTable()
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
                var checkbox = new RulerSwitch(capi, ElementBounds.Fixed(0, 0, 22, 22),
                    value => ruler?.SetVisibility(value)) { On = ruler?.State.ShowRuler == true };
                visibility.AddRowCells([new TableCell(checkbox, "show", input: TableInput.Pointer,
                    cancelInput: checkbox.CancelPress),
                    new TableCell(new GuiElementStaticText(capi, RulerText("show"),
                    EnumTextOrientation.Left, ElementBounds.Fixed(0, 0, Width(RulerText("show")), height),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor])), "label")]);
            });

            string size = RulerSize();
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

    private void ComposeRulerContent(GuiComposer composer, ElementBounds _)
    {
        if (!composer.TryAddTable(RulerTable, "ruler-table"))
            throw new InvalidOperationException("The ruler table was unexpectedly skipped during HUD composition.");
    }

    // Native controls activate on mouse-down and have no non-activating cancellation API.
    // Defer their activation to a captured, in-bounds release without changing native artwork.
    private sealed class RulerSwitch : GuiElementSwitch
    {
        private bool pressed;
        public override bool Focusable => false;

        public RulerSwitch(ICoreClientAPI api, ElementBounds bounds, Action<bool> toggle)
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
