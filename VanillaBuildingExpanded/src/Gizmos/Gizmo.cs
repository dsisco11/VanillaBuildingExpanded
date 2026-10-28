using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Owns persistent controls and their lifecycle under one manager's exclusive capture authority.</summary>
public abstract partial class Gizmo : IDisposable
{
    private bool visible = true;
    private bool enabled = true;
    private bool ending;
    private bool beginning;
    private bool beginCanceled;
    private GizmoHandle? initializingHandle;
    private bool disposing;
    private bool resourcesDisposed;

    /// <summary>Gets the session-local owner identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Gets the dimension in which this owner's controls are eligible.</summary>
    public abstract int Dimension { get; }
    /// <summary>Gets whether disposal has begun, including deferred terminal cleanup.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Gets whether the notifying setter is restoring the saved interaction baseline.</summary>
    protected bool IsRestoringInteractionValue { get; private set; }
    /// <summary>Gets whether a persistent handle is captured.</summary>
    public bool IsInteracting => CapturedHandle is not null;
    /// <summary>Gets the hovered identity, synchronized with the actual owned instance.</summary>
    public GizmoHandleId? HoveredHandle
    {
        get => hoveredHandleId;
        internal set => SetHandleState(ref hoveredHandleId, ref hoveredHandleInstance, value);
    }
    /// <summary>Gets the captured identity, synchronized with the actual owned instance.</summary>
    public GizmoHandleId? CapturedHandle
    {
        get => capturedHandleId;
        private set => SetHandleState(ref capturedHandleId, ref capturedHandleInstance, value, true);
    }

    /// <summary>Initializes the read-only view over persistent handle configuration.</summary>
    protected Gizmo()
    {
        Handles = new ReadOnlyDictionary<GizmoHandleId, GizmoHandle>(handles);
    }

    /// <summary>Gets or sets owner visibility; hiding cancels before changing eligibility.</summary>
    /// <exception cref="ObjectDisposedException">The owner is disposed.</exception>
    public bool Visible
    {
        get => visible;
        set
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (visible == value) return;
            if (!value) CancelInteraction();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            visible = value;
            Invalidate();
        }
    }

    /// <summary>Gets or sets input eligibility; disabling cancels before changing eligibility.</summary>
    /// <exception cref="ObjectDisposedException">The owner is disposed.</exception>
    public bool Enabled
    {
        get => enabled;
        set
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (enabled == value) return;
            if (!value) CancelInteraction();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            enabled = value;
            Invalidate();
        }
    }

    /// <summary>Reports a successfully initialized capture after parameter and edit scopes are ready.</summary>
    public event Action<GizmoHandleId>? OnInteractionStarted;
    /// <summary>Reports successful completion after parameter cleanup and owner commit.</summary>
    public event Action<GizmoHandleId>? OnInteractionCompleted;
    /// <summary>Reports cancellation after notifying baseline restoration and owner cancellation.</summary>
    public event Action<GizmoHandleId>? OnInteractionCanceled;

    internal event Action<Gizmo>? Invalidated;
    internal event Action<Gizmo>? InteractionEnded;
    internal GizmoManager? Owner { get; set; }
    internal bool IsEligible(int dimension) => !IsDisposed && Visible && Enabled && Dimension == dimension;

    /// <summary>Enumerates candidates referring only to actual owned handles.</summary>
    /// <param name="pointer">The pointer in the last drawn context.</param>
    /// <returns>Positive finite distances and local priorities for owned controls.</returns>
    protected abstract IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer);
    /// <summary>Publishes the geometry for this shared frame.</summary>
    /// <param name="context">The current camera and dimension snapshot.</param>
    protected abstract void RenderCore(GizmoContext context);
    /// <summary>Initializes a capture-local interaction before successful-start notification.</summary>
    /// <param name="handle">The eligible actual member's identity.</param>
    /// <param name="pointer">The validated press pointer.</param>
    /// <returns>Whether initialization succeeded; false or exceptions unwind through cancellation.</returns>
    protected virtual bool BeginInteractionCore(GizmoHandleId handle, GizmoPointer pointer) => true;
    /// <summary>Updates the captured interaction without retargeting its control.</summary>
    /// <param name="pointer">The current pointer in the active dimension.</param>
    protected abstract void UpdateInteractionCore(GizmoPointer pointer);
    /// <summary>Ends parameters and completes the borrowed edit scope, releasing local resources.</summary>
    protected virtual void CompleteInteractionCore() { }
    /// <summary>Unwinds prepared or accepted sessions; repeated calls must be harmless.</summary>
    protected virtual void CancelInteractionCore() { }
    /// <summary>Saves the sole gizmo rollback value before parameter or owner initialization.</summary>
    protected virtual void SaveInteractionValue() { }
    /// <summary>Restores through the notifying value path before owner cancellation and public notification.</summary>
    protected virtual void RestoreInteractionValue() { }
    /// <summary>Releases composition-owned resources after cancellation, before owned handles are disposed.</summary>
    protected virtual void DisposeCore() { }
    /// <summary>Clears typed value observers after terminal cleanup and handle disposal.</summary>
    protected virtual void ClearValueSubscriptions() { }

    /// <summary>Call after an owner change that invalidates rendered geometry or dimension.</summary>
    protected void Invalidate()
    {
        HoveredHandle = null;
        if (!Visible || !Enabled || IsDisposed) CancelInteraction();
        Invalidated?.Invoke(this);
    }

    internal GizmoHit? HitTest(GizmoPointer pointer)
    {
        GizmoHit? best = null;
        GizmoHandle? bestHandle = null;
        foreach (var hit in HitTestCore(pointer))
        {
            if (!double.IsFinite(hit.Distance) || hit.Distance <= 0)
                throw new InvalidOperationException("Gizmo hit geometry returned an invalid ray distance.");
            if (!handles.TryGetValue(hit.Handle, out var owned))
                throw new InvalidOperationException("Hit geometry returned a handle not owned by this gizmo.");
            if (!owned.IsEligible(pointer.Context.Dimension)) continue;
            if (best is not { } previous || hit.Priority < previous.Priority
                || (hit.Priority == previous.Priority && (hit.Distance < previous.Distance
                    || (hit.Distance == previous.Distance && hit.Handle.Value < previous.Handle.Value))))
            {
                best = hit;
                bestHandle = owned;
            }
        }
        if (bestHandle is null || !OwnsHandle(bestHandle)
            || !bestHandle.IsEligible(pointer.Context.Dimension)) return null;
        return best;
    }

    internal void Render(GizmoContext context) => RenderCore(context);

    internal bool BeginInteraction(GizmoHandleId handle, GizmoPointer pointer)
    {
        if (IsInteracting || beginning || ending || editStateTarget is not null || !IsEligible(pointer.Context.Dimension)) return false;
        if (!handles.TryGetValue(handle, out var expectedHandle) || !expectedHandle.IsEligible(pointer.Context.Dimension))
            return false;
        bool accepted = false;
        beginning = true;
        beginCanceled = false;
        initializingHandle = expectedHandle;
        try
        {
            SaveEditBaseline();
            if (!BeginInteractionCore(handle, pointer)) return false;
            if (beginCanceled || !IsEligible(pointer.Context.Dimension)) return false;
            if (!OwnsHandle(expectedHandle) || !expectedHandle.IsEligible(pointer.Context.Dimension)) return false;
            CapturedHandle = handle;
            if (!IsInteracting || !IsEligible(pointer.Context.Dimension)) return false;
            accepted = true;
            OnInteractionStarted?.Invoke(handle);
            return IsInteracting;
        }
        finally
        {
            try
            {
                if (!accepted)
                {
                    try
                    {
                        if (IsInteracting) CancelInteraction();
                        else
                        {
                            try { CancelInteractionCore(); }
                            finally { editStateTarget?.Cancel(); }
                        }
                    }
                    finally { baselineSaved = false; }
                }
            }
            finally
            {
                initializingHandle = null;
                beginning = false;
            }
        }
    }

    internal void UpdateInteraction(GizmoPointer pointer)
    {
        if (IsInteracting) UpdateInteractionCore(pointer);
    }

    internal void CompleteInteraction() => EndInteraction(true);

    /// <summary>
    /// Owners cancel before assigning a genuinely different authoritative value.
    /// Rollback and its value event occur before the cancellation notification.
    /// </summary>
    public void CancelInteraction() => EndInteraction(false);

    /// <summary>Protects termination before state callbacks and finishes cleanup even when observers throw.</summary>
    /// <param name="complete">Whether to commit instead of restoring the saved value.</param>
    private void EndInteraction(bool complete)
    {
        if (CapturedHandle is not { } handle)
        {
            if (!complete)
            {
                if (beginning && !beginCanceled)
                {
                    beginCanceled = true;
                    try { CancelInteractionCore(); }
                    finally { editStateTarget?.Cancel(); }
                }
                else editStateTarget?.Cancel();
            }
            return;
        }
        var notification = complete ? OnInteractionCompleted : OnInteractionCanceled;
        ending = true;
        try
        {
            try
            {
                try { CapturedHandle = null; }
                finally { HoveredHandle = null; }
            }
            finally
            {
                try { InteractionEnded?.Invoke(this); }
                finally
                {
                    if (complete)
                    {
                        CompleteInteractionCore();
                        editStateTarget?.Complete();
                        baselineSaved = false;
                    }
                    else
                    {
                        if (editStateTarget is { } edit)
                        {
                            try { CancelInteractionCore(); }
                            finally { edit.Cancel(); }
                        }
                        else
                        {
                            try { RestoreEditBaseline(); }
                            finally { CancelInteractionCore(); }
                        }
                    }
                    notification?.Invoke(handle);
                }
            }
        }
        finally
        {
            ending = false;
            if (IsDisposed && !disposing) DisposeResources();
        }
    }

    /// <summary>Cancels unfinished work and disposes resources and remaining handles exactly once.</summary>
    /// <remarks>Disposal during a terminal callback defers resource teardown until that callback unwinds.</remarks>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        disposing = true;
        try
        {
            CancelInteraction();
        }
        finally
        {
            try
            {
                try { HoveredHandle = null; }
                finally { Invalidated?.Invoke(this); }
            }
            finally
            {
                disposing = false;
                if (!ending) DisposeResources();
            }
        }
    }

    private void DisposeResources()
    {
        if (resourcesDisposed) return;
        resourcesDisposed = true;
        try { DisposeCore(); }
        finally
        {
            try { DisposeHandles(); }
            finally
            {
                OnInteractionStarted = null;
                OnInteractionCompleted = null;
                OnInteractionCanceled = null;
                Invalidated = null;
                InteractionEnded = null;
                HandleInvalidated = null;
                ClearValueSubscriptions();
            }
        }
    }
}

/// <summary>
/// Prefer immutable values. Equality compares assignments; mutations inside an unchanged
/// reference are not observed. Owners use the same notifying setter as manipulation.
/// </summary>
/// <typeparam name="TValue">The authoritative edited value type.</typeparam>
public abstract class Gizmo<TValue> : Gizmo
{
    private TValue value;
    private TValue baseline;

    /// <summary>Creates a single authoritative value and its interaction rollback baseline.</summary>
    /// <param name="initialValue">The immutable initial value.</param>
    protected Gizmo(TValue initialValue)
    {
        value = initialValue;
        baseline = initialValue;
    }

    /// <summary>Gets or replaces the authoritative value; a different external assignment cancels before publication.</summary>
    /// <exception cref="ObjectDisposedException">The gizmo is disposed outside its rollback path.</exception>
    public TValue Value
    {
        get => value;
        set => SetValue(value, false);
    }

    /// <summary>Publishes an accepted parameter edit through the same notifying setter, not as an external replacement.</summary>
    internal void SetInteractionValue(TValue value) => SetValue(value, true);

    private void SetValue(TValue value, bool interaction)
    {
        ObjectDisposedException.ThrowIf(IsDisposed && !IsRestoringInteractionValue, this);
        if (EqualityComparer<TValue>.Default.Equals(this.value, value)) return;
        if (!interaction && !IsRestoringInteractionValue)
            CancelInteraction();
        ObjectDisposedException.ThrowIf(IsDisposed && !IsRestoringInteractionValue, this);
        this.value = value;
        Invalidate();
        OnValueChanged?.Invoke(value);
    }
    /// <summary>Notifies synchronously after a changed value is stored, including previews and rollback.</summary>
    public event Action<TValue>? OnValueChanged;

    /// <inheritdoc />
    protected sealed override void SaveInteractionValue() => baseline = value;

    /// <inheritdoc />
    protected sealed override void RestoreInteractionValue() => Value = baseline;

    /// <inheritdoc />
    protected sealed override void ClearValueSubscriptions() => OnValueChanged = null;
}
