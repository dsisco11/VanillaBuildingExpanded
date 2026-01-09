namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Represents the rotation interval constraint for IRotatable block entities.
/// Corresponds to the "rotatatableInterval" attribute in block JSON definitions.
/// </summary>
public enum ERotatableInterval
{
    /// <summary>
    /// No rotation interval defined.
    /// </summary>
    None = 0,

    /// <summary>
    /// 22.5 degree rotation steps (16 orientations).
    /// </summary>
    Deg22_5,

    /// <summary>
    /// 22.5 degree rotation steps, but skipping 45° multiples (only 0°, 22.5°, 67.5°, 90°, etc.).
    /// Results in 8 orientations instead of 16.
    /// </summary>
    Deg22_5Not45,

    /// <summary>
    /// 45 degree rotation steps (8 orientations).
    /// </summary>
    Deg45,

    /// <summary>
    /// 90 degree rotation steps (4 orientations).
    /// </summary>
    Deg90,
}

/// <summary>
/// Extension methods for <see cref="ERotatableInterval"/>.
/// </summary>
public static class ERotatableIntervalExtensions
{
    /// <summary>
    /// Gets the base rotation step in degrees for this interval.
    /// </summary>
    public static float ToDegrees(this ERotatableInterval interval) => interval switch
    {
        ERotatableInterval.Deg22_5 => 22.5f,
        ERotatableInterval.Deg22_5Not45 => 22.5f,
        ERotatableInterval.Deg45 => 45f,
        ERotatableInterval.Deg90 => 90f,
        _ => 0f,
    };

    /// <summary>
    /// Parses a rotatatableInterval string from block attributes into the enum.
    /// </summary>
    public static ERotatableInterval Parse(string? intervalString) => intervalString switch
    {
        "22.5deg" => ERotatableInterval.Deg22_5,
        "22.5degnot45deg" => ERotatableInterval.Deg22_5Not45,
        "45deg" => ERotatableInterval.Deg45,
        "90deg" => ERotatableInterval.Deg90,
        _ => ERotatableInterval.None,
    };

    /// <summary>
    /// Checks if a given angle should be skipped for this interval.
    /// For <see cref="ERotatableInterval.Deg22_5Not45"/>, angles that are multiples of 45° are skipped.
    /// </summary>
    public static bool ShouldSkipAngle(this ERotatableInterval interval, float angleDegrees)
    {
        if (interval != ERotatableInterval.Deg22_5Not45)
            return false;

        // Skip angles that are multiples of 45° (0°, 45°, 90°, 135°, etc.)
        // Use modulo with small epsilon for floating point comparison
        float remainder = angleDegrees % 45f;
        return remainder < 0.01f || remainder > 44.99f;
    }

    /// <summary>
    /// Gets the total number of valid rotation steps for this interval.
    /// </summary>
    public static int GetStepCount(this ERotatableInterval interval) => interval switch
    {
        ERotatableInterval.Deg22_5 => 16,       // 360 / 22.5 = 16
        ERotatableInterval.Deg22_5Not45 => 12,   // 16 steps minus 4 that are aligned to 45° axis angles
        ERotatableInterval.Deg45 => 8,          // 360 / 45 = 8
        ERotatableInterval.Deg90 => 4,          // 360 / 90 = 4
        _ => 0,
    };
}
