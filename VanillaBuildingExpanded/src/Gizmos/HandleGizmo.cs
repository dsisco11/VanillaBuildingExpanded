using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Composes owned handles through the existing manager and native input adapter.</summary>
/// <typeparam name="TValue">The authoritative edited value type.</typeparam>
public abstract class HandleGizmo<TValue> : Gizmo<TValue>
{
    private readonly GizmoDragInputBehavior input = new();
    private readonly Dictionary<GizmoHandle, GizmoGeometrySource> geometry = [];
    private readonly Action<GizmoHandleDraw> draw;
    private string? lastRejection;

    /// <summary>Reports a changed rejection reason through the consuming session's existing notice path.</summary>
    public event Action<string>? NoticeRequested;

    /// <summary>Creates a composition without native subscriptions or another global capture owner.</summary>
    /// <param name="initialValue">The initial notifying value.</param>
    /// <param name="draw">The borrowed drawing sink, receiving the same snapshot used for default picking.</param>
    /// <exception cref="ArgumentNullException">The drawing sink is null.</exception>
    protected HandleGizmo(TValue initialValue, Action<GizmoHandleDraw> draw) : base(initialValue)
    {
        ArgumentNullException.ThrowIfNull(draw);
        this.draw = draw;
    }

    /// <inheritdoc />
    protected sealed override bool CancelOnExternalValueChange => true;

    /// <summary>Gets the shared draw/pick source for an attached handle.</summary>
    /// <param name="handle">The exact attached handle.</param>
    /// <returns>The bounded geometry cache used by this composition.</returns>
    /// <exception cref="ArgumentException">The handle is not an actual member.</exception>
    /// <exception cref="ObjectDisposedException">The gizmo is disposed.</exception>
    public GizmoGeometrySource GetGeometrySource(GizmoHandle handle)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(handle);
        if (!ReferenceEquals(handle.Owner, this) || !Handles.TryGetValue(handle.Id, out var member)
            || !ReferenceEquals(member, handle))
            throw new ArgumentException("Geometry requires an attached handle.", nameof(handle));
        if (!geometry.TryGetValue(handle, out var source))
            geometry.Add(handle, source = new(handle));
        return source;
    }

    /// <inheritdoc />
    protected sealed override void RenderCore(GizmoContext context)
    {
        foreach (var detached in geometry.Keys.Where(h => !ReferenceEquals(h.Owner, this)).ToArray())
            geometry.Remove(detached);
        foreach (var handle in Handles.Values.ToArray())
        {
            if (IsDisposed) return;
            if (!ReferenceEquals(handle.Owner, this)) continue;
            if (GetGeometrySource(handle).GetSnapshot(context) is { } snapshot) draw(snapshot);
        }
    }

    /// <inheritdoc />
    protected sealed override IEnumerable<GizmoHit> HitTestCore(GizmoPointer pointer)
    {
        var targets = Handles.Values.Select(h => h.HitTarget).OfType<IGizmoHitTarget>().Distinct().ToArray();
        foreach (var target in targets)
        {
            foreach (var hit in target.HitTest(pointer))
            {
                hit.Validate(this);
                if (hit.Handle is GizmoHandle handle && ReferenceEquals(handle.Owner, this)
                    && Handles.TryGetValue(handle.Id, out var member) && ReferenceEquals(member, handle)
                    && handle.IsEligible(pointer.Context.Dimension))
                    yield return new(handle.Id, hit.Distance, hit.Priority);
            }
        }
    }

    /// <inheritdoc />
    protected sealed override bool BeginInteractionCore(GizmoHandleId handle, GizmoPointer pointer)
    {
        lastRejection = null;
        if (!Handles.TryGetValue(handle, out var member) || member.InteractionTarget is not { } target)
            return Reject("interaction-target-missing");
        return input.TryBegin(target, pointer, out var rejection) || Reject(rejection);
    }

    /// <inheritdoc />
    protected sealed override void UpdateInteractionCore(GizmoPointer pointer)
    {
        if (input.TryUpdate(pointer, out var rejection)) lastRejection = null;
        else Reject(rejection);
    }

    private bool Reject(string reason)
    {
        if (lastRejection != reason)
        {
            lastRejection = reason;
            NoticeRequested?.Invoke(reason);
        }
        return false;
    }

    /// <inheritdoc />
    protected sealed override void CompleteInteractionCore() => input.Complete();

    /// <inheritdoc />
    protected sealed override void CancelInteractionCore() => input.Cancel();

    /// <inheritdoc />
    protected sealed override void DisposeCore()
    {
        try { input.Dispose(); }
        finally
        {
            geometry.Clear();
            NoticeRequested = null;
        }
    }
}
