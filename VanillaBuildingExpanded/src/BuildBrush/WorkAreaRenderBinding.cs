using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaRenderBinding : IDisposable
{
    private const EnumRenderStage Stage = EnumRenderStage.Opaque;
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly WorkAreaController workArea;
    private readonly WorkAreaRenderer renderer;
    private readonly System.Func<bool>? deferMembership;
    private WorkAreaBounds? bounds;
    private bool registered;
    private bool mismatched;
    private bool failed;
    private bool disposed;
    private bool queued;

    public WorkAreaRenderBinding(ICoreClientAPI api, WorkAreaController workArea, IWorkAreaRenderState? renderState = null,
        System.Func<bool>? deferMembership = null)
    {
        this.api = api;
        world = api.World;
        this.workArea = workArea;
        this.deferMembership = deferMembership;
        renderer = new WorkAreaRenderer(api, GetEligibleBounds, OnFailure, renderState);
        workArea.Changed += OnChanged;
        api.Event.PlayerDimensionChanged += OnDimensionChanged;
        OnChanged(workArea.State);
    }

    private void OnChanged(WorkAreaState state)
    {
        if (disposed) return;
        bounds = state.Start is { } start && state.End is { } end
            && start.IsValid(world.BlockAccessor) && end.IsValid(world.BlockAccessor) ? state.Bounds : null;
        Refresh();
    }

    private WorkAreaBounds? GetEligibleBounds()
    {
        var state = workArea.State;
        var player = world.Player?.Entity;
        return !disposed && !failed && ReferenceEquals(api.World, world)
            && state.CanPick && state.ShowWorkArea && player?.Alive == true
            && bounds is { } box && player.Pos.Dimension == box.MinCell.Dimension ? box : null;
    }

    public void Refresh()
    {
        if (disposed) return;
        var player = world.Player?.Entity;
        bool mismatch = ReferenceEquals(api.World, world) && player is not null
            && bounds is { } box && player.Pos.Dimension != box.MinCell.Dimension;
        if (mismatch && !mismatched) Notify("workarea-dimension-mismatch");
        mismatched = mismatch;
        bool eligible = GetEligibleBounds() is not null;
        if (eligible == registered) return;
        if (InRenderCallback) { QueueMembership(); return; }
        try
        {
            if (eligible)
            {
                api.Event.RegisterRenderer(renderer, Stage, "vbe-workarea");
                registered = true;
            }
            else Unregister();
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            OnFailure(error);
        }
    }

    private bool InRenderCallback => renderer.IsInCallback || deferMembership?.Invoke() == true;

    private void QueueMembership()
    {
        if (queued) return;
        queued = true;
        api.Event.EnqueueMainThreadTask(() =>
        {
            queued = false;
            if (disposed) Release();
            else Refresh();
        }, "vbe-workarea-render-membership");
    }

    private void OnDimensionChanged(IPlayer player)
    {
        if (disposed || player != world.Player) return;
        workArea.CancelEndpointEdit();
        Refresh();
    }

    private void OnFailure(Exception error)
    {
        if (failed || disposed) return;
        failed = true;
        api.Logger.Error("[WorkArea] Wireframe unavailable for this session: {0}", error);
        Notify("workarea-render-unavailable");
        // Native render-stage iteration indexes the live list, including after callbacks.
        api.Event.EnqueueMainThreadTask(() =>
        {
            if (!disposed) Refresh();
        }, "vbe-workarea-render-failure");
    }

    private void Notify(string key)
    {
        string code = $"vanillabuildingexpanded:{key}";
        api.TriggerIngameError(this, code, Lang.Get(code));
    }

    private void Unregister()
    {
        if (!registered) return;
        api.Event.UnregisterRenderer(renderer, Stage);
        registered = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        workArea.Changed -= OnChanged;
        api.Event.PlayerDimensionChanged -= OnDimensionChanged;
        bounds = null;
        if (InRenderCallback) QueueMembership();
        else Release();
    }

    private void Release()
    {
        try { Unregister(); }
        finally { renderer.Dispose(); }
    }
}
