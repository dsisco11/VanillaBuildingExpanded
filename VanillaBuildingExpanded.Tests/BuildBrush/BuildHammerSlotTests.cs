using Vintagestory.API.Common;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Checks hammer identification from the resolved equipment slot without any player object.</summary>
public sealed class BuildHammerSlotTests
{
    #region Public API

    /// <summary>A hammer stack returns its actual collectible instance.</summary>
    [Fact]
    public void HammerSlot_ReturnsHammer()
    {
        ItemBuildHammer hammer = new() { ItemId = 10 };
        ItemSlot slot = new DummySlot(new ItemStack(hammer));

        Assert.True(slot.TryGetBuildHammer(out ItemBuildHammer? found));
        Assert.Same(hammer, found);
    }

    /// <summary>An unrelated item is not identified as a build hammer.</summary>
    [Fact]
    public void OtherItemSlot_DoesNotReturnHammer()
    {
        ItemSlot slot = new DummySlot(new ItemStack(new Item { ItemId = 11 }));

        Assert.False(slot.TryGetBuildHammer(out ItemBuildHammer? found));
        Assert.Null(found);
    }

    /// <summary>Both an empty slot and a missing resolved slot are handled without a player fallback.</summary>
    [Fact]
    public void EmptyOrMissingSlot_DoesNotReturnHammer()
    {
        Assert.False(new DummySlot().TryGetBuildHammer(out _));
        Assert.False(((ItemSlot?)null).TryGetBuildHammer(out _));
    }

    #endregion
}
