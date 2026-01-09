using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;

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
    /// 22.5 degree rotation steps, but skipping 45° multiples (only 22.5°, 67.5°, 112.5°, etc.).
    /// Results in 12 orientations instead of 16.
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
    #region Predefined Rotation Angle Tables

    /// <summary>
    /// Valid rotation angles for 90° interval (4 orientations).
    /// </summary>
    public static readonly ImmutableArray<float> Angles90Deg = [0f, 90f, 180f, 270f];

    /// <summary>
    /// Valid rotation angles for 45° interval (8 orientations).
    /// </summary>
    public static readonly ImmutableArray<float> Angles45Deg = [0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f];

    /// <summary>
    /// Valid rotation angles for 22.5° interval (16 orientations).
    /// </summary>
    public static readonly ImmutableArray<float> Angles22_5Deg = [
        0f, 22.5f, 45f, 67.5f, 90f, 112.5f, 135f, 157.5f,
        180f, 202.5f, 225f, 247.5f, 270f, 292.5f, 315f, 337.5f
    ];

    /// <summary>
    /// Valid rotation angles for 22.5° interval skipping 45° multiples (12 orientations).
    /// Excludes: 0°, 90°, 180°, 270° (the cardinal directions handled by variants).
    /// </summary>
    public static readonly ImmutableArray<float> Angles22_5DegNot45 = [
        22.5f, 45f, 67.5f, 112.5f, 135f, 157.5f,
        202.5f, 225f, 247.5f, 292.5f, 315f, 337.5f
    ];

    /// <summary>
    /// Lookup table mapping each interval to its valid rotation angles.
    /// </summary>
    private static readonly FrozenDictionary<ERotatableInterval, ImmutableArray<float>> AngleLookup =
        new Dictionary<ERotatableInterval, ImmutableArray<float>>
        {
            [ERotatableInterval.None] = [],
            [ERotatableInterval.Deg90] = Angles90Deg,
            [ERotatableInterval.Deg45] = Angles45Deg,
            [ERotatableInterval.Deg22_5] = Angles22_5Deg,
            [ERotatableInterval.Deg22_5Not45] = Angles22_5DegNot45,
        }.ToFrozenDictionary();

    #endregion

    /// <summary>
    /// Gets the predefined array of valid rotation angles for this interval.
    /// </summary>
    /// <param name="interval">The rotation interval.</param>
    /// <returns>Immutable array of valid angles in degrees.</returns>
    public static ImmutableArray<float> GetValidAngles(this ERotatableInterval interval)
    {
        return AngleLookup.TryGetValue(interval, out var angles) ? angles : [];
    }

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
    /// Checks if a given angle is valid for this interval.
    /// Uses the predefined lookup tables for O(n) lookup where n is small (max 16).
    /// </summary>
    public static bool IsValidAngle(this ERotatableInterval interval, float angleDegrees)
    {
        var validAngles = interval.GetValidAngles();
        if (validAngles.IsDefaultOrEmpty)
            return false;

        // Use epsilon comparison for floating point
        const float epsilon = 0.01f;
        foreach (var angle in validAngles)
        {
            if (Math.Abs(angle - angleDegrees) < epsilon)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a given angle should be skipped for this interval.
    /// For <see cref="ERotatableInterval.Deg22_5Not45"/>, angles that are multiples of 90° are skipped.
    /// </summary>
    public static bool ShouldSkipAngle(this ERotatableInterval interval, float angleDegrees)
    {
        return !interval.IsValidAngle(angleDegrees);
    }

    /// <summary>
    /// Gets the total number of valid rotation steps for this interval.
    /// </summary>
    public static int GetStepCount(this ERotatableInterval interval)
    {
        return interval.GetValidAngles().Length;
    }
}
