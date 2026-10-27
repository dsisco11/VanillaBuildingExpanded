namespace VanillaBuildingExpanded.Gizmos;

public abstract partial class Gizmo
{
    private GizmoEditStateTarget? editStateTarget;
    private bool baselineSaved;

    /// <summary>Shares the existing baseline with a composed session, never overwriting an accepted drag's start value.</summary>
    internal bool TryAcquireEditState(GizmoEditStateTarget target)
    {
        if (editStateTarget is not null || ending || !IsEligible(Dimension)) return false;
        SaveEditBaseline();
        editStateTarget = target;
        return true;
    }

    /// <summary>Restores through the existing notifying rollback path at most once, retaining the terminal reentrancy guard.</summary>
    internal void ReleaseEditState(GizmoEditStateTarget target, bool restore)
    {
        if (!ReferenceEquals(editStateTarget, target)) return;
        if (restore) RestoreEditBaseline();
        else baselineSaved = false;
    }

    /// <summary>Releases the owner guard after all rollback/owner callbacks, including throwing callbacks.</summary>
    internal void FinishEditState(GizmoEditStateTarget target)
    {
        if (ReferenceEquals(editStateTarget, target)) editStateTarget = null;
    }

    private void SaveEditBaseline()
    {
        if (baselineSaved) return;
        SaveInteractionValue();
        baselineSaved = true;
    }

    private void RestoreEditBaseline()
    {
        if (!baselineSaved) return;
        baselineSaved = false;
        IsRestoringInteractionValue = true;
        try { RestoreInteractionValue(); }
        finally { IsRestoringInteractionValue = false; }
    }
}
