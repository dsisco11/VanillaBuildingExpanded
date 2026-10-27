using System;
using System.Collections.Generic;

using VanillaBuildingExpanded.Gizmos;
using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaGizmoSession : IDisposable, IGizmoCreationInput
{
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly WorkAreaController workArea;
    private readonly System.Func<GizmoPointer, WorkAreaEndpoint?> creationTarget;
    private readonly GizmoManager manager = new();
    private readonly GizmoRenderAdapter renderer;
    private readonly GizmoInputAdapter input;
    private PositionGizmo? start, end, center;
    private bool publishing, syncing, disposed, pending, explicitlyCanceled;
    private bool wasShown, wasEnabled, mismatched;
    private Guid? selectedId;

    internal GizmoManager Manager => manager;
    internal GizmoRenderAdapter Renderer => renderer;
    internal PositionGizmo? Start => start;
    internal PositionGizmo? End => end;
    internal PositionGizmo? Center => center;
    public bool IsPending => pending;

    public WorkAreaGizmoSession(ICoreClientAPI api, WorkAreaController workArea, List<Action<bool>> focusListeners,
        Func<GizmoContext>? capture = null, ILineRenderState? renderState = null)
    {
        this.api = api;
        world = api.World;
        this.workArea = workArea;
        creationTarget = new WorkAreaTerrainTarget(api).Resolve;
        renderer = new(api, manager, capture, renderState);
        input = new(api, manager, renderer, Eligible, focusListeners, this);
        workArea.Changed += OnChanged;
        api.Event.LeaveWorld += Dispose;
        Refresh();
    }

    private bool Eligible() => !disposed && ReferenceEquals(api.World, world)
        && workArea.State.CanPick && workArea.State.ShowWorkArea && world.Player?.Entity?.Alive == true;

    private bool MatchesDimension() => world.Player?.Entity is { } player
        && (workArea.State.Start is not { } a || a.Dimension == player.Pos.Dimension)
        && (workArea.State.End is not { } b || b.Dimension == player.Pos.Dimension);

    private bool Missing => workArea.State.Start is null || workArea.State.End is null;

    private void OnChanged(WorkAreaState _) => Refresh();

    public void Refresh()
    {
        if (disposed || syncing) return;
        syncing = true;
        try
        {
            var state = workArea.State;
            if (selectedId != workArea.SelectedId)
            {
                selectedId = workArea.SelectedId;
                pending = false;
                explicitlyCanceled = false;
                manager.Cancel();
            }
            if ((!wasShown && state.ShowWorkArea) || (!wasEnabled && state.IsBuildModeEnabled))
                explicitlyCanceled = false;
            wasShown = state.ShowWorkArea;
            wasEnabled = state.IsBuildModeEnabled;
            if (!publishing && manager.CapturedGizmo is PositionGizmo captured)
            {
                var anchor = ReferenceEquals(captured, center)
                    ? state.Bounds is { } bounds ? WorkAreaGizmoCoordinates.ToCenter(bounds) : (GizmoPosition?)null
                    : ReferenceEquals(captured, start) ? Anchor(state.Start, WorkAreaEndpointRole.Start)
                    : Anchor(state.End, WorkAreaEndpointRole.End);
                if (!workArea.IsEditing || anchor is null || captured.Value != anchor.Value)
                    manager.Cancel();
            }
            Sync(ref start, Anchor(workArea.State.Start, WorkAreaEndpointRole.Start), WorkAreaEndpointRole.Start);
            Sync(ref end, Anchor(workArea.State.End, WorkAreaEndpointRole.End), WorkAreaEndpointRole.End);
            Sync(ref center, start is not null && end is not null && workArea.State.Bounds is { } current
                ? WorkAreaGizmoCoordinates.ToCenter(current) : null, null);
            bool mismatch = !MatchesDimension();
            if (Eligible() && mismatch && !mismatched && workArea.State.Bounds is null)
                Notify("workarea-dimension-mismatch");
            mismatched = mismatch;
            if (!Eligible() || mismatch || !Missing) pending = false;
            renderer.SetActive(Eligible());
            input.Refresh();
        }
        finally { syncing = false; }
    }

    private GizmoPosition? Anchor(WorkAreaEndpoint? cell, WorkAreaEndpointRole role) =>
        cell is { } endpoint && endpoint.IsValid(world.BlockAccessor)
            ? WorkAreaGizmoCoordinates.ToAnchor(endpoint, role) : null;

    private GizmoPosition? ConstrainTranslation(PositionGizmoEdit edit) =>
        workArea.CommittedEndpoints is { Start: { } first, End: { } last }
            ? WorkAreaGizmoCoordinates.ConstrainTranslation(edit, new(first, last), world.BlockAccessor) : null;

    private void Sync(ref PositionGizmo? gizmo, GizmoPosition? position, WorkAreaEndpointRole? role)
    {
        if (position is not { } anchor)
        {
            if (gizmo is not null) { manager.Remove(gizmo); gizmo.Dispose(); gizmo = null; }
            return;
        }
        if (gizmo is null)
        {
            var instance = new PositionGizmo(anchor, renderer.Draw,
                edit => role is { } endpointRole
                    ? WorkAreaGizmoCoordinates.Constrain(edit, endpointRole, world.BlockAccessor)
                    : ConstrainTranslation(edit),
                role == WorkAreaEndpointRole.End ? GizmoShape.Diamond : GizmoShape.Circle);
            instance.OnInteractionStarted += _ =>
            {
                if (!(role is { } endpointRole ? workArea.BeginEndpointEdit(endpointRole) : workArea.BeginTranslation()))
                    manager.Cancel();
            };
            instance.OnValueChanged += value =>
            {
                if (syncing || !workArea.IsEditing) return;
                publishing = true;
                try
                {
                    if (role is { } endpointRole)
                    {
                        if (!WorkAreaGizmoCoordinates.TryDecode(value, endpointRole, world.BlockAccessor, out var selected))
                            throw new InvalidOperationException("The work area gizmo published an invalid cell.");
                        workArea.PreviewEndpoint(selected, world.BlockAccessor);
                    }
                    else
                    {
                        var (first, last) = workArea.CommittedEndpoints;
                        if (first is null || last is null)
                            throw new InvalidOperationException("Whole-workarea movement requires both endpoints.");
                        var delta = value.Coordinates - WorkAreaGizmoCoordinates.ToCenter(new(first.Value, last.Value)).Coordinates;
                        workArea.PreviewTranslation(checked((long)delta[0]), checked((long)delta[1]),
                            checked((long)delta[2]), world.BlockAccessor);
                    }
                    // A crossed candidate can normalize to unchanged cells without a Changed event.
                    Refresh();
                }
                finally { publishing = false; }
            };
            instance.OnInteractionCompleted += _ => workArea.CompleteEndpointEdit();
            instance.OnInteractionCanceled += _ => workArea.CancelEndpointEdit();
            instance.NoticeRequested += notice => Notify(notice switch
            {
                PositionGizmoNotice.InvalidValue => "workarea-invalid-endpoint",
                _ => "workarea-drag-unavailable"
            });
            gizmo = instance;
            manager.Add(instance);
        }
        else if (gizmo.Value != anchor) gizmo.Value = anchor;
        gizmo.Visible = Eligible() && MatchesDimension();
    }

    public void Offer()
    {
        if (!explicitlyCanceled && Eligible() && MatchesDimension() && Missing && !pending)
        {
            pending = true;
            Notify("workarea-create");
        }
    }

    public void Create(GizmoPointer pointer)
    {
        if (!pending || !Eligible() || !MatchesDimension()) return;
        if (creationTarget(pointer) is not { } cell)
        {
            Notify("workarea-aim-at-block");
            return;
        }
        workArea.TrySetEndpoints(workArea.State.Start ?? cell, workArea.State.End ?? cell, world.BlockAccessor);
    }

    public void Cancel(bool explicitlyCanceled)
    {
        pending = false;
        this.explicitlyCanceled |= explicitlyCanceled;
    }

    public void HandleWorldAction(EnumEntityAction action, bool on, ref EnumHandling handling) =>
        input.HandleWorldAction(action, on, ref handling);

    private void Notify(string key)
    {
        string code = $"vanillabuildingexpanded:{key}";
        api.TriggerIngameError(this, code, Lang.Get(code));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        workArea.Changed -= OnChanged;
        api.Event.LeaveWorld -= Dispose;
        try { input.Dispose(); }
        finally
        {
            try { manager.Dispose(); }
            finally
            {
                workArea.CancelEndpointEdit();
                renderer.Dispose();
            }
        }
        pending = false;
        start = end = center = null;
    }
}
