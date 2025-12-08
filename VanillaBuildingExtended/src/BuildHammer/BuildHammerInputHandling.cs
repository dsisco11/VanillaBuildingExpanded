using VanillaBuildingExtended.BuildHammer;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Common;

namespace VanillaBuildingExtended.src.BuildHammer;
internal class BuildHammerInputHandling
{
    #region Fields
    protected readonly ICoreClientAPI api;
    #endregion

    #region Accessors
    protected ILogger Logger => api.Logger;
    protected IClientPlayer Player => api.World.Player;
    protected ItemBuildHammer? Hammer
    {
        get
        {
            ItemSlot activeSlot = Player.Entity.LeftHandItemSlot;
            return activeSlot?.Itemstack?.Collectible as ItemBuildHammer;
        }
    }

    protected BuildBrushManager_Client brushManager => VanillaBuildingExtendedModSystem.buildBrushManager as BuildBrushManager_Client;
    #endregion

    public BuildHammerInputHandling(ICoreClientAPI api)
    {
        this.api = api;
    }

    public bool Input_RotateBuildCursor_Forward(KeyCombination keys)
    {
        var brush = this.brushManager.GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        this.brushManager.RotateCursor(Player, EModeCycleDirection.Forward);
        return true;
    }

    public bool Input_RotateBuildCursor_Backward(KeyCombination keys)
    {
        var brush = this.brushManager.GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        this.brushManager.RotateCursor(Player, EModeCycleDirection.Backward);
        return true;
    }

    public bool Input_CycleSnappingMode_Forward(KeyCombination keys)
    {
        var brush = this.brushManager.GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        this.brushManager.CycleSnappingMode(Player, EModeCycleDirection.Forward);
        return true;
    }

    public bool Input_CycleSnappingMode_Backward(KeyCombination keys)
    {
        var brush = this.brushManager.GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        this.brushManager.CycleSnappingMode(Player, EModeCycleDirection.Backward);
        return true;
    }

    public void InWorldAction(in ICoreClientAPI client, EnumEntityAction action, bool on, ref EnumHandling handling)
    {
        handling = EnumHandling.PassThrough;
        if (action == EnumEntityAction.InWorldRightMouseDown && on)
        {
            TryPlaceBlock(client, ref handling);
        }
    }

    public static bool TryPrecheckBlockPlacement(in ICoreClientAPI client, in BlockPos position, in ItemStack itemstack, out string failureCode)
    {
        failureCode = string.Empty;
        IWorldAccessor World = client.World;
        IPlayer player = client.World.Player;
        if (!World.BlockAccessor.IsValidPos(position))
        {
            failureCode = "outsideworld";
            return false;
        }
        // Block.CanPlaceBlock does its own access checks, so we can skip this.
        //if (!tryAccess(blockSelection, EnumBlockAccessFlags.BuildOrBreak))
        //{
        //    return false;
        //}

        if (itemstack is null || itemstack.Class != EnumItemClass.Block)
        {
            return false;
        }

        Block oldBlock = World.BlockAccessor.GetBlock(position);
        Block liqBlock = World.BlockAccessor.GetBlock(position, 2);
        bool preventPlacementInLava = true;// unsure where to get the actual setting from, so we just hardcode it for now
        if (preventPlacementInLava && liqBlock.LiquidCode == "lava" && player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            failureCode = "toohottoplacehere";
            return false;
        }
        return true;
    }

    private void TryPlaceBlock(in ICoreClientAPI client, ref EnumHandling handling)
    {
        if (!brushManager.HasHammer(client.World.Player))
        {
            return;
        }

        var brush = brushManager.GetBrush(client.World.Player);
        BlockPos brushPos = brush.Position;
        BlockSelection blockSelection = brush.Selection;
        blockSelection.Position = brushPos;

        if (blockSelection is null)
        {
            Logger.Warning("[Build Hammer]: No valid placement position.");
            return;
        }

        ItemStack? stackToPlace = brush.ItemStack;
        if (stackToPlace is null)
        {
            Logger.Warning("[Build Hammer]: No item selected for placement.");
            return;
        }

        Block block = stackToPlace.Block;
        if (block is null)
        {
            Logger.Warning("[Build Hammer]: Selected item is not a block.");
            return;
        }

        handling = EnumHandling.PreventSubsequent;
        if (!TryPrecheckBlockPlacement(client, brushPos, stackToPlace, out string precheckFailure))
        {
            Logger.Warning($"[Build Hammer]: Precheck for block placement failed: {precheckFailure}");
            client.TriggerIngameError(this, precheckFailure, Lang.Get($"placefailure-{precheckFailure}"));
            return;
        }

        IWorldAccessor World = client.World;
        IPlayer player = client.World.Player;

        string failureCode = string.Empty;
        if (block.CanPlaceBlock(api.World, Player, blockSelection, ref failureCode))
        {
            Block oldBlock = World.BlockAccessor.GetBlock(brushPos);
            block.DoPlaceBlock(client.World, Player, blockSelection, stackToPlace);
            client.Network.SendPacketClient(ClientPackets.BlockInteraction(blockSelection, 1, 0));
            World.BlockAccessor.MarkBlockModified(brushPos);
            World.BlockAccessor.TriggerNeighbourBlockUpdate(brushPos);
        }
    }

}
