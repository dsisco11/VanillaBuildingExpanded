using VanillaBuildingExtended.BuildHammer;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VanillaBuildingExtended;

public class ItemBuildHammer : Item
{
    #region Fields
    #endregion

    #region Accessors
    protected ILogger Logger => api.Logger;
    protected ICoreServerAPI? server => api as ICoreServerAPI;
    protected ICoreClientAPI? client => api as ICoreClientAPI;
    protected IPlayer? Player => client?.World.Player;
    #endregion

    public override void OnLoaded(ICoreAPI api)
    {
        this.api = api;
    }

    public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null)
    {
        if (world is IClientWorldAccessor clientWorld)
        {
            IClientPlayer byPlayer = clientWorld.Player;
            BuildBrushManager_Client brushManager = VanillaBuildingExtendedModSystem.buildBrushManager as BuildBrushManager_Client;
            var brush = brushManager.GetBrush(byPlayer);
            if (brush is null)
                return;

            if (extractedStack is not null)
            {
                brush.IsActive = false;
                return;
            }
            bool isHoldingHammer = brushManager.HasHammer(byPlayer);
            brush.IsActive = isHoldingHammer;
        }
    }
}
