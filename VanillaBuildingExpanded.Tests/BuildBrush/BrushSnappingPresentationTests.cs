using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushSnappingPresentationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Presentation_TopAndBottom_RetainsBothHorizontalDirectionsAndOriginalAnchor(bool top)
    {
        Context context = new(top ? BlockFacing.UP : BlockFacing.DOWN, 1, -1);
        BrushSnappingPresentation snapshot = Assert.IsType<BrushSnappingPresentation>(context.Presentation);

        Assert.Equal(new BlockPos(3, 4, 5), snapshot.TargetPosition);
        Assert.Equal(new BlockPos(4, 4, 4), context.Brush.Position);
        Assert.Equal(context.Brush.Position, context.Brush.Selection.Position);
        Assert.Same(context.Selection.Face, snapshot.Face);
        Assert.Equal(1, snapshot.Result.AppliedSnapping.FaceU);
        Assert.Equal(-1, snapshot.Result.AppliedSnapping.FaceV);
        BlockPos leakedAnchor = snapshot.TargetPosition;
        leakedAnchor.X = 99;
        BlockPos leakedDestination = snapshot.Result.Position;
        leakedDestination.X = 99;
        context.Selection.Position.X = 20;
        context.Selection.HitPosition.X = 0.1;
        Assert.Equal(new BlockPos(3, 4, 5), snapshot.TargetPosition);
        Assert.Equal(new BlockPos(4, 4, 4), snapshot.Result.Position);
        Assert.Null(context.Presentation);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("dimension")]
    [InlineData("face")]
    [InlineData("hit")]
    [InlineData("box")]
    [InlineData("block")]
    [InlineData("mode")]
    [InlineData("offset")]
    [InlineData("slot")]
    [InlineData("attributes")]
    [InlineData("item")]
    [InlineData("count")]
    [InlineData("disabled")]
    [InlineData("position")]
    [InlineData("dirty")]
    public void Presentation_ChangedContext_IsSuppressedBeforeAnotherPlacementResolution(string change)
    {
        Context context = new();
        BrushSnappingPresentation before = Assert.IsType<BrushSnappingPresentation>(context.Presentation);
        ItemStack held = Assert.IsType<ItemStack>(context.Slot.Itemstack);
        switch (change)
        {
            case "target": context.Selection.Position.X++; break;
            case "dimension": context.Selection.Position.SetDimension(1); break;
            case "face": context.Selection.Face = BlockFacing.SOUTH; break;
            case "hit": context.Selection.HitPosition.X = 0.8; break;
            case "box": context.Selection.SelectionBoxIndex++; break;
            case "block": context.Selection.Block = new Block { BlockId = 200 }; break;
            case "mode": context.Brush.Snapping = EBuildBrushSnapping.Vertical; break;
            case "offset": context.Brush.Snapping |= EBuildBrushSnapping.ApplyFaceNormalOffset; break;
            case "slot": context.SlotNumber++; break;
            case "attributes": held.Attributes.SetString("lining", "changed"); break;
            case "item": context.Slot.Itemstack = new ItemStack(new Item { ItemId = 200 }); break;
            case "count": held.StackSize = 0; break;
            case "disabled": context.Brush.IsActive = false; break;
            case "position": context.Brush.Position = new BlockPos(99, 99, 99); break;
            case "dirty": context.Brush.MarkDirty(); break;
            default: throw new InvalidOperationException();
        }

        Assert.Null(context.Presentation);
        Assert.Equal(1, before.Result.AppliedSnapping.FaceU);
        context.VerifyValidationCount(1);
    }

    [Fact]
    public void Presentation_EligibilityAndTargetLoss_DoNotRunValidation()
    {
        Context context = new();
        Assert.NotNull(context.Presentation);
        Assert.Null(context.Client.GetSnappingPresentation(context.Selection, false));
        Assert.Null(context.Client.GetSnappingPresentation(null, true));
        for (int i = 0; i < 10; i++)
            Assert.NotNull(context.Presentation);
        context.VerifyValidationCount(1);

        context.Client.TryUpdate(null);
        Assert.Null(context.Presentation);
        context.VerifyValidationCount(1);
        context.Client.TryUpdate(context.Selection);
        Assert.NotNull(context.Presentation);
        context.VerifyValidationCount(2);
        context.Client.SetBuildMode(false, context.Selection);
        Assert.Null(context.Presentation);
        context.VerifyValidationCount(2);
    }

    [Theory]
    [InlineData(0, 0, EBuildBrushSnapping.Horizontal)]
    [InlineData(1, 0, EBuildBrushSnapping.None)]
    [InlineData(1, 0, EBuildBrushSnapping.Vertical)]
    [InlineData(0, 0, EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.ApplyFaceNormalOffset)]
    public void Presentation_NoPermittedTangentialSnap_IsSuppressed(int u, int v, EBuildBrushSnapping mode)
    {
        Context context = new(BlockFacing.NORTH, u, v, mode);
        Assert.Null(context.Presentation);
        context.VerifyValidationCount(1);
    }

    [Fact]
    public void Presentation_DisabledAxisIsAbsent_AndHeldOffsetDoesNotAddAGlyph()
    {
        Context context = new(BlockFacing.NORTH, 1, -1,
            EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.ApplyFaceNormalOffset);
        BrushSnappingPresentation snapshot = Assert.IsType<BrushSnappingPresentation>(context.Presentation);
        Assert.Equal(1, snapshot.Result.AppliedSnapping.FaceU);
        Assert.Equal(0, snapshot.Result.AppliedSnapping.FaceV);
        Assert.Equal(1, snapshot.Result.AppliedSnapping.FaceW);
        Assert.Equal(new BlockPos(4, 4, 4), context.Brush.Position);
    }

    [Fact]
    public void Presentation_ValidityRefresh_UsesItsExistingSingleCheckAndSuppressesRejection()
    {
        Context context = new();
        bool valid = false;
        context.Inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny))
            .Returns(() => valid);

        context.Client.TryUpdatePlacementValidity(context.Selection);
        Assert.Null(context.Presentation);
        context.VerifyValidationCount(2);
        valid = true;
        context.Client.TryUpdatePlacementValidity(context.Selection);
        Assert.NotNull(context.Presentation);
        context.VerifyValidationCount(3);

        context.Selection.Face = BlockFacing.SOUTH;
        context.Client.TryUpdatePlacementValidity(context.Selection);
        Assert.Null(context.Presentation);
        context.VerifyValidationCount(4);
    }

    [Fact]
    public void Presentation_RejectedResolution_DoesNotRetryOrShowPreviouslyAcceptedDirections()
    {
        Context context = new();
        context.Inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny))
            .Returns(false);
        BlockPos? previous = context.Brush.Position?.Copy();

        context.Client.TryUpdate(context.Selection);

        Assert.Equal(previous, context.Brush.Position);
        Assert.False(context.Brush.IsValidPlacement);
        Assert.Null(context.Presentation);
        context.VerifyValidationCount(2);
    }

    [Fact]
    public void Presentation_ResolutionInProgress_ClearsOldResultAndRejectsChangedInputs()
    {
        Context context = new();
        context.Inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny))
            .Callback(() =>
            {
                Assert.Null(context.Presentation);
                context.Brush.Snapping = EBuildBrushSnapping.Vertical;
            })
            .Returns(true);

        context.Client.TryUpdate(context.Selection);

        Assert.Null(context.Presentation);
        context.VerifyValidationCount(2);
    }

    [Fact]
    public void Presentation_NewTargetAtOldDestination_IsResolvedInsteadOfReusingOldOutcome()
    {
        Context context = new();
        BrushSnappingPresentation first = Assert.IsType<BrushSnappingPresentation>(context.Presentation);
        context.Selection.Position = context.Brush.Position!.Copy();

        context.Client.TryUpdate(context.Selection);

        BrushSnappingPresentation second = Assert.IsType<BrushSnappingPresentation>(context.Presentation);
        Assert.Equal(new BlockPos(4, 4, 5), second.TargetPosition);
        Assert.Equal(new BlockPos(5, 4, 5), second.Result.Position);
        Assert.True(second.Revision > first.Revision);
        context.VerifyValidationCount(2);
    }

    [Fact]
    public void Presentation_SourceRefresh_ReplacesAttributesAndSlotRevisionBeforePublishing()
    {
        Context context = new();
        BrushSnappingPresentation first = Assert.IsType<BrushSnappingPresentation>(context.Presentation);
        Assert.IsType<ItemStack>(context.Slot.Itemstack).Attributes.SetString("lining", "changed");
        context.SlotNumber++;
        context.Client.TryUpdate(context.Selection);

        BrushSnappingPresentation second = Assert.IsType<BrushSnappingPresentation>(context.Presentation);
        Assert.True(second.Revision > first.Revision);
        Assert.Equal(context.SlotNumber, context.Brush.SourceHotbarSlotNumber);
        context.VerifyValidationCount(2);
    }

    [Fact]
    public void Presentation_UnsupportedSource_SuspendsAndResumesWithoutStaleResult()
    {
        Context context = new();
        ItemStack original = Assert.IsType<ItemStack>(context.Slot.Itemstack);
        context.Slot.Itemstack = null;
        context.Client.TryUpdate(context.Selection);
        Assert.Null(context.Presentation);
        Assert.True(context.Brush.IsDisabled);
        context.VerifyValidationCount(1);

        context.Slot.Itemstack = original;
        context.Client.TryUpdate(context.Selection);
        Assert.NotNull(context.Presentation);
        context.VerifyValidationCount(2);
    }

    [Fact]
    public void Snapshot_MatchesRequiresRevision_AndTargetBlockIdentityIsCopied()
    {
        BlockSelection target = BrushPlacementResultTests.CreateSelection(BlockFacing.NORTH, 1, 0);
        target.Block = new Block { BlockId = 10 };
        BrushPlacementResult result = new BrushPlacementValidation(new BrushTestInputs().Validator.Object)
            .Resolve(new Block(), target, EBuildBrushSnapping.Horizontal, new(target));
        BrushSnappingPresentation snapshot = new(target, result, 7);
        Assert.True(snapshot.Matches(target, EBuildBrushSnapping.Horizontal, 7));
        Assert.False(snapshot.Matches(target, EBuildBrushSnapping.Horizontal, 8));
        target.Block.BlockId = 11;
        Assert.Equal(10, snapshot.TargetBlockId);
        Assert.False(snapshot.Matches(target, EBuildBrushSnapping.Horizontal, 7));
    }

    private sealed class Context
    {
        public BrushTestInputs Inputs { get; } = new();
        public BlockSelection Selection { get; }
        public DummySlot Slot { get; }
        public int SlotNumber { get; set; }
        public BuildBrushControllerClient Client { get; }
        public BuildBrushInstance Brush => Client.GetBrush();
        public BrushSnappingPresentation? Presentation => Client.GetSnappingPresentation(Selection, true);

        public Context(BlockFacing? face = null, int u = 1, int v = 0,
            EBuildBrushSnapping mode = EBuildBrushSnapping.Horizontal)
        {
            var world = TestHelpers.CreateMockWorld();
            Block block = TestHelpers.CreateAndRegisterBlock(world, 10);
            Slot = new DummySlot(new ItemStack(block));
            Inputs.Inventory.Setup(x => x.ActiveHotbarSlot).Returns(Slot);
            Inputs.Inventory.Setup(x => x.ActiveHotbarSlotNumber).Returns(() => SlotNumber);
            Selection = BrushPlacementResultTests.CreateSelection(face ?? BlockFacing.NORTH, u, v);
            Inputs.Selection = Selection;
            Client = new BuildBrushControllerClient(TestHelpers.CreateMockCapi().Object, () => Inputs.CreateBrush(world.Object));
            Brush.Snapping = mode;
            Client.SetBuildMode(true, Selection);
        }

        public void VerifyValidationCount(int count) =>
            Inputs.Validator.Verify(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny),
                Times.Exactly(count));
    }
}
