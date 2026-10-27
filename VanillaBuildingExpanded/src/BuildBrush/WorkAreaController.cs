using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaController
{
    private readonly record struct Edit(WorkAreaEndpointRole? Role, WorkAreaEndpoint? Start, WorkAreaEndpoint? End);
    public const int MaximumNameLength = 64;
    private readonly List<NamedWorkArea> areas = [];
    private readonly string defaultName;
    private Edit? edit;
    public ReadOnlyCollection<NamedWorkArea> Areas { get; }
    public Guid? SelectedId { get; private set; }
    public WorkAreaState State { get; private set; }
    public bool IsEditing => edit is not null;
    public (WorkAreaEndpoint? Start, WorkAreaEndpoint? End) CommittedEndpoints =>
        edit is { } baseline ? (baseline.Start, baseline.End) : (State.Start, State.End);

    public event Action<WorkAreaState>? Changed;
    public event Action<WorkAreaNotice>? NoticeRequested;
    public event Action? EndpointsCommitted;
    public event Action? AreasCommitted;

    public WorkAreaController(string defaultName = "Work area 1")
    {
        Areas = areas.AsReadOnly();
        RequireName(defaultName);
        this.defaultName = defaultName.Trim();
        var first = new NamedWorkArea(Guid.NewGuid(), this.defaultName, null, null);
        areas.Add(first);
        SelectedId = first.Id;
        State = new(null, null, false, false, false);
    }

    public WorkAreaNameError ValidateName(string? name, Guid? except = null)
    {
        if (name is null || !IsValidName(name.Trim()))
            return WorkAreaNameError.Invalid;
        return areas.Any(area => area.Id != except && string.Equals(area.Name, name.Trim(),
            StringComparison.OrdinalIgnoreCase)) ? WorkAreaNameError.Duplicate : WorkAreaNameError.None;
    }

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name)
        && name == name.Trim() && name.Length <= MaximumNameLength && !name.Any(char.IsControl);

    public Guid CreateArea(string name)
    {
        RequireName(name);
        CancelEndpointEdit();
        var area = new NamedWorkArea(Guid.NewGuid(), name.Trim(), null, null, true);
        areas.Add(area);
        SelectArea(area.Id);
        AreasCommitted?.Invoke();
        return area.Id;
    }

    public void RenameArea(Guid id, string name)
    {
        int index = AreaIndex(id);
        RequireName(name, id);
        if (areas[index].Name == name.Trim()) return;
        areas[index] = areas[index] with { Name = name.Trim() };
        Changed?.Invoke(State);
        AreasCommitted?.Invoke();
    }

    public void DeleteArea(Guid id)
    {
        int index = AreaIndex(id);
        CancelEndpointEdit();
        areas.RemoveAt(index);
        if (SelectedId == id)
        {
            SelectedId = areas.Count == 0 ? null : areas[Math.Min(index, areas.Count - 1)].Id;
            ProjectSelection();
        }
        Changed?.Invoke(State);
        AreasCommitted?.Invoke();
    }

    public void SelectArea(Guid id)
    {
        AreaIndex(id);
        if (SelectedId == id) return;
        CancelEndpointEdit();
        SelectedId = id;
        ProjectSelection();
        Changed?.Invoke(State);
    }

    public void RestoreAreas(IEnumerable<NamedWorkArea> restored)
    {
        ArgumentNullException.ThrowIfNull(restored);
        var entries = restored.ToArray();
        if (entries.Any(area => area.Id == Guid.Empty || !IsValidName(area.Name)
                || (area.Start is { } a && area.End is { } b && a.Dimension != b.Dimension))
            || entries.Select(area => area.Id).Distinct().Count() != entries.Length
            || entries.Select(area => area.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new ArgumentException("Saved work areas require unique IDs, valid unique names and matching endpoint dimensions.",
                nameof(restored));
        CancelEndpointEdit();
        areas.Clear();
        foreach (var area in entries)
        {
            var bounds = area.Bounds;
            areas.Add(area with { Start = bounds?.MinCell ?? area.Start, End = bounds?.MaxCell ?? area.End,
                IsVisible = false });
        }
        SelectedId = areas.Count == 0 ? null : areas[0].Id;
        ProjectSelection();
        Changed?.Invoke(State);
    }

    internal void ResetAreas() => RestoreAreas([new(Guid.NewGuid(), defaultName, null, null)]);

    public IEnumerable<NamedWorkArea> CommittedAreas => areas.Select(area =>
        area.Id == SelectedId && edit is { } baseline
            ? area with { Start = baseline.Start, End = baseline.End } : area);

    private int AreaIndex(Guid id)
    {
        int index = areas.FindIndex(area => area.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException($"Work area '{id}' does not exist.");
    }

    private void RequireName(string name, Guid? except = null)
    {
        if (ValidateName(name, except) != WorkAreaNameError.None)
            throw new ArgumentException("Work area names must be nonempty, unique, control-free and at most 64 characters.",
                nameof(name));
    }

    private void ProjectSelection()
    {
        var selected = areas.Find(area => area.Id == SelectedId);
        State = State with { Start = selected?.Start, End = selected?.End,
            ShowWorkArea = selected?.IsVisible == true, HasActiveArea = selected is not null };
    }

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
        if (SelectedId is not { } id) return;
        SetVisibility(id, visible);
    }

    public void SetVisibility(Guid id, bool visible)
    {
        int index = AreaIndex(id);
        if (SelectedId != id)
        {
            if (areas[index].IsVisible == visible) return;
            areas[index] = areas[index] with { IsVisible = visible };
            Changed?.Invoke(State);
            return;
        }
        if (!visible) CancelEndpointEdit();
        if (State.ShowWorkArea == visible) return;
        SetState(State with { ShowWorkArea = visible });
    }

    private void SetState(WorkAreaState next)
    {
        next = Normalize(next);
        if (State == next) return;
        State = next;
        if (SelectedId is { } id)
        {
            int index = AreaIndex(id);
            areas[index] = areas[index] with { Start = next.Start, End = next.End, IsVisible = next.ShowWorkArea };
        }
        Changed?.Invoke(next);
    }

    private static WorkAreaState Normalize(WorkAreaState state) => state.Bounds is { } bounds
        ? state with { Start = bounds.MinCell, End = bounds.MaxCell } : state;

    private static bool ValidEndpoints(WorkAreaEndpoint? start, WorkAreaEndpoint? end, IBlockAccessor accessor) =>
        (start is not { } first || first.IsValid(accessor))
        && (end is not { } second || second.IsValid(accessor))
        && (start is not { } a || end is not { } b || a.Dimension == b.Dimension);
}
