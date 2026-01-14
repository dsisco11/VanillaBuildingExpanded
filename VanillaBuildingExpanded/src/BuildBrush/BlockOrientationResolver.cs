using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Resolves and caches all valid orientation states for blocks.
/// Precomputes <see cref="BlockOrientation"/> arrays for efficient orientation cycling.
/// </summary>
public class BlockOrientationResolver
{
    #region Records
    /// <summary>
    /// Configuration for a rotation attribute.
    /// </summary>
    /// <param name="AttributeName">The name of the attribute in the tree.</param>
    /// <param name="IsDegrees">Whether the attribute stores degrees (true) or radians (false).</param>
    private readonly record struct RotationAttributeConfig(string AttributeName, bool IsDegrees);
    #endregion

    #region Constants
    /// <summary>
    /// Valid variant keys that indicate a block supports orientation variants.
    /// </summary>
    public static readonly ImmutableArray<string> ValidOrientationVariantKeys = [
        "rot",
        "rotation",
        "horizontalorientation",
        "orientation",
        "v",
        "side",
    ];
    
    /// <summary>
    /// Known rotation attribute names used by different block entity types.
    /// Order matters - we check in this order and use the first match.
    /// </summary>
    private static readonly FrozenDictionary<string, RotationAttributeConfig> KnownMeshRotationAttributes =
        new Dictionary<string, RotationAttributeConfig>
        {
            // Standard meshAngle (radians) - chests, crates, buckets, molds, ground storage, plant containers, etc.
            ["meshAngle"] = new("meshAngle", IsDegrees: false),
            // meshAngleRad - BEBehaviorMaterialFromAttributes, BEBehaviorShapeMaterialFromAttributes
            ["meshAngleRad"] = new("meshAngleRad", IsDegrees: false),
            // rotateYRad - BEBehaviorDoor, BEBeeHiveKiln
            ["rotateYRad"] = new("rotateYRad", IsDegrees: false),
            // rotDeg - BEBehaviorTrapDoor (stores degrees, not radians!)
            ["rotDeg"] = new("rotDeg", IsDegrees: true),
        }.ToFrozenDictionary();

    /// <summary>
    /// Returns whether a known mesh rotation attribute stores degrees (true) or radians (false).
    /// Unknown attributes default to radians.
    /// </summary>
    public static bool IsMeshRotationAttributeDegrees(string? attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return false;

        return KnownMeshRotationAttributes.TryGetValue(attributeName, out var config) && config.IsDegrees;
    }
    #endregion

    #region Fields
    /// <summary>
    /// The world accessor used for block lookups.
    /// </summary>
    public IWorldAccessor World { get; }

    /// <summary>
    /// Cache of orientation definitions keyed by untransformed block ID.
    /// </summary>
    private readonly Dictionary<int, ImmutableArray<BlockOrientation>> _orientationCache = [];

    /// <summary>
    /// Cache of orientation modes keyed by block code (for classification).
    /// </summary>
    private readonly Dictionary<AssetLocation, EBuildBrushRotationMode> _modeCache = [];

    /// <summary>
    /// Cache of orientation variants keyed by block base code.
    /// </summary>
    private readonly Dictionary<string, Block[]> _variantCache = [];
    #endregion

    #region Constructor
    public BlockOrientationResolver(IWorldAccessor world)
    {
        World = world;
    }
    #endregion

    #region Public API
    /// <summary>
    /// Gets the precomputed orientation definitions for a block.
    /// Returns cached array if available, otherwise computes and caches it.
    /// </summary>
    /// <param name="untransformedBlockId">The block ID to get orientations for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type-specific properties (e.g., for typed containers).</param>
    /// <returns>Array of all valid orientation states for the block.</returns>
    public ImmutableArray<BlockOrientation> GetOrientations(int untransformedBlockId, ItemStack? itemStack = null)
    {
        if (_orientationCache.TryGetValue(untransformedBlockId, out var cached))
            return cached;

        Block? block = World.GetBlock(untransformedBlockId);
        if (block is null)
        {
            var fallback = ImmutableArray.Create(new BlockOrientation(untransformedBlockId, 0f));
            _orientationCache[untransformedBlockId] = fallback;
            return fallback;
        }

        var definitions = ComputeOrientations(block, itemStack);
        _orientationCache[untransformedBlockId] = definitions;
        return definitions;
    }

    /// <summary>
    /// Finds the index in the orientation array that matches a specific block ID.
    /// Useful for syncing state when the brush block changes.
    /// </summary>
    /// <param name="definitions">The orientation definitions array.</param>
    /// <param name="blockId">The block ID to find.</param>
    /// <returns>The index of the matching definition, or 0 if not found.</returns>
    public static int FindIndexForBlockId(ImmutableArray<BlockOrientation> definitions, int blockId)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].BlockId == blockId)
                return i;
        }
        return 0;
    }

    /// <summary>
    /// Clears all caches. Call when world changes or for testing.
    /// </summary>
    public void ClearCaches()
    {
        _orientationCache.Clear();
        _modeCache.Clear();
        _variantCache.Clear();
    }

    /// <summary>
    /// Gets the rotation mode for a block (for display/classification purposes).
    /// </summary>
    public EBuildBrushRotationMode GetRotationMode(Block block)
    {
        if (block is null)
            return EBuildBrushRotationMode.None;

        if (_modeCache.TryGetValue(block.Code, out var cached))
            return cached;

        var mode = DetectRotationMode(block);
        _modeCache[block.Code] = mode;
        return mode;
    }

    #endregion

    #region Computation
    /// <summary>
    /// Computes all orientation definitions for a block based on its orientation mode.
    /// </summary>
    /// <param name="block">The block to compute orientations for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type-specific properties.</param>
    private ImmutableArray<BlockOrientation> ComputeOrientations(Block block, ItemStack? itemStack = null)
    {
        EBuildBrushRotationMode mode = GetRotationMode(block);

        return mode switch
        {
            EBuildBrushRotationMode.None => [new BlockOrientation(block.BlockId, 0f)],
            EBuildBrushRotationMode.VariantBased => ComputeVariantRotations(block),
            EBuildBrushRotationMode.Rotatable => ComputeRotatableRotations(block, itemStack),
            _ => [new BlockOrientation(block.BlockId, 0f)]
        };
    }

    /// <summary>
    /// Computes orientations for variant-based blocks (one definition per variant, 0° mesh angle).
    /// </summary>
    private ImmutableArray<BlockOrientation> ComputeVariantRotations(Block block)
    {
        Block[] variants = GetOrientationVariants(block);
        if (variants.Length == 0)
            return [new BlockOrientation(block.BlockId, 0f)];

        var builder = ImmutableArray.CreateBuilder<BlockOrientation>(variants.Length);
        for (int i = 0; i < variants.Length; i++)
        {
            builder.Add(new BlockOrientation(variants[i].BlockId, 0f));
        }
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Computes orientations for IRotatable blocks (same block ID, different mesh angles).
    /// Uses predefined lookup tables for valid angles.
    /// </summary>
    /// <param name="block">The block to compute rotations for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type-specific properties.</param>
    private ImmutableArray<BlockOrientation> ComputeRotatableRotations(Block block, ItemStack? itemStack = null)
    {
        ERotatableInterval interval = ResolveRotatableInterval(block, itemStack);
        if (interval == ERotatableInterval.None)
        {
            // No interval configured => not rotatable
            return [new BlockOrientation(block.BlockId, 0f)];
        }

        // Use predefined lookup table for valid angles
        var validAngles = interval.GetValidAngles();
        if (validAngles.IsDefaultOrEmpty)
        {
            return [new BlockOrientation(block.BlockId, 0f)];
        }

        string? rotationAttributeName = ResolveRotationAttributeName(block);
        var builder = ImmutableArray.CreateBuilder<BlockOrientation>(validAngles.Length);
        
        foreach (float angle in validAngles)
        {
            builder.Add(new BlockOrientation(block.BlockId, angle, rotationAttributeName));
        }
        
        return builder.MoveToImmutable();
    }

    #endregion

    #region TryGetRotationInterface

    /// <summary>
    /// Tries to get the IRotatable interface from a block entity or its behaviors.
    /// </summary>
    /// <returns> The IRotatable interface if found; otherwise, null. </returns>
    public static bool TryGetRotationInterface(BlockEntity? blockEntity, [NotNullWhen(true)] out IRotatable? rotatable)
    {
        if (blockEntity is null)
        {
            rotatable = null;
            return false;
        }
            
        // Check if the block entity itself implements IRotatable
        if (blockEntity is IRotatable r)
        {
            rotatable = r;
            return true;
        }
        // Check behaviors for IRotatable
        foreach (var behavior in blockEntity.Behaviors)
        {
            if (behavior is IRotatable br)
            {
                rotatable = br;
                return true;
            }
        }

        rotatable = null;
        return false;
    }

    /// <summary>
    /// Tries to get the IRotatable interface from a block entity or its behaviors.
    /// </summary>
    /// <returns> The IRotatable interface if found; otherwise, null. </returns>
    public bool TryGetRotationInterface(Block? block, [NotNullWhen(true)] out IRotatable? rotatable)
    {
        if (string.IsNullOrEmpty(block?.EntityClass))
        {
            rotatable = null;
            return false;
        }

        BlockEntity? tempEntity = null;
        try
        {
            Type? entityType = World.Api.ClassRegistry.GetBlockEntity(block.EntityClass);
            if (entityType is null)
            {
                rotatable = null;
                return false;
            }

            if (!typeof(IRotatable).IsAssignableFrom(entityType))
            {
                rotatable = null;
                return false;
            }

            // Check behaviors for IRotatable
            //World.Api.ClassRegistry.GetBlockEntityBehaviorClass(???)
            tempEntity = World.Api.ClassRegistry.CreateBlockEntity(block.EntityClass);
            if (tempEntity is null)
            {
                rotatable = null;
                return false;
            }

            if (tempEntity is IRotatable r)
            {
                rotatable = r;
                return true;
            }

            foreach (var behavior in tempEntity.Behaviors)
            {
                if (behavior is IRotatable rot)
                {
                    rotatable = rot;
                    return true;
                }
            }

            rotatable = null;
            return false;
        }
        catch
        {
            rotatable = null;
            return false;
        }
        finally 
        {
            // TODO: figure out if destroying the entity is actually needed, logically yes but...
            // destroy temp entity
            tempEntity?.OnBlockRemoved();
        }
    }
    #endregion

    #region Detection
    /// <summary>
    /// Detects the rotation mode for a block.
    /// </summary>
    private EBuildBrushRotationMode DetectRotationMode(Block block)
    {
        bool hasVariantRotation = HasVariantBasedRotation(block);
        bool hasRotatableEntity = TryGetRotationInterface(block, out _);
        ERotatableInterval interval = ResolveRotatableInterval(block);

        if (interval == ERotatableInterval.None)
            hasRotatableEntity = false;

        return (hasVariantRotation, hasRotatableEntity) switch
        {
            // If a block has both variant-based rotation and an IRotatable entity, prefer Rotatable.
            // (This keeps rotation consistent and avoids relying on variant-only block-id swaps for mesh rotation.)
            (true, true) => EBuildBrushRotationMode.Rotatable,
            (true, false) => EBuildBrushRotationMode.VariantBased,
            (false, true) => EBuildBrushRotationMode.Rotatable,
            (false, false) => EBuildBrushRotationMode.None,
        };
    }

    private string? ResolveRotationAttributeName(Block? block)
    {
        if (block is null)
            return null;

        // First try block attributes, rare but possible
        foreach (var attributeName in KnownMeshRotationAttributes.Keys)
        {
            if (block.Attributes is not null && block.Attributes.KeyExists(attributeName))
            {
                return attributeName;
            }
        }

        // Next, try block entity class
        if (string.IsNullOrEmpty(block.EntityClass))
            return null;

        if (!TryGetRotationInterface(block, out IRotatable? rotatable))
            return null;

        // Create a temporary tree to query the rotation attribute
        TreeAttribute tempTree = new();
        // Apply rotation to temp tree
        rotatable.OnTransformed(
            null,
            tempTree,
            0,
            [],
            [],
            null
        );
        // Find rotation attribute name within temp tree
        return ResolveRotationAttributeName(tempTree);
    }

    /// <summary>
    /// Resolves the rotation attribute name from a tree attribute.
    /// </summary>
    /// <returns>  The name of the rotation attribute if found; otherwise, null.  </returns>
    public static string? ResolveRotationAttributeName(ITreeAttribute tree)
    {
        foreach (var attributeName in KnownMeshRotationAttributes.Keys)
        {
            if (tree.HasAttribute(attributeName))
            {
                return attributeName;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if the block has orientation variants.
    /// </summary>
    private static bool HasVariantBasedRotation(Block block)
    {
        return block.Variant.Keys.Any(k => ValidOrientationVariantKeys.Contains(k));
    }

    /// <summary>
    /// Gets orientation variants for a block.
    /// </summary>
    private Block[] GetOrientationVariants(Block block)
    {
        string baseCode = block.Code.FirstCodePart();

        if (_variantCache.TryGetValue(baseCode, out Block[]? cached))
            return cached;

        string? foundVariantGroup = block.Variant.Keys
            .FirstOrDefault(k => ValidOrientationVariantKeys.Contains(k));

        if (foundVariantGroup is null)
        {
            _variantCache[baseCode] = [block];
            return [block];
        }

        AssetLocation? searchCode = block.CodeWithVariant(foundVariantGroup, "*");
        if (searchCode is null)
        {
            _variantCache[baseCode] = [block];
            return [block];
        }

        Block[] variants = World.SearchBlocks(searchCode);
        if (variants.Length == 0)
        {
            _variantCache[baseCode] = [block];
            return [block];
        }

        _variantCache[baseCode] = variants;
        return variants;
    }

    /// <summary>
    /// Resolves the "type" value for a typed block (e.g., crates, chests).
    /// Mirrors how BlockGenericTypedContainer resolves type: stack.Attributes?.GetString("type", defaultType)
    /// </summary>
    /// <param name="block">The block to resolve type for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type from.</param>
    /// <returns>The resolved type string, or null if no type is defined.</returns>
    public static string? ResolveBlockType(Block? block, ItemStack? itemStack = null)
    {
        if (block?.Attributes is null)
            return null;

        // Get defaultType from block attributes (same pattern as BlockGenericTypedContainer.OnLoaded)
        string? defaultType = block.Attributes["defaultType"]?.AsString();

        // Resolve type from ItemStack with defaultType fallback (same pattern as BlockGenericTypedContainer)
        return itemStack?.Attributes?.GetString("type", defaultType) ?? defaultType;
    }

    /// <summary>
    /// Resolves the rotation interval from block attributes.
    /// </summary>
    /// <param name="block">The block to get rotation interval for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type from (for typed containers).</param>
    private static ERotatableInterval ResolveRotatableInterval(Block block, ItemStack? itemStack = null)
    {
        if (block?.Attributes is null)
            return ERotatableInterval.None;

        string? type = ResolveBlockType(block, itemStack);

        string? intervalString = null;

        // Try rotatatableInterval (dictionary or direct)
        var rotatatableIntervalAttr = block.Attributes["rotatatableInterval"];
        if (rotatatableIntervalAttr is not null && rotatatableIntervalAttr.Exists)
        {
            if (!string.IsNullOrEmpty(type))
                intervalString = rotatatableIntervalAttr[type]?.AsString();

            if (string.IsNullOrEmpty(intervalString))
                intervalString = rotatatableIntervalAttr.AsString();
        }

        // Try properties[type].rotatatableInterval
        if (string.IsNullOrEmpty(intervalString) && !string.IsNullOrEmpty(type))
        {
            intervalString = block.Attributes["properties"]?[type]?["rotatatableInterval"]?.AsString();

            if (string.IsNullOrEmpty(intervalString))
                intervalString = block.Attributes["properties"]?["*"]?["rotatatableInterval"]?.AsString();
        }

        return ERotatableIntervalExtensions.Parse(intervalString);
    }
    #endregion
}
