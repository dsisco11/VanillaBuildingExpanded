using System;
using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Routes Shift-scroll to brush rotation while preserving unowned wheel events.</summary>
internal sealed class BuildBrushWheelInput : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Func<EModeCycleDirection, bool> rotate;
    private bool disposed;

    #region Public API
    /// <summary>Subscribes wheel input with a callback that reports whether rotation succeeded.</summary>
    public BuildBrushWheelInput(ICoreClientAPI api, Func<EModeCycleDirection, bool> rotate)
    {
        this.api = api;
        this.rotate = rotate;
        api.Event.MouseWheelMove += OnMouseWheel;
    }

    /// <summary>Detaches the wheel callback so retired systems cannot consume input.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.MouseWheelMove -= OnMouseWheel;
    }
    #endregion

    #region Private
    /// <summary>Consumes only successful rotations with either Shift key during grabbed-mouse gameplay.</summary>
    private void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (disposed || e.IsHandled || !api.Input.MouseGrabbed) return;
        bool[] keys = api.Input.KeyboardKeyState;
        if (!keys[(int)GlKeys.LShift] && !keys[(int)GlKeys.RShift]) return;

        // Preserve high-resolution wheel direction when the integer delta rounds to zero.
        float delta = e.deltaPrecise != 0 ? e.deltaPrecise : e.delta;
        if (delta != 0 && rotate(delta > 0 ? EModeCycleDirection.Forward : EModeCycleDirection.Backward))
            e.SetHandled();
    }
    #endregion
}
