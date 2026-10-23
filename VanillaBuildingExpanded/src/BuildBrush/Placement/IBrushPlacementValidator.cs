using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Checks placement rules for a brush without exposing the owning player.</summary>
public interface IBrushPlacementValidator
{
    #region Public API

    /// <summary>Checks the selected block at the resolved selection, preserving rule side effects.</summary>
    /// <param name="block">The actual block whose placement rules should run.</param>
    /// <param name="selection">The resolved selection, including its original face and hit data.</param>
    /// <param name="failureCode">The native placement failure code, passed through unchanged.</param>
    /// <returns>Whether the block can be placed at the selection.</returns>
    bool CanPlace(Block block, BlockSelection selection, ref string failureCode);

    #endregion
}
