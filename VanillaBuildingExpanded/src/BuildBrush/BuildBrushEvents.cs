using System;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

#region Base Event Args

/// <summary>
/// Base class for all BuildBrush state change events.
/// </summary>
public abstract class BuildBrushStateChangedEventArgs : EventArgs
{
}

#endregion

#region Orientation Events

/// <summary>
/// Event args for orientation index changes.
/// Raised by <see cref="BuildBrushOrientationInfo"/> when <see cref="BuildBrushOrientationInfo.CurrentIndex"/> changes.
/// </summary>
public class OrientationIndexChangedEventArgs : BuildBrushStateChangedEventArgs
{
    /// <summary>
    /// The orientation index before the change.
    /// </summary>
    public int PreviousIndex { get; }

    /// <summary>
    /// The orientation index after the change.
    /// </summary>
    public int CurrentIndex { get; }

    /// <summary>
    /// The orientation definition before the change.
    /// </summary>
    public BlockOrientationDefinition PreviousDefinition { get; }

    /// <summary>
    /// The orientation definition after the change.
    /// </summary>
    public BlockOrientationDefinition CurrentDefinition { get; }

    /// <summary>
    /// Whether the block variant changed (different BlockId).
    /// </summary>
    public bool VariantChanged => PreviousDefinition.BlockId != CurrentDefinition.BlockId;

    /// <summary>
    /// Whether only the mesh angle changed (same BlockId, different angle).
    /// </summary>
    public bool MeshAngleOnlyChanged => !VariantChanged && PreviousDefinition.MeshAngleDegrees != CurrentDefinition.MeshAngleDegrees;

    public OrientationIndexChangedEventArgs(
        int previousIndex,
        int currentIndex,
        BlockOrientationDefinition previousDefinition,
        BlockOrientationDefinition currentDefinition)
    {
        PreviousIndex = previousIndex;
        CurrentIndex = currentIndex;
        PreviousDefinition = previousDefinition;
        CurrentDefinition = currentDefinition;
    }
}

#endregion

#region Block Events

/// <summary>
/// Event args for block changes (untransformed or transformed).
/// </summary>
public class BlockChangedEventArgs : BuildBrushStateChangedEventArgs
{
    /// <summary>
    /// The block before the change.
    /// </summary>
    public Block? PreviousBlock { get; }

    /// <summary>
    /// The block after the change.
    /// </summary>
    public Block? CurrentBlock { get; }

    /// <summary>
    /// Whether this is the transformed (rotated) block or the original untransformed block.
    /// </summary>
    public bool IsTransformedBlock { get; }

    public BlockChangedEventArgs(Block? previousBlock, Block? currentBlock, bool isTransformedBlock)
    {
        PreviousBlock = previousBlock;
        CurrentBlock = currentBlock;
        IsTransformedBlock = isTransformedBlock;
    }
}

/// <summary>
/// Event args for when the rotation info itself is replaced (new block selected).
/// </summary>
public class RotationInfoChangedEventArgs : BuildBrushStateChangedEventArgs
{
    /// <summary>
    /// The previous rotation info (null if none was set).
    /// </summary>
    public BuildBrushOrientationInfo? PreviousRotation { get; }

    /// <summary>
    /// The current rotation info (null if cleared).
    /// </summary>
    public BuildBrushOrientationInfo? CurrentRotation { get; }

    /// <summary>
    /// The source block that triggered this change.
    /// </summary>
    public Block? SourceBlock { get; }

    public RotationInfoChangedEventArgs(
        BuildBrushOrientationInfo? previousRotation,
        BuildBrushOrientationInfo? currentRotation,
        Block? sourceBlock)
    {
        PreviousRotation = previousRotation;
        CurrentRotation = currentRotation;
        SourceBlock = sourceBlock;
    }
}

#endregion

#region Position Events

/// <summary>
/// Event args for position changes.
/// </summary>
public class PositionChangedEventArgs : BuildBrushStateChangedEventArgs
{
    /// <summary>
    /// The position before the change.
    /// </summary>
    public BlockPos? PreviousPosition { get; }

    /// <summary>
    /// The position after the change.
    /// </summary>
    public BlockPos? CurrentPosition { get; }

    public PositionChangedEventArgs(BlockPos? previousPosition, BlockPos? currentPosition)
    {
        // Clone to avoid mutation issues
        PreviousPosition = previousPosition?.Copy();
        CurrentPosition = currentPosition?.Copy();
    }
}

#endregion

#region Snapping Events

/// <summary>
/// Event args for snapping mode changes.
/// </summary>
public class SnappingModeChangedEventArgs : BuildBrushStateChangedEventArgs
{
    /// <summary>
    /// The snapping mode before the change.
    /// </summary>
    public EBuildBrushSnapping PreviousMode { get; }

    /// <summary>
    /// The snapping mode after the change.
    /// </summary>
    public EBuildBrushSnapping CurrentMode { get; }

    public SnappingModeChangedEventArgs(EBuildBrushSnapping previousMode, EBuildBrushSnapping currentMode)
    {
        PreviousMode = previousMode;
        CurrentMode = currentMode;
    }
}

#endregion

#region Lifecycle Events

/// <summary>
/// Event args for brush activation state changes.
/// </summary>
public class BrushActivationChangedEventArgs : BuildBrushStateChangedEventArgs
{
    /// <summary>
    /// Whether the brush was active before the change.
    /// </summary>
    public bool WasActive { get; }

    /// <summary>
    /// Whether the brush is active after the change.
    /// </summary>
    public bool IsActive { get; }

    public BrushActivationChangedEventArgs(bool wasActive, bool isActive)
    {
        WasActive = wasActive;
        IsActive = isActive;
    }
}

/// <summary>
/// Event args for dimension lifecycle events.
/// </summary>
public class DimensionLifecycleEventArgs : BuildBrushStateChangedEventArgs
{
    /// <summary>
    /// Whether the dimension was created (true) or destroyed (false).
    /// </summary>
    public bool IsCreated { get; }

    /// <summary>
    /// The dimension instance (available on creation, may be null on destruction).
    /// </summary>
    public BuildBrushDimension? Dimension { get; }

    public DimensionLifecycleEventArgs(bool isCreated, BuildBrushDimension? dimension)
    {
        IsCreated = isCreated;
        Dimension = dimension;
    }
}

#endregion
