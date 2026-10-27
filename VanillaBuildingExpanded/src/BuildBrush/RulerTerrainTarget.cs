using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerTerrainTarget(ICoreClientAPI api)
{
    private readonly IClientWorldAccessor world = api.World;

    public RulerEndpoint? Resolve(GizmoPointer pointer)
    {
        if (!ReferenceEquals(api.World, world) || world.Player?.Entity is not { } player
            || player.Pos.Dimension != pointer.Context.Dimension || pointer.Context.Camera is not { } camera
            || pointer.FarPoint is not { } far) return null;
        double dimensionY = (double)pointer.Context.Dimension * BlockPos.DimensionBoundary;
        Vec3d Native(Vector256<double> point) => new(point[0], point[1] + dimensionY, point[2]);
        BlockSelection? block = null;
        EntitySelection? entity = null;
        bool renderMeta = api.Settings.Bool["renderMetaBlocks"];
        world.RayTraceForSelection(Ray.FromPositions(Native(pointer.Ray.Origin), Native(far)),
            ref block, ref entity,
            (pos, value) => value is null || renderMeta || value.RenderPass != EnumChunkRenderPass.Meta
                || (value.GetInterface<IMetaBlock>(world, pos)?.IsSelectable(pos) ?? false),
            value => value.IsInteractable && value.EntityId != player.EntityId);
        if (entity is not null || block?.Position is not { } position || block.HitPosition is not { } hit
            || position.dimension != pointer.Context.Dimension) return null;
        var cell = new RulerEndpoint(position.X, position.Y, position.Z, position.dimension);
        if (!cell.IsValid(world.BlockAccessor) || world.BlockAccessor.GetChunkAtBlockPos(position) is null)
            return null;
        var localHit = Vector256.Create(position.X + hit.X,
            position.InternalY + hit.Y - dimensionY, position.Z + hit.Z, 0d);
        return camera.TryProject(localHit, out _) ? cell : null;
    }
}
