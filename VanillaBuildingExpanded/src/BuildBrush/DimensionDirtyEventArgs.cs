using System;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Event args for dimension dirty events.
/// </summary>
public class DimensionDirtyEventArgs : EventArgs
{
    /// <summary>
    /// The reason the dimension was marked dirty.
    /// </summary>
    public string Reason { get; }

    public DimensionDirtyEventArgs(string reason = "")
    {
        Reason = reason;
    }
}
