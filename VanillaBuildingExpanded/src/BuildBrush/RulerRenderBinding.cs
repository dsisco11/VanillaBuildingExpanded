using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerRenderBinding : IDisposable
{
    private const EnumRenderStage Stage = EnumRenderStage.Opaque;
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly RulerController ruler;
    private readonly RulerRenderer renderer;
    private RulerBounds? bounds;
    private bool registered;
    private bool mismatched;
    private bool failed;
    private bool disposed;

    public RulerRenderBinding(ICoreClientAPI api, RulerController ruler, IRulerRenderState? renderState = null)
    {
        this.api = api;
        world = api.World;
        this.ruler = ruler;
        renderer = new RulerRenderer(api, GetEligibleBounds, OnFailure, renderState);
        ruler.Changed += OnChanged;
        api.Event.PlayerDimensionChanged += OnDimensionChanged;
        OnChanged(ruler.State);
    }

    private void OnChanged(RulerState state)
    {
        if (disposed) return;
        bounds = state.Start is { } start && state.End is { } end
            && start.IsValid(world.BlockAccessor) && end.IsValid(world.BlockAccessor) ? state.Bounds : null;
        Refresh();
    }

    private RulerBounds? GetEligibleBounds()
    {
        var state = ruler.State;
        var player = world.Player?.Entity;
        return !disposed && !failed && ReferenceEquals(api.World, world)
            && state.CanPick && state.ShowRuler && player?.Alive == true
            && bounds is { } box && player.Pos.Dimension == box.MinCell.Dimension ? box : null;
    }

    public void Refresh()
    {
        if (disposed) return;
        var player = world.Player?.Entity;
        bool mismatch = ReferenceEquals(api.World, world) && player is not null
            && bounds is { } box && player.Pos.Dimension != box.MinCell.Dimension;
        if (mismatch && !mismatched) Notify("ruler-dimension-mismatch");
        mismatched = mismatch;
        bool eligible = GetEligibleBounds() is not null;
        if (eligible == registered) return;
        try
        {
            if (eligible)
            {
                api.Event.RegisterRenderer(renderer, Stage, "vbe-ruler");
                registered = true;
            }
            else Unregister();
        }
        catch (Exception error) when (RulerRenderer.IsRenderFailure(error))
        {
            OnFailure(error);
        }
    }

    private void OnDimensionChanged(IPlayer player)
    {
        if (disposed || player != world.Player) return;
        ruler.CancelPicking();
        Refresh();
    }

    private void OnFailure(Exception error)
    {
        if (failed || disposed) return;
        failed = true;
        api.Logger.Error("[Ruler] Wireframe unavailable for this session: {0}", error);
        Notify("ruler-render-unavailable");
        // Native render-stage iteration indexes the live list, including after callbacks.
        api.Event.EnqueueMainThreadTask(() =>
        {
            if (!disposed) Refresh();
        }, "vbe-ruler-render-failure");
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
        ruler.Changed -= OnChanged;
        api.Event.PlayerDimensionChanged -= OnDimensionChanged;
        bounds = null;
        try { Unregister(); }
        finally { renderer.Dispose(); }
    }
}
