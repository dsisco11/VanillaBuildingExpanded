using System;
using System.Globalization;

using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed partial class BuildModeHud
{
    private WorkAreaSizeInput[] workAreaSizeInputs = [];
    private WorkAreaSizeInput? focusedWorkAreaSize;
    private bool updatingWorkAreaSize;

    private void OnWorkAreaChanged(WorkAreaState _)
    {
        if (updatingWorkAreaSize) RefreshWorkAreaSizes();
        else
        {
            FinishWorkAreaSizeEdit(commit: false);
            workAreaDirty = true;
        }
    }

    private string WorkAreaDimensionText(int axis)
    {
        if (workArea?.State.Bounds is not { } bounds) return WorkAreaText("unset");
        long value = axis == 0 ? bounds.SizeX : axis == 1 ? bounds.SizeY : bounds.SizeZ;
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void RefreshWorkAreaSizes()
    {
        foreach (var input in workAreaSizeInputs)
            input.SetValue(WorkAreaDimensionText(input.Axis));
    }

    private void InvalidWorkAreaSize()
    {
        const string code = "vanillabuildingexpanded:workarea-invalid-size";
        capi.TriggerIngameError(this, code, Vintagestory.API.Config.Lang.Get(code));
    }

    private void FocusWorkAreaSizeInput(WorkAreaSizeInput input)
    {
        if (!input.Enabled || workArea?.State.CanPick != true || workArea.State.Bounds is null
            || focusedWorkAreaSize == input) return;
        FinishWorkAreaSizeEdit(commit: true);
        updatingWorkAreaSize = true;
        try { workArea.CancelEndpointEdit(); }
        finally { updatingWorkAreaSize = false; }
        focusedWorkAreaSize = input;
        input.OnFocusGained();
        capi.Gui.RequestFocus(this);
    }

    private void FinishWorkAreaSizeEdit(bool commit)
    {
        var input = focusedWorkAreaSize;
        if (input is null) return;
        focusedWorkAreaSize = null;
        string text = input.GetText();
        input.CancelPress();
        input.OnFocusLost();
        base.UnFocus();
        if (commit && workArea?.State.CanPick == true && workArea.State.Bounds is { } bounds)
        {
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                InvalidWorkAreaSize();
            else
            {
                updatingWorkAreaSize = true;
                try
                {
                    workArea.TryResize(input.Axis == 0 ? value : bounds.SizeX,
                        input.Axis == 1 ? value : bounds.SizeY, input.Axis == 2 ? value : bounds.SizeZ,
                        capi.World.BlockAccessor);
                }
                finally { updatingWorkAreaSize = false; }
            }
        }
        input.SetValue(WorkAreaDimensionText(input.Axis));
    }

    public override void UnFocus()
    {
        FinishWorkAreaSizeEdit(commit: true);
        base.UnFocus();
    }

    public override bool TryClose()
    {
        FinishWorkAreaSizeEdit(commit: false);
        return base.TryClose();
    }

    public override bool OnEscapePressed()
    {
        if (focusedWorkAreaSize is null) return base.OnEscapePressed();
        FinishWorkAreaSizeEdit(commit: false);
        return true;
    }

    public override void OnKeyDown(KeyEvent args)
    {
        if (focusedWorkAreaSize is not { } input || args.Handled) return;
        if (args.KeyCode == (int)GlKeys.Escape) FinishWorkAreaSizeEdit(commit: false);
        else if (args.KeyCode == (int)GlKeys.Enter) FinishWorkAreaSizeEdit(commit: true);
        else if (args.KeyCode == (int)GlKeys.Tab)
        {
            int next = (input.Axis + (args.ShiftPressed ? 2 : 1)) % 3;
            FinishWorkAreaSizeEdit(commit: true);
            FocusWorkAreaSizeInput(workAreaSizeInputs[next]);
        }
        else input.OnKeyDown(capi, args);
        args.Handled = true;
    }

    public override void OnKeyPress(KeyEvent args)
    {
        if (focusedWorkAreaSize is not { } input || args.Handled) return;
        input.OnKeyPress(capi, args);
        args.Handled = true;
    }

    public override void OnKeyUp(KeyEvent args)
    {
        if (focusedWorkAreaSize is not { } input || args.Handled) return;
        input.OnKeyUp(capi, args);
        args.Handled = true;
    }

    private sealed class WorkAreaSizeInput : GuiElementNumberInput
    {
        private readonly Action<WorkAreaSizeInput> focus;
        private readonly Action invalid;
        private bool pressed;
        public int Axis { get; }
        private long Maximum => Axis == 1 ? BlockPos.DimensionBoundary : 1L << 32;

        public WorkAreaSizeInput(ICoreClientAPI api, int axis, double width, string text, bool editable,
            Action<WorkAreaSizeInput> focus, Action invalid)
            : base(api, ElementBounds.Fixed(0, 0, width, 28), null!,
                CairoFont.TextInput().WithFontSize(UiPalette.TextFontSize))
        {
            Axis = axis;
            this.focus = focus;
            this.invalid = invalid;
            enabled = editable;
            // Native IntMode rejects valid dimensions larger than Int32. Commit validation uses Int64.
            IntMode = false;
            lines = [text];
            linesStaging = [text];
        }

        public void CancelPress()
        {
            if (!pressed) return;
            pressed = false;
            base.OnMouseUp(api, new MouseEvent(-1, -1, EnumMouseButton.Left, 0));
        }

        public override void OnMouseDown(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Handled || e.Button != EnumMouseButton.Left || !IsPositionInside(e.X, e.Y)) return;
            if (Enabled)
            {
                focus(this);
                base.OnMouseDown(api, e);
                pressed = true;
            }
            e.Handled = true;
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent e)
        {
            if (e.X >= Bounds.absX + Bounds.OuterWidth - 17 * Vintagestory.API.Config.RuntimeEnv.GUIScale)
                Step(e.Y > Bounds.absY + Bounds.OuterHeight / 2 ? -1 : 1);
            else base.OnMouseDownOnElement(api, e);
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent e)
        {
            if (e.Button != EnumMouseButton.Left) return;
            CancelPress();
            e.Handled = true;
        }

        public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs e)
        {
            if (!Enabled || !IsPositionInside(api.Input.MouseX, api.Input.MouseY)) return;
            focus(this);
            if (e.deltaPrecise != 0) Step(e.deltaPrecise > 0 ? 1 : -1);
            e.SetHandled(true);
        }

        public override void OnKeyUp(ICoreClientAPI api, KeyEvent e)
        {
            if (HasFocus && e.KeyCode is (int)GlKeys.Up or (int)GlKeys.Down)
            {
                Step(e.KeyCode == (int)GlKeys.Up ? 1 : -1);
                e.Handled = true;
            }
            else base.OnKeyUp(api, e);
        }

        private void Step(int direction)
        {
            var keys = api.Input.KeyboardKeyStateRaw;
            int step = direction;
            if (keys[(int)GlKeys.ShiftLeft] || keys[(int)GlKeys.ShiftRight]) step *= 10;
            if (keys[(int)GlKeys.ControlLeft] || keys[(int)GlKeys.ControlRight]) step *= 100;
            if (!long.TryParse(GetText(), NumberStyles.None, CultureInfo.InvariantCulture, out long value)
                || value < 1 || value > Maximum || value + step < 1 || value + step > Maximum)
            {
                invalid();
                return;
            }
            SetValue((value + step).ToString(CultureInfo.InvariantCulture));
        }
    }
}
