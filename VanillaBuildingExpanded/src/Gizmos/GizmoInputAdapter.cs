using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.Gizmos;

internal interface IGizmoCreationInput
{
    bool IsPending { get; }
    void Create(GizmoPointer pointer);
    void Cancel(bool explicitlyCanceled);
}

/// <summary>Session input ownership; the owner routes world actions here before placement.</summary>
internal sealed class GizmoInputAdapter : IDisposable
{
    private enum Gesture { None, Owned, Retired }

    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly GizmoManager manager;
    private readonly GizmoRenderAdapter renderer;
    private readonly Func<bool> enabled;
    private readonly List<Action<bool>> focusListeners;
    private readonly IGizmoCreationInput? creation;
    private Gesture left, right;
    private bool leftDown, rightDown, escapeDown;
    private bool focused = true, failed, disposed;
    private bool grabbed;
    private EnumCameraMode cameraMode;
    private int dimension;
    private int mouseX, mouseY;

    public GizmoInputAdapter(ICoreClientAPI api, GizmoManager manager, GizmoRenderAdapter renderer,
        Func<bool> enabled, List<Action<bool>> focusListeners, IGizmoCreationInput? creation = null)
    {
        this.api = api;
        this.world = api.World;
        this.manager = manager;
        this.renderer = renderer;
        this.enabled = enabled;
        this.focusListeners = focusListeners;
        this.creation = creation;
        grabbed = api.Input.MouseGrabbed;
        cameraMode = world.Player.CameraMode;
        dimension = world.Player.Entity.Pos.Dimension;
        leftDown = api.Input.MouseButton.Left;
        rightDown = api.Input.MouseButton.Right;
        mouseX = api.Input.MouseX;
        mouseY = api.Input.MouseY;
        api.Event.MouseDown += OnMouseDown;
        api.Event.MouseUp += OnMouseUp;
        api.Event.MouseMove += OnMouseMove;
        api.Event.KeyDown += OnKeyDown;
        api.Event.KeyUp += OnKeyUp;
        api.Event.PlayerDimensionChanged += OnDimensionChanged;
        api.Event.LeaveWorld += Dispose;
        renderer.ContextReady += OnFrame;
        focusListeners.Add(OnFocusChanged);
    }

    private bool DialogOwnsInput => api.Gui.OpenedGuis.Any(gui =>
        gui.DialogType == EnumDialogType.Dialog || gui.CaptureAllInputs() || gui.CaptureRawMouse());

    private bool Eligible => !failed && focused && !manager.IsDisposed && enabled()
        && ReferenceEquals(api.World, world) && world.Player?.Entity?.Alive == true
        && !api.IsGamePaused && !DialogOwnsInput;

    private bool Usable(GizmoContext? context) => context?.Camera is { } camera
        && world.Player is { Entity: { } entity } player && context.Dimension == entity.Pos.Dimension
        && camera.MouseGrabbed == api.Input.MouseGrabbed && camera.CameraMode == player.CameraMode
        && camera.Width == api.Render.FrameWidth && camera.Height == api.Render.FrameHeight;

    private bool HudOwnsPointer(int x, int y) => api.Gui.OpenedGuis.Any(gui =>
        gui is BuildModeHud hud ? hud.ReservesPointer(x, y) :
        gui.ShouldReceiveMouseEvents() && gui.Composers.Values.Any(composer => composer.Bounds.PointInside(x, y)));

    public void Refresh() => Guard(() =>
    {
        ReconcileEscape();
        RefreshCore();
    });

    private void ReconcileEscape()
    {
        if (escapeDown && !api.Input.KeyboardKeyStateRaw[(int)GlKeys.Escape]) escapeDown = false;
    }

    private bool RefreshCore(GizmoContext? context = null, bool reconcile = true)
    {
        var player = world.Player;
        bool modeChanged = grabbed != api.Input.MouseGrabbed
            || (player is not null && cameraMode != player.CameraMode)
            || (player?.Entity is { } entity && dimension != entity.Pos.Dimension);
        grabbed = api.Input.MouseGrabbed;
        if (player is not null) cameraMode = player.CameraMode;
        if (player?.Entity is { } currentEntity) dimension = currentEntity.Pos.Dimension;
        bool eligible = Eligible;
        if (!eligible || modeChanged)
        {
            CancelOperation();
            if (!manager.IsDisposed) manager.InvalidateFrame();
        }
        if (reconcile)
        {
            if (!api.Input.MouseButton.Left)
            {
                leftDown = false;
                if (left == Gesture.Owned)
                {
                    left = Gesture.Retired;
                    ClearWorldButton(EnumMouseButton.Left);
                    CancelOperation();
                }
            }
            if (!api.Input.MouseButton.Right)
            {
                rightDown = false;
                if (right == Gesture.Owned) right = Gesture.Retired;
                if (right != Gesture.None) ClearWorldButton(EnumMouseButton.Right);
            }
        }
        if (!Usable(context ?? renderer.Context))
        {
            CancelOperation();
            if (!manager.IsDisposed) manager.InvalidateFrame();
            return false;
        }
        return eligible && !modeChanged;
    }

    private void OnFrame(GizmoContext context) => Guard(() =>
    {
        ReconcileEscape();
        if (!RefreshCore(context)) return;
        var pointer = context.Camera!.CreatePointer(context, mouseX, mouseY);
        if (manager.CapturedGizmo is not null) manager.Update(pointer);
        else if (renderer.Context is { } drawn && Usable(drawn) && !HudOwnsPointer(mouseX, mouseY))
            manager.UpdateHover(drawn.Camera!.CreatePointer(drawn, mouseX, mouseY));
    });

    private void OnMouseMove(MouseEvent e)
    {
        mouseX = e.X;
        mouseY = e.Y;
    }

    private void OnMouseDown(MouseEvent e) => Guard(() =>
    {
        ReconcileEscape();
        mouseX = e.X;
        mouseY = e.Y;
        if (e.Button is not (EnumMouseButton.Left or EnumMouseButton.Right)) return;
        ref bool down = ref (e.Button == EnumMouseButton.Left ? ref leftDown : ref rightDown);
        ref Gesture gesture = ref (e.Button == EnumMouseButton.Left ? ref left : ref right);
        bool fresh = !down;
        down = true;
        if (fresh && gesture == Gesture.Retired) gesture = Gesture.None;
        bool usable = RefreshCore();
        if (gesture != Gesture.None)
        {
            if (!DialogOwnsInput) e.Handled = true;
            return;
        }
        if (e.Handled || !fresh || !usable || HudOwnsPointer(e.X, e.Y)) return;
        if (e.Button == EnumMouseButton.Right)
        {
            if (manager.CapturedGizmo is null && creation?.IsPending != true) return;
            gesture = Gesture.Owned;
            e.Handled = true;
            CancelOperation(explicitlyCanceled: true);
            return;
        }
        var context = renderer.Context!;
        var pointer = context.Camera!.CreatePointer(context, e.X, e.Y);
        if (manager.UpdateHover(pointer) is not null)
        {
            gesture = Gesture.Owned;
            e.Handled = true;
            manager.TryBegin(pointer);
        }
        else if (creation?.IsPending == true)
        {
            gesture = Gesture.Owned;
            e.Handled = true;
            creation.Create(pointer);
        }
    });

    private void OnMouseUp(MouseEvent e) => Guard(() =>
    {
        mouseX = e.X;
        mouseY = e.Y;
        if (e.Button is not (EnumMouseButton.Left or EnumMouseButton.Right)) return;
        ref bool down = ref (e.Button == EnumMouseButton.Left ? ref leftDown : ref rightDown);
        ref Gesture gesture = ref (e.Button == EnumMouseButton.Left ? ref left : ref right);
        down = false;
        bool usable = RefreshCore(reconcile: false);
        if (gesture == Gesture.None) return;
        bool complete = gesture == Gesture.Owned && e.Button == EnumMouseButton.Left;
        gesture = Gesture.Retired;
        ClearWorldButton(e.Button);
        bool priorHandled = e.Handled;
        if (!DialogOwnsInput) e.Handled = true;
        if (!complete || manager.CapturedGizmo is null) return;
        if (!usable || priorHandled) { CancelOperation(); return; }
        var context = renderer.Context!;
        manager.Update(context.Camera!.CreatePointer(context, e.X, e.Y));
        manager.Complete();
    });

    private void OnKeyDown(KeyEvent e) => Guard(() =>
    {
        bool dialog = DialogOwnsInput;
        RefreshCore();
        if (e.KeyCode != (int)GlKeys.Escape) return;
        if (escapeDown) { if (!dialog) e.Handled = true; return; }
        if (e.Handled || dialog || (manager.CapturedGizmo is null && creation?.IsPending != true)) return;
        escapeDown = true;
        e.Handled = true;
        CancelOperation(explicitlyCanceled: true);
    });

    private void OnKeyUp(KeyEvent e) => Guard(() =>
    {
        RefreshCore();
        if (e.KeyCode != (int)GlKeys.Escape || !escapeDown) return;
        escapeDown = false;
        if (!DialogOwnsInput) e.Handled = true;
    });

    public void HandleWorldAction(EnumEntityAction action, bool on, ref EnumHandling handling)
    {
        if (disposed) return;
        Refresh();
        if (handling != EnumHandling.PassThrough) return;
        var gesture = action switch
        {
            EnumEntityAction.LeftMouseDown or EnumEntityAction.InWorldLeftMouseDown => left,
            EnumEntityAction.RightMouseDown or EnumEntityAction.InWorldRightMouseDown => right,
            _ => Gesture.None
        };
        if (gesture != Gesture.None) handling = EnumHandling.PreventSubsequent;
    }

    private void CancelOperation(bool explicitlyCanceled = false)
    {
        try { if (!manager.IsDisposed) manager.Cancel(); }
        finally
        {
            if (left != Gesture.None) ClearWorldButton(EnumMouseButton.Left);
            if (right != Gesture.None) ClearWorldButton(EnumMouseButton.Right);
            if (creation?.IsPending == true) creation.Cancel(explicitlyCanceled);
        }
    }

    private void ClearWorldButton(EnumMouseButton button)
    {
        if (button == EnumMouseButton.Left) api.Input.InWorldMouseButton.Left = false;
        else api.Input.InWorldMouseButton.Right = false;
    }

    private void OnDimensionChanged(IPlayer player)
    {
        if (ReferenceEquals(player, world.Player)) Guard(() =>
        {
            CancelOperation();
            manager.InvalidateFrame();
        });
    }

    private void OnFocusChanged(bool value)
    {
        focused = value;
        if (!value) Guard(() =>
        {
            CancelOperation();
            if (left == Gesture.Owned) left = Gesture.Retired;
            if (right == Gesture.Owned) right = Gesture.Retired;
            leftDown = rightDown = false;
            escapeDown = false;
            manager.InvalidateFrame();
        });
    }

    private void Guard(Action action)
    {
        if (disposed) return;
        try { action(); }
        catch (Exception error) when (error.GetType() == typeof(Exception)
            || error is InvalidOperationException or ArgumentException or NotSupportedException
                or ExternalException or OutOfMemoryException)
        {
            if (failed) throw;
            failed = true;
            try
            {
                CancelOperation();
                if (!manager.IsDisposed) manager.InvalidateFrame();
            }
            finally
            {
                api.Logger.Error("[Gizmo] Input unavailable for this session: {0}", error);
                const string code = "vanillabuildingexpanded:gizmo-input-unavailable";
                api.TriggerIngameError(this, code, Lang.Get(code));
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { CancelOperation(); }
        finally
        {
            left = right = Gesture.None;
            api.Event.MouseDown -= OnMouseDown;
            api.Event.MouseUp -= OnMouseUp;
            api.Event.MouseMove -= OnMouseMove;
            api.Event.KeyDown -= OnKeyDown;
            api.Event.KeyUp -= OnKeyUp;
            api.Event.PlayerDimensionChanged -= OnDimensionChanged;
            api.Event.LeaveWorld -= Dispose;
            renderer.ContextReady -= OnFrame;
            focusListeners.Remove(OnFocusChanged);
        }
    }
}
