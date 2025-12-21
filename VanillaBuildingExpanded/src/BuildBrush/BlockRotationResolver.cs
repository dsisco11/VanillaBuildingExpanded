using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Resolves and caches all valid rotation states for blocks.
/// Precomputes <see cref="BlockRotationDefinition"/> arrays for efficient rotation cycling.
/// </summary>
public class BlockRotationResolver
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
    /// Cache of rotation definitions keyed by untransformed block ID.
    /// </summary>
    private readonly Dictionary<int, BlockRotationDefinition[]> _rotationCache = [];

    /// <summary>
    /// Cache of rotation modes keyed by block code (for classification).
    /// </summary>
    private readonly Dictionary<AssetLocation, EBuildBrushRotationMode> _modeCache = [];

    /// <summary>
    /// Cache of orientation variants keyed by block base code.
    /// </summary>
    private readonly Dictionary<string, Block[]> _variantCache = [];
    #endregion

    #region Constructor
    public BlockRotationResolver(IWorldAccessor world)
    {
        World = world;
    }
    #endregion

    #region Public API
    /// <summary>
    /// Gets the precomputed rotation definitions for a block.
    /// Returns cached array if available, otherwise computes and caches it.
    /// </summary>
    /// <param name="untransformedBlockId">The block ID to get rotations for.</param>
    /// <returns>Array of all valid rotation states for the block.</returns>
    public BlockRotationDefinition[] GetRotations(int untransformedBlockId)
    {
        if (_rotationCache.TryGetValue(untransformedBlockId, out var cached))
            return cached;

        Block? block = World.GetBlock(untransformedBlockId);
        if (block is null)
        {
            var fallback = new[] { new BlockRotationDefinition(untransformedBlockId, 0f) };
            _rotationCache[untransformedBlockId] = fallback;
            return fallback;
        }

        var definitions = ComputeRotations(block);
        _rotationCache[untransformedBlockId] = definitions;
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
    /// Finds the index in the rotation array that matches a specific block ID.
    /// Useful for syncing state when the brush block changes.
    /// </summary>
    /// <param name="definitions">The rotation definitions array.</param>
    /// <param name="blockId">The block ID to find.</param>
    /// <returns>The index of the matching definition, or 0 if not found.</returns>
    public static int FindIndexForBlockId(BlockRotationDefinition[] definitions, int blockId)
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
        _rotationCache.Clear();
        _modeCache.Clear();
        _variantCache.Clear();
    }
    #endregion

    #region Computation
    /// <summary>
    /// Computes all rotation definitions for a block based on its rotation mode.
    /// </summary>
    private BlockRotationDefinition[] ComputeRotations(Block block)
    {
        EBuildBrushRotationMode mode = GetRotationMode(block);

        return mode switch
        {
            EBuildBrushRotationMode.None => [new BlockRotationDefinition(block.BlockId, 0f)],
            EBuildBrushRotationMode.VariantBased => ComputeVariantRotations(block),
            EBuildBrushRotationMode.Rotatable => ComputeRotatableRotations(block),
            EBuildBrushRotationMode.Hybrid => ComputeHybridRotations(block),
            _ => [new BlockRotationDefinition(block.BlockId, 0f)]
        };
    }

    /// <summary>
    /// Computes rotations for variant-based blocks (one definition per variant, 0° mesh angle).
    /// </summary>
    private BlockRotationDefinition[] ComputeVariantRotations(Block block)
    {
        Block[] variants = GetOrientationVariants(block);
        if (variants.Length == 0)
            return [new BlockRotationDefinition(block.BlockId, 0f)];

        var definitions = new BlockRotationDefinition[variants.Length];
        for (int i = 0; i < variants.Length; i++)
        {
            definitions[i] = new BlockRotationDefinition(variants[i].BlockId, 0f);
        }
        return definitions;
    }

    /// <summary>
    /// Computes rotations for IRotatable blocks (same block ID, different mesh angles).
    /// </summary>
    private BlockRotationDefinition[] ComputeRotatableRotations(Block block)
    {
        float intervalDegrees = ResolveRotationIntervalDegrees(block);
        if (intervalDegrees <= 0f)
        {
            // Default to 90° if no interval specified
            intervalDegrees = 90f;
        }

        int stepCount = (int)(360f / intervalDegrees);
        if (stepCount <= 0)
            stepCount = 1;

        var definitions = new BlockRotationDefinition[stepCount];
        for (int i = 0; i < stepCount; i++)
        {
            float angle = i * intervalDegrees;
            definitions[i] = new BlockRotationDefinition(block.BlockId, angle);
        }
        return definitions;
    }

    /// <summary>
    /// Computes rotations for hybrid blocks.
    /// Each variant gets mesh-angle sub-steps within its "slice" of 360°.
    /// </summary>
    private BlockRotationDefinition[] ComputeHybridRotations(Block block)
    {
        Block[] variants = GetOrientationVariants(block);
        if (variants.Length == 0)
            return ComputeRotatableRotations(block);

        float intervalDegrees = ResolveRotationIntervalDegrees(block);
        if (intervalDegrees <= 0f)
        {
            // No mesh-angle interval, fall back to variant-only
            return ComputeVariantRotations(block);
        }

        // Each variant "owns" a slice of 360°
        float sliceDegrees = 360f / variants.Length;

        // Calculate how many mesh-angle steps fit within each slice
        int stepsPerVariant = (int)(sliceDegrees / intervalDegrees);
        if (stepsPerVariant <= 0)
            stepsPerVariant = 1;

        var definitions = new List<BlockRotationDefinition>();

        for (int v = 0; v < variants.Length; v++)
        {
            int variantBlockId = variants[v].BlockId;

            for (int s = 0; s < stepsPerVariant; s++)
            {
                float meshAngle = s * intervalDegrees;
                definitions.Add(new BlockRotationDefinition(variantBlockId, meshAngle));
            }
        }

        return definitions.Count > 0
            ? [.. definitions]
            : [new BlockRotationDefinition(block.BlockId, 0f)];
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
    /// Resolves the rotation interval in degrees from block attributes.
    /// </summary>
    private static float ResolveRotationIntervalDegrees(Block block)
    {
        if (block?.Attributes is null)
            return 0f;

        string? type = block.Attributes["type"]?.AsString();
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

        if (string.IsNullOrEmpty(intervalString))
            return 0f;

        return intervalString switch
        {
            "22.5deg" => 22.5f,
            "22.5degnot45deg" => 22.5f,
            "45deg" => 45f,
            "90deg" => 90f,
            _ => 0f
        };
    }
    #endregion
}
