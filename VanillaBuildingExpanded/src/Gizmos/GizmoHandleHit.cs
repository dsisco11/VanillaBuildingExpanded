using System;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>A validated owner/handle reference and hit rank, independent of the handle's interaction semantics.</summary>
public readonly record struct GizmoHandleHit
{
    /// <summary>Gets the owner at the time of the hit; ownership is rechecked before consuming it.</summary>
    public Gizmo Owner { get; }

    /// <summary>Gets the actual attached handle instance, not an ID-to-operation mapping.</summary>
    public IGizmoHandle Handle { get; }

    /// <summary>Gets the positive finite distance along the normalized pointer ray.</summary>
    public double Distance { get; }

    /// <summary>Gets the local pick priority; lower values win before distance.</summary>
    public int Priority { get; }

    /// <summary>Creates a hit for an attached handle.</summary>
    /// <param name="owner">The expected owning gizmo.</param>
    /// <param name="handle">The owned handle instance.</param>
    /// <param name="distance">The positive finite ray distance.</param>
    /// <param name="priority">The local priority, such as center before arrow roots.</param>
    /// <exception cref="ArgumentNullException">An owner or handle is null.</exception>
    /// <exception cref="ArgumentException">The handle is detached or belongs to another owner.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The distance is nonpositive or nonfinite.</exception>
    public GizmoHandleHit(Gizmo owner, IGizmoHandle handle, double distance, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handle);
        if (!ReferenceEquals(handle.Owner, owner))
            throw new ArgumentException("A hit must reference a handle attached to its owner.", nameof(handle));
        if (!double.IsFinite(distance) || distance <= 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        Owner = owner;
        Handle = handle;
        Distance = distance;
        Priority = priority;
    }

    /// <summary>Rejects default, stale or foreign-owner hits before arbitration or capture.</summary>
    /// <param name="owner">The owner whose hit target produced this result.</param>
    /// <exception cref="InvalidOperationException">The hit or its current ownership is invalid.</exception>
    public void Validate(Gizmo owner)
    {
        if (Owner is null || Handle is null || !ReferenceEquals(Owner, owner)
            || !ReferenceEquals(Handle.Owner, owner) || !double.IsFinite(Distance) || Distance <= 0)
            throw new InvalidOperationException("Hit targets must return valid, currently owned handle hits.");
    }

    /// <summary>Compares hits within one owner by priority, distance, then stable handle ID.</summary>
    /// <param name="other">Another valid hit from the same owner.</param>
    /// <returns>A negative value if this hit wins, zero for equal ranks, otherwise a positive value.</returns>
    /// <exception cref="InvalidOperationException">Either hit is invalid or the owners differ.</exception>
    /// <remarks>Cross-owner depth-bin/insertion ordering remains the manager's responsibility.</remarks>
    public int CompareWithinOwner(GizmoHandleHit other)
    {
        Validate(Owner);
        other.Validate(Owner);
        int priority = Priority.CompareTo(other.Priority);
        if (priority != 0) return priority;
        int distance = Distance.CompareTo(other.Distance);
        return distance != 0 ? distance : Handle.Id.Value.CompareTo(other.Handle.Id.Value);
    }
}
