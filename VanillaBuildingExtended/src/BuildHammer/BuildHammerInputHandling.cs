using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

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
    #endregion

    public BuildHammerInputHandling(ICoreClientAPI api)
    {
        this.api = api;
    }

    public bool Input_RotateBuildCursor(KeyCombination keys)
    {
        if (!this.Hammer?.State?.IsActive ?? false)
        {
            return false;
        }
        this.Hammer?.RotateCursor(1);
        return true;
    }

    public bool Input_RotateBuildCursor_Forward(KeyCombination keys)
    {
        if (!this.Hammer?.State?.IsActive ?? false)
        {
            return false;
        }
        this.Hammer?.RotateCursor(1);
        return true;
    }

    public bool Input_RotateBuildCursor_Backward(KeyCombination keys)
    {
        if (!this.Hammer?.State?.IsActive ?? false)
        {
            return false;
        }
        this.Hammer?.RotateCursor(-1);
        return true;
    }

    public bool Input_CycleSnappingMode_Forward(KeyCombination keys)
    {
        if (!this.Hammer?.State?.IsActive ?? false)
        {
            return false;
        }
        this.Hammer?.CycleSnappingMode(1);
        return true;
    }

    public bool Input_CycleSnappingMode_Backward(KeyCombination keys)
    {
        if (!this.Hammer?.State?.IsActive ?? false)
        {
            return false;
        }
        this.Hammer?.CycleSnappingMode(-1);
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

    private void TryPlaceBlock(in ICoreClientAPI client, ref EnumHandling handling)
    {
        ItemBuildHammer? hammer = this.Hammer;
        if (hammer is null)
        {
            return;
        }

        BlockSelection blockSelection = hammer.State.Selection;
        if (blockSelection is null)
        {
            Logger.Warning("Build Hammer: No valid placement position.");
            return;
        }

        ItemStack? stackToPlace = hammer.State.ItemStack;
        if (stackToPlace is null)
        {
            Logger.Warning("Build Hammer: No item selected for placement.");
            return;
        }

        Block block = stackToPlace.Block;
        if (block is null)
        {
            Logger.Warning("Build Hammer: Selected item is not a block.");
            return;
        }

        string failureCode = string.Empty;
        handling = EnumHandling.PreventSubsequent;
        IBlockAccessor accessor = client.World.BlockAccessor;
        if (block.CanPlaceBlock(api.World, Player, blockSelection, ref failureCode))
        {
            //accessor.SetBlock(block.BlockId, hammer.State.Position, hammer.State.ItemStack);
            block.DoPlaceBlock(client.World, Player, blockSelection, stackToPlace);
        }
    }
}
