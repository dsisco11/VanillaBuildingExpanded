using System;
using System.Numerics;

using VanillaBuildingExtended.src.Extensions.Math;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExtended;

[Flags]
public enum EBuildBrushSnapping
{
    None = 1 << 0,
    Horizontal = 1 << 1,
    Vertical = 1 << 2,
}

public record class BuildBrushState
{
    private ItemStack? _itemStack;
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
    public BlockSelection? Selection;
}

public class ItemBuildHammer : Item
{
    #region Fields
    private ICoreAPI api;
    private BlockPos? previousCheckedPlacementPos;
    #endregion

    #region Properties
    public BuildBrushState State { get; } = new();
    #endregion

    #region Accessors
    protected ILogger Logger => api.Logger;
    protected ICoreServerAPI? server => api as ICoreServerAPI;
    protected ICoreClientAPI? client => api as ICoreClientAPI;
    #endregion

    #region Handlers
    public override void OnLoaded(ICoreAPI api)
    {
        this.api = api;
        if (api.Side == EnumAppSide.Client)
        {
            client!.Event.PlayerEntitySpawn += Event_PlayerEntitySpawn;
            client!.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
            client!.Event.RegisterGameTickListener(Thunk_Client, 100);
            client!.Event.RegisterGameTickListener(Thunk_Client_Slow, 500);
        }
    }

    private void Event_AfterActiveSlotChanged(ActiveSlotChangeEventArgs obj)
    {
        int slotId = obj.ToSlot;
        this.State.ItemStack = client!.World.Player.InventoryManager.GetHotbarInventory()?[slotId]?.Itemstack;
    }

    private void Event_PlayerEntitySpawn(IClientPlayer byPlayer)
    {
        bool isHoldingHammer = GetIsHoldingBuildHammer(byPlayer);
        SetBuildModeEnabled(byPlayer, isHoldingHammer);
    }

    public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null)
    {
        if (world is IClientWorldAccessor clientWorld)
        {
            IClientPlayer byPlayer = clientWorld.Player;
            if (extractedStack is not null)
            {
                SetBuildModeEnabled(byPlayer, false);
                return;
            }
            bool isHoldingHammer = GetIsHoldingBuildHammer(byPlayer);
            SetBuildModeEnabled(byPlayer, isHoldingHammer);
        }
    }

    #endregion

    #region Hammer Activation
    public void SetBuildModeEnabled(in IClientPlayer byPlayer, bool enabled)
    {
        if (enabled)
        {
            EnableBuildMode(byPlayer);
        }
        else
        {
            DisableBuildMode(byPlayer);
        }
    }

    private void EnableBuildMode(in IClientPlayer byPlayer)
    {
        this.State.IsActive = true;
        //OnBlockSelectionChanged(byPlayer.CurrentBlockSelection, null);
    }

    private void DisableBuildMode(in IClientPlayer byPlayer)
    {
        this.State.IsActive = false;
        //if (api.Side == EnumAppSide.Server)
        //{
        //}
        //else if (api.Side == EnumAppSide.Client)
        //{
        //    client!.World.SetBlocksPreviewDimension(-1);
        //}
    }
    #endregion

    #region Handlers
    protected void OnBlockSelection(BlockSelection? value)
    {
        this.State.Selection = value;
        if (value is null)
        {
            this.State.IsValid = false;
            this.State.Position = null;
            return;
        }

        BlockPos resolved = value.Position;
        Vector3 faceNormal = value.Face.Normalf.ToSNT();
        Vector3 faceCenterPoint = (faceNormal * 0.5f) + new Vector3(0.5f);
        Vector3 faceRelativeHitPos = value.HitPosition.ToSNT() - faceCenterPoint;
        Vector2 hitPos = value.Face.ToAB(faceRelativeHitPos);

        if (State.Snapping.HasFlag(EBuildBrushSnapping.Horizontal))
        {
            int scale = hitPos.X > 0f ? 1 : -1;
            FastVec3i horzDir = value.Face.Axis switch
            {
                EnumAxis.X => new FastVec3i(0, 0, scale),
                EnumAxis.Y => new FastVec3i(scale, 0, 0),
                EnumAxis.Z => new FastVec3i(scale, 0, 0),
                _ => new FastVec3i(0, 0, 0),
            };

            resolved.Add(horzDir);
        }

        if (State.Snapping.HasFlag(EBuildBrushSnapping.Vertical))
        {
            int scale = hitPos.Y > 0f ? 1 : -1;
            FastVec3i vertDir = value.Face.Axis switch
            {
                EnumAxis.X => new FastVec3i(0, scale, 0),
                EnumAxis.Z => new FastVec3i(0, scale, 0),
                EnumAxis.Y => new FastVec3i(scale, 0, 0),
                _ => new FastVec3i(0, 0, 0),
            };

            resolved.Add(vertDir);
        }

        if (State.Snapping == EBuildBrushSnapping.None)
        {
            resolved.Add(value?.Face.Normali);
        }

        State.Position = resolved;
        State.Selection!.Position = resolved;
    }

    protected void UpdateValidPlacementState()
    {
        if (this.State.Position is null || this.State.ItemStack is null)
        {
            this.State.IsValid = false;
            return;
        }
        this.previousCheckedPlacementPos = this.State.Position;
        Block block = this.State.ItemStack.Block;
        if (block is null)
        {
            this.State.IsValid = false;
            return;
        }
        IBlockAccessor blockAccessor = client!.World.BlockAccessor;
        Block existingBlock = blockAccessor.GetBlock(this.State.Position);
        string failureCode = string.Empty;
        bool canPlace = block.CanPlaceBlock(client!.World, client.World.Player, this.State.Selection!, ref failureCode);
        this.State.IsValid = canPlace;
    }
    #endregion

    #region Private Methods
    private bool GetIsHoldingBuildHammer(in IClientPlayer? byPlayer)
    {
        return byPlayer?.Entity.LeftHandItemSlot?.Itemstack?.Item is ItemBuildHammer;
    }

    private void Thunk_Client(float dt)
    {
        if (!this.State.IsActive)
        {
            return;
        }

        BlockSelection? currentSelection = this.client!.World?.Player?.CurrentBlockSelection;
        this.OnBlockSelection(currentSelection);
        if (this.State.Position != this.previousCheckedPlacementPos)
        {
            this.UpdateValidPlacementState();
        }
    }
    private void Thunk_Client_Slow(float dt)
    {
        if (!this.State.IsActive)
        {
            return;
        }
        // force recheck validity every so often in case something changed
        this.UpdateValidPlacementState();
    }
    #endregion

    #region API
    public void RotateCursor(int direction = 1)
    {
        CollectibleObject item = this.State.ItemStack!.Collectible;
        if (item is Block block)
        {
            AssetLocation nextCode = block.GetRotatedBlockCode(direction >= 0 ? 90 : -90);
            this.State.ItemStack = new ItemStack(client!.World.BlockAccessor.GetBlock(nextCode)!);
        }
    }

    #region Brush Snapping
    private static int BrushSnappingMode = 0;
    private static readonly EBuildBrushSnapping[] BrushSnappingModes = [
        EBuildBrushSnapping.None,
        EBuildBrushSnapping.Horizontal,
        EBuildBrushSnapping.Vertical,
        EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical,
    ];

    public string ToString(EBuildBrushSnapping mode)
    {
        return mode switch
        {
            EBuildBrushSnapping.None => "None",
            EBuildBrushSnapping.Horizontal => "Horizontal",
            EBuildBrushSnapping.Vertical => "Vertical",
            EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical => "Horizontal & Vertical",
            _ => "Unknown",
        };
    }

    public void CycleSnappingMode(int direction = 1)
    {
        int d = direction >= 0 ? 1 : -1;
        BrushSnappingMode = (BrushSnappingMode + d) % BrushSnappingModes.Length;
        this.State.Snapping = BrushSnappingModes[BrushSnappingMode];
    }
    #endregion
    #endregion
}
