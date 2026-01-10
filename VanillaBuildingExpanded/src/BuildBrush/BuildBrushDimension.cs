using System;
using System.Collections.Generic;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Event args for dimension dirty events.
/// </summary>
public class DimensionDirtyEventArgs : EventArgs
{
    /// <summary>
    /// The reason the dimension was marked dirty.
    /// </summary>
    public string Reason { get; }

    public DimensionDirtyEventArgs(string reason = "")
    {
        Reason = reason;
    }
}

/// <summary>
/// Wrapper class that manages a mini-dimension for the build brush system.
/// Handles block placement and rotation within the preview dimension.
/// </summary>
public class BuildBrushDimension
{
    #region Fields
    private readonly IWorldAccessor world;
    private readonly ICoreServerAPI? sapi;
    private IMiniDimension? dimension;
    private int dimensionId = -1;

    /// <summary>
    /// The BuildBrushInstance this dimension is subscribed to (if any).
    /// </summary>
    private BuildBrushInstance? _subscribedInstance;

    /// <summary>
    /// The current block placed in the dimension (may be rotated variant).
    /// </summary>
    private Block? currentBlock;

    /// <summary>
    /// The original unrotated block.
    /// </summary>
    private Block? originalBlock;

    /// <summary>
    /// The position within the mini-dimension where the block is placed.
    /// </summary>
    private BlockPos? internalBlockPos;

    /// <summary>
    /// Minimum corner of the active bounds (blocks that have been placed).
    /// </summary>
    private BlockPos? activeBoundsMin;

    /// <summary>
    /// Maximum corner of the active bounds (blocks that have been placed).
    /// </summary>
    private BlockPos? activeBoundsMax;

    /// <summary>
    /// Tracks nested BeginUpdate/EndUpdate calls.
    /// </summary>
    private int _updateDepth = 0;

    /// <summary>
    /// Whether MarkDirty was called during a batch update.
    /// </summary>
    private bool _isDirtyDuringUpdate = false;

    #endregion

    #region Properties
    /// <summary>
    /// The underlying mini-dimension.
    /// </summary>
    public IMiniDimension? Dimension => dimension;

    /// <summary>
    /// The sub-dimension ID assigned to this brush dimension.
    /// </summary>
    public int DimensionId => dimensionId;

    /// <summary>
    /// Whether the dimension has been initialized.
    /// </summary>
    public bool IsInitialized => dimension is not null && dimensionId >= 0;

    /// <summary>
    /// The current rotation angle in degrees (0, 90, 180, 270).
    /// </summary>
    public int RotationAngle { get; private set; } = 0;

    /// <summary>
    /// The detected rotation mode for the current block.
    /// </summary>
    public EBuildBrushRotationMode RotationMode { get; private set; } = EBuildBrushRotationMode.None;

    /// <summary>
    /// The current block in the dimension (may be a rotated variant).
    /// </summary>
    public Block? CurrentBlock => currentBlock;

    /// <summary>
    /// The original unrotated block.
    /// </summary>
    public Block? OriginalBlock => originalBlock;

    /// <summary>
    /// Whether there are any blocks placed in the dimension.
    /// </summary>
    public bool HasActiveBounds => activeBoundsMin is not null && activeBoundsMax is not null;
    #endregion

    #region Events
    /// <summary>
    /// Raised when the dimension content changes and the mesh needs to be rebuilt.
    /// </summary>
    public event EventHandler<DimensionDirtyEventArgs>? OnDirty;
    #endregion

    #region Constructor
    public BuildBrushDimension(IWorldAccessor world)
    {
        this.world = world;
        this.sapi = world.Api as ICoreServerAPI;
    }
    #endregion

    #region Initialization
    /// <summary>
    /// Initializes the mini-dimension. Must be called on server side.
    /// </summary>
    /// <param name="existingDimensionId">Optional existing dimension ID to reuse.</param>
    /// <returns>True if initialization succeeded.</returns>
    public bool Initialize(int existingDimensionId = -1)
    {
        if (sapi is null)
            return false;

        // Create the mini-dimension
        dimension = world.BlockAccessor.CreateMiniDimension(new Vec3d());

        if (existingDimensionId >= 0)
        {
            // Reuse existing dimension ID
            dimensionId = existingDimensionId;
            sapi.Server.SetMiniDimension(dimension, dimensionId);
        }
        else
        {
            // Allocate new dimension ID
            dimensionId = sapi.Server.LoadMiniDimension(dimension);
        }

        dimension.SetSubDimensionId(dimensionId);
        dimension.BlocksPreviewSubDimension_Server = dimensionId;

        return true;
    }

    /// <summary>
    /// Initializes the dimension wrapper on client side by wrapping an existing dimension.
    /// This is used when the entity is received from the server and we need to wrap its dimension.
    /// </summary>
    /// <param name="existingDimension">The dimension from the entity to wrap.</param>
    /// <returns>True if initialization succeeded.</returns>
    public bool InitializeClientSide(IMiniDimension existingDimension)
    {
        if (existingDimension is null)
            return false;

        dimension = existingDimension;
        dimensionId = existingDimension.subDimensionId;

        // Try to detect active bounds from existing blocks
        UpdateActiveBoundsFromDimension();

        return true;
    }

    /// <summary>
    /// Scans the dimension to update active bounds based on existing blocks.
    /// Used on client side when wrapping a dimension that already has content.
    /// </summary>
    private void UpdateActiveBoundsFromDimension()
    {
        if (dimension is null)
            return;

        // Check the origin position for a block (most common case for single-block brush)
        BlockPos originPos = new(0, 0, 0, Dimensions.MiniDimensions);
        Block? block = dimension.GetBlock(originPos);
        if (block is not null && block.BlockId != 0)
        {
            currentBlock = block;
            originalBlock = block;
            internalBlockPos = originPos.Copy();
            activeBoundsMin = originPos.Copy();
            activeBoundsMax = originPos.Copy();
        }
    }

    /// <summary>
    /// Clears the dimension contents without destroying it.
    /// </summary>
    public void Clear()
    {
        dimension?.ClearChunks();
        currentBlock = null;
        originalBlock = null;
        internalBlockPos = null;
        activeBoundsMin = null;
        activeBoundsMax = null;
        RotationAngle = 0;
        RotationMode = EBuildBrushRotationMode.None;
        MarkDirty(nameof(Clear));
    }

    /// <summary>
    /// Destroys the dimension and releases resources.
    /// </summary>
    public void Destroy()
    {
        // Unsubscribe from any instance events
        Unsubscribe();

        if (dimension is not null)
        {
            dimension.ClearChunks();
            dimension.UnloadUnusedServerChunks();
        }
        dimension = null;
        dimensionId = -1;
        Clear();
    }

    /// <summary>
    /// Begins a batch update. MarkDirty calls will be deferred until EndUpdate is called.
    /// Supports nested calls.
    /// </summary>
    public void BeginUpdate()
    {
        _updateDepth++;
    }

    /// <summary>
    /// Ends a batch update. If MarkDirty was called during the batch, raises OnDirty once.
    /// </summary>
    public void EndUpdate()
    {
        if (_updateDepth <= 0)
            return;

        _updateDepth--;

        if (_updateDepth == 0 && _isDirtyDuringUpdate)
        {
            _isDirtyDuringUpdate = false;
            OnDirty?.Invoke(this, new DimensionDirtyEventArgs(nameof(EndUpdate)));
        }
    }

    /// <summary>
    /// Marks the dimension as dirty, indicating the mesh needs to be rebuilt.
    /// If inside a BeginUpdate/EndUpdate block, defers the event until EndUpdate.
    /// </summary>
    /// <param name="reason">Optional reason for the dirty state (for debugging).</param>
    public void MarkDirty(string reason = "")
    {
        if (_updateDepth > 0)
        {
            _isDirtyDuringUpdate = true;
            return;
        }

        OnDirty?.Invoke(this, new DimensionDirtyEventArgs(reason));
    }

    /// <summary>
    /// Gets the block entity at the internal block position.
    /// </summary>
    /// <returns>The block entity if one exists, null otherwise.</returns>
    public BlockEntity? GetBlockEntity()
    {
        if (dimension is null || internalBlockPos is null)
            return null;

        return dimension.GetBlockEntity(internalBlockPos);
    }
    #endregion

    #region Block Management
    /// <summary>
    /// Sets the block to be displayed in the dimension.
    /// </summary>
    /// <param name="block">The block to display.</param>
    /// <param name="rotationMode">The rotation mode for this block (optional, auto-detects if not provided).</param>
    public void SetBlock(Block block, EBuildBrushRotationMode? rotationMode = null)
    {
        if (dimension is null || !IsInitialized)
            return;

        // Clear existing block
        Clear();

        originalBlock = block;
        currentBlock = block;
        RotationMode = rotationMode ?? EBuildBrushRotationMode.None;

        // Place the block in the dimension
        PlaceBlockInDimension();
    }

    /// <summary>
    /// Updates the block variant (for variant-based rotation).
    /// </summary>
    /// <param name="variantBlock">The rotated variant block.</param>
    public void SetVariantBlock(Block variantBlock)
    {
        if (dimension is null || !IsInitialized || originalBlock is null)
            return;

        currentBlock = variantBlock;
        PlaceBlockInDimension();
    }

    /// <summary>
    /// Places the current block in the mini-dimension at the origin.
    /// </summary>
    private void PlaceBlockInDimension()
    {
        if (dimension is null || currentBlock is null)
            return;

        // Calculate position in mini-dimension space
        // Block is placed at origin (0, 0, 0) within the dimension
        internalBlockPos = new BlockPos(0, 0, 0, Dimensions.MiniDimensions);
        dimension.AdjustPosForSubDimension(internalBlockPos);

        // Set the block
        dimension.SetBlock(currentBlock.BlockId, internalBlockPos);

        // Update active bounds to include this position
        UpdateActiveBounds(internalBlockPos);

        // If block has entity data, spawn the block entity
        if (!string.IsNullOrEmpty(currentBlock.EntityClass))
        {
            dimension.SpawnBlockEntity(currentBlock.EntityClass, internalBlockPos);
        }

        dimension.Dirty = true;
        MarkDirty(nameof(PlaceBlockInDimension));
    }

    /// <summary>
    /// Updates the active bounds to include the specified position.
    /// </summary>
    /// <param name="pos">The position to include in bounds.</param>
    private void UpdateActiveBounds(BlockPos pos)
    {
        if (activeBoundsMin is null || activeBoundsMax is null)
        {
            // First block placed, initialize bounds
            activeBoundsMin = pos.Copy();
            activeBoundsMax = pos.Copy();
        }
        else
        {
            // Expand bounds to include this position
            activeBoundsMin.X = Math.Min(activeBoundsMin.X, pos.X);
            activeBoundsMin.Y = Math.Min(activeBoundsMin.Y, pos.Y);
            activeBoundsMin.Z = Math.Min(activeBoundsMin.Z, pos.Z);

            activeBoundsMax.X = Math.Max(activeBoundsMax.X, pos.X);
            activeBoundsMax.Y = Math.Max(activeBoundsMax.Y, pos.Y);
            activeBoundsMax.Z = Math.Max(activeBoundsMax.Z, pos.Z);
        }
    }

    /// <summary>
    /// Gets the active bounds of the dimension (the region containing placed blocks).
    /// </summary>
    /// <param name="min">Output: minimum corner of the bounds.</param>
    /// <param name="max">Output: maximum corner of the bounds.</param>
    /// <returns>True if there are active bounds, false if the dimension is empty.</returns>
    public bool GetActiveBounds(out BlockPos min, out BlockPos max)
    {
        if (activeBoundsMin is null || activeBoundsMax is null)
        {
            min = new BlockPos(0, 0, 0, Dimensions.MiniDimensions);
            max = new BlockPos(0, 0, 0, Dimensions.MiniDimensions);
            return false;
        }

        min = activeBoundsMin.Copy();
        max = activeBoundsMax.Copy();
        return true;
    }
    #endregion

    #region Rotation
    /// <summary>
    /// Applies rotation to the block in the dimension.
    /// </summary>
    /// <param name="angle">The target rotation angle in degrees (0, 90, 180, 270).</param>
    /// <param name="previousAppliedAngle">The previously applied rotation angle in degrees.</param>
    /// <param name="eventArgs">Event args containing previous and current orientation definitions.</param>
    /// <param name="orientationInfo">The orientation info to use for applying rotation.</param>
    public void ApplyRotation(OrientationIndexChangedEventArgs eventArgs, BuildBrushOrientationInfo orientationInfo)
    {
        if (dimension is null || !IsInitialized || originalBlock is null || eventArgs is null || orientationInfo is null)
            return;

        // Extract definitions from event args
        BlockOrientationDefinition previousDef = eventArgs.PreviousDefinition;
        BlockOrientationDefinition currentDef = eventArgs.CurrentDefinition;

        // Normalize angle
        RotationAngle = ((int)currentDef.MeshAngleDegrees % 360 + 360) % 360;
        
        bool variantChanged = eventArgs.VariantChanged;
        Block? variantBlock = variantChanged ? orientationInfo.CurrentBlock : null;

        switch (RotationMode)
        {
            case EBuildBrushRotationMode.None:
                // No rotation possible
                break;

            case EBuildBrushRotationMode.VariantBased:
                // Use the provided variant block (skip if already set to avoid redundant placement)
                if (variantChanged && variantBlock is not null)
                {
                    currentBlock = variantBlock;
                    PlaceBlockInDimension();
                }
                break;

            case EBuildBrushRotationMode.Rotatable:
                // Apply rotation via IRotatable
                ApplyRotatableRotation(orientationInfo, previousDef);
                break;

            case EBuildBrushRotationMode.Hybrid:
                // Apply both variant and IRotatable
                if (variantChanged && variantBlock is not null)
                {
                    currentBlock = variantBlock;
                }
                ApplyRotatableRotation(orientationInfo, previousDef, variantChanged);
                break;
        }
    }

    /// <summary>
    /// Applies rotation to IRotatable block entities using orientation info.
    /// </summary>
    /// <param name="orientationInfo">The orientation info containing current state.</param>
    /// <param name="previousDefinition">The previous orientation definition for delta computation.</param>
    /// <param name="forceReplacement">If true, forces full block replacement (e.g., when variant changed).</param>
    private void ApplyRotatableRotation(BuildBrushOrientationInfo orientationInfo, BlockOrientationDefinition previousDefinition, bool forceReplacement = false)
    {
        if (dimension is null || currentBlock is null || internalBlockPos is null || orientationInfo is null)
            return;

        if (string.IsNullOrEmpty(currentBlock.EntityClass))
            return;

        // If variant changed, re-place the block first
        if (forceReplacement)
        {
            PlaceBlockInDimension();
        }

        // Get the live block entity
        var existingBe = dimension.GetBlockEntity(internalBlockPos);
        if (existingBe is null)
            return;

        // Use the new centralized method from BuildBrushOrientationInfo
        if (!orientationInfo.ApplyToBlockEntity(existingBe, previousDefinition, orientationInfo.Current))
        {
            world.Logger.Error("Failed to apply rotatable rotation to block entity at {0} in BuildBrushDimension.", internalBlockPos);
        }

        dimension.Dirty = true;
        MarkDirty(nameof(ApplyRotatableRotation));
    }
    #endregion

    #region Sync
    /// <summary>
    /// Sends dirty chunks to nearby players.
    /// </summary>
    /// <param name="players">The players to sync to.</param>
    public void SyncToPlayers(IPlayer[] players)
    {
        dimension?.CollectChunksForSending(players);
    }
    #endregion

    #region Subscription Management
    /// <summary>
    /// Subscribes this dimension to events from a BuildBrushInstance.
    /// Automatically unsubscribes from any previously subscribed instance.
    /// </summary>
    /// <param name="instance">The instance to subscribe to.</param>
    public void SubscribeTo(BuildBrushInstance instance)
    {
        if (instance == _subscribedInstance)
            return;

        // Unsubscribe from previous instance
        Unsubscribe();

        _subscribedInstance = instance;

        // Subscribe to relevant events
        instance.OnBlockTransformedChanged += Instance_OnBlockTransformedChanged;
        instance.OnOrientationChanged += Instance_OnOrientationChanged;
        instance.OnRotationInfoChanged += Instance_OnRotationInfoChanged;
    }

    /// <summary>
    /// Unsubscribes this dimension from all BuildBrushInstance events.
    /// Safe to call even if not subscribed.
    /// </summary>
    public void Unsubscribe()
    {
        if (_subscribedInstance is null)
            return;

        _subscribedInstance.OnBlockTransformedChanged -= Instance_OnBlockTransformedChanged;
        _subscribedInstance.OnOrientationChanged -= Instance_OnOrientationChanged;
        _subscribedInstance.OnRotationInfoChanged -= Instance_OnRotationInfoChanged;

        _subscribedInstance = null;
    }

    /// <summary>
    /// Gets the currently subscribed BuildBrushInstance (if any).
    /// </summary>
    public BuildBrushInstance? SubscribedInstance => _subscribedInstance;
    #endregion

    #region Event Handlers
    /// <summary>
    /// Handles block transformed changes from the subscribed instance.
    /// Updates the dimension's block when the transformed block changes.
    /// </summary>
    private void Instance_OnBlockTransformedChanged(object? sender, BlockChangedEventArgs e)
    {
        if (e.CurrentBlock is null)
        {
            Clear();
            return;
        }

        // Get rotation mode from the instance's rotation info
        var instance = sender as BuildBrushInstance;
        EBuildBrushRotationMode mode = instance?.Rotation?.Mode ?? EBuildBrushRotationMode.None;

        BeginUpdate();
        try
        {
            // If this is just a variant change (same base block, different rotation variant),
            // use SetVariantBlock. Otherwise, use SetBlock for a full reset.
            if (originalBlock is not null && e.PreviousBlock is not null &&
                originalBlock.BlockId != 0 && 
                mode == EBuildBrushRotationMode.VariantBased)
            {
                SetVariantBlock(e.CurrentBlock);
            }
            else
            {
                SetBlock(e.CurrentBlock, mode);
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    /// <summary>
    /// Handles orientation changes from the subscribed instance.
    /// Applies rotation based on the new orientation.
    /// </summary>
    private void Instance_OnOrientationChanged(object? sender, OrientationIndexChangedEventArgs e)
    {
        if (_subscribedInstance is null)
            return;

        // Get the rotation info from the instance
        var rotation = _subscribedInstance.Rotation;
        if (rotation is null)
            return;

        // For VariantBased rotation, the block change is already handled by
        // Instance_OnBlockTransformedChanged. We only need to handle mesh angle
        // changes here for Rotatable and Hybrid modes.
        if (rotation.Mode == EBuildBrushRotationMode.VariantBased)
        {
            // Block change already handled by OnBlockTransformedChanged
            return;
        }

        BeginUpdate();
        try
        {
            // Apply the rotation using event args and orientation info
            // Note: sender is the BuildBrushInstance (via Rotation_OnOrientationChanged forwarder)
            ApplyRotation(e, rotation);
        }
        finally
        {
            EndUpdate();
        }
    }

    /// <summary>
    /// Handles rotation info replacement from the subscribed instance.
    /// This occurs when a new block is selected.
    /// </summary>
    private void Instance_OnRotationInfoChanged(object? sender, RotationInfoChangedEventArgs e)
    {
        // When rotation info changes, the block is also changing
        // The block change will be handled by Instance_OnBlockTransformedChanged
        // This handler is here for potential future use or for cases where
        // we need to react specifically to rotation info changes
    }
    #endregion
}
