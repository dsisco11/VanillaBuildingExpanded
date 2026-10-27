namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Determines which point stays anchored when numeric dimensions change.</summary>
internal enum WorkAreaResizeMode
{
    /// <summary>Keeps the minimum corner fixed and moves the maximum corner.</summary>
    Expansion,

    /// <summary>Keeps the center fixed, rounding half-block parity ties toward positive axes.</summary>
    Centered
}
