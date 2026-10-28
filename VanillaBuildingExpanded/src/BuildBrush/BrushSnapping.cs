using System.Numerics;
using System.Runtime.CompilerServices;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded;

/// <summary>
/// Used to track and compare snapping state changes.
/// </summary>
public readonly record struct BrushSnappingState(int FaceU = 0, int FaceV = 0, EBuildBrushSnapping Mode = EBuildBrushSnapping.None);

/// <summary>
/// Represents face-local snapping directions resolved from the block face quadrants hit by a ray-trace.
/// </summary>
public readonly record struct BrushSnapping
{
    #region Fields
    /// <summary> The threshold value to determine if the hit position is considered centered or towards an edge. </summary>
    private const float CenterThreshold = 0.15f;
    /// <summary> The original block selection ray used to determine the snapping directions. </summary>
    public readonly BlockSelectionRay Selection;
    /// <summary> The face-local U snapping direction: -1, 0, or 1. Maps to Z on X-normal faces, otherwise X. </summary>
    public readonly int FaceU;
    /// <summary> The face-local V snapping direction: -1, 0, or 1. Maps to Z on Y-normal faces, otherwise Y. </summary>
    public readonly int FaceV;
    #endregion

    #region Constructors
    public BrushSnapping(in BlockSelectionRay selectionRay)
    {
        this.Selection = selectionRay;

        // Use System Vectors for better performance.
        Vector3 faceNormal = selectionRay.Face.Normalf.ToSNT();
        Vector3 faceCenterPoint = (faceNormal * 0.5f) + new Vector3(0.5f);
        Vector3 faceRelativeHitPos = selectionRay.HitPosition - faceCenterPoint;
        Vector2 hitPos = selectionRay.Face.ToAB(faceRelativeHitPos);

        this.FaceU = hitPos.X switch
        {
            > CenterThreshold => 1,
            < -CenterThreshold => -1,
            _ => 0,
        };

        this.FaceV = hitPos.Y switch
        {
            > CenterThreshold => 1,
            < -CenterThreshold => -1,
            _ => 0,
        };
    }

    public BrushSnapping(in BlockSelection selection) : this(new BlockSelectionRay(selection)) { }
    #endregion

    #region Methods
    /// <summary>
    /// Resolves the final block position based on the snapping directions and the provided snapping mode flags.
    /// </summary>
    /// <param name="snappingMode"></param>
    /// <returns></returns>
    public readonly BlockPos ResolvePosition(EBuildBrushSnapping snappingMode)
    {
        BlockPos resolved = Selection.Position.Copy();
        if (snappingMode == EBuildBrushSnapping.None || snappingMode.HasFlag(EBuildBrushSnapping.ApplyFaceNormalOffset))
        {
            resolved.Add(Selection.Face.Normali);
        }

        if (snappingMode.HasFlag(EBuildBrushSnapping.Horizontal))
        {
            FastVec3i faceUOffset = GetFaceUOffset(Selection.Face.Axis);
            resolved.Add(faceUOffset);
        }

        if (snappingMode.HasFlag(EBuildBrushSnapping.Vertical))
        {
            FastVec3i faceVOffset = GetFaceVOffset(Selection.Face.Axis);
            resolved.Add(faceVOffset);
        }

        return resolved;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly FastVec3i GetFaceUOffset(EnumAxis axis)
    {
        return axis switch
        {
            EnumAxis.X => new(0, 0, FaceU),
            EnumAxis.Y => new(FaceU, 0, 0),
            EnumAxis.Z => new(FaceU, 0, 0),
            _ => new FastVec3i(0, 0, 0),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly FastVec3i GetFaceVOffset(EnumAxis axis)
    {
        return axis switch
        {
            EnumAxis.X => new(0, FaceV, 0),
            EnumAxis.Y => new(0, 0, FaceV),
            EnumAxis.Z => new(0, FaceV, 0),
            _ => new FastVec3i(0, 0, 0),
        };
    }
    #endregion
}
