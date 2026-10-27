using System;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerController
{
    private readonly record struct Edit(RulerEndpointRole Role, RulerEndpoint? Start, RulerEndpoint? End);
    private Edit? edit;
    public RulerState State { get; private set; }
    public bool IsEditing => edit is not null;
    public (RulerEndpoint? Start, RulerEndpoint? End) CommittedEndpoints =>
        edit is { } baseline ? (baseline.Start, baseline.End) : (State.Start, State.End);

    public event Action<RulerState>? Changed;
    public event Action<RulerNotice>? NoticeRequested;
    public event Action? EndpointsCommitted;

    public void RestoreEndpoints(RulerEndpoint? start, RulerEndpoint? end)
    {
        if (start is { } first && end is { } second && first.Dimension != second.Dimension)
            throw new ArgumentException("Saved ruler endpoints must belong to the same dimension.", nameof(end));
        if (State.Start != start || State.End != end) CancelEndpointEdit();
        SetState(State with { Start = start, End = end });
    }

    public bool BeginEndpointEdit(RulerEndpointRole role)
    {
        ValidateRole(role);
        if (edit is not null || !State.CanPick
            || (role == RulerEndpointRole.Start ? State.Start : State.End) is null) return false;
        edit = new(role, State.Start, State.End);
        return IsEditing;
    }

    public bool PreviewEndpoint(RulerEndpoint endpoint, IBlockAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (edit is not { } baseline) return false;
        var original = baseline.Role == RulerEndpointRole.Start ? baseline.Start : baseline.End;
        if (endpoint.Dimension != original!.Value.Dimension || !endpoint.IsValid(accessor))
            return RejectEndpoint();
        SetState(baseline.Role == RulerEndpointRole.Start
            ? State with { Start = endpoint } : State with { End = endpoint });
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

    public bool TrySetEndpoint(RulerEndpointRole role, RulerEndpoint endpoint, IBlockAccessor accessor)
    {
        ValidateRole(role);
        var (start, end) = CommittedEndpoints;
        return TrySetEndpoints(role == RulerEndpointRole.Start ? endpoint : start,
            role == RulerEndpointRole.End ? endpoint : end, accessor);
    }

    public bool TrySetEndpoints(RulerEndpoint? start, RulerEndpoint? end, IBlockAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (!State.CanPick) return false;
        if ((start is { } first && !first.IsValid(accessor))
            || (end is { } second && !second.IsValid(accessor))
            || (start is { } a && end is { } b && a.Dimension != b.Dimension))
            return RejectEndpoint();
        CancelEndpointEdit();
        SetState(State with { Start = start, End = end });
        EndpointsCommitted?.Invoke();
        return true;
    }

    private bool RejectEndpoint()
    {
        NoticeRequested?.Invoke(RulerNotice.InvalidEndpoint);
        return false;
    }

    private static void ValidateRole(RulerEndpointRole role)
    {
        if (role is not (RulerEndpointRole.Start or RulerEndpointRole.End))
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
        if (State.ShowRuler == visible) return;
        SetState(State with { ShowRuler = visible });
    }

    private void SetState(RulerState next)
    {
        if (State == next) return;
        State = next;
        Changed?.Invoke(next);
    }
}
