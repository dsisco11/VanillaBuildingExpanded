using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushTargetMeshSource
{
    private readonly ICoreClientAPI api;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;

    public BrushTargetMeshSource(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
        EnsureMainThread();
    }

    public void EnsureMainThread()
    {
        int current = Environment.CurrentManagedThreadId;
        if (current != ownerThreadId || (RuntimeEnv.MainThreadId != 0 && current != RuntimeEnv.MainThreadId))
            throw new InvalidOperationException("Brush target mesh resources require the client main thread.");
    }

    public Block? GetSupportedBlock(BrushSnappingPresentation presentation)
    {
        EnsureMainThread();
        if (presentation.Result.Outcome != BrushPlacementOutcome.AcceptedSnapping)
            return null;

        BlockPos position = presentation.TargetPosition;
        IBlockAccessor blocks = api.World.BlockAccessor;
        Block? block = blocks.GetBlock(position);
        if (block is null || block.Id == 0 || block.IsMissing || block.Id != presentation.TargetBlockId
            || block.GetType() != typeof(Block) || block.EntityClass is not null
            || block.BlockBehaviors.Length != 0 || block.CollectibleBehaviors.Length != 0
            || blocks.GetBlockEntity(position) is not null
            || !IsSupportedRenderPass(block.RenderPass)
            || block.DrawType is not (EnumDrawType.Cube or EnumDrawType.JSON)
            || block.RandomDrawOffset != 0 || block.RandomizeRotations || block.RandomSizeAdjust != 0
            || block.HasAlternates || block.HasTiles
            || block.Lod0Shape is not null || block.Lod2Shape is not null
            || block.Lod0Mesh is not null || block.Lod2Mesh is not null || block.DoNotRenderAtLod2
            || (block.VertexFlags is not null && (block.VertexFlags.WindMode != EnumWindBitMode.NoWind
                || block.VertexFlags.ZOffset != 0)))
            return null;

        CompositeShape? shape = block.Shape;
        if (shape?.Base is null || shape.Format != EnumShapeFormat.VintageStory || shape.VoxelizeTexture
            || shape.Alternates?.Length > 0 || shape.BakedAlternates?.Length > 0 || shape.Overlays?.Length > 0)
            return null;

        var decors = blocks.GetSubDecors(position);
        if (decors is not null)
        {
            foreach (Block? decor in decors.Values)
                if (decor is not null && decor.Id != 0) return null;
        }
        return block;
    }

    public MeshData? GetBorrowedMesh(Block block)
    {
        EnsureMainThread();
        return api.TesselatorManager.GetDefaultBlockMesh(block);
    }

    internal static bool IsSupportedRenderPass(EnumChunkRenderPass pass) =>
        pass is EnumChunkRenderPass.Opaque or EnumChunkRenderPass.OpaqueNoCull
            or EnumChunkRenderPass.BlendNoCull or EnumChunkRenderPass.Transparent;

    public MeshRef Upload(MeshData data)
    {
        EnsureMainThread();
        return api.Render.UploadMesh(data)
            ?? throw new InvalidOperationException("Brush target mesh upload returned no mesh.");
    }
}
