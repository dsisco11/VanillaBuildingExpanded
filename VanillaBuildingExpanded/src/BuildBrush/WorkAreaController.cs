using System;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaController
{
    private readonly record struct Edit(WorkAreaEndpointRole? Role, WorkAreaEndpoint? Start, WorkAreaEndpoint? End);
    private Edit? edit;
    public WorkAreaState State { get; private set; }
    public bool IsEditing => edit is not null;
    public (WorkAreaEndpoint? Start, WorkAreaEndpoint? End) CommittedEndpoints =>
        edit is { } baseline ? (baseline.Start, baseline.End) : (State.Start, State.End);

    public event Action<WorkAreaState>? Changed;
    public event Action<WorkAreaNotice>? NoticeRequested;
    public event Action? EndpointsCommitted;

    public void RestoreEndpoints(WorkAreaEndpoint? start, WorkAreaEndpoint? end)
    {
        if (start is { } first && end is { } second && first.Dimension != second.Dimension)
            throw new ArgumentException("Saved work area endpoints must belong to the same dimension.", nameof(end));
        var next = Normalize(State with { Start = start, End = end });
        if (State.Start != next.Start || State.End != next.End) CancelEndpointEdit();
        SetState(State with { Start = next.Start, End = next.End });
    }

    public bool BeginEndpointEdit(WorkAreaEndpointRole role)
    {
        ValidateRole(role);
        if (edit is not null || !State.CanPick
            || (role == WorkAreaEndpointRole.Start ? State.Start : State.End) is null) return false;
        edit = new(role, State.Start, State.End);
        return IsEditing;
    }

    public bool PreviewEndpoint(WorkAreaEndpoint endpoint, IBlockAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (edit is not { Role: not null } baseline) return false;
        var original = baseline.Role == WorkAreaEndpointRole.Start ? baseline.Start : baseline.End;
        if (endpoint.Dimension != original!.Value.Dimension || !endpoint.IsValid(accessor))
            return RejectEndpoint();
        var next = Normalize(baseline.Role == WorkAreaEndpointRole.Start
            ? State with { Start = endpoint, End = baseline.End }
            : State with { Start = baseline.Start, End = endpoint });
        if (!ValidEndpoints(next.Start, next.End, accessor)) return RejectEndpoint();
        SetState(next);
        return true;
    }

    public bool BeginTranslation()
    {
        if (edit is not null || !State.CanPick || State.Bounds is null) return false;
        edit = new(null, State.Start, State.End);
        return IsEditing;
    }

    public bool PreviewTranslation(long x, long y, long z, IBlockAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (edit is not { Role: null, Start: { } start, End: { } end }) return false;
        if (!start.TryTranslate(x, y, z, accessor, out var first)
            || !end.TryTranslate(x, y, z, accessor, out var last)) return RejectEndpoint();
        SetState(State with { Start = first, End = last });
        return true;
    }

    public bool CompleteEndpointEdit()
    {
        if (edit is null) return false;
        edit = null;
        // An explicit unchanged completion also lets persistence retry an earlier dirty commit.
        EndpointsCommitted?.Invoke();
        return true;
    }

    public void CancelEndpointEdit()
    {
        if (edit is not { } baseline) return;
        edit = null;
        SetState(State with { Start = baseline.Start, End = baseline.End });
    }

    public bool TrySetEndpoint(WorkAreaEndpointRole role, WorkAreaEndpoint endpoint, IBlockAccessor accessor)
    {
        ValidateRole(role);
        var (start, end) = CommittedEndpoints;
        return TrySetEndpoints(role == WorkAreaEndpointRole.Start ? endpoint : start,
            role == WorkAreaEndpointRole.End ? endpoint : end, accessor);
    }

    public bool TrySetEndpoints(WorkAreaEndpoint? start, WorkAreaEndpoint? end, IBlockAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (!State.CanPick) return false;
        var next = Normalize(State with { Start = start, End = end });
        if (!ValidEndpoints(start, end, accessor) || !ValidEndpoints(next.Start, next.End, accessor))
            return RejectEndpoint();
        CancelEndpointEdit();
        SetState(State with { Start = next.Start, End = next.End });
        EndpointsCommitted?.Invoke();
        return true;
    }

    private bool RejectEndpoint()
    {
        NoticeRequested?.Invoke(WorkAreaNotice.InvalidEndpoint);
        return false;
    }

    private static void ValidateRole(WorkAreaEndpointRole role)
    {
        if (role is not (WorkAreaEndpointRole.Start or WorkAreaEndpointRole.End))
            throw new ArgumentOutOfRangeException(nameof(role));
    }

    public void SetContext(bool buildModeEnabled, bool playerAlive)
    {
        if (!buildModeEnabled || !playerAlive) CancelEndpointEdit();
        SetState(State with
        {
            IsBuildModeEnabled = buildModeEnabled,
            IsPlayerAlive = playerAlive
        });
    }

    public void SetVisibility(bool visible)
    {
        if (!visible) CancelEndpointEdit();
        if (State.ShowWorkArea == visible) return;
        SetState(State with { ShowWorkArea = visible });
    }

    private void SetState(WorkAreaState next)
    {
        next = Normalize(next);
        if (State == next) return;
        State = next;
        Changed?.Invoke(next);
    }

    private static WorkAreaState Normalize(WorkAreaState state) => state.Bounds is { } bounds
        ? state with { Start = bounds.MinCell, End = bounds.MaxCell } : state;

    private static bool ValidEndpoints(WorkAreaEndpoint? start, WorkAreaEndpoint? end, IBlockAccessor accessor) =>
        (start is not { } first || first.IsValid(accessor))
        && (end is not { } second || second.IsValid(accessor))
        && (start is not { } a || end is not { } b || a.Dimension == b.Dimension);
}
