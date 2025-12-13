using Vintagestory.API.Common;
using Vintagestory.Common.Collectible.Block;

namespace VanillaBuildingExpanded;
public static class BlockSelectionExtensions
{
    public static BlockPosFacing ToPosFacing(this BlockSelection blockSelection)
    {
        return new BlockPosFacing(blockSelection.Position, blockSelection.Face, string.Empty);
    }
}
