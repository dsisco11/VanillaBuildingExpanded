using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Immutable placement intent for a build brush operation.
/// </summary>
public sealed record BuildBrushPlacementRequest(
    BlockSelection Selection,
    BlockPos Position,
    Block PlacementBlock,
    ItemStack ItemStack,
    BrushOrientation? Rotation
);
