using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Central owner of all placement state for the build brush.
/// Manages position, rotation, block selection, and drives the mini-dimension entity.
/// </summary>
public class BuildBrushInstance
{
    #region Constants
    public static readonly EBuildBrushSnapping[] BrushSnappingModes = [
        EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical,
        EBuildBrushSnapping.Horizontal,
        EBuildBrushSnapping.Vertical,
        EBuildBrushSnapping.None,
    ];
    #endregion

    #region Fields
    /// <summary>
    /// The player this brush instance belongs to.
    /// </summary>
    public IPlayer Player { get; internal set; }
    public IWorldAccessor World { get; internal set; }
    private int? _blockId = null;
    private BlockPos _position = new(0, 0, 0);
    private Block? _blockUntransformed = null;
    private Block? _blockTransformed = null;
    private ItemStack? _itemStack = null;
    private ItemStack? _sourceItemStack = null;
    private EBuildBrushSnapping _snapping = BrushSnappingModes[0];
    private BrushSnappingState lastCheckedSnappingState = new();
    private bool _isValidPlacementBlock = true;
    private bool _isActive = false;

    /// <summary>
    /// The mini-dimension wrapper for this brush.
    /// </summary>
    private BuildBrushDimension? _dimension;

    /// <summary>
    /// The entity that renders the brush preview.
    /// </summary>
    private BuildBrushEntity? _entity;

    /// <summary>
    /// Encapsulates all rotation data and logic for the current block.
    /// </summary>
    private BuildBrushRotationInfo? _rotation;

    /// <summary>
    /// Resolver for precomputing and caching rotation definitions.
    /// </summary>
    private BlockRotationResolver? _rotationResolver;
    #endregion

    #region Events
    /// <summary>
    /// Raised when the brush block changes (including rotation variants).
    /// </summary>
    public event Action<BuildBrushInstance, Block?>? OnBlockChanged;

    /// <summary>
    /// Raised when the brush position changes.
    /// </summary>
    public event Action<BuildBrushInstance, BlockPos?>? OnPositionChanged;
    #endregion

    #region Properties
    public bool IsDirty { get; private set; } = false;

    private long markedDirtyCallbackId;

    /// <summary>
    /// Indicates whether the build brush is currently active and in-use by the owning player.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;

            _isActive = value;
            IsDirty = true;

            // Manage dimension/entity lifecycle based on active state
            if (value)
            {
                ActivateDimension();
            }
            else
            {
                DeactivateDimension();
            }
        }
    }

    /// <summary>
    /// Indicates whether the current placement position is valid.
    /// </summary>
    public bool IsValidPlacement
    {
        get => _isValidPlacement;
        private set
        {
            _isValidPlacement = value;
            UpdateEntityValidity(value);
        }
    }
    private bool _isValidPlacement;

    /// <summary>
    /// Updates the entity's validity watched attribute for rendering.
    /// </summary>
    private void UpdateEntityValidity(bool isValid)
    {
        if (_entity is null)
            return;

        _entity.WatchedAttributes.SetBool("isValid", isValid);
        _entity.WatchedAttributes.MarkPathDirty("isValid");
    }

    /// <summary>
    /// Indicates whether the brush can currently be used to place blocks.
    /// </summary>
    public bool IsDisabled => !IsActive || !_isValidPlacementBlock || Position is null;

    /// <summary>
    /// The mini-dimension wrapper for this brush.
    /// </summary>
    public BuildBrushDimension? Dimension => _dimension;

    /// <summary>
    /// The entity that renders the brush preview.
    /// </summary>
    public BuildBrushEntity? Entity => _entity;

    /// <summary>
    /// The rotation info for the current block.
    /// </summary>
    public BuildBrushRotationInfo? Rotation => _rotation;

    /// <summary>
    /// The detected rotation mode for the current block.
    /// </summary>
    public EBuildBrushRotationMode RotationMode => _rotation?.Mode ?? EBuildBrushRotationMode.None;

    /// <summary>
    /// The current rotation angle in degrees (from the current rotation definition).
    /// </summary>
    public float RotationAngleDegrees => _rotation?.CurrentMeshAngleDegrees ?? 0f;

    /// <summary>
    /// The fully resolved position of the build cursor.
    /// </summary>
    public BlockPos Position
    {
        get => _position;
        set
        {
            if (_position == value)
                return;

            _position = value;
            Selection = new()
            {
                Position = value?.Copy(),
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 0.5, 0.5),
                DidOffset = true
            };

            // Raise position changed event
            OnPositionChanged?.Invoke(this, _position);
        }
    }

    /// <summary>
    /// The current orientation index for the block placement.
    /// This indexes into the precomputed orientation definitions array.
    /// </summary>
    public int OrientationIndex
    {
        get => _rotation?.CurrentIndex ?? 0;
        set
        {
            if (_rotation is null)
                return;

            if (_rotation.Definitions.IsDefaultOrEmpty || _rotation.Definitions.Length <= 1)
            {
                return;
            }

            _rotation.CurrentIndex = value;

            // Update the transformed block based on the new orientation state
            BlockTransformed = _rotation.CurrentBlock;

            // Update dimension with new block/rotation
            UpdateDimensionBlock();
        }
    }

    /// <summary>
    /// The total number of orientation states available for the current block.
    /// </summary>
    public int OrientationCount => _rotation?.OrientationCount ?? 0;

    /// <summary>
    /// Rotates the brush cursor in the specified direction.
    /// Cycles through precomputed rotation definitions.
    /// </summary>
    /// <param name="direction">The direction to rotate (Forward = +1, Backward = -1).</param>
    /// <returns>True if rotation was applied, false if rotation is not supported for current block.</returns>
    public bool Rotate(EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        if (_rotation is null || !_rotation.CanRotate)
            return false;

        // Cycle to next/previous rotation definition
        _rotation.Rotate(direction);

        // Update the transformed block based on the new rotation state
        BlockTransformed = _rotation.CurrentBlock;

        // Update dimension with new block/rotation
        UpdateDimensionBlock();

        // Raise block changed to update the renderer
        OnBlockChanged?.Invoke(this, _blockTransformed);

        return true;
    }

    /// <summary>
    /// The block currently chosen for placement.
    /// </summary>
    public int? BlockId
    {
        get => _blockId;
        set
        {
            if (value is null)
            {
                _blockId = null;
                _isValidPlacementBlock = false;
                BlockUntransformed = null;
                return;
            }

            if (value == _blockId)
            {
                return;
            }

            _blockId = value;
            MarkDirty();
            Block block = World.GetBlock(value.Value);
            _isValidPlacementBlock = IsValidPlacementBlock(block);
            if (_isValidPlacementBlock)
            {
                BlockUntransformed = block;
            }
            else
            {
                //_blockId = null; // we don't set to null here to preserve the attempted block id (for change detection)
                BlockUntransformed = null;
            }
        }
    }

    /// <summary>
    /// The snapping mode for this brush placement.
    /// </summary>
    public EBuildBrushSnapping Snapping
    {
        get => _snapping;
        set
        {
            _snapping = value;
            IsDirty = true;
        }
    }
    #endregion

    #region Accessors
    protected ILogger Logger => World.Logger;
    public BlockSelection Selection { get; private set; } = new();
    public Block? BlockUntransformed
    {
        get => _blockUntransformed!;
        private set
        {
            //Logger.Audit($"[{nameof(BuildBrushInstance)}][set {nameof(BlockUntransformed)}]: Setting untransformed block to '{value}'.");
            if (_blockUntransformed == value)
            {
                return;
            }

            _blockUntransformed = value;

            // Ensure resolver exists
            _rotationResolver ??= new BlockRotationResolver(World);

            // Create rotation info for the new block using the resolver
            // Pass source ItemStack to resolve type-specific properties (e.g., for typed containers)
            _rotation = value is not null
                ? BuildBrushRotationInfo.Create(value, _rotationResolver, _sourceItemStack)
                : null;

            // Sync rotation index to match the block's current variant
            if (_rotation is not null && _blockId.HasValue)
            {
                _rotation.TrySetIndexForBlockId(_blockId.Value);
            }

            // Update transformed block to current rotation state
            BlockTransformed = _rotation?.CurrentBlock;
        }
    }

    public Block? BlockTransformed
    {
        get => _blockTransformed!;
        private set
        {
            if (_blockTransformed == value)
                return;

            //Logger.Audit($"[{nameof(BuildBrushInstance)}][set {nameof(BlockTransformed)}]: Setting transformed block to '{value}'.");
            _blockTransformed = value;
            ItemStack = value is not null ? new ItemStack(value) : null;

            // Notify listeners of block change
            OnBlockChanged?.Invoke(this, value);
            UpdateDimensionBlock();
        }
    }

    public ItemStack? ItemStack
    {
        get => _itemStack;
        private set
        {
            _itemStack = value;
            DummySlot = value is not null ? new DummySlot(value) : (ItemSlot?)null;
        }
    }

    public ItemSlot? DummySlot { get; private set; } = null;

    #endregion

    #region Constructors
    public BuildBrushInstance(IPlayer player, IWorldAccessor world)
    {
        Player = player;
        World = world;

        // Initialize with the block in the player's active hotbar slot
        TryUpdateBlockId();
    }
    #endregion

    #region Update Logic
    public void MarkDirty()
    {
        if (IsDirty)
        {
            return;
        }

        IsDirty = true;
        markedDirtyCallbackId = World.RegisterCallback(this.DoDirtyUpdate, 1);
    }

    private void DoDirtyUpdate(float dt)
    {
        World.UnregisterCallback(markedDirtyCallbackId);
        markedDirtyCallbackId = 0;
        TryUpdate();
    }

    public bool TryUpdate(BlockSelection? blockSelection = null, bool force = false)
    {
        blockSelection ??= Player.CurrentBlockSelection;
        if (blockSelection is null)
        {// Player currently has no block selection
            IsValidPlacement = false;
            Position = null;
            return false;
        }

        BrushSnapping snapping = new(blockSelection);
        BrushSnappingState snappingState = new(snapping.Horizontal, snapping.Vertical, Snapping);
        if (snappingState == lastCheckedSnappingState && blockSelection.Position == Position && !force && !IsDirty)
        {
            return false;
        }
        lastCheckedSnappingState = snappingState;

        BlockPos? resolvedPos = ResolveFinalSelectionPosition(_blockUntransformed, blockSelection, Snapping, snapping, out bool isValidPlacement);
        bool result = resolvedPos != Position;

        IsDirty = false;
        IsValidPlacement = isValidPlacement;
        Position = resolvedPos;
        return result;
    }

    /// <summary>
    /// Updates the BlockId based on the player's currently active hotbar slot.
    /// </summary>
    /// <returns> True if the BlockId was updated; otherwise, false. </returns>
    public bool TryUpdateBlockId()
    {
        var hotbarSlot = Player.InventoryManager.ActiveHotbarSlot;
        var currentItemStack = hotbarSlot?.Itemstack;
        var currentBlockId = currentItemStack?.Block?.Id;
        if (currentBlockId != BlockId)
        {
            // Store source ItemStack for type resolution (e.g., for typed containers like crates/chests)
            _sourceItemStack = currentItemStack;
            BlockId = currentBlockId;
            return true;
        }
        return false;
    }
    #endregion

    #region Placement Logic

    /// <summary>
    /// Determines if the specified block type is valid for placement with this brush.
    /// </summary>
    /// <param name="blockType"></param>
    /// <returns></returns>
    public bool IsValidPlacementBlock(in Block? blockType)
    {
        if (blockType is null)
        {
            return false;
        }

        if (blockType.BlockId == 0)
        {
            return false;
        }

        if (blockType.IsMissing)
        {
            return false;
        }

        // Allow blocks without an EntityClass
        if (string.IsNullOrEmpty(blockType.EntityClass))
        {
            return true;
        }

        // Allow blocks with EntityClass if the entity type implements IRotatable
        return IsBlockEntityRotatable(blockType);
    }

    /// <summary>
    /// Checks if the block's entity class implements <see cref="IRotatable"/>.
    /// </summary>
    /// <param name="block">The block to check.</param>
    /// <returns>True if the block entity implements IRotatable; otherwise, false.</returns>
    private bool IsBlockEntityRotatable(in Block? block)
    {
        if (block is null || string.IsNullOrEmpty(block.EntityClass))
        {
            return false;
        }

        Type? entityType = World.Api.ClassRegistry.GetBlockEntity(block.EntityClass);
        if (entityType is null)
        {
            return false;
        }

        return typeof(IRotatable).IsAssignableFrom(entityType);
    }

    /// <summary>
    /// Resolves the block position based on the given snapping mode.
    /// </summary>
    /// <param name="blockSelection"></param>
    /// <param name="snappingMode"></param>
    /// <returns></returns>
    public BlockPos ResolveFinalSelectionPosition(in Block? placingBlock, [NotNull] in BlockSelection blockSelection, EBuildBrushSnapping snappingMode, BrushSnapping snapping, out bool isValidPos)
    {
        if (placingBlock is null)
        {
            isValidPos = true;
            return blockSelection.Position;
        }

        if (TryGetValidSnappedPosition(placingBlock, blockSelection, snapping, snappingMode, out BlockPos outSnappedPos))
        {
            isValidPos = true;
            return outSnappedPos;
        }

        // the snapped position is invalid, use an unsnapped position instead.
        isValidPos = TryGetValidSnappedPosition(placingBlock, blockSelection, snapping, EBuildBrushSnapping.None, out BlockPos outUnsnappedPos);
        return outUnsnappedPos;
    }

    /// <summary>
    /// Resolves a selection position based on the given snapping mode, and checks if the block can be placed there.
    /// </summary>
    /// <returns>
    /// True if a valid placement position was found; otherwise, false.
    /// </returns>
    public bool TryGetValidSnappedPosition(in Block? placingBlock, in BlockSelection blockSelection, in BrushSnapping snapping, EBuildBrushSnapping snappingMode, out BlockPos outBlockPos)
    {
        if (placingBlock is null)
        {
            outBlockPos = blockSelection.Position.Copy();
            return true;
        }

        outBlockPos = snapping.ResolvePosition(snappingMode);

        string failureCode = "";
        BlockSelection newSelection = blockSelection.Clone();
        newSelection.DidOffset = true;
        newSelection.SetPos(outBlockPos.X, outBlockPos.Y, outBlockPos.Z);
        return placingBlock.CanPlaceBlock(World, Player, newSelection, ref failureCode);
    }

    public bool TryUpdatePlacementValidity(in BlockSelection blockSelection)
    {
        if (_blockTransformed is null || Position is null)
        {
            return false;
        }
        string failureCode = "";
        BlockSelection newSelection = blockSelection.Clone();
        newSelection.DidOffset = true;
        newSelection.SetPos(Position.X, Position.Y, Position.Z);
        this.IsValidPlacement = _blockTransformed.CanPlaceBlock(World, Player, newSelection, ref failureCode);
        return this.IsValidPlacement;
    }
    #endregion

    #region Event Handlers
    public void OnEquipped()
    {
        DisplaySnappingModeNotice();
        TryUpdateBlockId();
    }

    public void OnUnequipped()
    {
        IsActive = false;
        BlockId = 0;
    }

    public void OnBlockPlaced()
    {
        //Logger.Audit($"[{nameof(BuildBrushInstance)}][{nameof(OnBlockPlaced)}]: Block placed by player '{Player.PlayerName}'.");
        Player.InventoryManager.ActiveHotbarSlot?.MarkDirty();
        TryUpdateBlockId();
        if (World.Side != EnumAppSide.Client)
        {
            return;
        }

        TryUpdate();
    }
    #endregion

    #region Private
    /// <summary>
    /// Shows a HUD notice to the player indicating the current snapping mode.
    /// </summary>
    public void DisplaySnappingModeNotice()
    {
        if (World.Side != EnumAppSide.Client)
        {
            return;
        }

        ModInfo? modInfo = World.Api.ModLoader.GetModSystem<VanillaBuildingExpandedModSystem>()?.Mod.Info;
        if (modInfo is null)
        {
            return;
        }

        ICoreClientAPI? client = World.Api as ICoreClientAPI;
        if (client is null)
        {
            return;
        }

        client.TriggerIngameError(this, $"{modInfo.ModID}:brush-snapping-mode-changed", Lang.Get($"{modInfo.ModID}:brush-snapping-mode-changed-{Snapping.GetCode()}"));
    }
    #endregion

    #region Dimension & Entity Management
    /// <summary>
    /// Activates the dimension and entity when the brush becomes active.
    /// </summary>
    private void ActivateDimension()
    {
        // Only server creates dimensions
        if (World.Side != EnumAppSide.Server)
            return;

        if (_dimension is not null)
            return; // Already active

        if (InitializeDimension())
        {
            SpawnEntity();
            UpdateDimensionBlock();
        }
        else
        {
            Logger.Warning($"[{nameof(BuildBrushInstance)}][{nameof(ActivateDimension)}]: Failed to initialize dimension for player {Player.PlayerName}");
        }
    }

    /// <summary>
    /// Deactivates and destroys the dimension and entity when the brush becomes inactive.
    /// </summary>
    private void DeactivateDimension()
    {
        // Only server manages dimensions
        if (World.Side != EnumAppSide.Server)
            return;

        DestroyDimension();
    }

    /// <summary>
    /// Initializes the mini-dimension and entity for this brush instance.
    /// Must be called from the server side.
    /// </summary>
    /// <param name="existingDimensionId">Optional existing dimension ID to reuse.</param>
    /// <returns>True if initialization succeeded.</returns>
    private bool InitializeDimension(int existingDimensionId = -1)
    {
        if (World.Side != EnumAppSide.Server)
        {
            Logger.Warning($"[{nameof(BuildBrushInstance)}][{nameof(InitializeDimension)}]: Cannot initialize dimension on client side.");
            return false;
        }

        _dimension = new BuildBrushDimension(World);
        if (!_dimension.Initialize(existingDimensionId))
        {
            Logger.Error($"[{nameof(BuildBrushInstance)}][{nameof(InitializeDimension)}]: Failed to initialize dimension.");
            _dimension = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Spawns the brush entity and associates it with the dimension.
    /// Must be called after InitializeDimension().
    /// </summary>
    /// <returns>True if the entity was spawned successfully.</returns>
    private bool SpawnEntity()
    {
        if (_dimension?.Dimension is null || !_dimension.IsInitialized)
        {
            Logger.Warning($"[{nameof(BuildBrushInstance)}][{nameof(SpawnEntity)}]: Cannot spawn entity without initialized dimension.");
            return false;
        }

        if (World.Side != EnumAppSide.Server)
        {
            Logger.Warning($"[{nameof(BuildBrushInstance)}][{nameof(SpawnEntity)}]: Cannot spawn entity on client side.");
            return false;
        }

        var sapi = World.Api as Vintagestory.API.Server.ICoreServerAPI;
        if (sapi is null)
            return false;

        _entity = BuildBrushEntity.CreateAndLink(sapi, _dimension.Dimension, Player.PlayerUID);
        
        // Set initial position
        if (_position is not null)
        {
            _entity.Pos.SetPos(_position.ToVec3d());
            _entity.ServerPos.SetPos(_position.ToVec3d());
        }

        // Subscribe to position changes on server
        OnPositionChanged += OnBrushPositionChanged_Server;

        // Spawn the entity in the world
        World.SpawnEntity(_entity);

        return true;
    }

    /// <summary>
    /// Server-side handler for position changes to update entity position.
    /// </summary>
    private void OnBrushPositionChanged_Server(BuildBrushInstance instance, BlockPos? position)
    {
        if (_entity is null || position is null)
            return;

        var vec = position.ToVec3d();
        _entity.Pos.SetPos(vec);
        _entity.ServerPos.SetPos(vec);
    }

    /// <summary>
    /// Associates this brush instance with an existing client-side entity.
    /// Called on the client when receiving the entity from server.
    /// </summary>
    /// <param name="entity">The entity received from server.</param>
    public void AssociateEntity(BuildBrushEntity entity)
    {
        _entity = entity;
    }

    /// <summary>
    /// Destroys the entity and dimension.
    /// </summary>
    public void DestroyDimension()
    {
        // Unsubscribe from server-side position events
        OnPositionChanged -= OnBrushPositionChanged_Server;

        if (_entity is not null)
        {
            _entity.Die(Vintagestory.API.Common.EnumDespawnReason.Removed);
            _entity = null;
        }

        _dimension?.Destroy();
        _dimension = null;
    }

    /// <summary>
    /// Updates the block in the dimension based on current rotation state.
    /// </summary>
    private void UpdateDimensionBlock()
    {
        if (_dimension is null || !_dimension.IsInitialized)
            return;

        Block? block = _blockTransformed ?? _blockUntransformed;
        if (block is not null)
        {
            _dimension.SetBlock(block, _rotation?.Mode);
            
            // Apply mesh angle rotation if applicable
            if (_rotation is not null && _rotation.HasRotatableEntity)
            {
                ApplyRotation();
            }
        }
        else
        {
            _dimension.Clear();
        }
    }

    /// <summary>
    /// Applies the current rotation based on rotation mode.
    /// Uses the mesh angle from the current rotation definition.
    /// </summary>
    private void ApplyRotation()
    {
        if (_dimension is null || !_dimension.IsInitialized || _rotation is null)
            return;

        // Get the mesh angle in degrees from the current definition
        float meshAngleDegrees = _rotation.CurrentMeshAngleDegrees;

        switch (_rotation.Mode)
        {
            case EBuildBrushRotationMode.None:
                // No rotation possible
                break;

            case EBuildBrushRotationMode.VariantBased:
                // Variant rotation is handled by block ID swap, no mesh angle needed
                _dimension.ApplyRotation(0, _blockTransformed);
                break;

            case EBuildBrushRotationMode.Rotatable:
                // Apply mesh angle rotation
                _dimension.ApplyRotation((int)meshAngleDegrees);
                break;

            case EBuildBrushRotationMode.Hybrid:
                // Block ID swap is already handled, apply mesh angle offset
                _dimension.ApplyRotation((int)meshAngleDegrees, _blockTransformed);
                break;
        }

        // Update entity yaw for visual rotation if using IRotatable
        if (_rotation.HasRotatableEntity && _entity is not null)
        {
            float yawRadians = _rotation.CurrentMeshAngleRadians;
            _entity.Pos.Yaw = yawRadians;
            _entity.ServerPos.Yaw = yawRadians;
        }
    }

    /// <summary>
    /// Syncs the dimension to nearby players.
    /// </summary>
    /// <param name="players">The players to sync to.</param>
    public void SyncDimensionToPlayers(IPlayer[] players)
    {
        _dimension?.SyncToPlayers(players);
    }
    #endregion
}
