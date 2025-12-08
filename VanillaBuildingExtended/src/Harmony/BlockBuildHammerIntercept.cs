using HarmonyLib;

using VanillaBuildingExtended.BuildHammer;

using Vintagestory.API.Common;

namespace VanillaBuildingExtended.src.Harmony;

[Harmony]
public static class BlockBuildHammerIntercept
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Block), nameof(Block.TryPlaceBlock))]
    public static bool Intercept_TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode, ref bool __result, bool __runOriginal)
    {
        if (world.Side == EnumAppSide.Server)
        {
            BuildBrushManager_Server brushManager = VanillaBuildingExtendedModSystem.buildBrushManager_Server;
            BuildBrushInstance brush = brushManager.GetBrush(byPlayer)!;
            if (brush is not null && brush.BlockTransformed is not null)
            {
                brush.BlockTransformed.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
                __result = true;
                return false;// skip original
            }
        }

        return true;// dont skip original
    }
}
