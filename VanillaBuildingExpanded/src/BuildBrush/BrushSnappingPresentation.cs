using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushSnappingPresentation
{
    private readonly BlockSelection target;
    private readonly int? targetBlockId;

    public long Revision { get; }
    public BlockPos TargetPosition => target.Position.Copy();
    public Vec3d TargetHitPosition => target.HitPosition.Clone();
    public BlockFacing Face => target.Face;
    public int? TargetBlockId => targetBlockId;
    public BrushPlacementResult Result { get; }

    public BrushSnappingPresentation(BlockSelection target, BrushPlacementResult result, long revision)
    {
        this.target = target.Clone();
        targetBlockId = target.Block?.Id;
        Result = result;
        Revision = revision;
    }

    public bool Matches(BlockSelection selection, EBuildBrushSnapping mode, long revision) =>
        Revision == revision && Result.RequestedSnapping.Mode == mode &&
        target.Position == selection.Position && target.Face == selection.Face &&
        target.HitPosition.Equals(selection.HitPosition) &&
        target.SelectionBoxIndex == selection.SelectionBoxIndex &&
        targetBlockId == selection.Block?.Id;
}
