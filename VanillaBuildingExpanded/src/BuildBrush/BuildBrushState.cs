using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Pure brush state container (no side effects or API-side branching).
/// </summary>
public sealed class BuildBrushState
{
    #region Properties
    public bool IsActive { get; private set; }
    public int OrientationIndex { get; private set; }
    public BlockSelection? Selection { get; private set; }
    public BlockPos? Position { get; private set; }
    public EBuildBrushSnapping Snapping { get; private set; }
    public int? BlockId { get; private set; }
    public long LastAppliedSeq { get; private set; }
    #endregion

    #region Public
    public void Apply(
        bool isActive,
        int orientationIndex,
        BlockSelection? selection,
        BlockPos? position,
        EBuildBrushSnapping snapping,
        int? blockId = null,
        long? lastAppliedSeq = null)
    {
        IsActive = isActive;
        OrientationIndex = orientationIndex;
        Selection = selection?.Clone();
        Position = position?.Copy();
        Snapping = snapping;
        if (blockId.HasValue)
        {
            BlockId = blockId.Value;
        }
        if (lastAppliedSeq.HasValue)
        {
            LastAppliedSeq = lastAppliedSeq.Value;
        }
    }
    #endregion
}
