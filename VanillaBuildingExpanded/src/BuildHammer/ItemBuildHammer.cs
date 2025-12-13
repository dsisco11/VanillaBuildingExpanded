using System.Text;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded;

public class ItemBuildHammer : Item
{
    #region Fields
    #endregion

    public override void OnLoaded(ICoreAPI api)
    {
        this.api = api;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        string wood = inSlot.Itemstack.Attributes.GetString("material", "oak");
        dsc.AppendLine(Lang.Get("Material: {0}", Lang.Get($"material-{wood}")));
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        var material = itemStack.Attributes.GetString("material", "oak");
        return Lang.GetMatching($"item-{Code.Path}-{material}", Lang.Get($"material-{material}"));
    }
}
