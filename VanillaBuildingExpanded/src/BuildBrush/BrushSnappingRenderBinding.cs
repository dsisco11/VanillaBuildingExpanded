using System;

using VanillaBuildingExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushSnappingRenderBinding : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly BuildBrushControllerClient controller;
    private readonly BrushTargetMeshCache cache;
    internal BrushSnappingRenderer Renderer { get; }
    private bool registered, disposed, queued, pendingInvalidation, pendingShaderReload;

    public BrushSnappingRenderBinding(ICoreClientAPI api, BuildBrushControllerClient controller,
        IBrushOverlayRenderState? state = null, System.Func<Block, BlockPos, MeshData?>? getMesh = null,
        Func<IBrushSnappingShader>? createShader = null,
        Func<IBrushSnappingChevronShader>? createChevronShader = null)
    {
        this.api = api;
        world = api.World;
        this.controller = controller;
        cache = new BrushTargetMeshCache(api, getMesh);
        Renderer = new BrushSnappingRenderer(api, GetTarget, state, createShader, createChevronShader);
        api.Event.BlockChanged += OnBlockChanged;
        api.Event.ReloadShapes += OnGeometryReload;
        api.Event.ReloadTextures += OnGeometryReload;
        api.Event.BlockTexturesLoaded += OnGeometryReload;
        api.Event.ReloadShader += OnShaderReload;
        api.Event.LeaveWorld += Dispose;
        try
        {
            Register();
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            Renderer.Disable(error);
        }
    }

    private BrushTargetGeometry? GetTarget()
    {
        var player = world.Player?.Entity;
        if (disposed || pendingInvalidation || !ReferenceEquals(api.World, world) || player?.Alive != true)
            return null;
        var selection = world.Player?.CurrentBlockSelection;
        var presentation = controller.GetSnappingPresentation(selection, true);
        if (presentation is null || presentation.TargetPosition.dimension != player.Pos.Dimension)
            return null;
        return cache.GetTarget(presentation);
    }

    private void OnBlockChanged(BlockPos position, Block? oldBlock)
    {
        // MarkBlockDirty synchronously emits this notification for our own capture request.
        if (oldBlock is null && cache.IsRequestingNativeMeshAt(position)) return;
        BlockPos? target = world.Player?.CurrentBlockSelection?.Position;
        if (disposed || !ReferenceEquals(api.World, world) || target is null
            || target.dimension != position.dimension || Math.Abs((long)target.X - position.X) > 1
            || Math.Abs((long)target.Y - position.Y) > 1 || Math.Abs((long)target.Z - position.Z) > 1) return;
        // A world edit at the original target may change its geometry without changing its ID.
        OnGeometryReload();
    }

    private void OnGeometryReload()
    {
        if (disposed) return;
        pendingInvalidation = true;
        UpdateResources();
    }

    private bool OnShaderReload()
    {
        if (disposed) return true;
        pendingInvalidation = true;
        pendingShaderReload = true;
        // Engine reload events normally run outside rendering; never claim deferred compilation succeeded.
        if (Renderer.IsInCallback)
        {
            QueueUpdate();
            return false;
        }
        return UpdateResources();
    }

    private bool UpdateResources()
    {
        if (Renderer.IsInCallback)
        {
            QueueUpdate();
            return false;
        }
        if (disposed)
        {
            Release();
            return false;
        }
        try
        {
            if (pendingInvalidation) cache.Invalidate();
            pendingInvalidation = false;
            bool reload = pendingShaderReload;
            pendingShaderReload = false;
            if (reload)
            {
                if (!Renderer.ReloadShader()) return false;
                Register();
            }
            return true;
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            Renderer.Disable(error);
            return false;
        }
    }

    private void QueueUpdate()
    {
        if (queued) return;
        queued = true;
        api.Event.EnqueueMainThreadTask(() =>
        {
            queued = false;
            UpdateResources();
        }, "vbe-snapping-resources");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.BlockChanged -= OnBlockChanged;
        api.Event.ReloadShapes -= OnGeometryReload;
        api.Event.ReloadTextures -= OnGeometryReload;
        api.Event.BlockTexturesLoaded -= OnGeometryReload;
        api.Event.ReloadShader -= OnShaderReload;
        api.Event.LeaveWorld -= Dispose;
        if (Renderer.IsInCallback) QueueUpdate();
        else Release();
    }

    private void Release()
    {
        try
        {
            try
            {
                if (registered)
                {
                    api.Event.UnregisterRenderer(Renderer, BrushSnappingRenderer.Stage);
                    registered = false;
                }
            }
            finally
            {
                try { Renderer.Dispose(); }
                finally { cache.Dispose(); }
            }
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            Renderer.Disable(error);
        }
    }

    private void Register()
    {
        if (registered) return;
        api.Event.RegisterRenderer(Renderer, BrushSnappingRenderer.Stage, "vbe-snapping");
        registered = true;
    }
}
