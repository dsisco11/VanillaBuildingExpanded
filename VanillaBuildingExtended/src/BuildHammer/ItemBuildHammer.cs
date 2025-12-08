using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common.Collectible.Block;
using Vintagestory.GameContent;

namespace VanillaBuildingExtended;

public class ItemBuildHammer : Item
{
    #region Fields
    private readonly BuildBrushState _state = new();
    #endregion

    #region Properties
    //public readonly Dictionary<string, BuildBrushState> States = [];
    #endregion

    #region Accessors
    protected ILogger Logger => api.Logger;
    protected ICoreServerAPI? server => api as ICoreServerAPI;
    protected ICoreClientAPI? client => api as ICoreClientAPI;
    protected IPlayer? Player => client?.World.Player;

    public BuildBrushState GetState(in IPlayer? player)
    {
        return _state;
        //if (player is null)
        //{
        //    return null;
        //}

        //if (States.TryGetValue(player.PlayerUID, out BuildBrushState? state))
        //{
        //    return state;
        //}

        //// initialize
        //state = new BuildBrushState()
        //{
        //    IsActive = false,
        //    Snapping = EBuildBrushSnapping.None,
        //    IsValid = false,
        //    Position = null,
        //    ItemSlot = player.Entity.LeftHandItemSlot!,
        //    ItemStack = player.Entity.LeftHandItemSlot?.Itemstack,
        //    Selection = null,
        //};
        //States.Add(player.PlayerUID, state);

        //return state;
    }
    #endregion

    #region Handlers
    public override void OnLoaded(ICoreAPI api)
    {
        this.api = api;
        if (api.Side == EnumAppSide.Client)
        {
            client!.Event.PlayerEntitySpawn += Event_PlayerEntitySpawn;
            client!.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
            client!.Event.BlockChanged += Event_BlockChanged;
            client!.Event.RegisterGameTickListener(Thunk_Client, 100);
            client!.Event.RegisterGameTickListener(Thunk_Client_Slow, 500);
        }
    }

    private void Event_BlockChanged(BlockPos pos, Block oldBlock)
    {
        // When a block is changed, we need to re-validate the placement
        var state = GetState(Player);
        if (state is null || !state.IsActive)
            return;

        // make sure the block that changes was the one we were just about to place
        if (state.Position != pos)
            return;

        state.Selection = Player.CurrentBlockSelection;
        if(TryUpdateBlockSelection(client.World!, Player, state.Selection))
        {
            UpdateValidPlacementState();
        }
    }

    private void Event_AfterActiveSlotChanged(ActiveSlotChangeEventArgs obj)
    {
        int slotId = obj.ToSlot;
        var state = GetState(Player);
        if (state is not null)
        {
            state.Block = Player!.InventoryManager.GetHotbarInventory()?[slotId]?.Itemstack?.Block;
        }
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
        var state = GetState(byPlayer);
        if (state is null)
            return;

        state.IsActive = true;
    }

    private void DisableBuildMode(in IClientPlayer byPlayer)
    {
        var state = GetState(byPlayer);
        if (state is null)
            return;

        state.IsActive = false;
    }
    #endregion

    #region Handlers
    protected bool TryUpdateBlockSelection(in IWorldAccessor world, in IPlayer byPlayer, BlockSelection? blockSelection)
    {
        var state = GetState(Player);
        if (state is null)
            return false;

        state.Selection = blockSelection?.Clone();
        if (blockSelection is null)
        {
            state.IsValid = false;
            state.Position = null;
            return false;
        }

        BrushSnapping snapping = new (blockSelection);
        BrushSnappingState snappingState = new (snapping.Horizontal, snapping.Vertical, state.Snapping);
        if (snappingState == state.PreviousSnappingState && blockSelection.Position == state.Position)
        {
            return false;
        }
        state.PreviousSnappingState = snappingState;

        BlockPos? resolvedPos = ResolveFinalSelectionPosition(world, byPlayer, state.Block, blockSelection, state.Snapping, snapping);
        bool result = resolvedPos != state.Position;

        state.Position = resolvedPos;
        //state.Selection.SetPos(resolvedPos.X, resolvedPos.Y, resolvedPos.Z);
        //state.Selection.DidOffset = true;
        return result;
    }

    /// <summary>
    /// Resolves the block position based on the given snapping mode.
    /// </summary>
    /// <param name="blockSelection"></param>
    /// <param name="snappingMode"></param>
    /// <returns></returns>
    public static BlockPos ResolveFinalSelectionPosition(IWorldAccessor world, IPlayer byPlayer, in Block? placingBlock, [NotNull] in BlockSelection blockSelection, EBuildBrushSnapping snappingMode, BrushSnapping? snapping = null)
    {
        if (placingBlock is null)
        {
            return blockSelection.Position;
        }

        snapping ??= new(blockSelection);

        if (TryGetValidSnappedPosition(world, byPlayer, placingBlock, blockSelection, snapping.Value, snappingMode, out BlockPos outSnappedPos))
        {
            return outSnappedPos;
        }

        // the snapped position is invalid, use an unsnapped position instead.
        TryGetValidSnappedPosition(world, byPlayer, placingBlock, blockSelection, snapping.Value, EBuildBrushSnapping.None, out BlockPos outUnsnappedPos);
        return outUnsnappedPos;
    }

    /// <summary>
    /// Resolves a selection position based on the given snapping mode, and checks if the block can be placed there.
    /// </summary>
    /// <returns>
    /// True if a valid placement position was found; otherwise, false.
    /// </returns>
    public static bool TryGetValidSnappedPosition(IWorldAccessor world, IPlayer byPlayer, in Block? placingBlock, in BlockSelection blockSelection, in BrushSnapping snapping, EBuildBrushSnapping snappingMode, out BlockPos outBlockPos)
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
        return placingBlock.CanPlaceBlock(world, byPlayer, newSelection, ref failureCode);
    }

    protected void UpdateValidPlacementState()
    {
        var state = GetState(Player);
        if (state is null)
            return;

        if (state.Position is null || state.ItemStack is null)
        {
            state.IsValid = false;
            return;
        }
        state.PreviousCheckedPlacementPos = state.Position;
        Block block = state.ItemStack.Block;
        if (block is null)
        {
            state.IsValid = false;
            return;
        }
        IBlockAccessor blockAccessor = client!.World.BlockAccessor;
        Block existingBlock = blockAccessor.GetBlock(state.Position);
        string failureCode = string.Empty;
        bool canPlace = block.CanPlaceBlock(client!.World, client.World.Player, state.Selection!, ref failureCode);
        state.IsValid = canPlace;
    }
    #endregion

    #region Private Methods
    private bool GetIsHoldingBuildHammer(in IClientPlayer? byPlayer)
    {
        return byPlayer?.Entity.LeftHandItemSlot?.Itemstack?.Item is ItemBuildHammer;
    }

    private void Thunk_Client(float dt)
    {
        if (Player is null)
            return;

        if (client?.World is null)
            return;

        var state = GetState(Player);
        if (state is null) 
            return;

        if (!state.IsActive)
            return;

        BlockSelection? currentSelection = this.client!.World?.Player?.CurrentBlockSelection;
        if (TryUpdateBlockSelection(client.World!, Player, currentSelection))
        {
            this.UpdateValidPlacementState();
            return;
        }
    }

    private void Thunk_Client_Slow(float dt)
    {
        var state = GetState(Player);
        if (!state?.IsActive ?? false)
            return;

        // Here we force check validity every so often in case something changed
        this.UpdateValidPlacementState();
    }
    #endregion

    #region API
    public void RotateCursor(in IPlayer player, EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        var state = GetState(player);
        if (state is null)
            return;

        Block? block = state.Block;
        if (block is null)
            return;

        var angle = direction == EModeCycleDirection.Forward ? 90 : -90;
        state.Rotation += angle;
        AssetLocation nextCode = block.GetRotatedBlockCode(angle);
        state.Block = client!.World.BlockAccessor.GetBlock(nextCode);
    }

    #region Brush Snapping
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
            EBuildBrushSnapping.None => "none",
            EBuildBrushSnapping.Horizontal => "horizontal",
            EBuildBrushSnapping.Vertical => "vertical",
            EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical => "horizontal-vertical",
            _ => "Unknown",
        };
    }

    public void CycleSnappingMode(in IPlayer player, EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        if (client?.World is null)
            return;

        var state = GetState(player);
        if (state is null)
        {
            return;
        }

        int d = direction == EModeCycleDirection.Forward ? 1 : -1;
        state.SnappingModeIndex = (state.SnappingModeIndex + d) % BrushSnappingModes.Length;
        state.Snapping = BrushSnappingModes[state.SnappingModeIndex];
        string text = Lang.Get($"vbe-snapping-mode-changed--{ToString(state.Snapping)}");
        client.TriggerIngameError(this, "vbe-snapping-mode-changed", text);
        TryUpdateBlockSelection(client.World!, player, state.Selection);
    }
    #endregion
    #endregion

    #region Public
    public bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var state = GetState(byPlayer);
        if (state is null || !state.IsActive || state.Position is null)
            return false;

        if (itemstack is null || itemstack.Class != EnumItemClass.Block)
            return false;

        int? blockId = state.BlockId;
        if (blockId is null)
            return false;

        // Update itemstack to use the modified block-id (due to rotation)
        itemstack.Id = blockId.Value;
        itemstack.ResolveBlockOrItem(world);
        return true;
    }
    #endregion
}
