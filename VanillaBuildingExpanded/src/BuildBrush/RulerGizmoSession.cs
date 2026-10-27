using System;
using System.Collections.Generic;

using VanillaBuildingExpanded.Gizmos;
using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerGizmoSession : IDisposable, IGizmoCreationInput
{
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly RulerController ruler;
    private readonly System.Func<GizmoPointer, RulerEndpoint?> creationTarget;
    private readonly GizmoManager manager = new();
    private readonly GizmoRenderAdapter renderer;
    private readonly GizmoInputAdapter input;
    private PositionGizmo? start, end, center;
    private bool publishing, syncing, disposed, pending, explicitlyCanceled;
    private bool wasShown, wasEnabled, mismatched;

    internal GizmoManager Manager => manager;
    internal GizmoRenderAdapter Renderer => renderer;
    internal PositionGizmo? Start => start;
    internal PositionGizmo? End => end;
    internal PositionGizmo? Center => center;
    public bool IsPending => pending;

    public RulerGizmoSession(ICoreClientAPI api, RulerController ruler, List<Action<bool>> focusListeners,
        Func<GizmoContext>? capture = null, ILineRenderState? renderState = null)
    {
        this.api = api;
        world = api.World;
        this.ruler = ruler;
        creationTarget = new RulerTerrainTarget(api).Resolve;
        renderer = new(api, manager, capture, renderState);
        input = new(api, manager, renderer, Eligible, focusListeners, this);
        ruler.Changed += OnChanged;
        api.Event.LeaveWorld += Dispose;
        Refresh();
    }

    private bool Eligible() => !disposed && ReferenceEquals(api.World, world)
        && ruler.State.CanPick && ruler.State.ShowRuler && world.Player?.Entity?.Alive == true;

    private bool MatchesDimension() => world.Player?.Entity is { } player
        && (ruler.State.Start is not { } a || a.Dimension == player.Pos.Dimension)
        && (ruler.State.End is not { } b || b.Dimension == player.Pos.Dimension);

    private bool Missing => ruler.State.Start is null || ruler.State.End is null;

    private void OnChanged(RulerState _) => Refresh();

    public void Refresh()
    {
        if (disposed || syncing) return;
        syncing = true;
        try
        {
            var state = ruler.State;
            if ((!wasShown && state.ShowRuler) || (!wasEnabled && state.IsBuildModeEnabled))
                explicitlyCanceled = false;
            wasShown = state.ShowRuler;
            wasEnabled = state.IsBuildModeEnabled;
            if (!publishing && manager.CapturedGizmo is PositionGizmo captured)
            {
                var anchor = ReferenceEquals(captured, center)
                    ? state.Bounds is { } bounds ? RulerGizmoCoordinates.ToCenter(bounds) : (GizmoPosition?)null
                    : ReferenceEquals(captured, start) ? Anchor(state.Start, RulerEndpointRole.Start)
                    : Anchor(state.End, RulerEndpointRole.End);
                if (!ruler.IsEditing || anchor is null || captured.Value != anchor.Value)
                    manager.Cancel();
            }
            Sync(ref start, Anchor(ruler.State.Start, RulerEndpointRole.Start), RulerEndpointRole.Start);
            Sync(ref end, Anchor(ruler.State.End, RulerEndpointRole.End), RulerEndpointRole.End);
            Sync(ref center, start is not null && end is not null && ruler.State.Bounds is { } current
                ? RulerGizmoCoordinates.ToCenter(current) : null, null);
            bool mismatch = !MatchesDimension();
            if (Eligible() && mismatch && !mismatched && ruler.State.Bounds is null)
                Notify("ruler-dimension-mismatch");
            mismatched = mismatch;
            if (!Eligible() || mismatch || !Missing) pending = false;
            renderer.SetActive(Eligible());
            input.Refresh();
        }
        finally { syncing = false; }
    }

    private GizmoPosition? Anchor(RulerEndpoint? cell, RulerEndpointRole role) =>
        cell is { } endpoint && endpoint.IsValid(world.BlockAccessor)
            ? RulerGizmoCoordinates.ToAnchor(endpoint, role) : null;

    private GizmoPosition? ConstrainTranslation(PositionGizmoEdit edit) =>
        ruler.CommittedEndpoints is { Start: { } first, End: { } last }
            ? RulerGizmoCoordinates.ConstrainTranslation(edit, new(first, last), world.BlockAccessor) : null;

    private void Sync(ref PositionGizmo? gizmo, GizmoPosition? position, RulerEndpointRole? role)
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
                    ? RulerGizmoCoordinates.Constrain(edit, endpointRole, world.BlockAccessor)
                    : ConstrainTranslation(edit),
                role == RulerEndpointRole.End ? GizmoShape.Diamond : GizmoShape.Circle);
            instance.OnInteractionStarted += _ =>
            {
                if (!(role is { } endpointRole ? ruler.BeginEndpointEdit(endpointRole) : ruler.BeginTranslation()))
                    manager.Cancel();
            };
            instance.OnValueChanged += value =>
            {
                if (syncing || !ruler.IsEditing) return;
                publishing = true;
                try
                {
                    if (role is { } endpointRole)
                    {
                        if (!RulerGizmoCoordinates.TryDecode(value, endpointRole, world.BlockAccessor, out var selected))
                            throw new InvalidOperationException("The ruler gizmo published an invalid cell.");
                        ruler.PreviewEndpoint(selected, world.BlockAccessor);
                    }
                    else
                    {
                        var (first, last) = ruler.CommittedEndpoints;
                        if (first is null || last is null)
                            throw new InvalidOperationException("Whole-ruler movement requires both endpoints.");
                        var delta = value.Coordinates - RulerGizmoCoordinates.ToCenter(new(first.Value, last.Value)).Coordinates;
                        ruler.PreviewTranslation(checked((long)delta[0]), checked((long)delta[1]),
                            checked((long)delta[2]), world.BlockAccessor);
                    }
                    // A crossed candidate can normalize to unchanged cells without a Changed event.
                    Refresh();
                }
                finally { publishing = false; }
            };
            instance.OnInteractionCompleted += _ => ruler.CompleteEndpointEdit();
            instance.OnInteractionCanceled += _ => ruler.CancelEndpointEdit();
            instance.NoticeRequested += notice => Notify(notice switch
            {
                PositionGizmoNotice.InvalidValue => "ruler-invalid-endpoint",
                _ => "ruler-drag-unavailable"
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
            Notify("ruler-create");
        }
    }

    public void Create(GizmoPointer pointer)
    {
        if (!pending || !Eligible() || !MatchesDimension()) return;
        if (creationTarget(pointer) is not { } cell)
        {
            Notify("ruler-aim-at-block");
            return;
        }
        ruler.TrySetEndpoints(ruler.State.Start ?? cell, ruler.State.End ?? cell, world.BlockAccessor);
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
        ruler.Changed -= OnChanged;
        api.Event.LeaveWorld -= Dispose;
        try { input.Dispose(); }
        finally
        {
            try { manager.Dispose(); }
            finally
            {
                ruler.CancelEndpointEdit();
                renderer.Dispose();
            }
        }
        pending = false;
        start = end = center = null;
    }
}
