using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushPlacementResultTests
{
    [Theory]
    [MemberData(nameof(BrushSnappingTests.WorldSpaceSnappingCases), MemberType = typeof(BrushSnappingTests))]
    public void Resolve_AllFaces_ReportsRequestedResolvedAndAcceptedDirections(
        int faceIndex, int u, int v, EBuildBrushSnapping mode, bool held, int deltaX, int deltaY, int deltaZ)
    {
        BlockSelection source = CreateSelection(BlockFacing.ALLFACES[faceIndex], u, v);
        if (held)
            mode |= EBuildBrushSnapping.ApplyFaceNormalOffset;
        bool horizontal = !mode.HasFlag(EBuildBrushSnapping.None) && mode.HasFlag(EBuildBrushSnapping.Horizontal);
        bool vertical = !mode.HasFlag(EBuildBrushSnapping.None) && mode.HasFlag(EBuildBrushSnapping.Vertical);
        int appliedU = horizontal ? u : 0;
        int appliedV = (source.Face.Axis == EnumAxis.Y ? horizontal : vertical) ? v : 0;
        bool normalAllowed = mode.HasFlag(EBuildBrushSnapping.None)
            || (source.Face.Axis == EnumAxis.Y ? vertical : horizontal);
        BrushSnappingState expected = new(appliedU, appliedV,
            held || (appliedU == 0 && appliedV == 0 && normalAllowed) ? 1 : 0, mode);

        foreach (bool valid in new[] { true, false })
        {
            BrushTestInputs inputs = new();
            inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny))
                .Returns(valid);
            BrushPlacementResult result = new BrushPlacementValidation(inputs.Validator.Object)
                .Resolve(new Block(), source, mode, new BrushSnapping(source));

            Assert.Equal(new BlockPos(3 + deltaX, 4 + deltaY, 5 + deltaZ), result.Position);
            Assert.Equal(new BrushSnappingState(u, v, 1, mode), result.RequestedSnapping);
            Assert.Equal(expected, result.ResolvedSnapping);
            Assert.Equal(valid ? expected : new BrushSnappingState(0, 0, 0, mode), result.AppliedSnapping);
            Assert.Equal(!valid ? BrushPlacementOutcome.InvalidPlacement
                : appliedU != 0 || appliedV != 0 ? BrushPlacementOutcome.AcceptedSnapping
                : BrushPlacementOutcome.OrdinaryPlacement, result.Outcome);
            Assert.Equal(valid, result.IsValid);
            inputs.Validator.Verify(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny),
                Times.Once);
            Assert.Equal(new BlockPos(3, 4, 5), source.Position);
            Assert.False(source.DidOffset);
        }
    }

    [Fact]
    public void Resolve_CoincidentPositions_DoNotDetermineOutcome()
    {
        BrushTestInputs inputs = new();
        BrushPlacementValidation resolver = new(inputs.Validator.Object);
        Block block = new();
        BlockSelection tangential = CreateSelection(BlockFacing.NORTH, 1, 0);
        BlockSelection ordinary = CreateSelection(BlockFacing.EAST, 1, 0);
        BrushPlacementResult snap = resolver.Resolve(block, tangential, EBuildBrushSnapping.Horizontal, new(tangential));
        BrushPlacementResult fallback = resolver.Resolve(block, ordinary, EBuildBrushSnapping.None, new(ordinary));
        inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny))
            .Returns(false);
        BrushPlacementResult invalid = resolver.Resolve(block, tangential, EBuildBrushSnapping.Horizontal, new(tangential));

        Assert.Equal(snap.Position, fallback.Position);
        Assert.Equal(snap.Position, invalid.Position);
        Assert.Equal(BrushPlacementOutcome.AcceptedSnapping, snap.Outcome);
        Assert.Equal(BrushPlacementOutcome.OrdinaryPlacement, fallback.Outcome);
        Assert.Equal(BrushPlacementOutcome.InvalidPlacement, invalid.Outcome);
        Assert.Equal(1, fallback.RequestedSnapping.FaceU);
        Assert.Equal(0, fallback.AppliedSnapping.FaceU);
        inputs.Validator.Verify(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny),
            Times.Exactly(3));
    }

    [Fact]
    public void Resolve_NoBlockAndMutatedPosition_PreservesNoRuleAndOwnershipContracts()
    {
        BrushTestInputs inputs = new();
        BlockSelection selection = CreateSelection(BlockFacing.NORTH, 1, -1);
        BrushPlacementResult result = new BrushPlacementValidation(inputs.Validator.Object)
            .Resolve(null, selection, EBuildBrushSnapping.Horizontal, new(selection));
        BlockPos position = result.Position;
        position.X = 99;
        selection.Position.Y = 99;

        Assert.True(result.IsValid);
        Assert.Equal(BrushPlacementOutcome.NoBlock, result.Outcome);
        Assert.Equal(new BlockPos(3, 4, 5), result.Position);
        Assert.Equal(0, result.AppliedSnapping.FaceU);
        Assert.Empty(inputs.Validator.Invocations);
    }

    internal static BlockSelection CreateSelection(BlockFacing face, int u, int v) => new()
    {
        Position = new BlockPos(3, 4, 5),
        Face = face,
        HitPosition = face.Axis switch
        {
            EnumAxis.X => new Vec3d((face.Normali.X + 1) * 0.5, 0.5 + v * 0.4, 0.5 + u * 0.4),
            EnumAxis.Y => new Vec3d(0.5 + u * 0.4, (face.Normali.Y + 1) * 0.5, 0.5 + v * 0.4),
            EnumAxis.Z => new Vec3d(0.5 + u * 0.4, 0.5 + v * 0.4, (face.Normali.Z + 1) * 0.5),
            _ => throw new InvalidOperationException()
        }
    };
}
