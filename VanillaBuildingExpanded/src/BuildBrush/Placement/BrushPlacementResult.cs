using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal enum BrushPlacementOutcome
{
    NoBlock,
    InvalidPlacement,
    OrdinaryPlacement,
    AcceptedSnapping
}

internal sealed class BrushPlacementResult
{
    private readonly BlockPos position;
    private readonly bool hasBlock;

    public BlockPos Position => position.Copy();
    public BrushSnappingState RequestedSnapping { get; }
    public BrushSnappingState ResolvedSnapping { get; }
    public bool IsValid { get; }
    public BrushSnappingState AppliedSnapping => IsValid && hasBlock
        ? ResolvedSnapping
        : new BrushSnappingState(0, 0, 0, RequestedSnapping.Mode);
    public BrushPlacementOutcome Outcome => !hasBlock ? BrushPlacementOutcome.NoBlock
        : !IsValid ? BrushPlacementOutcome.InvalidPlacement
        : ResolvedSnapping.FaceU != 0 || ResolvedSnapping.FaceV != 0
            ? BrushPlacementOutcome.AcceptedSnapping
            : BrushPlacementOutcome.OrdinaryPlacement;

    public BrushPlacementResult(BlockPos position, BrushSnappingState requestedSnapping,
        BrushSnappingState resolvedSnapping, bool hasBlock, bool isValid)
    {
        this.position = position.Copy();
        RequestedSnapping = requestedSnapping;
        ResolvedSnapping = resolvedSnapping;
        this.hasBlock = hasBlock;
        IsValid = isValid;
    }

    public BrushPlacementResult WithValidity(bool isValid) =>
        new(position, RequestedSnapping, ResolvedSnapping, hasBlock, isValid);
}
