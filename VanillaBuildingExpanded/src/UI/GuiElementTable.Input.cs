using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.UI;

/// <summary>Opt-in pointer routing and row-major nested focus for the owning table.</summary>
internal sealed partial class GuiElementTable
{
    private readonly Dictionary<EnumMouseButton, List<CellEntry>> pointerCaptures = [];
    private CellEntry? focusedChild;
    private CellEntry? hoveredChild;
    private CellEntry? pendingPointerFocus;
    private bool pointerFocusPending;
    private bool reverseFocusEntry;
    private bool cancellingInput;
    private long inputRevision;

    internal bool HasPointerCapture => pointerCaptures.Count > 0;

    internal bool WantsPointerEvents(int x, int y) => !disposed && (HasPointerCapture
        || hoveredChild is not null || PointerHitOrder().Any(entry => IsPointerHit(entry, x, y)));

    /// <summary>Only explicit keyboard participants with native focus capability make the group focusable.</summary>
    public override bool Focusable => !disposed && elements.Values.Any(IsKeyboardEligible);

    /// <summary>Routes a press front-to-back and tracks every recipient, even if it leaves the event unhandled.</summary>
    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (disposed || cancellingInput || args.Handled) return;
        if (pointerCaptures.Remove(args.Button, out var previous)) CleanupInput(previous);
        UpdateCursor(args.X, args.Y);
        pendingPointerFocus = null;
        pointerFocusPending = false;
        reverseFocusEntry = false;
        long revision = inputRevision;
        foreach (var entry in PointerHitOrder())
        {
            if (!IsPointerHit(entry, args.X, args.Y)) continue;
            if (!pointerCaptures.TryGetValue(args.Button, out var captures))
                pointerCaptures.Add(args.Button, captures = []);
            captures.Add(entry);
            try { entry.Cell.Element.OnMouseDown(api, args); }
            catch (Exception error)
            {
                try { CancelInput(); }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("Table press and cancellation failed.", error, cleanupError);
                }
                throw;
            }
            if (disposed || inputRevision != revision) return;
            if (!args.Handled) continue;
            pointerFocusPending = true;
            pendingPointerFocus = IsKeyboardEligible(entry) ? entry : null;
            if (HasFocus)
            {
                SetFocusedChild(pendingPointerFocus);
                pointerFocusPending = false;
                pendingPointerFocus = null;
            }
            break;
        }
        if (!disposed) UpdateCursor(args.X, args.Y);
    }

    /// <summary>Releases matching captures outside bounds; handled releases only cancel, never activate.</summary>
    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (disposed || cancellingInput) return;
        if (!pointerCaptures.Remove(args.Button, out var captures))
        {
            UpdateCursor(args.X, args.Y);
            return;
        }
        List<Exception>? errors = null;
        long revision = inputRevision;
        foreach (var entry in captures)
        {
            if (disposed) break;
            try
            {
                if (!args.Handled && errors is null && inputRevision == revision)
                    entry.Cell.Element.OnMouseUp(api, args);
            }
            catch (Exception error) { (errors ??= []).Add(error); }
            finally
            {
                if (!disposed)
                {
                    try { CleanupInput([entry]); }
                    catch (Exception error) { (errors ??= []).Add(error); }
                }
            }
        }
        if (!disposed && inputRevision == revision) UpdateCursor(args.X, args.Y);
        if (errors is not null) throw new AggregateException("Table release cleanup failed.", errors);
    }

    /// <summary>Forwards captured movement outside bounds, hovered movement, and one hover-exit notification.</summary>
    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (disposed || cancellingInput || args.Handled) return;
        var previousHover = hoveredChild;
        UpdateCursor(args.X, args.Y);
        long revision = inputRevision;
        var captured = pointerCaptures.Values.SelectMany(entries => entries).ToHashSet();
        // Capture recipients precede hover-only recipients, without duplicating a child's move.
        foreach (var entry in PointerHitOrder().OrderByDescending(captured.Contains))
        {
            if (!captured.Contains(entry) && !IsPointerHit(entry, args.X, args.Y) &&
                !ReferenceEquals(entry, previousHover)) continue;
            entry.Cell.Element.OnMouseMove(api, args);
            if (disposed || inputRevision != revision) return;
            if (args.Handled) break;
        }
        if (!disposed) UpdateCursor(args.X, args.Y);
    }

    /// <summary>Routes wheel input only over eligible visible cells; the native wheel event has no coordinates.</summary>
    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (disposed || cancellingInput || args.IsHandled) return;
        int x = api.Input.MouseX;
        int y = api.Input.MouseY;
        UpdateCursor(x, y);
        long revision = inputRevision;
        foreach (var entry in PointerHitOrder())
        {
            if (!IsPointerHit(entry, x, y)) continue;
            entry.Cell.Element.OnMouseWheel(api, args);
            if (disposed || inputRevision != revision) return;
            if (args.IsHandled) break;
        }
        if (!disposed) UpdateCursor(x, y);
    }

    /// <summary>Routes keys to the nested focus owner and traverses internally only for unhandled Tab.</summary>
    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (disposed || cancellingInput || args.Handled) return;
        bool tab = args.KeyCode == (int)GlKeys.Tab;
        if (tab) reverseFocusEntry = args.ShiftPressed;
        if (!HasFocus) return;
        if (focusedChild is { } current && !IsKeyboardEligible(current)) SetFocusedChild(null);
        if (focusedChild is { Cell.Element.HasFocus: true } focused)
            focused.Cell.Element.OnKeyDown(api, args);
        if (disposed || cancellingInput || !HasFocus || args.Handled || !tab) return;
        var candidates = KeyboardOrder().ToArray();
        int index = Array.IndexOf(candidates, focusedChild);
        int next = index < 0 ? (args.ShiftPressed ? candidates.Length - 1 : 0) :
            index + (args.ShiftPressed ? -1 : 1);
        // Boundary Tab belongs to the outer composer. Do not clear or wrap the current child.
        if (next >= 0 && next < candidates.Length && SetFocusedChild(candidates[next]))
            args.Handled = true;
    }

    /// <summary>Forwards key release only to the focused eligible child and expires remembered Tab entry direction.</summary>
    public override void OnKeyUp(ICoreClientAPI api, KeyEvent args)
    {
        if (disposed || cancellingInput) return;
        if (args.KeyCode == (int)GlKeys.Tab) reverseFocusEntry = false;
        if (!args.Handled && HasFocus && focusedChild is { Cell.Element.HasFocus: true } focused &&
            IsKeyboardEligible(focused))
            focused.Cell.Element.OnKeyUp(api, args);
    }

    /// <summary>Forwards character input only to the focused eligible child while the group has focus.</summary>
    public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
    {
        if (disposed || cancellingInput || args.Handled || !HasFocus) return;
        if (focusedChild is { Cell.Element.HasFocus: true } focused && IsKeyboardEligible(focused))
            focused.Cell.Element.OnKeyPress(api, args);
    }

    /// <summary>Enters the group at the requested pointer selection or first/last keyboard participant.</summary>
    public override void OnFocusGained()
    {
        if (disposed || cancellingInput || HasFocus || !Focusable) return;
        base.OnFocusGained();
        var target = pointerFocusPending ? pendingPointerFocus :
            (reverseFocusEntry ? KeyboardOrder().LastOrDefault() : KeyboardOrder().FirstOrDefault());
        pendingPointerFocus = null;
        pointerFocusPending = false;
        reverseFocusEntry = false;
        SetFocusedChild(target);
    }

    /// <summary>Leaves nested focus and cancels pointer state without synthesizing an activating release.</summary>
    public override void OnFocusLost()
    {
        if (disposed) return;
        CancelInput();
    }

    /// <summary>Cancels capture, cursor, group focus, and nested focus without activating any child.</summary>
    /// <remarks>The host must call this when hiding, disabling routing, or returning the pointer to gameplay.</remarks>
    /// <exception cref="AggregateException">Cleanup failed; all captured children and nested focus were still processed.</exception>
    public void CancelInput()
    {
        if (disposed || cancellingInput) return;
        var captures = pointerCaptures.Values.SelectMany(entries => entries).Distinct().ToArray();
        var focused = focusedChild;
        ClearInputTracking();
        cancellingInput = true;
        try { CleanupInput(captures, focused); }
        finally { cancellingInput = false; }
    }

    /// <summary>Clears references before callbacks so reentrant cancellation cannot replay an interaction.</summary>
    private void ClearInputTracking()
    {
        inputRevision++;
        pointerCaptures.Clear();
        focusedChild = hoveredChild = pendingPointerFocus = null;
        pointerFocusPending = reverseFocusEntry = false;
        MouseOverCursor = null;
        hasFocus = false;
    }

    private static bool IsKeyboardEligible(CellEntry entry) =>
        (entry.Cell.Input & TableInput.Keyboard) != 0 && entry.Cell.Element.Focusable;

    private IEnumerable<CellEntry> KeyboardOrder() => rows.SelectMany(row => row).Where(IsKeyboardEligible);

    private IEnumerable<CellEntry> PointerHitOrder() =>
        drawOrder.Reverse().Where(entry => (entry.Cell.Input & TableInput.Pointer) != 0);

    private bool IsPointerHit(CellEntry entry, int x, int y) =>
        arrangedLayout is not null && tableBounds.Initialized && tableBounds.OuterWidth > 0 &&
        tableBounds.OuterHeight > 0 && IsPositionInside(x, y) && entry.Bounds.Initialized &&
        entry.Bounds.OuterWidth > 0 && entry.Bounds.OuterHeight > 0 && entry.Cell.Element.IsPositionInside(x, y)
        && (entry.Cell.Element is not GuiElementTable nested
            || nested.PointerHitOrder().Any(child => nested.IsPointerHit(child, x, y)));

    private void UpdateCursor(int x, int y)
    {
        hoveredChild = PointerHitOrder().FirstOrDefault(entry => IsPointerHit(entry, x, y));
        MouseOverCursor = hoveredChild?.Cell.Element.MouseOverCursor;
    }

    private void UpdateCursorFromClient()
    {
        if (!elements.Values.Any(entry => (entry.Cell.Input & TableInput.Pointer) != 0))
        {
            hoveredChild = null;
            MouseOverCursor = null;
            return;
        }
        UpdateCursor(api.Input.MouseX, api.Input.MouseY);
    }

    /// <summary>Changes nested focus without giving outgoing controls an activating mouse release.</summary>
    private bool SetFocusedChild(CellEntry? target)
    {
        if (ReferenceEquals(focusedChild, target)) return target?.Cell.Element.HasFocus == true;
        var previous = focusedChild;
        focusedChild = null;
        if (previous is not null)
        {
            bool captured = false;
            foreach (var button in pointerCaptures.Keys.ToArray())
            {
                var entries = pointerCaptures[button];
                captured |= entries.Remove(previous);
                if (entries.Count == 0) pointerCaptures.Remove(button);
            }
            CleanupInput(captured ? [previous] : [], previous);
        }
        if (disposed || cancellingInput || !HasFocus || target is null || !IsKeyboardEligible(target)) return false;
        focusedChild = target;
        try
        {
            if (!target.Cell.Element.HasFocus) target.Cell.Element.OnFocusGained();
        }
        finally
        {
            if (!target.Cell.Element.HasFocus) focusedChild = null;
        }
        return target.Cell.Element.HasFocus;
    }

    /// <summary>Processes all non-activating cleanup even when one callback fails, then surfaces every failure.</summary>
    private void CleanupInput(IEnumerable<CellEntry> captures, CellEntry? focused = null)
    {
        bool wasCancelling = cancellingInput;
        cancellingInput = true;
        List<Exception>? errors = null;
        try
        {
            foreach (var entry in captures)
            {
                if (disposed) break;
                try { entry.Cell.CancelInput!(); }
                catch (Exception error) { (errors ??= []).Add(error); }
            }
            if (!disposed && focused?.Cell.Element.HasFocus == true)
            {
                try { focused.Cell.Element.OnFocusLost(); }
                catch (Exception error) { (errors ??= []).Add(error); }
            }
        }
        finally
        {
            cancellingInput = wasCancelling;
        }
        if (errors is not null) throw new AggregateException("Table input cancellation failed.", errors);
    }
}
