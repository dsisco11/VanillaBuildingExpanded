using System;

namespace VanillaBuildingExtended;

[Flags]
public enum EBuildBrushSnapping : byte
{
    None = 1 << 0,
    Horizontal = 1 << 1,
    Vertical = 1 << 2,
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

