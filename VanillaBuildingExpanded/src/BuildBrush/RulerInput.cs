using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerInput : IDisposable
{
    private enum Gesture { None, Owned, Retired }

    private readonly ICoreClientAPI api;
    private readonly RulerController ruler;
    private readonly List<Action<bool>> focusListeners;
    private Gesture left;
    private Gesture right;
    private bool focused = true;
    private bool disposed;

    public RulerInput(ICoreClientAPI api, RulerController ruler, List<Action<bool>> focusListeners)
    {
        this.api = api;
        this.ruler = ruler;
        this.focusListeners = focusListeners;
        api.Event.MouseDown += OnMouseDown;
        api.Event.MouseUp += OnMouseUp;
        api.Event.PlayerDimensionChanged += OnDimensionChanged;
        focusListeners.Add(OnFocusChanged);
    }

    private bool Gameplay => focused && api.Input.MouseGrabbed && !api.IsGamePaused
        && !api.Gui.OpenedGuis.Any(gui => gui.DialogType == EnumDialogType.Dialog
            || gui.CaptureAllInputs() || gui.CaptureRawMouse());

    private bool Picking => ruler.State.CanPick && ruler.State.PickMode != RulerPickMode.Idle;

    public void ReconcileButtons()
    {
        if (disposed) return;
        // Raw-capturing dialogs or earlier handlers can hide MouseUp from the API.
        var buttons = api.Input.MouseButton;
        if (left == Gesture.Owned && !buttons.Left) left = Gesture.Retired;
        if (right == Gesture.Owned && !buttons.Right) right = Gesture.Retired;
    }

    public void HandleWorldAction(EnumEntityAction action, bool on, ref EnumHandling handling)
    {
        if (disposed || handling != EnumHandling.PassThrough) return;
        Gesture gesture;
        switch (action)
        {
            case EnumEntityAction.LeftMouseDown:
            case EnumEntityAction.InWorldLeftMouseDown: gesture = left; break;
            case EnumEntityAction.RightMouseDown:
            case EnumEntityAction.InWorldRightMouseDown: gesture = right; break;
            default: return;
        }

        // A release/focus reset can precede a final native action callback. Only a fresh
        // physical press retires this suppression latch, never the picker's Idle state.
        if (gesture != Gesture.None || (on && Gameplay && Picking))
            handling = EnumHandling.PreventSubsequent;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ClearOwnedWorldButtons();
        left = right = Gesture.None;
        ruler.CancelPicking();
        api.Event.MouseDown -= OnMouseDown;
        api.Event.MouseUp -= OnMouseUp;
        api.Event.PlayerDimensionChanged -= OnDimensionChanged;
        focusListeners.Remove(OnFocusChanged);
    }

    private void OnMouseDown(MouseEvent e)
    {
        if (disposed || e.Handled
            || e.Button is not (EnumMouseButton.Left or EnumMouseButton.Right)) return;
        ref Gesture gesture = ref (e.Button == EnumMouseButton.Left ? ref left : ref right);
        if (gesture == Gesture.Retired) gesture = Gesture.None;
        if (!Gameplay) return;
        if (gesture == Gesture.Owned)
        {
            e.Handled = true;
            return;
        }
        if (!Picking) return;
        if (api.World.Player?.Entity?.Alive != true)
        {
            ruler.SetContext(ruler.State.IsBuildModeEnabled, false);
            return;
        }

        gesture = Gesture.Owned;
        e.Handled = true;
        if (e.Button == EnumMouseButton.Right)
        {
            ruler.CancelPicking();
            return;
        }

        var player = api.World.Player;
        BlockPos? position = player.CurrentEntitySelection is null
            ? player.CurrentBlockSelection?.Position?.Copy() : null;
        bool valid = position is not null && position.dimension >= 0
            && position.Y >= 0 && position.Y < BlockPos.DimensionBoundary
            && (long)position.dimension * BlockPos.DimensionBoundary + position.Y <= int.MaxValue
            && api.World.BlockAccessor.IsValidPos(position);
        ruler.TrySelect(position, valid);
    }

    private void OnMouseUp(MouseEvent e)
    {
        if (disposed || e.Button is not (EnumMouseButton.Left or EnumMouseButton.Right)) return;
        ref Gesture gesture = ref (e.Button == EnumMouseButton.Left ? ref left : ref right);
        if (gesture == Gesture.None) return;
        gesture = Gesture.Retired;
        if (Gameplay) e.Handled = true;
    }

    private void OnDimensionChanged(IPlayer player)
    {
        if (!disposed && player == api.World.Player) ruler.CancelPicking();
    }

    private void OnFocusChanged(bool value)
    {
        focused = value;
        if (value) return;
        ClearOwnedWorldButtons();
        if (left == Gesture.Owned) left = Gesture.Retired;
        if (right == Gesture.Owned) right = Gesture.Retired;
    }

    private void ClearOwnedWorldButtons()
    {
        if (left != Gesture.None) api.Input.InWorldMouseButton.Left = false;
        if (right != Gesture.None) api.Input.InWorldMouseButton.Right = false;
    }
}
