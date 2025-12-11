using System.Diagnostics.CodeAnalysis;

using Vintagestory.API.Common;

namespace VanillaBuildingExtended;
public static class IPlayerExtensions
{
    /// <summary>
    /// Attempts to get the build hammer instance the player is currently holding.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="outHammerInstance"></param>
    /// <returns></returns>
    public static bool TryGetBuildHammer(this IPlayer player, [NotNullWhen(true)] out ItemBuildHammer outHammerInstance)
    {
        ItemSlot? activeSlot = player.InventoryManager?.OffhandHotbarSlot ?? player.Entity.LeftHandItemSlot;
        var hammerInstance = activeSlot?.Itemstack?.Collectible as ItemBuildHammer;
        if (hammerInstance is null)
        {
            outHammerInstance = null!;
            return false;
        }
        outHammerInstance = hammerInstance;
        return true;
    }

    /// <summary>
    /// Determines whether the player is currently holding a build hammer in their left hand (offhand slot).
    /// </summary>
    /// <param name="player"></param>
    /// <returns></returns>
    public static bool IsHoldingBuildHammer(this IPlayer player)
    {
        return TryGetBuildHammer(player, out _);
    }
}
