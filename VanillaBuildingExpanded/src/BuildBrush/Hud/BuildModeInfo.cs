namespace VanillaBuildingExpanded.BuildBrush;

internal readonly record struct BuildModeInfo(
    bool IsSuspended,
    EBuildBrushSnapping Snapping,
    int OrientationIndex,
    int OrientationCount,
    float? RotationAngleDegrees)
{
    public static BuildModeInfo FromBrush(BuildBrushInstance brush)
    {
        bool suspended = !brush.IsValidPlacementBlock(brush.BlockUntransformed);
        bool canRotate = !suspended && brush.Rotation?.CanRotate == true;
        return new BuildModeInfo(
            suspended,
            brush.Snapping,
            canRotate ? brush.OrientationIndex : 0,
            canRotate ? brush.OrientationCount : 0,
            canRotate && brush.RotationMode == EBuildBrushRotationMode.Rotatable
                ? brush.RotationAngleDegrees : null);
    }
}
