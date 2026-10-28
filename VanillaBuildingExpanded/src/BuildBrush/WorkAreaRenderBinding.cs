using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaRenderBinding : IDisposable
{
    private const EnumRenderStage Stage = EnumRenderStage.OIT;
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly WorkAreaController workArea;
    private readonly WorkAreaRenderer renderer;
    private readonly System.Func<bool>? deferMembership;
    private readonly List<NamedWorkArea> areas = [];
    private readonly List<WorkAreaBounds> eligibleBounds = [];
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
        areas.Clear();
        foreach (var area in workArea.Areas)
            if (area.Start is { } start && area.End is { } end
                && start.IsValid(world.BlockAccessor) && end.IsValid(world.BlockAccessor) && area.Bounds is not null)
                areas.Add(area);
        Refresh();
    }

    private IReadOnlyList<WorkAreaBounds> GetEligibleBounds()
    {
        var state = workArea.State;
        var player = world.Player?.Entity;
        eligibleBounds.Clear();
        if (!disposed && !failed && ReferenceEquals(api.World, world) && state.CanPick && player?.Alive == true)
            foreach (var area in areas)
                if (area.IsVisible && area.Bounds is { } box && player.Pos.Dimension == box.MinCell.Dimension)
                    eligibleBounds.Add(box);
        return eligibleBounds;
    }

    public void Refresh()
    {
        if (disposed) return;
        var player = world.Player?.Entity;
        bool mismatch = ReferenceEquals(api.World, world) && player is not null
            && areas.Any(area => area.Id == workArea.SelectedId && area.Bounds is { } box
                && player.Pos.Dimension != box.MinCell.Dimension);
        if (mismatch && !mismatched) Notify("workarea-dimension-mismatch");
        mismatched = mismatch;
        bool eligible = GetEligibleBounds().Count != 0;
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
        areas.Clear();
        eligibleBounds.Clear();
        if (InRenderCallback) QueueMembership();
        else Release();
    }

    private void Release()
    {
        try { Unregister(); }
        finally { renderer.Dispose(); }
    }
}
