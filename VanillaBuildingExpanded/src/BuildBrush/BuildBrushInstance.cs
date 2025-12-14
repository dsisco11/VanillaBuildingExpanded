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

        return string.IsNullOrEmpty(blockType.EntityClass);
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

    private void UpdateOrientationVariantsList()
    {
        //Logger.Audit($"[{nameof(BuildBrushInstance)}][{nameof(UpdateOrientationVariantsList)}]: Updating orientation variants for block '{BlockUntransformed}'.");
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

        string baseCode = BlockUntransformed.Code.FirstCodePart();
        if (OrientationVariantCache.TryGetValue(baseCode, out Block[]? cachedVariants))
        {
            OrientationVariants = cachedVariants.ToImmutableArray();
            if (SetOrientationToBaseBlock())
            {
                return;
            }
        }

        // find the first of the possible variant groups which the block-definition actually has.
        string? foundVariantGroup = BlockUntransformed.Variant.Keys.Where(static k => ValidOrientationVariantKeys.Contains(k)).FirstOrDefault();
        if (foundVariantGroup is null)
        {
            OrientationVariants = [BlockUntransformed];
            OrientationIndex = 0;
            return;
        }

        AssetLocation? searchCode = BlockUntransformed.CodeWithVariant(foundVariantGroup, "*");
        if (searchCode is null)
        {
            return;
        }

        Block[] variants = World.SearchBlocks(searchCode);
        if (!OrientationVariantCache.TryAdd(baseCode, variants))
        {
            Logger.Warning($"[{nameof(BuildBrushInstance)}][{nameof(UpdateOrientationVariantsList)}]: Failed to add orientation variants to cache for block code '{baseCode}'.");
        }
        OrientationVariants = [.. variants];
        SetOrientationToBaseBlock();
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
