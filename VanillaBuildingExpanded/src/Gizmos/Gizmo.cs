using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VanillaBuildingExpanded.Gizmos;

public abstract partial class Gizmo : IDisposable
{
    private readonly Dictionary<GizmoHandleId, GizmoLabel> labels = [];
    private bool visible = true;
    private bool enabled = true;
    private bool ending;
    private bool beginning;
    private bool beginCanceled;
    private GizmoHandle? initializingHandle;
    private bool disposing;
    private bool resourcesDisposed;

    public Guid Id { get; } = Guid.NewGuid();
    public abstract int Dimension { get; }
    public bool IsDisposed { get; private set; }
    protected bool IsRestoringInteractionValue { get; private set; }
    public bool IsInteracting => CapturedHandle is not null;
    /// <summary>Gets the hovered identity; handle-based compositions also retain the actual owned instance.</summary>
    public GizmoHandleId? HoveredHandle
    {
        get => hoveredHandleId;
        internal set => SetHandleState(ref hoveredHandleId, ref hoveredHandleInstance, value);
    }
    /// <summary>Gets the captured identity; handle-based compositions also retain the actual owned instance.</summary>
    public GizmoHandleId? CapturedHandle
    {
        get => capturedHandleId;
        private set => SetHandleState(ref capturedHandleId, ref capturedHandleInstance, value, true);
    }

    /// <summary>Gets optional labels keyed by their owning handle IDs.</summary>
    public IReadOnlyDictionary<GizmoHandleId, GizmoLabel> Labels { get; }

    /// <summary>Initializes read-only views over the gizmo's label and handle configuration.</summary>
    protected Gizmo()
    {
        Labels = new ReadOnlyDictionary<GizmoHandleId, GizmoLabel>(labels);
        Handles = new ReadOnlyDictionary<GizmoHandleId, GizmoHandle>(handles);
    }

    /// <summary>Adds or replaces a handle's label without changing geometry or interaction state.</summary>
    /// <param name="handle">The handle whose rendered anchor carries the label.</param>
    /// <param name="label">The immutable text, offset and visibility configuration.</param>
    public void SetLabel(GizmoHandleId handle, GizmoLabel label)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(label);
        labels[handle] = label;
    }

    /// <summary>Removes a handle's optional label without changing its hit target.</summary>
    /// <param name="handle">The handle whose label should be removed.</param>
    /// <returns>Whether the handle previously had a label.</returns>
    public bool RemoveLabel(GizmoHandleId handle)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return labels.Remove(handle);
    }

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

    public event Action<GizmoHandleId>? OnInteractionStarted;
    public event Action<GizmoHandleId>? OnInteractionCompleted;
    public event Action<GizmoHandleId>? OnInteractionCanceled;

    internal event Action<Gizmo>? Invalidated;
    internal event Action<Gizmo>? InteractionEnded;
    internal GizmoManager? Owner { get; set; }
    internal bool IsEligible(int dimension) => !IsDisposed && Visible && Enabled && Dimension == dimension;

    protected abstract IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer);
    protected abstract void RenderCore(GizmoContext context);
    protected virtual bool BeginInteractionCore(GizmoHandleId handle, GizmoPointer pointer) => true;
    protected abstract void UpdateInteractionCore(GizmoPointer pointer);
    protected virtual void CompleteInteractionCore() { }
    protected virtual void CancelInteractionCore() { }
    protected virtual void SaveInteractionValue() { }
    protected virtual void RestoreInteractionValue() { }
    protected virtual void DisposeCore() { }
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
            if (hasHandleComposition)
            {
                if (!handles.TryGetValue(hit.Handle, out var owned))
                    throw new InvalidOperationException("Hit geometry returned a handle not owned by this gizmo.");
                if (!owned.IsEligible(pointer.Context.Dimension)) continue;
            }
            if (best is not { } previous || hit.Priority < previous.Priority
                || (hit.Priority == previous.Priority && (hit.Distance < previous.Distance
                    || (hit.Distance == previous.Distance && hit.Handle.Value < previous.Handle.Value))))
            {
                best = hit;
                bestHandle = hasHandleComposition ? handles[hit.Handle] : null;
            }
        }
        if (hasHandleComposition && (bestHandle is null || !OwnsHandle(bestHandle)
            || !bestHandle.IsEligible(pointer.Context.Dimension))) return null;
        return best;
    }

    internal void Render(GizmoContext context) => RenderCore(context);

    internal bool BeginInteraction(GizmoHandleId handle, GizmoPointer pointer)
    {
        if (IsInteracting || beginning || ending || editStateTarget is not null || !IsEligible(pointer.Context.Dimension)) return false;
        handles.TryGetValue(handle, out var expectedHandle);
        if (hasHandleComposition && (expectedHandle is null || !expectedHandle.IsEligible(pointer.Context.Dimension)))
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
            if (expectedHandle is not null
                ? !OwnsHandle(expectedHandle) || !expectedHandle.IsEligible(pointer.Context.Dimension)
                : hasHandleComposition) return false;
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
                labels.Clear();
            }
        }
    }
}

/// <summary>
/// Prefer immutable values. Equality compares assignments; mutations inside an unchanged
/// reference are not observed. Owners use the same notifying setter as manipulation.
/// </summary>
public abstract class Gizmo<TValue> : Gizmo
{
    private TValue value;
    private TValue baseline;

    protected Gizmo(TValue initialValue)
    {
        value = initialValue;
        baseline = initialValue;
    }

    /// <summary>Whether external changes cancel capture before assignment; legacy owners retain their existing binding.</summary>
    protected virtual bool CancelOnExternalValueChange => false;

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
        if (!interaction && !IsRestoringInteractionValue && CancelOnExternalValueChange)
            CancelInteraction();
        ObjectDisposedException.ThrowIf(IsDisposed && !IsRestoringInteractionValue, this);
        this.value = value;
        Invalidate();
        OnValueChanged?.Invoke(value);
    }
    public event Action<TValue>? OnValueChanged;

    protected sealed override void SaveInteractionValue() => baseline = value;

    protected sealed override void RestoreInteractionValue() => Value = baseline;

    protected sealed override void ClearValueSubscriptions() => OnValueChanged = null;
}
