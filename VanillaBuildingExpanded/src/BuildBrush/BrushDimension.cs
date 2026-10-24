using System;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>
/// Wrapper class that manages a mini-dimension for the build brush system.
/// Handles block placement and rotation within the preview dimension.
/// </summary>
public class BrushDimension
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

    private ItemStack? placementInitializedStack;
    private int? placementInitializedSlotNumber;

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
    public BrushDimension(IWorldAccessor world)
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
        placementInitializedStack = null;
        placementInitializedSlotNumber = null;
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

        ArgumentNullException.ThrowIfNull(block);

        // Early-out: if the block ID hasn't changed, skip replacement.
        // We deliberately do the check here (against the current state) so callers don't need to pass
        // any "previous block" argument.
        Block? existingBaseBlock = originalBlock;
        if (existingBaseBlock is null && internalBlockPos is not null)
        {
            existingBaseBlock = dimension.GetBlock(internalBlockPos);
        }

        if (existingBaseBlock is not null && existingBaseBlock.BlockId == block.BlockId)
        {
            // Still allow rotation mode to be updated when the selection stays the same.
            if (rotationMode.HasValue)
            {
                RotationMode = rotationMode.Value;
            }
            RefreshBlockEntityPlacement();
            return;
        }

        RecreatePreviewBlock(block, rotationMode ?? EBuildBrushRotationMode.None);
    }

    private void RecreatePreviewBlock(Block block, EBuildBrushRotationMode rotationMode)
    {
        Clear();

        originalBlock = block;
        currentBlock = block;
        RotationMode = rotationMode;

        // Place the block in the dimension
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

        ItemStack? source = _subscribedInstance?.ItemStack;
        dimension.SetBlock(currentBlock.BlockId, internalBlockPos, source?.Clone());

        // Update active bounds to include this position
        UpdateActiveBounds(internalBlockPos);

        // If block has entity data, spawn the block entity
        if (!string.IsNullOrEmpty(currentBlock.EntityClass))
        {
            var existingBe = dimension.GetBlockEntity(internalBlockPos);
            if (existingBe is null)
            {
                dimension.SpawnBlockEntity(currentBlock.EntityClass, internalBlockPos, source?.Clone());
            }
        }

        // Native SetBlock/SpawnBlockEntity already invoke OnBlockPlaced on newly created entities.
        var blockEntity = dimension.GetBlockEntity(internalBlockPos);
        if (blockEntity is not null && source is not null && CopyStackPropertiesToBlockEntity(blockEntity, source))
        {
            blockEntity.MarkDirty(true);
            dimension.Dirty = true;
        }
        placementInitializedStack = source?.Clone();
        placementInitializedSlotNumber = _subscribedInstance?.SourceHotbarSlotNumber;
        MarkDirty(nameof(PlaceBlockInDimension));
    }

    private void RefreshBlockEntityPlacement()
    {
        if (dimension is null || internalBlockPos is null ||
            _subscribedInstance?.ItemStack is not { } source ||
            currentBlock is null || source.Block?.Id != currentBlock.Id)
            return;

        // Brush rotation changes the prepared stack, not its held source, and does not require a new entity.
        string[] ignoredAttributes = _subscribedInstance.Rotation?.RotationAttribute is { } rotationAttribute
            ? [rotationAttribute]
            : [];
        if (placementInitializedSlotNumber == _subscribedInstance.SourceHotbarSlotNumber &&
            placementInitializedStack is not null &&
            source.Attributes.Equals(world, placementInitializedStack.Attributes, ignoredAttributes))
            return;

        // Start from native defaults on any slot or source-property change instead of cleaning up stale overrides.
        BeginUpdate();
        try
        {
            var sourceRotation = _subscribedInstance.Rotation;
            RecreatePreviewBlock(currentBlock, RotationMode);
            if (sourceRotation is not null)
            {
                var definition = sourceRotation.Current;
                ApplyRotation(new OrientationIndexChangedEventArgs(
                    sourceRotation.CurrentIndex, sourceRotation.CurrentIndex, definition, definition), sourceRotation);
            }
            dimension.Dirty = true;
        }
        finally
        {
            EndUpdate();
        }
    }

    /// <summary>
    /// Supplements native placement initialization using matching item-stack and serialized entity properties.
    /// Matching names and types are a compatibility heuristic, not a guaranteed shared schema.
    /// </summary>
    private bool CopyStackPropertiesToBlockEntity(BlockEntity blockEntity, ItemStack source)
    {
        // Start from current entity state so properties owned by native hooks survive deserialization.
        TreeAttribute tree = new();
        blockEntity.ToTreeAttributes(tree);
        bool changed = false;
        foreach (var entry in source.Attributes)
        {
            // Preserve preview identity and brush-controlled rotation. Restrict copying to scalar values:
            // structured data such as inventories may have different item and entity representations.
            // The key already exists in the source (this loop); require it in the entity tree too,
            // with the exact same attribute type, rather than adding unknown or incompatible fields.
            if (entry.Key is "posx" or "posy" or "posz" or "blockCode" ||
                BlockOrientationResolver.IsMeshRotationAttribute(entry.Key) ||
                entry.Key == _subscribedInstance?.Rotation?.RotationAttribute ||
                entry.Value is not (StringAttribute or BoolAttribute or IntAttribute or LongAttribute or FloatAttribute or DoubleAttribute) ||
                tree[entry.Key] is not { } target ||
                target.GetType() != entry.Value.GetType())
                continue;

            if (!target.Equals(world, entry.Value))
            {
                tree[entry.Key] = entry.Value.Clone();
                changed = true;
            }
        }

        // Deserialize only when needed to apply values to the entity and refresh its derived state.
        if (changed)
            blockEntity.FromTreeAttributes(tree, world);
        return changed;
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
    public void ApplyRotation(OrientationIndexChangedEventArgs eventArgs, BrushOrientation orientationInfo)
    {
        if (dimension is null || !IsInitialized || originalBlock is null || eventArgs is null || orientationInfo is null)
            return;

        if (RotationMode == EBuildBrushRotationMode.None)
        {
            return;
        }

        // Extract definitions from event args
        BlockOrientation currentDef = eventArgs.CurrentDefinition;

        // Normalize angle
        RotationAngle = ((int)currentDef.MeshAngleDegrees % 360 + 360) % 360;
        
        bool variantChanged = eventArgs.VariantChanged;
        Block? variantBlock = null;
        if (variantChanged)
        {
            variantBlock = world.GetBlock(currentDef.BlockId);
            if (variantBlock is null || variantBlock.BlockId == 0 || variantBlock.IsMissing)
            {
                world.Logger.Error(
                    "[{0}]: Invalid orientation definition block id {1} (mode={2}). Cannot resolve block for preview rotation.",
                    nameof(BrushDimension),
                    currentDef.BlockId,
                    RotationMode
                );
                return;
            }
        }
        // Use the provided variant block (skip if already set to avoid redundant placement)
        if (variantChanged && variantBlock is not null)
        {
            if (currentBlock is null || currentBlock.BlockId != variantBlock.BlockId)
            {
                currentBlock = variantBlock;
                PlaceBlockInDimension();
            }
        }

        switch (RotationMode)
        {
            case EBuildBrushRotationMode.Rotatable:
                // Apply rotation via IRotatable
                ApplyRotatableRotation(orientationInfo, currentDef);
                break;
        }
    }

    /// <summary>
    /// Applies rotation to IRotatable block entities using orientation info.
    /// </summary>
    /// <param name="orientationInfo">The orientation info containing current state.</param>
    /// <param name="currentDefinition">The current/target orientation definition to apply.</param>
    private void ApplyRotatableRotation(
        BrushOrientation orientationInfo,
        BlockOrientation currentDefinition)
    {
        if (dimension is null || currentBlock is null || internalBlockPos is null || orientationInfo is null)
            return;

        if (string.IsNullOrEmpty(currentBlock.EntityClass))
            return;

        // Get the live block entity
        var existingBe = dimension.GetBlockEntity(internalBlockPos);
        if (existingBe is null)
            return;

        // Use the new centralized method from BuildBrushOrientationInfo
        if (!orientationInfo.ApplyToPlacedBlockEntity(existingBe, currentDefinition, _subscribedInstance?.ItemStack))
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
        instance.OnOrientationChanged += Instance_OnOrientationChanged;
        instance.OnRotationInfoChanged += Instance_OnRotationInfoChanged;
        instance.OnPlacementItemStackChanged += Instance_OnPlacementItemStackChanged;
    }

    /// <summary>
    /// Unsubscribes this dimension from all BuildBrushInstance events.
    /// Safe to call even if not subscribed.
    /// </summary>
    public void Unsubscribe()
    {
        if (_subscribedInstance is null)
            return;

        _subscribedInstance.OnOrientationChanged -= Instance_OnOrientationChanged;
        _subscribedInstance.OnRotationInfoChanged -= Instance_OnRotationInfoChanged;
        _subscribedInstance.OnPlacementItemStackChanged -= Instance_OnPlacementItemStackChanged;

        _subscribedInstance = null;
    }

    /// <summary>
    /// Gets the currently subscribed BuildBrushInstance (if any).
    /// </summary>
    public BuildBrushInstance? SubscribedInstance => _subscribedInstance;
    #endregion

    #region Event Handlers
    private void Instance_OnPlacementItemStackChanged(object? sender, EventArgs e)
    {
        RefreshBlockEntityPlacement();
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

        BeginUpdate();
        try
        {
            // OrientationDefinition is the authority for what block-id should be placed.
            Block? baseBlock = world.GetBlock(e.CurrentDefinition.BlockId);
            if (baseBlock is null || baseBlock.BlockId == 0 || baseBlock.IsMissing)
            {
                world.Logger.Error(
                    "[{0}]: Invalid orientation definition block id {1} (mode={2}). Cannot update preview block.",
                    nameof(BrushDimension),
                    e.CurrentDefinition.BlockId,
                    rotation.Mode
                );
                return;
            }

            SetBlock(baseBlock, rotation.Mode);

            // Apply the rotation using event args and orientation info.
            // This handles VariantBased variant swaps as well as Rotatable mesh-angle rotation.
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
        // When rotation info is cleared (block removed/invalid), no orientation event will fire.
        // Clear the preview dimension so downstream systems rebuild.
        if (e.CurrentRotation is null || e.SourceBlock is null)
        {
            Clear();
            return;
        }
    }
    #endregion
}
