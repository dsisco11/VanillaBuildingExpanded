using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaBuildingExpanded;

/// <summary>Retains the saved wooden hammer collectible without build-mode activation.</summary>
public class ItemBuildHammer : Item
{
    #region Public API

    /// <summary>Resolves the localized name shared by all saved material variants.</summary>
    public override string GetHeldItemName(ItemStack itemStack)
    {
        return Lang.GetMatching($"vanillabuildingexpanded:item-{Code.Path}");
    }

    #endregion
}
