using System.Diagnostics.CodeAnalysis;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded;

/// <summary>Adapts game-facing player equipment access to slot-based hammer detection.</summary>
public static class IPlayerExtensions
{
    #region Public API

    /// <summary>Resolves the player's effective offhand and returns its build hammer, if present.</summary>
    public static bool TryGetBuildHammer(this IPlayer player, [NotNullWhen(true)] out ItemBuildHammer outHammerInstance)
    {
        // Preserve the native offhand fallback here so callers do not repeat player-specific lookup rules.
        ItemSlot? slot = player.InventoryManager?.OffhandHotbarSlot ?? player.Entity.LeftHandItemSlot;
        bool found = slot.TryGetBuildHammer(out ItemBuildHammer? hammer);
        outHammerInstance = hammer!;
        return found;
    }

    /// <summary>Determines whether the player has a build hammer in their effective offhand slot.</summary>
    public static bool IsHoldingBuildHammer(this IPlayer player)
    {
        return player.TryGetBuildHammer(out _);
    }

    #endregion
}
