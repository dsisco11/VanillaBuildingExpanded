using System;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Owns the passive indicator's presentation and rendering resources.</summary>
internal interface IBuildModeIndicator : IDisposable
{
    /// <summary>Shows or hides the indicator without taking input focus.</summary>
    void SetVisible(bool visible);
}
