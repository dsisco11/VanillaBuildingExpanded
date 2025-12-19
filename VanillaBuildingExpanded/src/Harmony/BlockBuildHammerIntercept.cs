using HarmonyLib;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.src.Harmony;

[Harmony]
public static class BlockBuildHammerIntercept
{
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
}
