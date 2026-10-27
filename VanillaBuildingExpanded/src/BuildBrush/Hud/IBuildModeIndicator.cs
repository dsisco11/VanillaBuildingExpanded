using System;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Owns the non-focusing build panel's presentation and rendering resources.</summary>
internal interface IBuildModeIndicator : IDisposable
{
    void Update(BuildModeInfo info);

    /// <summary>Shows or hides the indicator without taking input focus.</summary>
    void SetVisible(bool visible);
}
