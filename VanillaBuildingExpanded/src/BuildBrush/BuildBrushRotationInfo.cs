using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Encapsulates all rotation-related data and logic for a build brush block.
/// Combines variant-based rotation detection with IRotatable entity support.
/// </summary>
public class BuildBrushRotationInfo
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

    /// <summary>
    /// Cache of orientation variants per block base code.
    /// </summary>
    public static readonly Dictionary<AssetLocation, Block[]> OrientationVariantCache = [];

    /// <summary>
    /// Cache of detected rotation modes per block code.
    /// </summary>
    private static readonly Dictionary<AssetLocation, EBuildBrushRotationMode> RotationModeCache = [];
    #endregion

    #region Fields
    private readonly IWorldAccessor world;
    private readonly Block originalBlock;
    private readonly float rotatableIntervalRadians;
    private int _currentIndex = 0;
    private int _currentAngle = 0;
    #endregion

    #region Properties
    /// <summary>
    /// The rotation mode for this block.
    /// </summary>
    public EBuildBrushRotationMode Mode { get; }

    /// <summary>
    /// Available orientation variants for variant-based rotation.
    /// </summary>
    public ImmutableArray<Block> Variants { get; }

    /// <summary>
    /// The current variant index (for variant-based rotation).
    /// </summary>
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (Variants.IsDefaultOrEmpty)
            {
                _currentIndex = 0;
                return;
            }
            _currentIndex = ((value % Variants.Length) + Variants.Length) % Variants.Length;
        }
    }

    /// <summary>
    /// The current rotation angle in degrees (0, 90, 180, 270).
    /// </summary>
    public int CurrentAngle
    {
        get => _currentAngle;
        set => _currentAngle = ((value % 360) + 360) % 360;
    }

    /// <summary>
    /// Block entity tree attributes for IRotatable blocks.
    /// Null if the block doesn't have an IRotatable entity.
    /// </summary>
    public ITreeAttribute? EntityTree { get; private set; }

    /// <summary>
    /// The currently selected block variant based on CurrentIndex.
    /// </summary>
    public Block CurrentVariant => Variants.IsDefaultOrEmpty ? originalBlock : Variants[_currentIndex];

    /// <summary>
    /// Whether this block supports any form of rotation.
    /// </summary>
    public bool CanRotate => Mode != EBuildBrushRotationMode.None;

    /// <summary>
    /// Whether this block uses variant-based rotation.
    /// </summary>
    public bool HasVariants => Mode is EBuildBrushRotationMode.VariantBased or EBuildBrushRotationMode.Hybrid;

    /// <summary>
    /// Whether this block has an IRotatable block entity.
    /// </summary>
    public bool HasRotatableEntity => Mode is EBuildBrushRotationMode.Rotatable or EBuildBrushRotationMode.Hybrid;

    /// <summary>
    /// The rotation increment in degrees for this block.
    /// For variant-based rotation, this is calculated from the number of variants (360 / count).
    /// For IRotatable blocks, uses the rotatatableInterval from block attributes.
    /// </summary>
    public int RotationIncrement
    {
        get
        {
            // For variant-based rotation, calculate from variant count
            if (HasVariants && !Variants.IsDefaultOrEmpty)
            {
                // Common cases: 4 variants = 90°, 8 variants = 45°, 16 variants = 22.5° (rounded to 22)
                return 360 / Variants.Length;
            }

            // For IRotatable blocks, use the resolved interval from block attributes
            if (HasRotatableEntity && rotatableIntervalRadians > 0)
            {
                return (int)(rotatableIntervalRadians * GameMath.RAD2DEG);
            }

            // Default to 90 degrees
            return 90;
        }
    }

    /// <summary>
    /// The rotation increment in radians for IRotatable blocks.
    /// Returns 0 if not applicable.
    /// </summary>
    public float RotationIncrementRadians => rotatableIntervalRadians > 0 ? rotatableIntervalRadians : (GameMath.PIHALF);
    #endregion

    #region Constructor
    private BuildBrushRotationInfo(
        IWorldAccessor world,
        Block block,
        EBuildBrushRotationMode mode,
        ImmutableArray<Block> variants,
        float rotatableIntervalRadians)
    {
        this.world = world;
        this.originalBlock = block;
        this.Mode = mode;
        this.Variants = variants;
        this.rotatableIntervalRadians = rotatableIntervalRadians;

        // Initialize entity tree if block has IRotatable
        if (HasRotatableEntity)
        {
            InitializeEntityTree(block);
        }
    }
    #endregion

    #region Factory
    /// <summary>
    /// Creates rotation info for a block, detecting its rotation capabilities.
    /// </summary>
    /// <param name="block">The block to analyze.</param>
    /// <param name="world">The world accessor.</param>
    /// <returns>Rotation info for the block.</returns>
    public static BuildBrushRotationInfo Create(Block block, IWorldAccessor world)
    {
        if (block is null)
            return CreateEmpty(world);

        // Detect rotation mode
        EBuildBrushRotationMode mode = DetectRotationMode(block, world);

        // Get variants
        ImmutableArray<Block> variants = GetOrientationVariants(block, world);

        // Resolve rotatable interval for IRotatable blocks
        float rotatableInterval = 0f;
        if (mode is EBuildBrushRotationMode.Rotatable or EBuildBrushRotationMode.Hybrid)
        {
            rotatableInterval = BlockEntityRotationHelper.ResolveRotationInterval(block);
        }

        return new BuildBrushRotationInfo(world, block, mode, variants, rotatableInterval);
    }

    /// <summary>
    /// Creates an empty rotation info (for null/invalid blocks).
    /// </summary>
    private static BuildBrushRotationInfo CreateEmpty(IWorldAccessor world)
    {
        return new BuildBrushRotationInfo(
            world,
            null!,
            EBuildBrushRotationMode.None,
            ImmutableArray<Block>.Empty,
            0f);
    }
    #endregion

    #region Detection
    /// <summary>
    /// Detects the rotation mode for a block.
    /// </summary>
    private static EBuildBrushRotationMode DetectRotationMode(Block block, IWorldAccessor world)
    {
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
    /// Checks if the block has orientation variants by looking for known variant keys.
    /// </summary>
    private static bool HasVariantBasedRotation(Block block, IWorldAccessor world)
    {
        // Check if block has any of the valid orientation variant keys
        return block.Variant.Keys.Any(static k => ValidOrientationVariantKeys.Contains(k));
    }

    /// <summary>
    /// Checks if the block has a block entity that implements IRotatable.
    /// </summary>
    private static bool HasRotatableBlockEntity(Block block, IWorldAccessor world)
    {
        if (string.IsNullOrEmpty(block.EntityClass))
            return false;

        try
        {
            Type? entityType = world.Api.ClassRegistry.GetBlockEntity(block.EntityClass);
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
    /// Gets or creates the orientation variants for a block.
    /// </summary>
    private static ImmutableArray<Block> GetOrientationVariants(Block block, IWorldAccessor world)
    {
        string baseCode = block.Code.FirstCodePart();

        // Check cache
        if (OrientationVariantCache.TryGetValue(baseCode, out Block[]? cachedVariants))
        {
            return cachedVariants.ToImmutableArray();
        }

        // Find the variant group
        string? foundVariantGroup = block.Variant.Keys
            .FirstOrDefault(k => ValidOrientationVariantKeys.Contains(k));

        if (foundVariantGroup is null)
        {
            return [block];
        }

        // Search for all variants
        AssetLocation? searchCode = block.CodeWithVariant(foundVariantGroup, "*");
        if (searchCode is null)
        {
            return [block];
        }

        Block[] variants = world.SearchBlocks(searchCode);
        if (variants.Length == 0)
        {
            return [block];
        }

        // Cache the results
        OrientationVariantCache.TryAdd(baseCode, variants);
        return variants.ToImmutableArray();
    }
    #endregion

    #region Entity Tree
    /// <summary>
    /// Initializes the block entity tree attributes.
    /// </summary>
    private void InitializeEntityTree(Block block)
    {
        if (string.IsNullOrEmpty(block.EntityClass))
            return;

        try
        {
            EntityTree = new TreeAttribute();
            EntityTree.SetString("blockCode", block.Code.ToShortString());
        }
        catch
        {
            EntityTree = null;
        }
    }

    /// <summary>
    /// Applies rotation to the entity tree using IRotatable.OnTransformed.
    /// </summary>
    /// <param name="angle">The rotation angle in degrees.</param>
    public void ApplyRotationToEntityTree(int angle)
    {
        if (EntityTree is null || !HasRotatableEntity)
            return;

        Block block = CurrentVariant;
        if (string.IsNullOrEmpty(block?.EntityClass))
            return;

        try
        {
            BlockEntity be = world.ClassRegistry.CreateBlockEntity(block.EntityClass);
            if (be is null)
                return;

            be.CreateBehaviors(block, world);

            // Find IRotatable - check entity first, then behaviors
            IRotatable? rotatable = be as IRotatable;
            if (rotatable is null)
            {
                foreach (var behavior in be.Behaviors)
                {
                    if (behavior is IRotatable r)
                    {
                        rotatable = r;
                        break;
                    }
                }
            }

            if (rotatable is not null)
            {
                rotatable.OnTransformed(
                    world,
                    EntityTree,
                    angle,
                    new Dictionary<int, AssetLocation>(),
                    new Dictionary<int, AssetLocation>(),
                    null);
            }
        }
        catch
        {
            // Rotation failed silently
        }
    }
    #endregion

    #region Index Synchronization
    /// <summary>
    /// Sets the current index to match a specific block variant.
    /// </summary>
    /// <param name="blockId">The block ID to match.</param>
    /// <returns>True if a matching variant was found.</returns>
    public bool TrySetIndexForBlock(int blockId)
    {
        if (Variants.IsDefaultOrEmpty)
            return false;

        Block? foundVariant = Variants.FirstOrDefault(b => b.BlockId == blockId);
        if (foundVariant is null)
            return false;

        int foundIndex = Variants.IndexOf(foundVariant);
        if (foundIndex < 0)
            return false;

        CurrentIndex = foundIndex;
        return true;
    }
    #endregion

    #region Cache Management
    /// <summary>
    /// Clears all rotation-related caches.
    /// </summary>
    public static void ClearCaches()
    {
        RotationModeCache.Clear();
        OrientationVariantCache.Clear();
    }
    #endregion
}
