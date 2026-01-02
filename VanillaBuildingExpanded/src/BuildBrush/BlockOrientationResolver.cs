using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Resolves and caches all valid orientation states for blocks.
/// Precomputes <see cref="BlockOrientationDefinition"/> arrays for efficient orientation cycling.
/// </summary>
public class BlockOrientationResolver
{
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
    #endregion

    #region Fields
    /// <summary>
    /// The world accessor used for block lookups.
    /// </summary>
    public IWorldAccessor World { get; }

    /// <summary>
    /// Cache of orientation definitions keyed by untransformed block ID.
    /// </summary>
    private readonly Dictionary<int, ImmutableArray<BlockOrientationDefinition>> _orientationCache = [];

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
    public ImmutableArray<BlockOrientationDefinition> GetOrientations(int untransformedBlockId, ItemStack? itemStack = null)
    {
        if (_orientationCache.TryGetValue(untransformedBlockId, out var cached))
            return cached;

        Block? block = World.GetBlock(untransformedBlockId);
        if (block is null)
        {
            var fallback = ImmutableArray.Create(new BlockOrientationDefinition(untransformedBlockId, 0f));
            _orientationCache[untransformedBlockId] = fallback;
            return fallback;
        }

        var definitions = ComputeOrientations(block, itemStack);
        _orientationCache[untransformedBlockId] = definitions;
        return definitions;
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

    /// <summary>
    /// Finds the index in the orientation array that matches a specific block ID.
    /// Useful for syncing state when the brush block changes.
    /// </summary>
    /// <param name="definitions">The orientation definitions array.</param>
    /// <param name="blockId">The block ID to find.</param>
    /// <returns>The index of the matching definition, or 0 if not found.</returns>
    public static int FindIndexForBlockId(ImmutableArray<BlockOrientationDefinition> definitions, int blockId)
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
    #endregion

    #region Computation
    /// <summary>
    /// Computes all orientation definitions for a block based on its orientation mode.
    /// </summary>
    /// <param name="block">The block to compute orientations for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type-specific properties.</param>
    private ImmutableArray<BlockOrientationDefinition> ComputeOrientations(Block block, ItemStack? itemStack = null)
    {
        EBuildBrushRotationMode mode = GetRotationMode(block);

        return mode switch
        {
            EBuildBrushRotationMode.None => [new BlockOrientationDefinition(block.BlockId, 0f)],
            EBuildBrushRotationMode.VariantBased => ComputeVariantRotations(block),
            EBuildBrushRotationMode.Rotatable => ComputeRotatableRotations(block, itemStack),
            EBuildBrushRotationMode.Hybrid => ComputeHybridRotations(block, itemStack),
            _ => [new BlockOrientationDefinition(block.BlockId, 0f)]
        };
    }

    /// <summary>
    /// Computes orientations for variant-based blocks (one definition per variant, 0° mesh angle).
    /// </summary>
    private ImmutableArray<BlockOrientationDefinition> ComputeVariantRotations(Block block)
    {
        Block[] variants = GetOrientationVariants(block);
        if (variants.Length == 0)
            return [new BlockOrientationDefinition(block.BlockId, 0f)];

        var builder = ImmutableArray.CreateBuilder<BlockOrientationDefinition>(variants.Length);
        for (int i = 0; i < variants.Length; i++)
        {
            builder.Add(new BlockOrientationDefinition(variants[i].BlockId, 0f));
        }
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Computes orientations for IRotatable blocks (same block ID, different mesh angles).
    /// </summary>
    /// <param name="block">The block to compute rotations for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type-specific properties.</param>
    private ImmutableArray<BlockOrientationDefinition> ComputeRotatableRotations(Block block, ItemStack? itemStack = null)
    {
        ERotatableInterval interval = ResolveRotatableInterval(block, itemStack);
        if (interval == ERotatableInterval.None)
        {
            // Default to 90° if no interval specified
            interval = ERotatableInterval.Deg90;
        }

        float intervalDegrees = interval.ToDegrees();
        int totalSteps = (int)(360f / intervalDegrees);
        if (totalSteps <= 0)
            totalSteps = 1;

        var builder = ImmutableArray.CreateBuilder<BlockOrientationDefinition>(interval.GetStepCount());
        for (int i = 0; i < totalSteps; i++)
        {
            float angle = i * intervalDegrees;

            // Skip angles that should be omitted (e.g., 45° multiples for Deg22_5Not45)
            if (interval.ShouldSkipAngle(angle))
                continue;

            builder.Add(new BlockOrientationDefinition(block.BlockId, angle));
        }
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Computes orientations for hybrid blocks.
    /// Each variant gets mesh-angle sub-steps within its "slice" of 360°.
    /// </summary>
    /// <param name="block">The block to compute rotations for.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type-specific properties.</param>
    private ImmutableArray<BlockOrientationDefinition> ComputeHybridRotations(Block block, ItemStack? itemStack = null)
    {
        Block[] variants = GetOrientationVariants(block);
        if (variants.Length == 0)
            return ComputeRotatableRotations(block, itemStack);

        ERotatableInterval interval = ResolveRotatableInterval(block, itemStack);
        if (interval == ERotatableInterval.None)
        {
            // No mesh-angle interval, fall back to variant-only
            return ComputeVariantRotations(block);
        }

        float intervalDegrees = interval.ToDegrees();

        // Each variant "owns" a slice of 360°
        float sliceDegrees = 360f / variants.Length;

        // Calculate how many mesh-angle steps fit within each slice
        int totalStepsPerVariant = (int)(sliceDegrees / intervalDegrees);
        if (totalStepsPerVariant <= 0)
            totalStepsPerVariant = 1;

        var builder = ImmutableArray.CreateBuilder<BlockOrientationDefinition>(variants.Length * totalStepsPerVariant);

        for (int v = 0; v < variants.Length; v++)
        {
            int variantBlockId = variants[v].BlockId;

            for (int s = 0; s < totalStepsPerVariant; s++)
            {
                float meshAngle = s * intervalDegrees;

                // Skip angles that should be omitted (e.g., 45° multiples for Deg22_5Not45)
                if (interval.ShouldSkipAngle(meshAngle))
                    continue;

                builder.Add(new BlockOrientationDefinition(variantBlockId, meshAngle));
            }
        }

        return builder.Count > 0
            ? builder.ToImmutable()
            : [new BlockOrientationDefinition(block.BlockId, 0f)];
    }
    #endregion

    #region Detection
    /// <summary>
    /// Detects the rotation mode for a block.
    /// </summary>
    private EBuildBrushRotationMode DetectRotationMode(Block block)
    {
        bool hasVariantRotation = HasVariantBasedRotation(block);
        bool hasRotatableEntity = HasRotatableBlockEntity(block);

        return (hasVariantRotation, hasRotatableEntity) switch
        {
            (true, true) => EBuildBrushRotationMode.Hybrid,
            (true, false) => EBuildBrushRotationMode.VariantBased,
            (false, true) => EBuildBrushRotationMode.Rotatable,
            (false, false) => EBuildBrushRotationMode.None,
        };
    }

    /// <summary>
    /// Checks if the block has orientation variants.
    /// </summary>
    private static bool HasVariantBasedRotation(Block block)
    {
        return block.Variant.Keys.Any(k => ValidOrientationVariantKeys.Contains(k));
    }

    /// <summary>
    /// Checks if the block has an IRotatable block entity.
    /// </summary>
    private bool HasRotatableBlockEntity(Block block)
    {
        if (string.IsNullOrEmpty(block.EntityClass))
            return false;

        try
        {
            Type? entityType = World.Api.ClassRegistry.GetBlockEntity(block.EntityClass);
            if (entityType is null)
                return false;

            return typeof(IRotatable).IsAssignableFrom(entityType);
        }
        catch
        {
            return false;
        }
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
