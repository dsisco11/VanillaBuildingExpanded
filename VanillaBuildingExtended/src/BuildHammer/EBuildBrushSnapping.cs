using System;

namespace VanillaBuildingExtended;

[Flags]
public enum EBuildBrushSnapping : byte
{
    None = 1 << 0,
    Horizontal = 1 << 1,
    Vertical = 1 << 2,
}
