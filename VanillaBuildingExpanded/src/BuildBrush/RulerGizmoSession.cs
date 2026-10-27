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
    private readonly System.Func<GizmoPointer, RulerEndpoint?> target;
    private readonly GizmoManager manager = new();
    private readonly GizmoRenderAdapter renderer;
    private readonly GizmoInputAdapter input;
    private PositionGizmo? start, end;
    private bool publishing, syncing, disposed, pending, explicitlyCanceled;
    private bool wasShown, wasEnabled, mismatched;

    internal GizmoManager Manager => manager;
    internal GizmoRenderAdapter Renderer => renderer;
    internal PositionGizmo? Start => start;
    internal PositionGizmo? End => end;
    public bool IsPending => pending;

    public RulerGizmoSession(ICoreClientAPI api, RulerController ruler, List<Action<bool>> focusListeners,
        Func<GizmoContext>? capture = null, ILineRenderState? renderState = null)
    {
        this.api = api;
        world = api.World;
        this.ruler = ruler;
        target = new RulerTerrainTarget(api).Resolve;
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
                var cell = ReferenceEquals(captured, start) ? state.Start : state.End;
                var role = ReferenceEquals(captured, start) ? RulerEndpointRole.Start : RulerEndpointRole.End;
                if (!ruler.IsEditing || cell is null || captured.Value != RulerGizmoCoordinates.ToAnchor(cell.Value, role))
                    manager.Cancel();
            }
            Sync(ref start, ruler.State.Start, RulerEndpointRole.Start);
            Sync(ref end, ruler.State.End, RulerEndpointRole.End);
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

    private void Sync(ref PositionGizmo? gizmo, RulerEndpoint? cell, RulerEndpointRole role)
    {
        if (cell is not { } endpoint || !endpoint.IsValid(world.BlockAccessor))
        {
            if (gizmo is not null) { manager.Remove(gizmo); gizmo.Dispose(); gizmo = null; }
            return;
        }
        var anchor = RulerGizmoCoordinates.ToAnchor(endpoint, role);
        if (gizmo is null)
        {
            var instance = new PositionGizmo(anchor, renderer.Draw,
                pointer => target(pointer) is { } selected ? RulerGizmoCoordinates.ToAnchor(selected, role) : null,
                edit => RulerGizmoCoordinates.Constrain(edit, role, world.BlockAccessor),
                role == RulerEndpointRole.Start ? GizmoShape.Circle : GizmoShape.Diamond);
            instance.OnInteractionStarted += _ =>
            {
                if (!ruler.BeginEndpointEdit(role)) manager.Cancel();
            };
            instance.OnValueChanged += value =>
            {
                if (syncing || !ruler.IsEditing) return;
                publishing = true;
                try
                {
                    if (!RulerGizmoCoordinates.TryDecode(value, role, world.BlockAccessor, out var selected))
                        throw new InvalidOperationException("The ruler gizmo published an invalid cell.");
                    ruler.PreviewEndpoint(selected, world.BlockAccessor);
                }
                finally { publishing = false; }
            };
            instance.OnInteractionCompleted += _ => ruler.CompleteEndpointEdit();
            instance.OnInteractionCanceled += _ => ruler.CancelEndpointEdit();
            instance.NoticeRequested += notice => Notify(notice switch
            {
                PositionGizmoNotice.MissingTarget => "ruler-aim-at-block",
                PositionGizmoNotice.InvalidValue => "ruler-invalid-endpoint",
                _ => "ruler-axis-unavailable"
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
        if (target(pointer) is not { } cell)
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
        start = end = null;
    }
}
