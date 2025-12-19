using System;
using System.Collections.Generic;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Utility class for detecting how a block supports rotation.
/// </summary>
public static class BuildBrushRotationDetector
{
    /// <summary>
    /// Cache of rotation mode detection results per block code.
    /// </summary>
    private static readonly Dictionary<AssetLocation, EBuildBrushRotationMode> RotationModeCache = [];

    /// <summary>
    /// Detects the rotation mode for a given block.
    /// </summary>
    /// <param name="block">The block to analyze.</param>
    /// <param name="world">The world accessor for resolving block entity types.</param>
    /// <returns>The detected rotation mode.</returns>
    public static EBuildBrushRotationMode DetectRotationMode(Block block, IWorldAccessor world)
    {
        if (block is null)
            return EBuildBrushRotationMode.None;

        // Check cache first
        if (RotationModeCache.TryGetValue(block.Code, out var cachedMode))
            return cachedMode;

        bool hasVariantRotation = HasVariantBasedRotation(block, world);
        bool hasRotatableEntity = HasRotatableBlockEntity(block, world);

        EBuildBrushRotationMode mode = (hasVariantRotation, hasRotatableEntity) switch
        {
            (true, true) => EBuildBrushRotationMode.Hybrid,
            (true, false) => EBuildBrushRotationMode.VariantBased,
            (false, true) => EBuildBrushRotationMode.Rotatable,
            (false, false) => EBuildBrushRotationMode.None,
        };

        RotationModeCache[block.Code] = mode;
        return mode;
    }

    /// <summary>
    /// Checks if the block supports variant-based rotation by testing if GetRotatedBlockCode returns a different code.
    /// </summary>
    private static bool HasVariantBasedRotation(Block block, IWorldAccessor world)
    {
        // Test if rotating by 180 degrees produces a different block code
        AssetLocation rotatedCode = block.GetRotatedBlockCode(180);
        if (rotatedCode is null)
            return false;

        // If the rotated code is different from the original, it has variant-based rotation
        return !rotatedCode.Equals(block.Code);
    }

    /// <summary>
    /// Checks if the block has a block entity that implements IRotatable.
    /// </summary>
    private static bool HasRotatableBlockEntity(Block block, IWorldAccessor world)
    {
        // Check if the block has an entity class defined
        if (string.IsNullOrEmpty(block.EntityClass))
            return false;

        try
        {
            Type? entityType = world.Api.ClassRegistry.GetBlockEntity(block.EntityClass);
            if (entityType is null)
            {
                return false;
            }

            return typeof(IRotatable).IsAssignableFrom(entityType);
        }
        catch
        {
            // If we can't create the entity, assume it's not rotatable
            return false;
        }
    }

    /// <summary>
    /// Clears the rotation mode cache. Call this when blocks are reloaded.
    /// </summary>
    public static void ClearCache()
    {
        RotationModeCache.Clear();
    }
}
