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
    /// <summary>
    /// Tracks the last rotation angle that was applied to the block entity.
    /// Used to compute delta rotations for IRotatable.OnTransformed which expects relative angles.
    /// </summary>
    private float _previouslyAppliedMeshAngle;
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

            // Mutate
            _currentIndex = newIndex;

            // Raise event with previous and current state
            OnOrientationChanged?.Invoke(this, new OrientationIndexChangedEventArgs(
                previousIndex,
                _currentIndex,
                previousDef,
                Current
            ));

            // Update tracking after event is raised
            _previouslyAppliedMeshAngle = CurrentMeshAngleDegrees;
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

    public string? RotationAttribute => Current.RotationAttribute;

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
    internal BuildBrushOrientationInfo(
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
    /// Forces an <see cref="OnOrientationChanged"/> notification without changing the index.
    /// Useful when rotation definitions are replaced (e.g., selecting a new block) and listeners
    /// must re-apply orientation-dependent state even if the index remains the same.
    /// </summary>
    /// <param name="previousDefinition">The previous orientation definition (for delta computation).</param>
    /// <param name="currentDefinition">The current/target orientation definition to apply.</param>
    /// <param name="previousIndex">Optional previous index. Defaults to current index.</param>
    public void NotifyOrientationChanged(
        in BlockOrientationDefinition previousDefinition,
        in BlockOrientationDefinition currentDefinition,
        int? previousIndex = null)
    {
        if (Definitions.IsDefaultOrEmpty)
            return;

        int prevIndex = previousIndex ?? _currentIndex;

        OnOrientationChanged?.Invoke(this, new OrientationIndexChangedEventArgs(
            prevIndex,
            _currentIndex,
            previousDefinition,
            currentDefinition
        ));

        _previouslyAppliedMeshAngle = currentDefinition.MeshAngleDegrees;
    }

    /// <summary>
    /// Applies orientation to a block entity using IRotatable.OnTransformed.
    /// Computes delta rotation from previous definition and applies it to the entity.
    /// </summary>
    /// <param name="blockEntity">The block entity to rotate.</param>
    /// <param name="previousDefinition">The previous orientation definition (for delta computation).</param>
    /// <param name="currentDefinition">The current/target orientation definition to apply.</param>
    /// <param name="sourceAttributes">Optional source attributes to copy type from.</param>
    /// <returns>True if rotation was applied, false if the block entity doesn't support rotation.</returns>
    public bool ApplyToBlockEntity(
        BlockEntity blockEntity,
        in BlockOrientationDefinition previousDefinition,
        in BlockOrientationDefinition currentDefinition,
        ITreeAttribute? sourceAttributes = null)
    {
        if (blockEntity is null)
            return false;

        // Find IRotatable on entity or behaviors
        if (!BlockOrientationResolver.TryGetRotationInterface(blockEntity, out IRotatable? rotatable))
            return false;

        // Get current tree state from the block entity
        TreeAttribute tree = new();
        blockEntity.ToTreeAttributes(tree);

        // Copy type attribute from source (for typed containers)
        string? type = sourceAttributes?.GetString("type");
        if (!string.IsNullOrEmpty(type))
        {
            tree.SetString("type", type);
        }

        // Compute the relative/delta rotation (OnTransformed expects relative angles)
        int deltaAngle = ComputeShortestRotationDelta(
            currentDefinition.MeshAngleDegrees,
            previousDefinition.MeshAngleDegrees
        );

        // Apply delta rotation via OnTransformed
        rotatable.OnTransformed(
            _world,
            tree,
            deltaAngle,
            [], // oldBlockIdMapping - not needed for live rotation
            [], // oldItemIdMapping - not needed for live rotation
            null // flipAxis - no flip, only rotation
        );

        // Write back to BE
        blockEntity.FromTreeAttributes(tree, _world);
        blockEntity.MarkDirty(true);

        return true;
    }

    /// <summary>
    /// Applies the target orientation to a block entity that was just placed.
    /// This is intended to correct cases where vanilla placement logic overwrites
    /// mesh rotation based on player facing, even when the brush has a specific
    /// target rotation.
    /// </summary>
    /// <remarks>
    /// Prefers using <see cref="IRotatable.OnTransformed"/> when available (so any
    /// entity-specific rotation logic runs), but falls back to directly setting the
    /// known rotation attribute in the tree.
    /// </remarks>
    public bool ApplyToPlacedBlockEntity(BlockEntity blockEntity, in BlockOrientationDefinition targetDefinition, ItemStack? sourceItemStack = null)
    {
        if (blockEntity is null)
            return false;

        // Extract current state from the block entity
        TreeAttribute tree = new();
        blockEntity.ToTreeAttributes(tree);

        // Copy type attribute from source (for typed containers)
        string? type = sourceItemStack?.Attributes?.GetString("type");
        if (!string.IsNullOrEmpty(type))
        {
            tree.SetString("type", type);
        }

        string? attrName = targetDefinition.RotationAttribute;
        if (string.IsNullOrEmpty(attrName))
        {
            return false;
        }

        bool isDegrees = BlockOrientationResolver.IsMeshRotationAttributeDegrees(attrName);
        float currentValue = tree.GetFloat(attrName, 0f);
        float currentAngleDeg = isDegrees ? currentValue : currentValue * GameMath.RAD2DEG;

        // Prefer using the existing ApplyToBlockEntity path so any BE-specific rotation logic runs.
        // ApplyToBlockEntity expects a previous definition to compute a delta, so we synthesize one
        // from the currently placed BE rotation.
        if (BlockOrientationResolver.TryGetRotationInterface(blockEntity, out IRotatable? rotatable) && rotatable is not null)
        {
            var previousDefinition = new BlockOrientationDefinition(
                targetDefinition.BlockId,
                currentAngleDeg,
                targetDefinition.RotationAttribute
            );

            return ApplyToBlockEntity(
                blockEntity,
                previousDefinition,
                targetDefinition,
                sourceItemStack?.Attributes
            );
        }

        // Fallback: set the target angle directly (useful for preview blocks or non-IRotatable BEs).
        TrySetMeshRotation(tree, targetDefinition);
        blockEntity.FromTreeAttributes(tree, _world);
        blockEntity.MarkDirty(true);
        return true;
    }

    /// <summary>
    /// Prepares an ItemStack for placement with orientation applied.
    /// Clones the target stack and applies orientation attributes.
    /// </summary>
    /// <param name="target">The ItemStack to prepare (will be cloned).</param>
    /// <param name="currentDefinition">The current orientation definition to apply.</param>
    /// <param name="source">Optional source ItemStack to copy attributes from.</param>
    /// <returns>A new ItemStack with orientation applied.</returns>
    public ItemStack PrepareItemStackForPlacement(
        ItemStack target,
        in BlockOrientationDefinition currentDefinition,
        ItemStack? source = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Clone the target stack to avoid mutating the original
        ItemStack prepared = target.Clone();

        // Ensure attributes exist
        if (prepared.Attributes is null)
        {
            prepared.Attributes = new TreeAttribute();
        }

        // Copy type attribute from source (for typed containers like crates/chests)
        string? type = source?.Attributes?.GetString("type");
        if (!string.IsNullOrEmpty(type))
        {
            prepared.Attributes.SetString("type", type);
        }

        // Set meshAngle for IRotatable blocks
        if (HasRotatableEntity)
        {
            TrySetMeshRotation(
                prepared.Attributes,
                currentDefinition
            );
        }

        return prepared;
    }
    

    #region Mesh Rotation
    /// <summary>
    /// Attempts to set the rotation value in the tree attribute using known attribute names.
    /// </summary>
    /// <param name="tree">The tree attribute to modify.</param>
    /// <param name="rotationRadians">The rotation in radians.</param>
    /// <returns>True if an attribute was found and set; otherwise, false.</returns>
    private static bool TrySetMeshRotation(ITreeAttribute? tree, in BlockOrientationDefinition orientation)
    {
        if (tree is null)
            return false;

        // get attribute name from definition
        var attrName = orientation.RotationAttribute;
        if (!string.IsNullOrEmpty(attrName))
        {
            bool isDegrees = BlockOrientationResolver.IsMeshRotationAttributeDegrees(attrName);
            float value = isDegrees ? orientation.MeshAngleDegrees : orientation.MeshAngleDegrees * GameMath.DEG2RAD;
            tree.SetFloat(attrName, value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes the shortest-path rotation delta for IRotatable.OnTransformed.
    /// OnTransformed SUBTRACTS the delta from the current angle, so to go from
    /// previousAngle to targetAngle, we compute: previousAngle - targetAngle.
    /// Handles wrap-around at 0°/360° boundary correctly.
    /// </summary>
    /// <param name="targetAngle">The target angle in degrees.</param>
    /// <param name="previousAngle">The previous angle in degrees.</param>
    /// <returns>The shortest rotation delta in degrees (-180 to 180).</returns>
    private static int ComputeShortestRotationDelta(float targetAngle, float previousAngle)
    {
        // OnTransformed subtracts the delta: newAngle = currentAngle - delta
        // To achieve targetAngle from previousAngle: targetAngle = previousAngle - delta
        // So: delta = previousAngle - targetAngle
        // Formula with wrap-around: ((previous - target + 540) % 360) - 180
        int delta = (((int)previousAngle - (int)targetAngle + 540) % 360) - 180;
        return delta;
    }
    #endregion
    #endregion
}
