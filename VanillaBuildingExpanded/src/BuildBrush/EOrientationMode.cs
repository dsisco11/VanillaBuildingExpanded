namespace VanillaBuildingExpanded;

/// <summary>
/// Defines how block orientation/rotation is handled for placement.
/// </summary>
public enum EOrientationMode : byte
{
    /// <summary>
    /// Block has no orientation variants or rotation support.
    /// </summary>
    None = 0,

    /// <summary>
    /// Block uses code variants for orientation (e.g., doors, fences with "horizontalorientation" variant).
    /// Orientation is changed by cycling through <see cref="BuildHammer.BuildBrushInstance.OrientationVariants"/>.
    /// </summary>
    Static = 1,

    /// <summary>
    /// Block uses block entity to store rotation angle (e.g., chests, containers with <see cref="Vintagestory.API.Common.IRotatable"/>).
    /// Orientation is changed by adjusting <see cref="BuildHammer.BuildBrushInstance.RotationY"/> in radians.
    /// </summary>
    Dynamic = 2,
}

public static class EOrientationModeExtensions
{
    public static string GetCode(this EOrientationMode mode)
    {
        return mode switch
        {
            EOrientationMode.None => "none",
            EOrientationMode.Static => "static",
            EOrientationMode.Dynamic => "dynamic",
            _ => "unknown",
        };
    }
}
