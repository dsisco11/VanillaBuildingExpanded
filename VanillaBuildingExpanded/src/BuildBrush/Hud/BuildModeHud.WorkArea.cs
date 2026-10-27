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
    private WorkAreaSelector? workAreaSelector;
    private readonly HashSet<EnumMouseButton> workAreaMenuGestures = [];
    private GuiDialog? workAreaDialog;
    private GuiElementTable WorkAreaTable => workAreaTable
        ?? throw new InvalidOperationException("Work area content is only available during HUD composition.");
    private GuiElementTable? ActiveWorkAreaTable => SingleComposer?.GetElement("workarea-table") as GuiElementTable;

    private static string WorkAreaText(string key, params object[] args) =>
        Lang.Get($"vanillabuildingexpanded:build-info-workarea-{key}", args);

    private void CancelWorkAreaInput(bool preserveMenuGestures = false)
    {
        FinishWorkAreaSizeEdit(commit: false);
        ActiveWorkAreaTable?.CancelInput();
        workAreaSelector?.OnFocusLost();
        if (!preserveMenuGestures) workAreaMenuGestures.Clear();
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
        CancelWorkAreaInput(preserveMenuGestures: true);
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
            var entries = workArea?.Areas.ToArray() ?? [];
            var ids = entries.Select(area => area.Id.ToString("N")).ToArray();
            var names = entries.Select(area => area.Name.Replace("&", "&amp;").Replace("<", "&lt;")
                .Replace(">", "&gt;")).ToArray();
            int selected = Array.FindIndex(entries, area => area.Id == workArea?.SelectedId);
            workAreaSelector = new WorkAreaSelector(capi, ids, names, selected,
                (code, _) => workArea?.SelectArea(Guid.ParseExact(code, "N")),
                ElementBounds.Fixed(0, 0, 280, 28));
            table.AddRowCells([new TableCell(workAreaSelector, "select", input: TableInput.Pointer,
                cancelInput: workAreaSelector.CancelPress)]);
            var commands = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
                new TableOptions(columnGap: 8));
            AddNestedRow(table, commands, "commands", () =>
            {
                var create = new WorkAreaButton(capi, WorkAreaText("new"), () => OpenWorkAreaName(false));
                var rename = new WorkAreaButton(capi, WorkAreaText("rename"), () => OpenWorkAreaName(true))
                    { Enabled = selected >= 0 };
                var delete = new WorkAreaButton(capi, WorkAreaText("delete"), ConfirmDeleteWorkArea)
                    { Enabled = selected >= 0 };
                commands.AddRowCells([
                    new TableCell(create, "new", input: TableInput.Pointer, cancelInput: create.CancelPress),
                    new TableCell(rename, "rename", input: TableInput.Pointer, cancelInput: rename.CancelPress),
                    new TableCell(delete, "delete", input: TableInput.Pointer, cancelInput: delete.CancelPress)]);
            });
            var visibility = new GuiElementTable(capi, ElementBounds.Fixed(0, 0, 0, 0),
                new TableOptions(columnGap: 8, verticalAlignment: EnumVerticalAlign.Middle));
            AddNestedRow(table, visibility, "visibility", () =>
            {
                var checkbox = new WorkAreaSwitch(capi, ElementBounds.Fixed(0, 0, 22, 22),
                    value => workArea?.SetVisibility(value)) { On = workArea?.State.ShowWorkArea == true,
                        Enabled = selected >= 0 };
                visibility.AddRowCells([new TableCell(checkbox, "show", input: TableInput.Pointer,
                    cancelInput: checkbox.CancelPress),
                    new TableCell(new GuiElementStaticText(capi, WorkAreaText("show"),
                    EnumTextOrientation.Left, ElementBounds.Fixed(0, 0, Width(WorkAreaText("show")), height),
                    CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize).WithColor([.. UiPalette.LabelColor])), "label")]);
            });

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
                        FocusWorkAreaSizeInput, InvalidWorkAreaSize);
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

    private sealed class WorkAreaSelector : GuiElementDropDown
    {
        private bool pressed;
        public WorkAreaSelector(ICoreClientAPI api, string[] ids, string[] names, int selected,
            SelectionChangedDelegate changed, ElementBounds bounds)
            : base(api, ids, names, selected, changed, bounds,
                CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize), false)
        {
            enabled = ids.Length != 0;
        }

        public override bool Focusable => false;
        public void CancelPress() => pressed = false;

        public override void OnMouseDown(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Handled || api.Input.MouseGrabbed || e.Button != EnumMouseButton.Left) return;
            if (listMenu.IsOpened) { base.OnMouseDown(api, e); return; }
            if (!IsPositionInside(e.X, e.Y)) return;
            pressed = Enabled;
            e.Handled = true;
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Button != EnumMouseButton.Left) return;
            bool open = pressed && !e.Handled && !api.Input.MouseGrabbed && IsPositionInside(e.X, e.Y);
            bool owned = pressed;
            CancelPress();
            if (open) listMenu.Open();
            if (owned) e.Handled = true;
        }

        public override void Dispose()
        {
            // Native dropdown disposal omits its current-value texture and rich text.
            currentValueTexture.Dispose();
            richTextElem.Dispose();
            base.Dispose();
        }
    }

    private sealed class WorkAreaButton : GuiElementTextButton
    {
        private readonly Action click;
        private bool pressed;
        public override bool Focusable => false;

        public WorkAreaButton(ICoreClientAPI api, string label, Action click)
            : base(api, label, CairoFont.WhiteSmallText(), CairoFont.WhiteSmallText(),
                () => { click(); return true; }, ElementBounds.Fixed(0, 0, 70, 26), EnumButtonStyle.Small)
        {
            this.click = click;
        }

        public void CancelPress() { pressed = false; SetActive(false); }

        public override void OnMouseDown(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Handled || api.Input.MouseGrabbed || e.Button != EnumMouseButton.Left
                || !IsPositionInside(e.X, e.Y)) return;
            pressed = Enabled;
            SetActive(pressed);
            e.Handled = true;
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Button != EnumMouseButton.Left) return;
            bool activate = pressed && !e.Handled && !api.Input.MouseGrabbed && IsPositionInside(e.X, e.Y);
            bool owned = pressed;
            CancelPress();
            if (owned) e.Handled = true;
            if (activate) click();
        }
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
            if (!Enabled) { e.Handled = true; return; }
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
