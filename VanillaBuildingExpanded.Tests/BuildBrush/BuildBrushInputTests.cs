using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Checks brush input ownership and held modifiers across mode and inventory changes.</summary>
public sealed class BuildBrushInputTests
{
    #region Public API

    /// <summary>Rotation consumes input and changes the placement variant only for an enabled brush.</summary>
    [Fact]
    public void SupportedRotation_ChangesVariantOnlyInBuildMode()
    {
        var world = TestHelpers.CreateMockWorld();
        var north = TestHelpers.CreateAndRegisterBlock(world, 10, "game:test-north");
        var east = TestHelpers.CreateAndRegisterBlock(world, 11, "game:test-east");
        north.VariantStrict["rot"] = "north";
        east.VariantStrict["rot"] = "east";
        world.Setup(w => w.SearchBlocks(It.IsAny<AssetLocation>())).Returns([north, east]);
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(north)));
        var api = TestHelpers.CreateMockCapi();
        var clientWorld = Mock.Get(api.Object.World);
        BuildBrushControllerClient client = new(api.Object, () => inputs.CreateBrush(world.Object));
        Assert.False(client.RotateCursor(EModeCycleDirection.Forward));
        clientWorld.Verify(w => w.PlaySound(It.IsAny<SoundAttributes>()), Times.Never);
        Assert.Equal(10, client.GetBrush().CurrentPlacementBlock!.Id);
        client.SetBuildMode(true, Selection());
        Assert.True(client.RotateCursor(EModeCycleDirection.Forward));
        clientWorld.Verify(w => w.PlaySound(new SoundAttributes(new AssetLocation("game:sounds/menubutton"), false)), Times.Once);
        Assert.Equal(11, client.GetBrush().CurrentPlacementBlock!.Id);
        Assert.True(client.RotateCursor(EModeCycleDirection.Backward));
        clientWorld.Verify(w => w.PlaySound(new SoundAttributes(new AssetLocation("game:sounds/menubutton"), false)), Times.Exactly(2));
        Assert.Equal(10, client.GetBrush().CurrentPlacementBlock!.Id);
        client.SetBuildMode(false, null);
        Assert.False(client.RotateCursor(EModeCycleDirection.Forward));
        clientWorld.Verify(w => w.PlaySound(new SoundAttributes(new AssetLocation("game:sounds/menubutton"), false)), Times.Exactly(2));
    }

    /// <summary>Disabled mode and unsupported sources never consume brush hotkeys or allow placement.</summary>
    [Theory]
    [InlineData(false, "block")]
    [InlineData(true, "empty")]
    [InlineData(true, "item")]
    [InlineData(true, "missing")]
    public void UnusableBrush_PassesControlsThrough(bool active, string source)
    {
        var world = TestHelpers.CreateMockWorld();
        Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
        block.IsMissing = source == "missing";
        ItemSlot slot = source switch
        {
            "empty" => new DummySlot(),
            "item" => new DummySlot(new ItemStack(new Item { ItemId = 20 })),
            _ => new DummySlot(new ItemStack(block))
        };
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(slot);
        BuildBrushControllerClient client = new(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        client.SetBuildMode(active, Selection());
        var brush = client.GetBrush();
        var snapping = brush.Snapping;

        Assert.False(client.CanUseBrushControls);
        Assert.False(client.RotateCursor(EModeCycleDirection.Forward));
        Assert.False(client.CycleSnappingMode(EModeCycleDirection.Forward));
        Assert.False(client.SetFaceOffset(true));
        Assert.False(client.SetFaceOffset(false));
        Assert.Equal(snapping, brush.Snapping);
        Assert.True(brush.IsDisabled);
    }

    /// <summary>A nonrotatable block still supports snapping and face offset without a target.</summary>
    [Fact]
    public void SupportedBlockWithoutTarget_ConsumesOnlySupportedControls()
    {
        var world = TestHelpers.CreateMockWorld();
        var block = TestHelpers.CreateAndRegisterBlock(world, 10);
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
        var api = TestHelpers.CreateMockCapi();
        BuildBrushControllerClient client = new(api.Object, () => inputs.CreateBrush(world.Object));
        client.SetBuildMode(true, null);
        var brush = client.GetBrush();

        Assert.True(client.CanUseBrushControls);
        Assert.False(client.RotateCursor(EModeCycleDirection.Forward));
        Mock.Get(api.Object.World).Verify(w => w.PlaySound(It.IsAny<SoundAttributes>()), Times.Never);
        Assert.True(client.SetFaceOffset(true));
        Assert.True(client.CycleSnappingMode(EModeCycleDirection.Forward));
        Assert.Equal(EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.ApplyFaceNormalOffset, brush.Snapping);
        Assert.True(client.SetFaceOffset(false));
        Assert.Equal(EBuildBrushSnapping.Horizontal, brush.Snapping);
        Assert.True(brush.IsDisabled);
    }

    /// <summary>Suspension and mode exit clear held offset while retaining the selected snapping preference.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExitOrSuspension_ClearsHeldOffsetWithoutRestoringItOnResume(bool exit)
    {
        var world = TestHelpers.CreateMockWorld();
        var block = TestHelpers.CreateAndRegisterBlock(world, 10);
        ItemSlot slot = new DummySlot(new ItemStack(block));
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(() => slot);
        BuildBrushControllerClient client = new(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        client.SetBuildMode(true, Selection());
        var brush = client.GetBrush();
        brush.Snapping = EBuildBrushSnapping.Vertical;
        client.SetFaceOffset(true);
        if (exit)
            client.SetBuildMode(false, null);
        else
        {
            slot = new DummySlot();
            client.TryUpdate(Selection());
        }

        Assert.Equal(EBuildBrushSnapping.Vertical, brush.Snapping);
        Assert.False(client.SetFaceOffset(false));
        Assert.True(brush.IsDisabled);
        slot = new DummySlot(new ItemStack(block));
        if (exit)
            client.SetBuildMode(true, Selection());
        else
            client.TryUpdate(Selection());
        Assert.Equal(EBuildBrushSnapping.Vertical, brush.Snapping);
        Assert.False(brush.IsDisabled);
    }

    /// <summary>Snapping cycles in both directions through None while a held modifier remains set.</summary>
    [Theory]
    [InlineData(EModeCycleDirection.Forward)]
    [InlineData(EModeCycleDirection.Backward)]
    public void SnappingCycle_PreservesHeldModifierAndWraps(EModeCycleDirection direction)
    {
        var world = TestHelpers.CreateMockWorld();
        var block = TestHelpers.CreateAndRegisterBlock(world, 10);
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
        BuildBrushControllerClient client = new(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        client.SetBuildMode(true, Selection());
        client.SetFaceOffset(true);
        var brush = client.GetBrush();
        var initial = brush.Snapping;
        HashSet<EBuildBrushSnapping> visited = [];
        for (int i = 0; i < 4; i++)
        {
            Assert.True(client.CycleSnappingMode(direction));
            Assert.True(brush.Snapping.HasFlag(EBuildBrushSnapping.ApplyFaceNormalOffset));
            Assert.NotEqual("unknown", brush.Snapping.GetCode());
            visited.Add(brush.Snapping & ~EBuildBrushSnapping.ApplyFaceNormalOffset);
        }
        Assert.Equal(4, visited.Count);
        Assert.Equal(initial, brush.Snapping);
    }

    #endregion

    #region Private

    /// <summary>Creates a target that allows the brush to build a placement request.</summary>
    private static BlockSelection Selection()
    {
        return new BlockSelection { Position = new BlockPos(1, 2, 3), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
    }

    #endregion
}
