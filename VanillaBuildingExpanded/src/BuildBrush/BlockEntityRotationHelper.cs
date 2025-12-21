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
    /// <param name="rotationRadians">The absolute rotation angle in radians to apply.</param>
    /// <returns>True if the rotation was applied successfully; otherwise, false.</returns>
    public static bool TrySetRotation(IWorldAccessor world, BlockPos position, float rotationRadians)
    {
        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(position);
        if (blockEntity is not IRotatable)
        {
            return false;
        }

        // Get the current tree attributes from the block entity
        TreeAttribute tree = new();
        blockEntity.ToTreeAttributes(tree);

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
    /// Parses the rotation interval from block attributes.
    /// </summary>
    /// <param name="block">The block to parse rotation interval from.</param>
    /// <returns>The rotation increment in radians, or 0 if not found.</returns>
    public static float ResolveRotationInterval(in Block? block)
    {
        if (block?.Attributes is null)
        {
            return 0f;
        }

        string? type = block.Attributes["type"]?.AsString();
        if (string.IsNullOrEmpty(type))
        {
            return 0f;
        }

        string? intervalString = null;

        // Try to get rotatatableInterval - it can be:
        // 1. A dictionary keyed by type (chest.json): rotatatableInterval: { "normal-generic": "22.5deg" }
        // 2. Nested in properties (crate.json): properties: { "wood-aged": { rotatatableInterval: "22.5deg" } }
        var rotatatableIntervalAttr = block.Attributes["rotatatableInterval"];
        if (rotatatableIntervalAttr is not null && rotatatableIntervalAttr.Exists)
        {
            if (!string.IsNullOrEmpty(type))
            {
                // Try to get interval for this specific type
                intervalString = rotatatableIntervalAttr[type]?.AsString();
            }
            
            // If still not found, try direct string value (unlikely but possible)
            if (string.IsNullOrEmpty(intervalString))
            {
                intervalString = rotatatableIntervalAttr.AsString();
            }
        }

        // Check properties[type].rotatatableInterval (crate-style)
        if (string.IsNullOrEmpty(intervalString) && !string.IsNullOrEmpty(type))
        {
            intervalString = block.Attributes["properties"]?[type]?["rotatatableInterval"]?.AsString();
            
            // Try wildcard fallback
            if (string.IsNullOrEmpty(intervalString))
            {
                intervalString = block.Attributes["properties"]?["*"]?["rotatatableInterval"]?.AsString();
            }
        }

        if (string.IsNullOrEmpty(intervalString))
        {
            // Default to 0 - rotation disabled if no interval specified
            return 0f;
        }

        return intervalString switch
        {
            "22.5deg" => 22.5f * GameMath.DEG2RAD,
            "22.5degnot45deg" => 22.5f * GameMath.DEG2RAD, // Still uses 22.5 degree increments
            "45deg" => 45f * GameMath.DEG2RAD,
            "90deg" => 90f * GameMath.DEG2RAD,
            _ => 0f
        };
    }

    /// <summary>
    /// Configuration for a rotation attribute.
    /// </summary>
    /// <param name="AttributeName">The name of the attribute in the tree.</param>
    /// <param name="IsDegrees">Whether the attribute stores degrees (true) or radians (false).</param>
    private readonly record struct RotationAttributeConfig(string AttributeName, bool IsDegrees);
}
