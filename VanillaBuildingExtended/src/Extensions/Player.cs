using System.Diagnostics.CodeAnalysis;

namespace VanillaBuildingExtended;
public static class IPlayerExtensions
{
    public static bool TryGetBuildHammer(this Vintagestory.API.Common.IPlayer player, [NotNullWhen(true)] out ItemBuildHammer outHammerInstance)
    {
        var activeSlot = player.Entity.LeftHandItemSlot;
        var hammerInstance = activeSlot?.Itemstack?.Collectible as ItemBuildHammer;
        if (hammerInstance is null)
        {
            outHammerInstance = null!;
            return false;
        }
        outHammerInstance = hammerInstance;
        return true;
    }

    public static bool HasBuildHammer(this Vintagestory.API.Common.IPlayer player)
    {
        var activeSlot = player.Entity.LeftHandItemSlot;
        var hammerInstance = activeSlot?.Itemstack?.Collectible as ItemBuildHammer;
        return hammerInstance is not null;
    }
}
