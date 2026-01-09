using VanillaBuildingExpanded.BuildHammer;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Unit tests for <see cref="ERotatableInterval"/> and <see cref="ERotatableIntervalExtensions"/>.
/// </summary>
public class ERotatableIntervalTests
{
    #region Parse Tests

    [Theory]
    [InlineData("22.5deg", ERotatableInterval.Deg22_5)]
    [InlineData("22.5degnot45deg", ERotatableInterval.Deg22_5Not45)]
    [InlineData("45deg", ERotatableInterval.Deg45)]
    [InlineData("90deg", ERotatableInterval.Deg90)]
    public void Parse_ValidString_ReturnsCorrectInterval(string input, ERotatableInterval expected)
    {
        var result = ERotatableIntervalExtensions.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("180deg")]
    [InlineData("22.5")]
    public void Parse_InvalidString_ReturnsNone(string? input)
    {
        var result = ERotatableIntervalExtensions.Parse(input);
        Assert.Equal(ERotatableInterval.None, result);
    }

    #endregion

    #region ToDegrees Tests

    [Theory]
    [InlineData(ERotatableInterval.None, 0f)]
    [InlineData(ERotatableInterval.Deg22_5, 22.5f)]
    [InlineData(ERotatableInterval.Deg22_5Not45, 22.5f)]
    [InlineData(ERotatableInterval.Deg45, 45f)]
    [InlineData(ERotatableInterval.Deg90, 90f)]
    public void ToDegrees_ReturnsCorrectValue(ERotatableInterval interval, float expected)
    {
        var result = interval.ToDegrees();
        Assert.Equal(expected, result);
    }

    #endregion

    #region GetStepCount Tests

    [Theory]
    [InlineData(ERotatableInterval.None, 0)]
    [InlineData(ERotatableInterval.Deg22_5, 16)]
    [InlineData(ERotatableInterval.Deg22_5Not45, 12)]
    [InlineData(ERotatableInterval.Deg45, 8)]
    [InlineData(ERotatableInterval.Deg90, 4)]
    public void GetStepCount_ReturnsCorrectCount(ERotatableInterval interval, int expected)
    {
        var result = interval.GetStepCount();
        Assert.Equal(expected, result);
    }

    #endregion

    #region ShouldSkipAngle Tests - Deg22_5Not45

    [Theory]
    [InlineData(45f)]
    [InlineData(135f)]
    [InlineData(225f)]
    [InlineData(315f)]
    public void ShouldSkipAngle_Deg22_5Not45_Diagonal45Angles_ReturnsTrue(float angle)
    {
        var result = ERotatableInterval.Deg22_5Not45.ShouldSkipAngle(angle);
        Assert.True(result, $"Expected angle {angle}° to be skipped for Deg22_5Not45");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(22.5f)]
    [InlineData(67.5f)]
    [InlineData(90f)]
    [InlineData(112.5f)]
    [InlineData(157.5f)]
    [InlineData(180f)]
    [InlineData(202.5f)]
    [InlineData(247.5f)]
    [InlineData(270f)]
    [InlineData(292.5f)]
    [InlineData(337.5f)]
    public void ShouldSkipAngle_Deg22_5Not45_NonDiagonalAngles_ReturnsFalse(float angle)
    {
        var result = ERotatableInterval.Deg22_5Not45.ShouldSkipAngle(angle);
        Assert.False(result, $"Expected angle {angle}° to NOT be skipped for Deg22_5Not45");
    }

    #endregion

    #region ShouldSkipAngle Tests - Other Intervals

    [Theory]
    [InlineData(ERotatableInterval.None)]
    [InlineData(ERotatableInterval.Deg22_5)]
    [InlineData(ERotatableInterval.Deg45)]
    [InlineData(ERotatableInterval.Deg90)]
    public void ShouldSkipAngle_OtherIntervals_NeverSkips(ERotatableInterval interval)
    {
        // Test various angles - none should be skipped for intervals other than Deg22_5Not45
        float[] testAngles = [0f, 22.5f, 45f, 67.5f, 90f, 180f, 270f];

        foreach (float angle in testAngles)
        {
            var result = interval.ShouldSkipAngle(angle);
            Assert.False(result, $"Expected angle {angle}° to NOT be skipped for {interval}");
        }
    }

    #endregion

    #region ShouldSkipAngle Edge Cases

    [Theory]
    [InlineData(0.001f, false)]     // Very close to 0°
    [InlineData(44.999f, true)]     // Very close to 45°
    [InlineData(89.999f, false)]    // Very close to 90°
    [InlineData(22.499f, false)]    // Just below 22.5°
    [InlineData(22.501f, false)]    // Just above 22.5°
    public void ShouldSkipAngle_Deg22_5Not45_EdgeCases_HandlesFloatingPointCorrectly(float angle, bool expectedSkip)
    {
        var result = ERotatableInterval.Deg22_5Not45.ShouldSkipAngle(angle);
        Assert.Equal(expectedSkip, result);
    }

    #endregion
}
