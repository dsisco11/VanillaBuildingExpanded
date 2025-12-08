using System.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExtended;

public record struct BlockSelectionRay
{
    /// <summary>
    /// The position of the block that was hit.
    /// </summary>
    public readonly BlockPos Position;
    /// <summary>
    /// The face of the block that was hit.
    /// </summary>
    public readonly BlockFacing Face;
    /// <summary>
    /// The Relative hit position within the block (from 0.0 to 1.0 on each axis).
    /// </summary>
    public readonly Vector3 HitPosition;

    public BlockSelectionRay(in BlockSelection blockSelection)
    {
        Position = blockSelection.Position.Copy();
        Face = blockSelection.Face;
        HitPosition = blockSelection.HitPosition.ToSNT();
    }

    public BlockSelectionRay(in BlockPos position, in BlockFacing face, Vector3 hitPosition)
    {
        Position = position.Copy();
        Face = face;
        HitPosition = hitPosition;
    }
}
