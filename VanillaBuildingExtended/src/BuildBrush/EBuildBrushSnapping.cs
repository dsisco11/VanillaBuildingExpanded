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
}

public static class EBuildBrushSnappingExtensions
{
    public static string GetCode(this EBuildBrushSnapping mode)
    {
        return mode switch
        {
            EBuildBrushSnapping.None => "none",
            EBuildBrushSnapping.Horizontal => "horizontal",
            EBuildBrushSnapping.Vertical => "vertical",
            EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical => "horizontal-vertical",
            _ => "Unknown",
        };
    }
}

