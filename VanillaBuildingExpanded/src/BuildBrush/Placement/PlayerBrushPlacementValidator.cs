using System;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Runs native block placement rules using the brush owner's real game context.</summary>
internal sealed class PlayerBrushPlacementValidator : IBrushPlacementValidator
{
    private readonly IWorldAccessor world;
    private readonly IPlayer player;

    #region Public API

    /// <summary>Retains the world and player used for native placement validation.</summary>
    public PlayerBrushPlacementValidator(IWorldAccessor world, IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);
        this.world = world;
        this.player = player;
    }

    /// <inheritdoc />
    public bool CanPlace(Block block, BlockSelection selection, ref string failureCode)
    {
        return block.CanPlaceBlock(world, player, selection, ref failureCode);
    }

    #endregion
}
