using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Encapsulates orientation state for a build brush block.
/// Holds a reference to the precomputed orientation definitions and tracks current index.
/// </summary>
public class BuildBrushOrientationInfo
{
    #region Fields
    private readonly IWorldAccessor _world;
    private readonly Block _originalBlock;
    private int _currentIndex;
    #endregion

    #region Events
    /// <summary>
    /// Raised when the orientation index changes.
    /// Provides both previous and current state for proper change detection.
    /// </summary>
    public event EventHandler<OrientationIndexChangedEventArgs>? OnOrientationChanged;
    #endregion

    #region Properties
    /// <summary>
    /// The rotation mode for this block (for classification/display purposes).
    /// </summary>
    public EBuildBrushRotationMode Mode { get; }

    /// <summary>
    /// All valid orientation definitions for this block.
    /// Precomputed array that can be cycled through.
    /// </summary>
    public ImmutableArray<BlockOrientationDefinition> Definitions { get; }

    /// <summary>
    /// The current index into the <see cref="Definitions"/> array.
    /// Setting this property raises <see cref="OnOrientationChanged"/> if the value changes.
    /// </summary>
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (Definitions.IsDefaultOrEmpty)
            {
                _currentIndex = 0;
                return;
            }

            // Wrap around (handles negative values too)
            int newIndex = ((value % Definitions.Length) + Definitions.Length) % Definitions.Length;

            if (newIndex == _currentIndex)
                return;

            // Capture previous state BEFORE mutation
            int previousIndex = _currentIndex;
            BlockOrientationDefinition previousDef = Current;
            Block? previousBlock = CurrentBlock;
            float previousAngle = CurrentMeshAngleDegrees;

            // Mutate
            _currentIndex = newIndex;

            // Raise event with previous and current state
            OnOrientationChanged?.Invoke(this, new OrientationIndexChangedEventArgs(
                previousIndex,
                _currentIndex,
                previousDef,
                Current,
                previousBlock,
                CurrentBlock,
                previousAngle,
                CurrentMeshAngleDegrees
            ));
        }
    }

    /// <summary>
    /// The current orientation definition (block-id + mesh-angle).
    /// </summary>
    public BlockOrientationDefinition Current => !Definitions.IsDefaultOrEmpty
        ? Definitions[_currentIndex]
        : new BlockOrientationDefinition(_originalBlock?.BlockId ?? 0, 0f);

    /// <summary>
    /// The block corresponding to the current rotation state.
    /// </summary>
    public Block CurrentBlock => _world.GetBlock(Current.BlockId) ?? _originalBlock;

    /// <summary>
    /// The current mesh angle in degrees.
    /// </summary>
    public float CurrentMeshAngleDegrees => Current.MeshAngleDegrees;

    /// <summary>
    /// The current mesh angle in radians.
    /// </summary>
    public float CurrentMeshAngleRadians => Current.MeshAngleDegrees * GameMath.DEG2RAD;

    /// <summary>
    /// Whether this block supports any form of rotation.
    /// </summary>
    public bool CanRotate => Mode != EBuildBrushRotationMode.None && !Definitions.IsDefaultOrEmpty && Definitions.Length > 1;

    /// <summary>
    /// Whether this block uses variant-based rotation (including hybrid).
    /// </summary>
    public bool HasVariants => Mode is EBuildBrushRotationMode.VariantBased or EBuildBrushRotationMode.Hybrid;

    /// <summary>
    /// Whether this block has an IRotatable block entity (including hybrid).
    /// </summary>
    public bool HasRotatableEntity => Mode is EBuildBrushRotationMode.Rotatable or EBuildBrushRotationMode.Hybrid;

    /// <summary>
    /// Gets the mesh angle increment in degrees between orientation steps.
    /// For rotatable blocks, this is typically 90°. For variant-only blocks, returns 0.
    /// </summary>
    public float MeshIncrementAngleDegrees
    {
        get
        {
            if (Definitions.IsDefaultOrEmpty || Definitions.Length <= 1)
                return 0f;

            // For IRotatable/Hybrid modes, compute the increment from definitions
            if (HasRotatableEntity && Definitions.Length >= 2)
            {
                // Use the difference between first two definitions as the step
                return Definitions[1].MeshAngleDegrees - Definitions[0].MeshAngleDegrees;
            }

            // Variant-based rotation doesn't use mesh angles (returns 0)
            return 0f;
        }
    }

    /// <summary>
    /// The total number of orientation states available.
    /// </summary>
    public int OrientationCount => Definitions.Length;
    #endregion

    #region Constructor
    private BuildBrushOrientationInfo(
        IWorldAccessor world,
        Block originalBlock,
        EBuildBrushRotationMode mode,
        ImmutableArray<BlockOrientationDefinition> definitions)
    {
        _world = world;
        _originalBlock = originalBlock;
        Mode = mode;
        Definitions = definitions;
        _currentIndex = 0;
    }
    #endregion

    #region Factory
    /// <summary>
    /// Creates rotation info for a block using the provided resolver.
    /// </summary>
    /// <param name="block">The block to create rotation info for.</param>
    /// <param name="resolver">The resolver to get rotation definitions from.</param>
    /// <param name="itemStack">Optional ItemStack to resolve type-specific properties (e.g., for typed containers).</param>
    /// <returns>Rotation info for the block.</returns>
    public static BuildBrushOrientationInfo Create(Block block, BlockOrientationResolver resolver, ItemStack? itemStack = null)
    {
        if (block is null || resolver is null)
            return CreateEmpty(resolver?.World);

        EBuildBrushRotationMode mode = resolver.GetRotationMode(block);
        ImmutableArray<BlockOrientationDefinition> definitions = resolver.GetOrientations(block.BlockId, itemStack);

        return new BuildBrushOrientationInfo(resolver.World, block, mode, definitions);
    }

    /// <summary>
    /// Creates an empty rotation info (for null/invalid blocks).
    /// </summary>
    private static BuildBrushOrientationInfo CreateEmpty(IWorldAccessor? world)
    {
        return new BuildBrushOrientationInfo(
            world!,
            null!,
            EBuildBrushRotationMode.None,
            ImmutableArray<BlockOrientationDefinition>.Empty);
    }
    #endregion

    #region Rotation
    /// <summary>
    /// Advances to the next orientation state.
    /// </summary>
    /// <param name="direction">Direction to cycle (Forward = +1, Backward = -1).</param>
    /// <returns>The new current orientation definition.</returns>
    public BlockOrientationDefinition Rotate(EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        CurrentIndex += (int)direction;
        return Current;
    }

    /// <summary>
    /// Sets the current index to match a specific block ID.
    /// Finds the first definition with the matching block ID.
    /// </summary>
    /// <param name="blockId">The block ID to match.</param>
    /// <returns>True if a matching definition was found.</returns>
    public bool TrySetIndexForBlockId(int blockId)
    {
        int index = BlockOrientationResolver.FindIndexForBlockId(Definitions, blockId);
        if (index >= 0 && index < Definitions.Length && Definitions[index].BlockId == blockId)
        {
            CurrentIndex = index;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Applies current orientation state to a tree attribute.
    /// Sets type (from source) and meshAngle (from current rotation) for proper rendering.
    /// </summary>
    /// <param name="targetAttributes">The tree attributes to apply orientation to.</param>
    /// <param name="sourceAttributes">Optional source attributes to copy type from.</param>
    public void ApplyOrientationAttributes(ITreeAttribute targetAttributes, ITreeAttribute? sourceAttributes = null)
    {
        if (targetAttributes is null)
            return;

        // Copy type attribute from source (for typed containers like crates/chests)
        string? type = sourceAttributes?.GetString("type");
        if (!string.IsNullOrEmpty(type))
        {
            targetAttributes.SetString("type", type);
        }

        // Set meshAngle for IRotatable blocks
        if (HasRotatableEntity)
        {
            targetAttributes.SetFloat("meshAngle", CurrentMeshAngleRadians);
        }
    }

    /// <summary>
    /// Applies orientation to an existing block entity in-place using IRotatable.OnTransformed.
    /// This resets the BE to the original state first, then applies the absolute rotation.
    /// This matches how WorldEdit/schematics apply rotations.
    /// </summary>
    /// <param name="blockEntity">The block entity to update.</param>
    /// <param name="originalTree">The original (un-rotated) tree attributes to start from.</param>
    /// <param name="absoluteAngleDegrees">The absolute rotation angle in degrees (0, 90, 180, 270).</param>
    /// <param name="sourceAttributes">Optional source attributes to copy type from.</param>
    /// <returns>True if rotation was applied, false if the block entity doesn't support rotation.</returns>
    public bool ApplyOrientationToBlockEntity(BlockEntity blockEntity, ITreeAttribute? originalTree, int absoluteAngleDegrees, ITreeAttribute? sourceAttributes = null)
    {
        if (blockEntity is null)
            return false;

        // Find IRotatable on entity or behaviors
        IRotatable? rotatable = blockEntity as IRotatable;
        if (rotatable is null)
        {
            foreach (var behavior in blockEntity.Behaviors)
            {
                if (behavior is IRotatable r)
                {
                    rotatable = r;
                    break;
                }
            }
        }

        if (rotatable is null)
            return false;

        // Start from original tree state (clone it to avoid modifying the original)
        ITreeAttribute tree;
        if (originalTree is not null)
        {
            tree = originalTree.Clone();
        }
        else
        {
            // Fallback: get current tree from BE (not ideal but better than nothing)
            tree = new TreeAttribute();
            blockEntity.ToTreeAttributes(tree);
        }

        // Copy type attribute from source (for typed containers)
        string? type = sourceAttributes?.GetString("type");
        if (!string.IsNullOrEmpty(type))
        {
            tree.SetString("type", type);
        }

        // Apply absolute rotation from original state via OnTransformed
        // This is how WorldEdit/schematics work - always from original with absolute angle
        if (absoluteAngleDegrees != 0)
        {
            rotatable.OnTransformed(
                _world,
                tree,
                absoluteAngleDegrees,
                new Dictionary<int, AssetLocation>(), // oldBlockIdMapping - not needed for live rotation
                new Dictionary<int, AssetLocation>(), // oldItemIdMapping - not needed for live rotation
                null // flipAxis - no flip, only rotation
            );
        }

        // Write back to BE
        blockEntity.FromTreeAttributes(tree, _world);
        blockEntity.MarkDirty(true);

        return true;
    }
    #endregion
}
