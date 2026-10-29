using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushSnappingRestrictionTests
{
    public static IEnumerable<object[]> BottomSlabFaceEdges()
    {
        (double X, double Z)[] edges =
        [
            (0.5, 0.1), (0.5, 0.9),
            (0.1, 0.5), (0.9, 0.5),
            (0.1, 0.1), (0.1, 0.9),
            (0.9, 0.1), (0.9, 0.9)
        ];

        foreach (bool topFace in new[] { true, false })
        {
            foreach (EBuildBrushSnapping mode in new[]
            {
                EBuildBrushSnapping.Horizontal,
                EBuildBrushSnapping.Vertical,
                EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical,
                EBuildBrushSnapping.None
            })
            {
                foreach (var edge in edges)
                {
                    foreach (bool held in new[] { false, true })
                    {
                        yield return [topFace, mode, edge.X, edge.Z, held];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BottomSlabFaceEdges))]
    public void ResolvePosition_BottomSlabFaceEdges_RespectsRestrictionsAndFaceOffset(
        bool topFace, EBuildBrushSnapping mode, double hitX, double hitZ, bool held)
    {
        BlockSlab slab = new()
        {
            BlockId = 100,
            Code = new AssetLocation("game:cobblestoneslab-granite-down-free"),
            Replaceable = 200,
            CollisionBoxes = [new Cuboidf(0, 0, 0, 1, 0.5f, 1)],
            SelectionBoxes = [new Cuboidf(0, 0, 0, 1, 0.5f, 1)]
        };
        slab.VariantStrict["rot"] = "down";
        Block air = new() { BlockId = 0, Replaceable = 10000 };
        BlockSelection source = new()
        {
            Position = new BlockPos(5, 10, 15),
            Face = topFace ? BlockFacing.UP : BlockFacing.DOWN,
            HitPosition = new Vec3d(hitX, topFace ? 0.5 : 0, hitZ)
        };
        Mock<IBlockAccessor> blocks = new();
        blocks.Setup(b => b.GetBlock(It.IsAny<BlockPos>()))
            .Returns((BlockPos position) => position.Equals(source.Position) ? slab : air);
        IPlayer player = WorkAreaRenderingTests.CreatePlayer();
        Mock<ILandClaimAPI> claims = new();
        claims.Setup(c => c.TryAccess(player, It.IsAny<BlockPos>(), EnumBlockAccessFlags.BuildOrBreak))
            .Returns(true);
        Mock<IWorldAccessor> world = new();
        world.Setup(w => w.BlockAccessor).Returns(blocks.Object);
        world.Setup(w => w.Claims).Returns(claims.Object);
        world.Setup(w => w.GetIntersectingEntities(
                It.IsAny<BlockPos>(), It.IsAny<Cuboidf[]>(), It.IsAny<ActionConsumable<Entity>>()))
            .Returns(Array.Empty<Entity>());
        BrushPlacementValidation placement = new(new PlayerBrushPlacementValidator(world.Object, player));

        // All neighbors are air; the selected slab itself cannot be replaced by another bottom slab.
        Assert.False(slab.IsReplacableBy(slab));
        Assert.True(air.IsReplacableBy(slab));

        bool edgeSnapping = mode == EBuildBrushSnapping.Horizontal ||
            mode == (EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical);
        int deltaX = edgeSnapping ? Math.Sign(hitX - 0.5) : 0;
        int deltaZ = edgeSnapping ? Math.Sign(hitZ - 0.5) : 0;
        int deltaY = held || mode == EBuildBrushSnapping.Vertical || mode == EBuildBrushSnapping.None
            ? (topFace ? 1 : -1) : 0;
        if (held)
        {
            mode |= EBuildBrushSnapping.ApplyFaceNormalOffset;
        }

        BlockPos resolved = placement.ResolvePosition(slab, source, mode, new BrushSnapping(source), out bool valid);

        Assert.Equal(new BlockPos(5 + deltaX, 10 + deltaY, 15 + deltaZ), resolved);
        Assert.True(valid);
        Assert.NotEqual(source.Position, resolved);
        Assert.Equal(new BlockPos(5, 10, 15), source.Position);
        Assert.False(source.DidOffset);
    }
}
