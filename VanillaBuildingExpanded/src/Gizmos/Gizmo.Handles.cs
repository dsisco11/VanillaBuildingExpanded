using System;
using System.Collections.Generic;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Controls persistent handle membership and synchronizes its actual hover/capture references.</summary>
public abstract partial class Gizmo
{
    /// <summary>The owner's persistent handle membership.</summary>
    private readonly Dictionary<GizmoHandleId, GizmoHandle> handles = [];
    /// <summary>The public hover identity synchronized with the actual owned reference.</summary>
    private GizmoHandleId? hoveredHandleId;
    /// <summary>The public capture identity synchronized with the actual owned reference.</summary>
    private GizmoHandleId? capturedHandleId;
    /// <summary>The actual hovered instance.</summary>
    private GizmoHandle? hoveredHandleInstance;
    /// <summary>The actual captured instance.</summary>
    private GizmoHandle? capturedHandleInstance;

    /// <summary>Gets the protected owner-local handle map.</summary>
    public IReadOnlyDictionary<GizmoHandleId, GizmoHandle> Handles { get; }
    /// <summary>Gets the hovered owned instance, or null for no hover.</summary>
    public GizmoHandle? HoveredHandleInstance => hoveredHandleInstance;
    /// <summary>Gets the captured owned instance, or null for no capture.</summary>
    public GizmoHandle? CapturedHandleInstance => capturedHandleInstance;
    /// <summary>Includes initialization so reentrant configuration changes terminate the prepared session.</summary>
    internal bool IsInteractionHandle(GizmoHandle handle) => ReferenceEquals(capturedHandleInstance, handle)
        || ReferenceEquals(initializingHandle, handle);
    /// <summary>Forwards current members' invalidation to owner-level consumers without changing global capture.</summary>
    internal event Action<GizmoHandle, GizmoHandleInvalidation>? HandleInvalidated;

    /// <summary>Attaches a validated detached instance; repeated attachment is an error, not a no-op.</summary>
    /// <param name="handle">The handle whose configuration remains owned by this gizmo until detachment.</param>
    /// <exception cref="ArgumentNullException">The handle is null.</exception>
    /// <exception cref="ArgumentException">The ID is duplicated or target configuration has a foreign owner.</exception>
    /// <exception cref="InvalidOperationException">The instance is already attached.</exception>
    /// <exception cref="ObjectDisposedException">The gizmo or handle is disposed.</exception>
    public void AddHandle(GizmoHandle handle)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(handle);
        ObjectDisposedException.ThrowIf(handle.IsDisposed, handle);
        if (handle.Owner is not null)
            throw new InvalidOperationException("Detach a handle before attaching or transferring it.");
        if (handles.ContainsKey(handle.Id))
            throw new ArgumentException("Handle IDs must be unique within their owner.", nameof(handle));
        handle.ValidateAttachment(this);
        handles.Add(handle.Id, handle);
        handle.SetOwner(this);
        handle.Invalidated += OnHandleInvalidated;
        HandleInvalidated?.Invoke(handle, GizmoHandleInvalidation.Geometry | GizmoHandleInvalidation.Visual);
    }

    /// <summary>Cancels before detaching, leaving the instance reusable and removing only owner subscriptions.</summary>
    /// <param name="handle">The exact member instance to detach.</param>
    /// <returns>Whether this instance belonged to the owner when removal began.</returns>
    /// <exception cref="ArgumentNullException">The handle is null.</exception>
    public bool RemoveHandle(GizmoHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!OwnsHandle(handle)) return false;
        try
        {
            if (IsInteractionHandle(handle)) CancelInteraction();
        }
        finally
        {
            try
            {
                if (ReferenceEquals(hoveredHandleInstance, handle)) HoveredHandle = null;
            }
            finally
            {
                if (OwnsHandle(handle))
                {
                    handle.Invalidated -= OnHandleInvalidated;
                    handles.Remove(handle.Id);
                    handle.SetOwner(null);
                    HandleInvalidated?.Invoke(handle, GizmoHandleInvalidation.Geometry
                        | GizmoHandleInvalidation.Visual | GizmoHandleInvalidation.LabelTexture);
                }
            }
        }
        return true;
    }

    /// <summary>Checks membership by identity as well as ID, including after reentrant callbacks.</summary>
    /// <param name="handle">The expected instance.</param>
    /// <returns>Whether the exact instance remains attached.</returns>
    private bool OwnsHandle(GizmoHandle handle) => ReferenceEquals(handle.Owner, this)
        && handles.TryGetValue(handle.Id, out var member) && ReferenceEquals(member, handle);

    /// <summary>Clears hover before a member becomes unavailable to picking.</summary>
    /// <param name="handle">The member whose local eligibility changed.</param>
    internal void ClearHandleHover(GizmoHandle handle)
    {
        if (ReferenceEquals(hoveredHandleInstance, handle)) HoveredHandle = null;
    }

    /// <summary>Updates ID/reference pairs atomically before notifying affected handle state.</summary>
    /// <param name="identity">The corresponding public identity field.</param>
    /// <param name="instance">The corresponding actual instance field.</param>
    /// <param name="value">The new identity, or null to clear state.</param>
    /// <param name="interacting">Whether to acknowledge capture instead of hover.</param>
    private void SetHandleState(ref GizmoHandleId? identity, ref GizmoHandle? instance, GizmoHandleId? value,
        bool interacting = false)
    {
        if (identity == value) return;
        GizmoHandle? next = null;
        if (value is { } id
            && (!handles.TryGetValue(id, out next) || !next.IsEligible(Dimension)))
        {
            value = null;
            next = null;
        }
        var previous = instance;
        var previousState = previous?.AppearanceState;
        var nextState = next?.AppearanceState;
        identity = value;
        instance = next;
        if (previous is not null && previousState is { } oldState) previous.StateChanged(oldState);
        if (next is not null && nextState is { } newState && ReferenceEquals(instance, next))
            next.StateChanged(newState);
        if (previous is not null && OwnsHandle(previous) && !ReferenceEquals(instance, previous))
        {
            if (interacting) previous.HitTarget?.UpdateInteracting(previous, false);
            else previous.HitTarget?.UpdateHover(previous, false);
        }
        if (next is not null && OwnsHandle(next) && ReferenceEquals(instance, next))
        {
            if (interacting) next.HitTarget?.UpdateInteracting(next, true);
            else next.HitTarget?.UpdateHover(next, true);
        }
    }

    /// <summary>Forwards notifications only while the emitting instance is still a member.</summary>
    /// <param name="handle">The emitter.</param>
    /// <param name="change">The changed categories.</param>
    private void OnHandleInvalidated(GizmoHandle handle, GizmoHandleInvalidation change)
    {
        if (OwnsHandle(handle)) HandleInvalidated?.Invoke(handle, change);
    }

    /// <summary>Disposes remaining owned handles, attempting all cleanup before surfacing failures.</summary>
    /// <exception cref="AggregateException">One or more handle cleanup callbacks failed.</exception>
    private void DisposeHandles()
    {
        List<Exception>? errors = null;
        foreach (var handle in new List<GizmoHandle>(handles.Values))
        {
            try { handle.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is not null) throw new AggregateException("Gizmo handle cleanup failed.", errors);
    }
}
