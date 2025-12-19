using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;
public class BuildBrushInstance
{
    #region Constants
    public static readonly EBuildBrushSnapping[] BrushSnappingModes = [
        EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical,
        EBuildBrushSnapping.Horizontal,
        EBuildBrushSnapping.Vertical,
        EBuildBrushSnapping.None,
    ];

    public static readonly Dictionary<AssetLocation, Block[]> OrientationVariantCache = [];
    public static readonly ImmutableArray<string> ValidOrientationVariantKeys = [
        "rot",
        "horizontalorientation",
        "orientation",
        "v",
        "side",
    ];
    #endregion

    #region Fields
    /// <summary>
    /// The player this brush instance belongs to.
    /// </summary>
    public IPlayer Player { get; internal set; }
    public IWorldAccessor World { get; internal set; }
    private int? _blockId = null;
    private int _orientationIndex = 0;
    private BlockPos _position = new(0, 0, 0);
    private Block? _blockUntransformed = null;
    private Block? _blockTransformed = null;
    private ItemStack? _itemStack = null;
    private EBuildBrushSnapping _snapping = BrushSnappingModes[0];
    private BrushSnappingState lastCheckedSnappingState = new();
    private bool _isValidPlacementBlock = true;
    private bool _isActive = false;
    private EOrientationMode _orientationMode = EOrientationMode.None;
    private float _rotationY = 0f;
    private float _rotationIncrementRadians = 0f;
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
            _isActive = value;
            IsDirty = true;
        }
    }

    /// <summary>
    /// Indicates whether the current placement position is valid.
    /// </summary>
    public bool IsValidPlacement { get; private set; }

    /// <summary>
    /// Indicates whether the brush can currently be used to place blocks.
    /// </summary>
    public bool IsDisabled => !IsActive || !_isValidPlacementBlock || Position is null;

    /// <summary>
    /// The fully resolved position of the build cursor.
    /// </summary>
    public BlockPos Position
    {
        get => _position;
        set
        {
            _position = value;
            Selection = new()
            {
                Position = value?.Copy(),
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 0.5, 0.5),
                DidOffset = true
            };
        }
    }

    /// <summary>
    /// The orientation index for the block placement.
    /// This determines which orientation variant of the block is used.
    /// </summary>
    public int OrientationIndex
    {
        get => this._orientationIndex;
        set
        {
            //Logger.Audit($"[{nameof(BuildBrushInstance)}][set {nameof(OrientationIndex)}]: Setting orientation index to '{value}' for block '{_blockUntransformed}'.");
            this._orientationIndex = 0;
            if (OrientationVariants.IsDefaultOrEmpty)
            {
                this.Logger.Warning($"[{nameof(BuildBrushInstance)}][set {nameof(OrientationIndex)}]: Block {_blockUntransformed} has no orientation variants.");
                return;
            }
            int clampedIndex = (value + OrientationVariants.Length) % OrientationVariants.Length;
            this._orientationIndex = clampedIndex;

            // Update the transformed block based on the new orientation
            if (!OrientationVariants.IsDefaultOrEmpty)
            {
                BlockTransformed = OrientationVariants[this._orientationIndex];
            }
        }
    }

    public ImmutableArray<Block> OrientationVariants { get; private set; } = ImmutableArray<Block>.Empty;

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

            if (_blockId != value)
            {
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
                    _blockId = null;
                    BlockUntransformed = null;
                }
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

    /// <summary>
    /// The orientation mode for the current block. Determines whether rotation uses
    /// code variants (Static) or block entity rotation (Dynamic).
    /// </summary>
    public EOrientationMode OrientationMode
    {
        get => _orientationMode;
        private set => _orientationMode = value;
    }

    /// <summary>
    /// The Y-axis rotation in radians for dynamic orientation blocks.
    /// Only applicable when <see cref="OrientationMode"/> is <see cref="EOrientationMode.Dynamic"/>.
    /// </summary>
    public float RotationY
    {
        get => _rotationY;
        set
        {
            // Normalize to [0, 2*PI) range
            _rotationY = value % (2f * GameMath.PI);
            if (_rotationY < 0f)
            {
                _rotationY += 2f * GameMath.PI;
            }
            IsDirty = true;
        }
    }

    /// <summary>
    /// The rotation increment in radians used when rotating dynamic orientation blocks.
    /// A value of 0 indicates rotation is disabled for this block.
    /// </summary>
    public float RotationIncrementRadians
    {
        get => _rotationIncrementRadians;
        private set => _rotationIncrementRadians = value;
    }
    #endregion

    #region Accessors
    protected ILogger Logger => World.Logger;
    public BlockSelection Selection { get; private set; } = new();
    public Block BlockUntransformed
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
            UpdateOrientationVariantsList();
        }
    }

    public Block BlockTransformed
    {
        get => _blockTransformed!;
        private set
        {
            //Logger.Audit($"[{nameof(BuildBrushInstance)}][set {nameof(BlockTransformed)}]: Setting transformed block to '{value}'.");
            _blockTransformed = value;
            ItemStack = value is not null ? new ItemStack(value) : null;
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
        int currentBlockId = Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.Id ?? 0;
        if (currentBlockId != BlockId)
        {
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

        if (blockType.IsLiquid())
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

    /// <summary>
    /// Called after a block has been placed using this brush.
    /// </summary>
    public void OnBlockPlaced(in BlockPos? position)
    {
        //Logger.Audit($"[{nameof(BuildBrushInstance)}][{nameof(OnBlockPlaced)}]: Block placed by player '{Player.PlayerName}'.");
        Player.InventoryManager?.ActiveHotbarSlot?.MarkDirty();

        // Apply rotation for dynamic orientation blocks (server-side only)
         if (World.Side == EnumAppSide.Server && OrientationMode == EOrientationMode.Dynamic && Position is not null)
        //if (OrientationMode == EOrientationMode.Dynamic && position is not null)
        {
            BlockEntityRotationHelper.TrySetRotation(World, position, RotationY);
        }

        TryUpdateBlockId();

        if (World.Side == EnumAppSide.Client)
        {
            TryUpdate();
        }
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

    private void UpdateOrientationVariantsList()
    {
        //Logger.Audit($"[{nameof(BuildBrushInstance)}][{nameof(UpdateOrientationVariantsList)}]: Updating orientation variants for block '{BlockUntransformed}'.");
        
        // Reset orientation state
        OrientationMode = EOrientationMode.None;
        RotationIncrementRadians = 0f;
        RotationY = 0f;
        
        if (BlockUntransformed is null)
        {
            OrientationVariants = [];
            return;
        }

        if (!IsValidPlacementBlock(BlockUntransformed))
        {
            OrientationVariants = [BlockUntransformed];
            return;
        }

        // Check for static (variant based) orientation FIRST
        // This takes priority because blocks like chests have both variants AND IRotatable,
        // but the variants are the primary rotation mechanism
        if (TrySetupStaticOrientation(BlockUntransformed))
        {
            return;
        }

        // Fall through to dynamic (block entity based) rotation
        // Only used for blocks without orientation variants but with IRotatable entity
        if (TrySetupDynamicOrientation(BlockUntransformed))
        {
            OrientationVariants = [BlockUntransformed];
            BlockTransformed = BlockUntransformed;
            return;
        }

        // No orientation support
        OrientationVariants = [BlockUntransformed];
        OrientationIndex = 0;
        OrientationMode = EOrientationMode.None;
    }

    /// <summary>
    /// Attempts to set up static orientation using block variants.
    /// </summary>
    /// <param name="block">The block to check.</param>
    /// <returns>True if the block has orientation variants; otherwise, false.</returns>
    private bool TrySetupStaticOrientation(in Block block)
    {
        string baseCode = block.Code.FirstCodePart();
        
        // Check cache first
        if (OrientationVariantCache.TryGetValue(baseCode, out Block[]? cachedVariants))
        {
            if (cachedVariants.Length > 1)
            {
                OrientationVariants = cachedVariants.ToImmutableArray();
                SetOrientationToBaseBlock();
                OrientationMode = EOrientationMode.Static;
                return true;
            }
            // Cached but only 1 variant means no static orientation
            return false;
        }

        // Find the first valid orientation variant group
        string? foundVariantGroup = block.Variant.Keys
            .Where(static k => ValidOrientationVariantKeys.Contains(k))
            .FirstOrDefault();
        
        if (foundVariantGroup is null)
        {
            return false;
        }

        AssetLocation? searchCode = block.CodeWithVariant(foundVariantGroup, "*");
        if (searchCode is null)
        {
            return false;
        }

        Block[] variants = World.SearchBlocks(searchCode);
        OrientationVariantCache.TryAdd(baseCode, variants);
        
        if (variants.Length <= 1)
        {
            return false;
        }

        OrientationVariants = [.. variants];
        SetOrientationToBaseBlock();
        OrientationMode = EOrientationMode.Static;
        return true;
    }

    /// <summary>
    /// Attempts to set up dynamic orientation for blocks that use block entity rotation.
    /// </summary>
    /// <param name="block">The block to check.</param>
    /// <returns>True if the block supports dynamic orientation; otherwise, false.</returns>
    private bool TrySetupDynamicOrientation(in Block block)
    {
        if (!IsBlockEntityRotatable(block))
        {
            return false;
        }

        OrientationMode = EOrientationMode.Dynamic;
        RotationIncrementRadians = ParseRotationInterval(block);
        return true;
    }

    /// <summary>
    /// Parses the rotation interval from block attributes.
    /// </summary>
    /// <param name="block">The block to parse rotation interval from.</param>
    /// <returns>The rotation increment in radians, or 0 if not found.</returns>
    private float ParseRotationInterval(in Block block)
    {
        if (block.Attributes is null)
        {
            return 0f;
        }

        // Get the type from the player's ItemStack attributes (for typed containers like chests/crates)
        string? type = Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Attributes?.GetString("type");
        if (string.IsNullOrEmpty(type))
        {
            // Fall back to defaultType if no type in ItemStack
            type = block.Attributes["defaultType"]?.AsString();
        }

        string? intervalString = null;

        // Try to get rotatatableInterval - it can be:
        // 1. A dictionary keyed by type (chest.json): rotatatableInterval: { "normal-generic": "22.5deg" }
        // 2. Nested in properties (crate.json): properties: { "wood-aged": { rotatatableInterval: "22.5deg" } }
        var rotatatableIntervalAttr = block.Attributes["rotatatableInterval"];
        if (rotatatableIntervalAttr is not null && rotatatableIntervalAttr.Exists)
        {
            if (!string.IsNullOrEmpty(type))
            {
                // Try to get interval for this specific type
                intervalString = rotatatableIntervalAttr[type]?.AsString();
            }
            
            // If still not found, try direct string value (unlikely but possible)
            if (string.IsNullOrEmpty(intervalString))
            {
                intervalString = rotatatableIntervalAttr.AsString();
            }
        }

        // Check properties[type].rotatatableInterval (crate-style)
        if (string.IsNullOrEmpty(intervalString) && !string.IsNullOrEmpty(type))
        {
            intervalString = block.Attributes["properties"]?[type]?["rotatatableInterval"]?.AsString();
            
            // Try wildcard fallback
            if (string.IsNullOrEmpty(intervalString))
            {
                intervalString = block.Attributes["properties"]?["*"]?["rotatatableInterval"]?.AsString();
            }
        }

        if (string.IsNullOrEmpty(intervalString))
        {
            // Default to 0 - rotation disabled if no interval specified
            return 0f;
        }

        return intervalString switch
        {
            "22.5deg" => 22.5f * GameMath.DEG2RAD,
            "22.5degnot45deg" => 22.5f * GameMath.DEG2RAD, // Still uses 22.5 degree increments
            "45deg" => 45f * GameMath.DEG2RAD,
            "90deg" => 90f * GameMath.DEG2RAD,
            _ => 0f
        };
    }

    /// <summary>
    /// Synchronizes the current orientation index to match the base block's orientation variant.
    /// </summary>
    /// <param name="baseCode"></param>
    /// <returns></returns>
    private bool SetOrientationToBaseBlock()
    {
        int? blockId = this.BlockId;
        Block? foundVariant = OrientationVariants.FirstOrDefault(block => block.BlockId == blockId);
        if (foundVariant is null)
        {
            return false;
        }

        int foundIndex = OrientationVariants.IndexOf(foundVariant);
        if (foundIndex < 0)
        {
            return false;
        }

        OrientationIndex = foundIndex;
        return true;
    }
    #endregion
}
