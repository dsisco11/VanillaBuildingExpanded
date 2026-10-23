using HarmonyLib;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.src.Harmony;

/// <summary>Routes native server placement through an enabled build brush.</summary>
[Harmony]
public static class BlockBuildBrushIntercept
{
    #region Public API
    /// <summary>Preserves native handling unless the brush owns the placement attempt.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Block), nameof(Block.TryPlaceBlock))]
    public static bool Intercept_TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode, ref bool __result, bool __runOriginal)
    {
        if (world is null || byPlayer is null || itemstack is null || blockSel is null)
        {
            return true;// dont skip original
        }

        if (world.Side == EnumAppSide.Server)
        {
            BuildBrushSystem_Server brushManager = world.Api.ModLoader.GetModSystem<BuildBrushSystem_Server>();
            if (brushManager.TryPlaceBrushBlock(world, byPlayer, itemstack, blockSel))
            {
                __result = true;
                return false;// skip original
            }
        }

        return true;// dont skip original
    }
    #endregion
}
