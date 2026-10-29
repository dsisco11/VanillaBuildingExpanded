using System.Numerics;
using System.Runtime.CompilerServices;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded;

/// <summary>
/// Used to track and compare snapping state changes.
/// </summary>
public readonly record struct BrushSnappingState(int FaceU = 0, int FaceV = 0, int FaceW = 0, EBuildBrushSnapping Mode = EBuildBrushSnapping.None);

/// <summary>
/// Represents face-local snapping directions, with U/V in the face plane and W along its outward normal.
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
    /// <summary> The outward face-local W direction. Raycast selections use 1, including on negative-facing normals. </summary>
    public readonly int FaceW;
    #endregion

    #region Constructors
    public BrushSnapping(in BlockSelectionRay selectionRay)
    {
        this.Selection = selectionRay;
        this.FaceW = 1;

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
    internal readonly (int U, int V) GetTangentialDirections(EBuildBrushSnapping mode)
    {
        bool noSnapping = mode.HasFlag(EBuildBrushSnapping.None);
        return GetTangentialDirections(!noSnapping && mode.HasFlag(EBuildBrushSnapping.Horizontal),
            !noSnapping && mode.HasFlag(EBuildBrushSnapping.Vertical));
    }

    private readonly (int U, int V) GetTangentialDirections(bool horizontal, bool vertical) =>
        (horizontal ? FaceU : 0, (Selection.Face.Axis == EnumAxis.Y ? horizontal : vertical) ? FaceV : 0);

    /// <summary>
    /// Resolves a candidate using world-space restrictions. W supplies outward placement when no permitted
    /// U/V movement exists; the explicit face-offset modifier adds W independently of those restrictions.
    /// </summary>
    /// <param name="snappingMode"></param>
    /// <returns></returns>
    public readonly BlockPos ResolvePosition(EBuildBrushSnapping snappingMode)
        => ResolvePosition(snappingMode, out _);

    public readonly BlockPos ResolvePosition(EBuildBrushSnapping snappingMode, out BrushSnappingState applied)
    {
        bool noSnapping = snappingMode.HasFlag(EBuildBrushSnapping.None);
        bool horizontal = !noSnapping && snappingMode.HasFlag(EBuildBrushSnapping.Horizontal);
        bool vertical = !noSnapping && snappingMode.HasFlag(EBuildBrushSnapping.Vertical);
        bool explicitFaceOffset = snappingMode.HasFlag(EBuildBrushSnapping.ApplyFaceNormalOffset);
        FastVec3i u = GetFaceUOffset(Selection.Face.Axis);
        FastVec3i v = GetFaceVOffset(Selection.Face.Axis);
        FastVec3i offset = new(horizontal ? u.X + v.X : 0, vertical ? u.Y + v.Y : 0, horizontal ? u.Z + v.Z : 0);
        (int appliedU, int appliedV) = GetTangentialDirections(horizontal, vertical);
        int appliedW = 0;

        if (explicitFaceOffset || ((noSnapping || horizontal || vertical) && offset.X == 0 && offset.Y == 0 && offset.Z == 0))
        {
            FastVec3i w = GetFaceWOffset();
            offset.X += explicitFaceOffset || noSnapping || horizontal ? w.X : 0;
            offset.Y += explicitFaceOffset || noSnapping || vertical ? w.Y : 0;
            offset.Z += explicitFaceOffset || noSnapping || horizontal ? w.Z : 0;
            bool normalPermitted = Selection.Face.Axis == EnumAxis.Y ? vertical : horizontal;
            appliedW = explicitFaceOffset || noSnapping || normalPermitted ? FaceW : 0;
        }

        applied = new BrushSnappingState(appliedU, appliedV, appliedW, snappingMode);
        return Selection.Position.Copy().Add(offset);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly FastVec3i GetFaceWOffset()
    {
        Vec3i normal = Selection.Face.Normali;
        return new(normal.X * FaceW, normal.Y * FaceW, normal.Z * FaceW);
    }
    #endregion
}
