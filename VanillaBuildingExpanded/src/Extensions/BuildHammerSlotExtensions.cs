using System.Diagnostics.CodeAnalysis;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded;

/// <summary>Identifies build hammers in an already resolved equipment slot.</summary>
public static class BuildHammerSlotExtensions
{
    #region Public API

    /// <summary>Returns the build hammer in the effective offhand slot, if one is present.</summary>
    public static bool TryGetBuildHammer(this ItemSlot? slot, [NotNullWhen(true)] out ItemBuildHammer? hammer)
    {
        hammer = slot?.Itemstack?.Collectible as ItemBuildHammer;
        return hammer is not null;
    }

    #endregion
}
