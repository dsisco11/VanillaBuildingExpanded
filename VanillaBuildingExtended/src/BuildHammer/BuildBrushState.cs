using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExtended;

public record class BuildBrushState
{
    /// <summary>
    /// Indicates whether the build brush is currently active.
    /// </summary>
    public bool IsActive;
    public EBuildBrushSnapping Snapping = EBuildBrushSnapping.None;
    /// <summary>
    /// Indicates whether the current placement position is valid.
    /// </summary>
    public bool IsValid;
    /// <summary>
    /// The position of the build cursor.
    /// </summary>
    public BlockPos? Position;
    /// <summary>
    /// The rotation angle for the item placement.
    /// </summary>
    public int Rotation = 0;
    public BlockSelection? Selection;
    /// <summary>
    /// The item currently selected for placement.
    /// </summary>
    public ItemSlot ItemSlot = new DummySlot();
    public ItemStack? ItemStack
    {
        get => this._itemStack;
        set
        {
            this._itemStack = value;
            this.ItemSlot.Itemstack = value;
        }
    }
    public Block? Block
    {
        get => _block;
        set
        {
            _block = value;
            if (value is not null)
            {
                this.ItemStack = new ItemStack(value);
            }
            else
            {
                this.ItemStack = null;
            }
        }
    }

    public int? BlockId => _itemStack?.Class == EnumItemClass.Block ? this.Block?.BlockId : null;

    #region State tracking
    /// <summary>
    /// The index of the current snapping mode.
    /// </summary>
    public int SnappingModeIndex = 0;

    /// <summary> 
    /// The previous position where placement validity was checked. 
    /// </summary>
    public BlockPos? PreviousCheckedPlacementPos;

    public BrushSnappingState PreviousSnappingState = new();
    private Block? _block;
    private ItemStack? _itemStack;
    #endregion
}
