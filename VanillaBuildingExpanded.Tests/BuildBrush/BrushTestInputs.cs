using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Supplies independent inventory, selection and rule inputs to brush component tests.</summary>
public sealed class BrushTestInputs
{
    #region Public API

    public Mock<IPlayerInventoryManager> Inventory { get; } = new();
    public Mock<IBrushPlacementValidator> Validator { get; } = new();
    public BlockSelection? Selection { get; set; }

    /// <summary>Defaults component tests to accepted placement; rule-decision tests override this explicitly.</summary>
    public BrushTestInputs()
    {
        Validator.Setup(v => v.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny))
            .Returns(true);
    }

    /// <summary>Creates a real brush with a mutable selection source and the configured inventory/rules.</summary>
    public BuildBrushInstance CreateBrush(IWorldAccessor world)
    {
        return new BuildBrushInstance(world, Inventory.Object, "uid-test", "Tester", Validator.Object, () => Selection);
    }

    #endregion
}
