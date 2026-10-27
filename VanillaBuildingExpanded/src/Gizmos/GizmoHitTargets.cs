using System;
using System.Collections.Generic;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Validates owner-local hits and routes feedback through existing handle state, never a second state store.</summary>
public abstract class GizmoHitTarget : IGizmoHitTarget
{
    /// <summary>Gets the sole owner whose attached handles this target may return.</summary>
    public Gizmo Owner { get; }

    /// <summary>Creates an owner-bound picking target without acquiring input or capture.</summary>
    /// <param name="owner">The expected owner.</param>
    /// <exception cref="ArgumentNullException">The owner is null.</exception>
    protected GizmoHitTarget(Gizmo owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
    }

    /// <summary>Validates, deduplicates and sorts eligible hits by priority, distance, then arbitrary ID.</summary>
    /// <param name="pointer">The pointer in the tested drawing context.</param>
    /// <returns>A materialized list of current owned-handle hits; empty for an ineligible owner.</returns>
    /// <exception cref="ArgumentException">The pointer is invalid.</exception>
    /// <exception cref="InvalidOperationException">The implementation returns malformed, stale or foreign hits.</exception>
    public IEnumerable<GizmoHandleHit> HitTest(GizmoPointer pointer)
    {
        pointer.Validate();
        if (!Owner.IsEligible(pointer.Context.Dimension)) return [];
        Dictionary<GizmoHandleId, GizmoHandleHit> hits = [];
        foreach (var hit in HitTestCore(pointer))
        {
            hit.Validate(Owner);
            if (hit.Handle is not GizmoHandle handle || !Owner.Handles.TryGetValue(handle.Id, out var member)
                || !ReferenceEquals(member, handle))
                throw new InvalidOperationException("Picking must return an actual member of the owner's handle collection.");
            if (!handle.IsEligible(pointer.Context.Dimension)) continue;
            if (!hits.TryGetValue(handle.Id, out var previous) || hit.CompareWithinOwner(previous) < 0)
                hits[handle.Id] = hit;
        }
        var ordered = new List<GizmoHandleHit>();
        foreach (var hit in hits.Values)
        {
            hit.Validate(Owner);
            if (hit.Handle is GizmoHandle member && member.IsEligible(pointer.Context.Dimension))
                ordered.Add(hit);
        }
        ordered.Sort((a, b) => a.CompareWithinOwner(b));
        return ordered;
    }

    /// <summary>Produces raw hits without selecting measurement or application operations.</summary>
    /// <param name="pointer">The validated pointer.</param>
    /// <returns>Hits to validate and arbitrate within this owner.</returns>
    protected abstract IEnumerable<GizmoHandleHit> HitTestCore(GizmoPointer pointer);

    /// <summary>Routes hover feedback to the owner's actual handle reference.</summary>
    /// <param name="handle">The currently attached member.</param>
    /// <param name="hovered">Whether to enter hover; clearing never clears another handle.</param>
    /// <exception cref="ArgumentException">The handle is not an actual member.</exception>
    /// <exception cref="InvalidOperationException">An ineligible handle is being hovered.</exception>
    public void UpdateHover(IGizmoHandle handle, bool hovered)
    {
        var member = RequireMember(handle);
        if (hovered && !member.IsEligible(Owner.Dimension))
            throw new InvalidOperationException("An ineligible handle cannot receive hover.");
        if (hovered) Owner.HoveredHandle = member.Id;
        else if (ReferenceEquals(Owner.HoveredHandleInstance, member)) Owner.HoveredHandle = null;
    }

    /// <summary>Acknowledges the manager's completed capture transition without independently acquiring/releasing it.</summary>
    /// <param name="handle">The currently attached member.</param>
    /// <param name="interacting">The state already published by the owner's interaction lifecycle.</param>
    /// <exception cref="ArgumentException">The handle is not an actual member.</exception>
    /// <exception cref="InvalidOperationException">Feedback precedes or contradicts the capture transition.</exception>
    public void UpdateInteracting(IGizmoHandle handle, bool interacting)
    {
        var member = RequireMember(handle);
        if (ReferenceEquals(Owner.CapturedHandleInstance, member) != interacting)
            throw new InvalidOperationException("Capture feedback must follow the existing owner's lifecycle transition.");
    }

    /// <summary>Rejects ID aliases, detached handles and foreign owners before state feedback.</summary>
    private GizmoHandle RequireMember(IGizmoHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle is not GizmoHandle member || !ReferenceEquals(member.Owner, Owner)
            || !Owner.Handles.TryGetValue(member.Id, out var actual) || !ReferenceEquals(actual, member))
            throw new ArgumentException("Feedback requires an actual attached handle.", nameof(handle));
        return member;
    }
}

/// <summary>Default picking consumes the exact last-published draw geometry, with explicit optional pick padding.</summary>
public sealed class GizmoGeometryHitTarget : GizmoHitTarget
{
    /// <summary>Gets the borrowed drawing source; this target does not rebuild it while picking.</summary>
    public GizmoGeometrySource Source { get; }
    /// <summary>Gets logical-pixel pick padding, GUI-scaled exactly once. The existing default is four.</summary>
    public double PickPadding { get; }
    /// <summary>Gets an explicit priority override, or null for marker-before-arrow priority.</summary>
    public int? Priority { get; }

    /// <summary>Wraps a drawing source without coupling it to movement measurement.</summary>
    /// <param name="owner">The expected owner.</param>
    /// <param name="source">The same source used by drawing.</param>
    /// <param name="pickPadding">Finite nonnegative logical-pixel padding; changes picking only.</param>
    /// <param name="priority">Optional explicit local priority independent of handle ID.</param>
    /// <exception cref="ArgumentNullException">The owner or source is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Padding is negative or nonfinite.</exception>
    /// <exception cref="ArgumentException">The source handle already belongs to another owner.</exception>
    public GizmoGeometryHitTarget(Gizmo owner, GizmoGeometrySource source, double pickPadding = 4,
        int? priority = null) : base(owner)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(pickPadding) || pickPadding < 0)
            throw new ArgumentOutOfRangeException(nameof(pickPadding));
        if (source.Handle.Owner is { } existing && !ReferenceEquals(existing, owner))
            throw new ArgumentException("The drawing source belongs to another owner.", nameof(source));
        Source = source;
        PickPadding = pickPadding;
        Priority = priority;
    }

    /// <summary>Tests only matching last-drawn contexts and currently eligible source handles.</summary>
    /// <param name="pointer">The validated pointer.</param>
    /// <returns>At most one hit from the visible shared snapshot.</returns>
    protected override IEnumerable<GizmoHandleHit> HitTestCore(GizmoPointer pointer)
    {
        if (Source.Snapshot is not { } draw || !ReferenceEquals(draw.Geometry.Context, pointer.Context)
            || !ReferenceEquals(Source.Handle.Owner, Owner) || !Source.Handle.IsEligible(pointer.Context.Dimension))
            return [];
        double padding = PickPadding * pointer.Context.Camera!.GuiScale;
        if (!double.IsFinite(padding))
            throw new InvalidOperationException("GUI-scaled pick padding must be finite.");
        return draw.Geometry.HitDistance(pointer, padding) is { } distance
            ? [new(Owner, Source.Handle, distance, Priority ?? (draw.Geometry.Shape == GizmoShape.Arrow ? 1 : 0))]
            : [];
    }
}

/// <summary>Explicit custom picking geometry/logic, independent of the visible shape and measurement target.</summary>
public sealed class GizmoDelegateHitTarget : GizmoHitTarget
{
    private readonly Func<GizmoPointer, IEnumerable<GizmoHandleHit>> hitTest;

    /// <summary>Creates a custom picker; all results still pass normal ownership/eligibility validation.</summary>
    /// <param name="owner">The expected owner.</param>
    /// <param name="hitTest">Borrowed independent picking logic.</param>
    /// <exception cref="ArgumentNullException">The owner or delegate is null.</exception>
    public GizmoDelegateHitTarget(Gizmo owner, Func<GizmoPointer, IEnumerable<GizmoHandleHit>> hitTest)
        : base(owner)
    {
        ArgumentNullException.ThrowIfNull(hitTest);
        this.hitTest = hitTest;
    }

    /// <summary>Invokes the deliberate custom picker without modifying measurement configuration.</summary>
    /// <param name="pointer">The validated pointer.</param>
    /// <returns>Raw custom hits for shared validation.</returns>
    protected override IEnumerable<GizmoHandleHit> HitTestCore(GizmoPointer pointer) => hitTest(pointer);
}

/// <summary>Immutable owner-local hierarchy of replaceable pickers for arbitrary handle identities.</summary>
public sealed class GizmoAggregateHitTarget : GizmoHitTarget
{
    /// <summary>Gets the protected copied child collection.</summary>
    public IReadOnlyList<IGizmoHitTarget> Targets { get; }

    /// <summary>Copies and validates a hierarchy's children; callers cannot mutate dispatch afterward.</summary>
    /// <param name="owner">The shared owner.</param>
    /// <param name="targets">The child targets, which may themselves be aggregates.</param>
    /// <exception cref="ArgumentNullException">The owner or child collection is null.</exception>
    /// <exception cref="ArgumentException">A child is null or belongs to another owner.</exception>
    public GizmoAggregateHitTarget(Gizmo owner, IEnumerable<IGizmoHitTarget> targets) : base(owner)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var copy = new List<IGizmoHitTarget>();
        foreach (var target in targets)
        {
            if (target is null || !ReferenceEquals(target.Owner, owner))
                throw new ArgumentException("Aggregate targets must share their owner's identity.", nameof(targets));
            copy.Add(target);
        }
        Targets = copy.AsReadOnly();
    }

    /// <summary>Aggregates child hits; shared validation deduplicates handles and applies deterministic local ordering.</summary>
    /// <param name="pointer">The validated pointer.</param>
    /// <returns>Child hits, independent of predefined part identifiers.</returns>
    protected override IEnumerable<GizmoHandleHit> HitTestCore(GizmoPointer pointer)
    {
        foreach (var target in Targets)
            foreach (var hit in target.HitTest(pointer))
                yield return hit;
    }
}
