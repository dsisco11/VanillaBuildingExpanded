using System.Diagnostics.CodeAnalysis;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExtended.BuildHammer;
public class BuildBrushInstance
{
    #region Constants
    public static readonly EBuildBrushSnapping[] BrushSnappingModes = [
        EBuildBrushSnapping.None,
        EBuildBrushSnapping.Horizontal,
        EBuildBrushSnapping.Vertical,
        EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical,
    ];
    #endregion

    #region Fields
    /// <summary>
    /// The player this brush instance belongs to.
    /// </summary>
    public IPlayer Player { get; internal set; }
    public IWorldAccessor World { get; internal set; }

    private bool IsDirty = false;
    private int _blockId = 0;
    private int _rotation = 0;
    private BlockPos _position = new (0,0,0);
    private Block? _blockUntransformed = null;
    private Block? _blockTransformed = null;
    private ItemStack? _itemStack = null;
    private EBuildBrushSnapping _snapping = EBuildBrushSnapping.None;
    private BrushSnappingState lastCheckedSnappingState = new();
    #endregion

    #region Properties
    /// <summary>
    /// Indicates whether the build brush is currently active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Indicates whether the current placement position is valid.
    /// </summary>
    public bool IsValid { get; private set; }

    /// <summary>
    /// The fully resolved position of the build cursor.
    /// </summary>
    public BlockPos Position
    {
        get => _position;
        private set
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
    /// The rotation angle for the item placement.
    /// </summary>
    public int Rotation 
    {
        get => _rotation;
        set
        {
            if (_rotation != value)
            {
                _rotation = value % 360;
                var transformedBlock = _blockUntransformed?.GetRotatedBlockCode(_rotation);
                if (transformedBlock != null)
                {
                    BlockTransformed = World.GetBlock(transformedBlock);
                }
            }
        }
    }

    /// <summary>
    /// The block currently chosen for placement.
    /// </summary>
    public int BlockId
    {
        get => _blockId;
        set
        {
            if (_blockId != value)
            {
                _blockId = value;
                var block = World.GetBlock(value);
                BlockUntransformed = block;
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
    public BlockSelection Selection { get; private set; } = new();

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

    public Block BlockUntransformed
    {
        get => _blockUntransformed!;
        private set
        {
            _blockUntransformed = value;
            var transformedBlock = _blockUntransformed!.GetRotatedBlockCode(_rotation);
            if (transformedBlock is not null)
            {
                BlockTransformed = World.GetBlock(transformedBlock);
            }
        }
    }

    public Block BlockTransformed
    {
        get => _blockTransformed!;
        private set
        {
            _blockTransformed = value;
            ItemStack = value is not null ? new ItemStack(value) : null;
        }
    }
    #endregion

    #region Constructors
    public BuildBrushInstance(IPlayer player, IWorldAccessor world)
    {
        Player = player;
        World = world;
    }
    #endregion

    #region Update Logic

    public bool TryUpdateBrush(BlockSelection? blockSelection = null, bool force = false)
    {
        blockSelection ??= Player.CurrentBlockSelection;
        if (blockSelection is null)
        {// Player currently has no block selection
            IsValid = false;
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
        IsValid = isValidPlacement;
        Position = resolvedPos;
        return result;
    }
    #endregion

    #region Placement Logic

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

        //snapping ??= new(blockSelection);

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
        var newSelection = blockSelection.Clone();
        newSelection.DidOffset = true;
        newSelection.SetPos(outBlockPos.X, outBlockPos.Y, outBlockPos.Z);
        return placingBlock.CanPlaceBlock(World, Player, newSelection, ref failureCode);
    }
    #endregion
}
