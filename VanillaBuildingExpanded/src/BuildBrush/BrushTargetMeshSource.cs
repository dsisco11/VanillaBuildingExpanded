using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushTargetMeshSource : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly System.Func<Block, BlockPos, MeshData?> getMesh;
    private readonly BrushTargetWorldMeshSource? worldMeshes;
    private BlockPos? targetPosition;

    public BrushTargetMeshSource(ICoreClientAPI api, System.Func<Block, BlockPos, MeshData?>? getMesh = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
        EnsureMainThread();
        if (getMesh is null)
        {
            worldMeshes = new BrushTargetWorldMeshSource(api);
            this.getMesh = worldMeshes.GetMesh;
        }
        else this.getMesh = getMesh;
    }

    public void EnsureMainThread()
    {
        int current = Environment.CurrentManagedThreadId;
        if (current != ownerThreadId || (RuntimeEnv.MainThreadId != 0 && current != RuntimeEnv.MainThreadId))
            throw new InvalidOperationException("Brush target mesh resources require the client main thread.");
    }

    public Block? GetTargetBlock(BrushSnappingPresentation presentation)
    {
        EnsureMainThread();
        if (!presentation.HasTangentialSnapping) return null;
        Vec3d hit = presentation.TargetHitPosition;
        if (!double.IsFinite(hit.X) || !double.IsFinite(hit.Y) || !double.IsFinite(hit.Z)) return null;
        BlockPos position = presentation.TargetPosition;
        Block? block = api.World.BlockAccessor.GetBlock(position);
        if (block is null || block.Id == 0 || block.Id != presentation.TargetBlockId) return null;
        targetPosition = position;
        return block;
    }

    public MeshData? GetCapturedMesh(Block block)
    {
        EnsureMainThread();
        return getMesh(block, targetPosition
            ?? throw new InvalidOperationException("No current snapping target."));
    }

    public void Invalidate() => worldMeshes?.Invalidate();
    public bool IsRequestingNativeMeshAt(BlockPos position) => worldMeshes?.IsRequestingMeshAt(position) == true;

    public MeshRef Upload(MeshData data)
    {
        EnsureMainThread();
        return api.Render.UploadMesh(data)
            ?? throw new InvalidOperationException("Brush target mesh upload returned no mesh.");
    }

    public void Dispose() => worldMeshes?.Dispose();
}
