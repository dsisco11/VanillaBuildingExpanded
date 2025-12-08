using HarmonyLib;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExtended.src.Harmony;

[Harmony]
public static class BlockBuildHammerIntercept
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Block), nameof(Block.TryPlaceBlock))]
    public static bool Intercept_TryPlaceBlock_Block(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode, ref bool __result)
    {
        if (byPlayer.TryGetBuildHammer(out ItemBuildHammer? hammerInstance))
        {
            BuildBrushState? state = hammerInstance.GetState(byPlayer);
            if (state is not null && state.IsActive && state.Block is not null)
            {
                if (world.Side == EnumAppSide.Client)
                {
                    // On client, modify the block placement position.
                    BlockPos resolvedPos = ItemBuildHammer.ResolveFinalSelectionPosition(world, byPlayer, state.Block, blockSel, state.Snapping);
                    blockSel.SetPos(resolvedPos.X, resolvedPos.Y, resolvedPos.Z);
                    blockSel.DidOffset = true;
                }

                // modify the itemstack block-id on Server & Client
                if (hammerInstance.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode))
                {
                    __result = true;
                    return false;// skip original
                }
            }
        }

        return true;// dont skip original
    }
}
