using System.Collections.Immutable;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Encapsulates rotation state for a build brush block.
/// Holds a reference to the precomputed rotation definitions and tracks current index.
/// </summary>
public class BuildBrushRotationInfo
{
    #region Fields
    private readonly IWorldAccessor _world;
    private readonly Block _originalBlock;
    private int _currentIndex;
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
            _currentIndex = ((value % Definitions.Length) + Definitions.Length) % Definitions.Length;
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
    /// The total number of orientation states available.
    /// </summary>
    public int OrientationCount => Definitions.Length;
    #endregion

    #region Constructor
    private BuildBrushRotationInfo(
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
    /// <returns>Rotation info for the block.</returns>
    public static BuildBrushRotationInfo Create(Block block, BlockRotationResolver resolver)
    {
        if (block is null || resolver is null)
            return CreateEmpty(resolver?.World);

        EBuildBrushRotationMode mode = resolver.GetRotationMode(block);
        ImmutableArray<BlockOrientationDefinition> definitions = resolver.GetRotations(block.BlockId);

        return new BuildBrushRotationInfo(resolver.World, block, mode, definitions);
    }

    /// <summary>
    /// Creates an empty rotation info (for null/invalid blocks).
    /// </summary>
    private static BuildBrushRotationInfo CreateEmpty(IWorldAccessor? world)
    {
        return new BuildBrushRotationInfo(
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
        int index = BlockRotationResolver.FindIndexForBlockId(Definitions, blockId);
        if (index >= 0 && index < Definitions.Length && Definitions[index].BlockId == blockId)
        {
            CurrentIndex = index;
            return true;
        }
        return false;
    }
    #endregion
}
