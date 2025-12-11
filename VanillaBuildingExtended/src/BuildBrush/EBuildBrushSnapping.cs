using System;

namespace VanillaBuildingExtended;

[Flags]
public enum EBuildBrushSnapping : byte
{
    None = 1 << 0,
    /// <summary> Snap to horizontal planes. </summary>
    Horizontal = 1 << 1,
    /// <summary> Snap to vertical planes. </summary>
    Vertical = 1 << 2,
    /// <summary> Brush is fixed in place and does not move with the player. </summary>
    Fixed = 1 << 3,
    /// <summary> When this flag is present, the brush placement is also offset along the face normal. </summary>
    ApplyFaceNormalOffset = 1 << 4
}

public static class EBuildBrushSnappingExtensions
{
    public static string GetCode(this EBuildBrushSnapping mode)
    {
        var sanitizedMode = mode & (EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical);
        return sanitizedMode switch
        {
            EBuildBrushSnapping.None => "none",
            EBuildBrushSnapping.Horizontal => "horizontal",
            EBuildBrushSnapping.Vertical => "vertical",
            EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical => "horizontal-vertical",
            _ => "unknown",
        };
    }
}

