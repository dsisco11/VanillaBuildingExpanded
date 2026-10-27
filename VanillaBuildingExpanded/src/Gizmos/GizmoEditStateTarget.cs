using System;
using System.Diagnostics.CodeAnalysis;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Adapts the gizmo's existing baseline and owner's transaction callbacks without another rollback store.</summary>
public sealed class GizmoEditStateTarget : IGizmoEditStateTarget
{
    private enum State { Idle, Beginning, Active, Ending }
    private readonly Gizmo owner;
    private readonly Func<bool> begin;
    private readonly Action complete, cancel;
    private State state;

    /// <summary>Creates a borrowed transaction binding; public gizmo lifecycle notifications remain the manager's responsibility.</summary>
    /// <param name="owner">The gizmo retaining the sole interaction baseline.</param>
    /// <param name="begin">The existing owner's begin path, without preview publication.</param>
    /// <param name="complete">The existing owner's completion path, including its dirty-save/retry semantics.</param>
    /// <param name="cancel">The existing owner's cancellation path; also unwinds a rejected/throwing begin.</param>
    /// <exception cref="ArgumentNullException">An owner or callback is null.</exception>
    public GizmoEditStateTarget(Gizmo owner, Func<bool> begin, Action complete, Action cancel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(begin);
        ArgumentNullException.ThrowIfNull(complete);
        ArgumentNullException.ThrowIfNull(cancel);
        this.owner = owner;
        this.begin = begin;
        this.complete = complete;
        this.cancel = cancel;
    }

    /// <summary>Acquires the existing baseline and initializes one owner edit, unwinding partial initialization on failure.</summary>
    /// <param name="rejection">An explicit lifecycle/owner rejection reason, otherwise null.</param>
    /// <returns>Whether initialization succeeded without reentrant termination or owner invalidation.</returns>
    /// <remarks>Callbacks are not also subscribed to public lifecycle events. Rejected begins invoke cancellation, never completion.</remarks>
    public bool TryBegin([NotNullWhen(false)] out string? rejection)
    {
        rejection = "edit-inactive-or-busy";
        if (state != State.Idle || !owner.TryAcquireEditState(this)) return false;
        state = State.Beginning;
        bool accepted = false;
        try
        {
            accepted = begin() && state == State.Beginning && owner.IsEligible(owner.Dimension);
            if (!accepted) { rejection = "owner-rejected"; return false; }
            state = State.Active;
            rejection = null;
            return true;
        }
        finally
        {
            if (!accepted)
            {
                state = State.Ending;
                try { owner.ReleaseEditState(this, true); }
                finally
                {
                    try { cancel(); }
                    finally
                    {
                        owner.FinishEditState(this);
                        state = State.Idle;
                    }
                }
            }
        }
    }

    /// <summary>Ends the existing owner scope once; commit failures propagate without invented rollback.</summary>
    public void Complete() => End(true);

    /// <summary>Restores once through the notifying setter, then cancels the existing owner scope once.</summary>
    public void Cancel() => End(false);

    private void End(bool commit)
    {
        if (state is State.Idle or State.Ending) return;
        if (state == State.Beginning) { state = State.Ending; return; }
        state = State.Ending;
        try
        {
            if (commit)
            {
                owner.ReleaseEditState(this, false);
                complete();
            }
            else
            {
                try { owner.ReleaseEditState(this, true); }
                finally { cancel(); }
            }
        }
        finally
        {
            owner.FinishEditState(this);
            state = State.Idle;
        }
    }
}
