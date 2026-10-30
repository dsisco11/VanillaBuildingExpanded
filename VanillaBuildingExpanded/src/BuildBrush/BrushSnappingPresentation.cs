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
    public bool ShowsSnappingRegions => Result.Outcome != BrushPlacementOutcome.NoBlock
        && !Result.RequestedSnapping.Mode.HasFlag(EBuildBrushSnapping.None)
        && (Result.RequestedSnapping.Mode & (EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical)) != 0;

    public BrushSnappingPresentation(BlockSelection target, BrushPlacementResult result, long revision)
    {
        this.target = target.Clone();
        targetBlockId = target.Block?.Id;
        Result = result;
        Revision = revision;
    }

    public bool Matches(BlockSelection selection, EBuildBrushSnapping mode, long revision)
    {
        if (Revision != revision || Result.RequestedSnapping.Mode != mode
            || target.Position != selection.Position || target.Face != selection.Face
            || targetBlockId != selection.Block?.Id) return false;
        Vec3d hit = selection.HitPosition;
        if (!double.IsFinite(hit.X) || !double.IsFinite(hit.Y) || !double.IsFinite(hit.Z)) return false;
        var directions = new BrushSnapping(selection).GetTangentialDirections(mode);
        return directions.U == Result.ResolvedSnapping.FaceU && directions.V == Result.ResolvedSnapping.FaceV;
    }
}
