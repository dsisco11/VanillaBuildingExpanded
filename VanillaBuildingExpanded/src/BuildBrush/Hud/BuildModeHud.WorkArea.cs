using System;
using System.Collections.Generic;
using System.Linq;

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
    private int workAreaListFirst;
    private Guid? workAreaListSelection;
    private readonly HashSet<EnumMouseButton> workAreaBlurGestures = [];
    private GuiDialog? workAreaDialog;
    private GuiElementTable WorkAreaTable => workAreaTable
        ?? throw new InvalidOperationException("Work area content is only available during HUD composition.");
    private GuiElementTable? ActiveWorkAreaTable => SingleComposer?.GetElement("workarea-table") as GuiElementTable;

    private static string WorkAreaText(string key, params object[] args) =>
        Lang.Get($"vanillabuildingexpanded:build-info-workarea-{key}", args);

    private void CancelWorkAreaInput(bool preserveBlurGestures = false)
    {
        FinishWorkAreaSizeEdit(commit: false);
        ActiveWorkAreaTable?.CancelInput();
        if (!preserveBlurGestures) workAreaBlurGestures.Clear();
    }

    private void CloseWorkAreaDialog()
    {
        var dialog = workAreaDialog;
        workAreaDialog = null;
        if (dialog is null) return;
        try { dialog.TryClose(); }
        finally { dialog.Dispose(); }
    }

    private void OpenWorkAreaName(bool rename)
    {
        if (workArea is null || (rename && workArea.SelectedId is null)) return;
        CancelWorkAreaInput();
        CloseWorkAreaDialog();
        workArea.CancelEndpointEdit();
        Guid? id = rename ? workArea.SelectedId : null;
        string name;
        if (id is { } selected) name = workArea.Areas.Single(area => area.Id == selected).Name;
        else
        {
            int number = 1;
            do { name = WorkAreaText("default-name", number++); }
            while (workArea.ValidateName(name) == WorkAreaNameError.Duplicate);
        }
        workAreaDialog = new WorkAreaNameDialog(capi, WorkAreaText(rename ? "rename-title" : "new-title"), name,
            value =>
            {
                var error = workArea.ValidateName(value, id);
                if (error != WorkAreaNameError.None)
                    return WorkAreaText(error == WorkAreaNameError.Duplicate ? "duplicate-name" : "invalid-name",
                        WorkAreaController.MaximumNameLength);
                if (id is { } existing) workArea.RenameArea(existing, value);
                else workArea.CreateArea(value);
                return null;
            });
        workAreaDialog.TryOpen();
    }

    private void ConfirmDeleteWorkArea()
    {
        if (workArea?.SelectedId is not { } id) return;
        CancelWorkAreaInput();
        CloseWorkAreaDialog();
        workArea.CancelEndpointEdit();
        string name = workArea.Areas.Single(area => area.Id == id).Name;
        workAreaDialog = new WorkAreaDeleteDialog(capi, WorkAreaText("delete-confirm", name), confirmed =>
        {
            if (confirmed) workArea.DeleteArea(id);
        });
        workAreaDialog.TryOpen();
    }

    private void ResetHudComposition()
    {
        CancelWorkAreaInput(preserveBlurGestures: true);
        foreach (var section in sections) section.CancelInput();
        ClearComposers();
    }

    public override void OnGuiClosed()
    {
        CloseWorkAreaDialog();
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
            TableCell Button(string key, string text, Action click, bool enabled = true, double width = 70)
            {
                var button = new GuiElementTextButton(capi, text, CairoFont.WhiteSmallText(),
                    CairoFont.WhiteSmallText(), () => { click(); return true; },
                    ElementBounds.Fixed(0, 0, width, 26), EnumButtonStyle.Small) { Enabled = enabled };
                return new TableCell(button, key, input: TableInput.Pointer, cancelInput: () =>
                {
                    if (!button.Bounds.Initialized) return;
                    // An outside release clears the native pending press without activating the button.
                    button.OnMouseUp(capi, new MouseEvent((int)Math.Floor(button.Bounds.renderX) - 1,
                        (int)Math.Floor(button.Bounds.renderY) - 1, EnumMouseButton.Left, 0));
                });
            }
            var entries = workArea?.Areas.ToArray() ?? [];
            int selected = Array.FindIndex(entries, area => area.Id == workArea?.SelectedId);
            var list = new WorkAreaListBox(capi, entries, workArea?.SelectedId,
                id => workArea?.SelectArea(id), workAreaListFirst,
                workAreaListSelection != workArea?.SelectedId, first => workAreaListFirst = first);
            workAreaListFirst = list.FirstVisible;
            workAreaListSelection = workArea?.SelectedId;
            table.AddRowCells([new TableCell(list, "select", input: TableInput.Pointer,
                cancelInput: list.CancelPress)]);
            var commands = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
                new TableOptions(columnGap: 8));
            AddNestedRow(table, commands, "commands", () =>
            {
                commands.AddRowCells([
                    Button("new", WorkAreaText("new"), () => OpenWorkAreaName(false)),
                    Button("rename", WorkAreaText("rename"), () => OpenWorkAreaName(true), selected >= 0),
                    Button("delete", WorkAreaText("delete"), ConfirmDeleteWorkArea, selected >= 0)]);
            });
            var visibility = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
                new TableOptions(columnGap: 8, verticalAlignment: EnumVerticalAlign.Middle));
            AddNestedRow(table, visibility, "visibility", () =>
            {
                var checkbox = new GuiElementSwitch(capi, value => workArea?.SetVisibility(value),
                    ElementBounds.Fixed(0, 0, 22, 22), 22, 3) { On = workArea?.State.ShowWorkArea == true,
                        Enabled = selected >= 0 };
                // Native switches toggle on press and retain no pending interaction to cancel.
                visibility.AddRowCells([new TableCell(checkbox, "show", input: TableInput.Pointer,
                    cancelInput: () => { }),
                    new TableCell(new GuiElementStaticText(capi, WorkAreaText("show"),
                    EnumTextOrientation.Left, ElementBounds.Fixed(0, 0, Width(WorkAreaText("show")), height),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor])), "label")]);
            });

            string resizeModeText = WorkAreaText("resize-mode",
                WorkAreaText(workArea?.ResizeMode == WorkAreaResizeMode.Centered ? "resize-centered" : "resize-expansion"));
            table.AddRowCells([Button("resize-mode", resizeModeText, () =>
                workArea?.SetResizeMode(workArea.ResizeMode == WorkAreaResizeMode.Expansion
                    ? WorkAreaResizeMode.Centered : WorkAreaResizeMode.Expansion),
                workArea is not null, Math.Max(140, Width(resizeModeText) + 24))]);

            var size = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
                new TableOptions(columnGap: 6, verticalAlignment: EnumVerticalAlign.Middle));
            AddNestedRow(table, size, "size", () =>
            {
                GuiElementStaticText Label(string text) => new(capi, text, EnumTextOrientation.Left,
                    ElementBounds.Fixed(0, 0, Width(text), height),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor]));
                var cells = new List<TableCell> { new(Label(WorkAreaText("size")), "label") };
                workAreaSizeInputs = new WorkAreaSizeInput[3];
                for (int axis = 0; axis < 3; axis++)
                {
                    string key = axis == 0 ? "x" : axis == 1 ? "y" : "z";
                    var input = new WorkAreaSizeInput(capi, axis, Math.Max(86, Width("4294967296") + 24),
                        WorkAreaDimensionText(axis), workArea?.State.CanPick == true && workArea.State.Bounds is not null,
                        FocusWorkAreaSizeInput, ApplyWorkAreaSize, InvalidWorkAreaSize);
                    workAreaSizeInputs[axis] = input;
                    cells.Add(new(Label(WorkAreaText("size-" + key)), key + "-label"));
                    cells.Add(new(input, key, input: TableInput.Pointer, cancelInput: input.CancelPress));
                }
                size.AddRowCells(cells);
            });
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

    private sealed class WorkAreaDeleteDialog(ICoreClientAPI api, string message, Action<bool> confirm)
        : GuiDialogConfirm(api, message, confirm)
    {
        public override bool UnregisterOnClose => true;
        public override bool DisableMouseGrab => true;
        public override bool CaptureAllInputs() => true;

        public override void OnKeyDown(KeyEvent e)
        {
            if (e.KeyCode == (int)GlKeys.Escape) TryClose();
            else base.OnKeyDown(e);
            e.Handled = true;
        }
    }

}
