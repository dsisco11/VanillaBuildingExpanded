using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Helper class for applying rotation to IRotatable block entities.
/// Handles the various attribute naming conventions used by different block entity types.
/// </summary>
public static class BlockEntityRotationHelper
{
    /// <summary>
    /// Known rotation attribute names used by different block entity types.
    /// Order matters - we check in this order and use the first match.
    /// </summary>
    private static readonly RotationAttributeConfig[] KnownConfigs =
    [
        // Standard meshAngle (radians) - chests, crates, buckets, molds, ground storage, plant containers, etc.
        new("meshAngle", IsDegrees: false),
        // meshAngleRad - BEBehaviorMaterialFromAttributes, BEBehaviorShapeMaterialFromAttributes
        new("meshAngleRad", IsDegrees: false),
        // rotateYRad - BEBehaviorDoor, BEBeeHiveKiln
        new("rotateYRad", IsDegrees: false),
        // rotDeg - BEBehaviorTrapDoor (stores degrees, not radians!)
        new("rotDeg", IsDegrees: true),
    ];

    /// <summary>
    /// Attempts to set the absolute rotation on a block entity that implements IRotatable.
    /// This overwrites any rotation set by the default placement logic.
    /// </summary>
    /// <param name="world">The world accessor.</param>
    /// <param name="position">The position of the block entity.</param>
    /// <param name="rotationDegrees">The absolute rotation angle in degrees to apply.</param>
    /// <returns>True if the rotation was applied successfully; otherwise, false.</returns>
    public static bool TrySetRotation(IWorldAccessor world, BlockPos position, float rotationDegrees)
    {
        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(position);
        if (blockEntity is not IRotatable)
        {
            return false;
        }

        // Get the current tree attributes from the block entity
        TreeAttribute tree = new();
        blockEntity.ToTreeAttributes(tree);

        // Convert degrees to radians for internal use
        float rotationRadians = rotationDegrees * GameMath.DEG2RAD;

        // Try to set the rotation using known attribute names
        bool applied = TrySetRotationInTree(tree, rotationRadians);
        if (!applied)
        {
            return false;
        }

        // Re-apply the modified attributes back to the block entity
        blockEntity.FromTreeAttributes(tree, world);
        blockEntity.MarkDirty(true);

        return true;
    }

    /// <summary>
    /// Attempts to set the rotation value in the tree attribute using known attribute names.
    /// </summary>
    /// <param name="tree">The tree attribute to modify.</param>
    /// <param name="rotationRadians">The rotation in radians.</param>
    /// <returns>True if an attribute was found and set; otherwise, false.</returns>
    private static bool TrySetRotationInTree(ITreeAttribute tree, float rotationRadians)
    {
        // Try each known attribute name and set if it exists in the tree
        foreach (var config in KnownConfigs)
        {
            if (tree.HasAttribute(config.AttributeName))
            {
                float valueToSet = config.IsDegrees
                    ? rotationRadians * GameMath.RAD2DEG
                    : rotationRadians;
                tree.SetFloat(config.AttributeName, valueToSet);
                return true;
            }
        }

        // Fallback: set meshAngle anyway since it's the most common
        // This handles cases where the block entity was just created and hasn't serialized yet
        tree.SetFloat("meshAngle", rotationRadians);
        return true;
    }
    
    /// <summary>
    /// Configuration for a rotation attribute.
    /// </summary>
    /// <param name="AttributeName">The name of the attribute in the tree.</param>
    /// <param name="IsDegrees">Whether the attribute stores degrees (true) or radians (false).</param>
    private readonly record struct RotationAttributeConfig(string AttributeName, bool IsDegrees);
}
