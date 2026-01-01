using System.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for <see cref="BrushSnapping"/> and <see cref="BrushSnappingState"/>.
/// Verifies snapping direction calculations and position resolution.
/// </summary>
public class BrushSnappingTests
{
    #region Constants

    /// <summary>
    /// The threshold value used in BrushSnapping for determining centered vs edge positions.
    /// Must match the CenterThreshold constant in BrushSnapping.
    /// </summary>
    private const float CenterThreshold = 0.15f;

    #endregion

    #region BrushSnappingState Tests

    [Fact]
    public void BrushSnappingState_NewWithoutArgs_HasZeroValues()
    {
        // Act - record struct with `new()` doesn't apply default parameter values
        // This is standard C# behavior for value types
        var state = new BrushSnappingState();

        // Assert - all values are zeroed (Mode = 0, not EBuildBrushSnapping.None which is 1)
        Assert.Equal(0, state.Horizontal);
        Assert.Equal(0, state.Vertical);
        Assert.Equal((EBuildBrushSnapping)0, state.Mode);
    }

    [Fact]
    public void BrushSnappingState_DefaultKeyword_HasZeroValues()
    {
        // Act - default struct (all bytes zeroed)
        var state = default(BrushSnappingState);

        // Assert - default struct has all zero values
        Assert.Equal(0, state.Horizontal);
        Assert.Equal(0, state.Vertical);
        Assert.Equal((EBuildBrushSnapping)0, state.Mode);
    }

    [Theory]
    [InlineData(-1, 1, EBuildBrushSnapping.Horizontal)]
    [InlineData(0, -1, EBuildBrushSnapping.Vertical)]
    [InlineData(1, 0, EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical)]
    public void BrushSnappingState_WithValues_StoresCorrectly(int horizontal, int vertical, EBuildBrushSnapping mode)
    {
        // Act
        var state = new BrushSnappingState(horizontal, vertical, mode);

        // Assert
        Assert.Equal(horizontal, state.Horizontal);
        Assert.Equal(vertical, state.Vertical);
        Assert.Equal(mode, state.Mode);
    }

    [Fact]
    public void BrushSnappingState_Equality_WorksCorrectly()
    {
        // Arrange
        var state1 = new BrushSnappingState(1, -1, EBuildBrushSnapping.Horizontal);
        var state2 = new BrushSnappingState(1, -1, EBuildBrushSnapping.Horizontal);
        var state3 = new BrushSnappingState(0, -1, EBuildBrushSnapping.Horizontal);

        // Assert
        Assert.Equal(state1, state2);
        Assert.NotEqual(state1, state3);
    }

    #endregion

    #region BrushSnapping Constructor Tests - BlockSelection Overload

    [Fact]
    public void Constructor_FromBlockSelection_CreatesCorrectSnapping()
    {
        // Arrange - create a mock BlockSelection
        var blockSelection = new BlockSelection
        {
            Position = new BlockPos(5, 10, 15),
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.9, 1.0, 0.1) // High X, low Z
        };

        // Act
        var snapping = new BrushSnapping(blockSelection);

        // Assert - verifies constructor delegates to BlockSelectionRay constructor correctly
        Assert.Equal(5, snapping.Selection.Position.X);
        Assert.Equal(10, snapping.Selection.Position.Y);
        Assert.Equal(15, snapping.Selection.Position.Z);
        Assert.Equal(BlockFacing.UP, snapping.Selection.Face);
        Assert.Equal(1, snapping.Horizontal);  // High X = positive horizontal for Y-axis
        Assert.Equal(-1, snapping.Vertical);   // Low Z = negative vertical for Y-axis
    }

    #endregion

    #region BrushSnapping Constructor Tests - Face X Axis

    [Fact]
    public void Constructor_FaceXAxis_CenterHit_BothDirectionsZero()
    {
        // Arrange - hit at center of face (0.5, 0.5, 0.5)
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.EAST; // X-axis
        var hitPosition = new Vector3(1.0f, 0.5f, 0.5f); // Face at x=1

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(0, snapping.Horizontal);
        Assert.Equal(0, snapping.Vertical);
    }

    [Fact]
    public void Constructor_FaceXAxis_TopRightHit_BothDirectionsPositive()
    {
        // Arrange - hit at top-right corner of EAST face
        // For X-axis face, horizontal=Z, vertical=Y
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.EAST;
        var hitPosition = new Vector3(1.0f, 0.9f, 0.9f); // High Y and high Z

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(1, snapping.Horizontal); // High Z = positive horizontal
        Assert.Equal(1, snapping.Vertical);   // High Y = positive vertical
    }

    [Fact]
    public void Constructor_FaceXAxis_BottomLeftHit_BothDirectionsNegative()
    {
        // Arrange - hit at bottom-left corner of EAST face
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.EAST;
        var hitPosition = new Vector3(1.0f, 0.1f, 0.1f); // Low Y and low Z

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(-1, snapping.Horizontal); // Low Z = negative horizontal
        Assert.Equal(-1, snapping.Vertical);   // Low Y = negative vertical
    }

    #endregion

    #region BrushSnapping Constructor Tests - Face Y Axis

    [Fact]
    public void Constructor_FaceYAxis_CenterHit_BothDirectionsZero()
    {
        // Arrange - hit at center of UP face
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.UP; // Y-axis
        var hitPosition = new Vector3(0.5f, 1.0f, 0.5f);

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(0, snapping.Horizontal);
        Assert.Equal(0, snapping.Vertical);
    }

    [Fact]
    public void Constructor_FaceYAxis_CornerHit_CorrectDirections()
    {
        // Arrange - hit at corner of UP face
        // For Y-axis face, horizontal=X, vertical=Z
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.UP;
        var hitPosition = new Vector3(0.9f, 1.0f, 0.1f); // High X, low Z

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(1, snapping.Horizontal);  // High X = positive horizontal
        Assert.Equal(-1, snapping.Vertical);   // Low Z = negative vertical
    }

    #endregion

    #region BrushSnapping Constructor Tests - Face Z Axis

    [Fact]
    public void Constructor_FaceZAxis_CenterHit_BothDirectionsZero()
    {
        // Arrange - hit at center of SOUTH face
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.SOUTH; // Z-axis
        var hitPosition = new Vector3(0.5f, 0.5f, 1.0f);

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(0, snapping.Horizontal);
        Assert.Equal(0, snapping.Vertical);
    }

    [Fact]
    public void Constructor_FaceZAxis_CornerHit_CorrectDirections()
    {
        // Arrange - hit at corner of SOUTH face
        // For Z-axis face, horizontal=X, vertical=Y
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.SOUTH;
        var hitPosition = new Vector3(0.1f, 0.9f, 1.0f); // Low X, high Y

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(-1, snapping.Horizontal); // Low X = negative horizontal
        Assert.Equal(1, snapping.Vertical);    // High Y = positive vertical
    }

    #endregion

    #region BrushSnapping Constructor Tests - Threshold Boundary

    [Theory]
    [InlineData(0.5f + CenterThreshold - 0.01f, 0)]  // Just inside center
    [InlineData(0.5f + CenterThreshold + 0.01f, 1)]  // Just outside center (positive)
    [InlineData(0.5f - CenterThreshold + 0.01f, 0)]  // Just inside center
    [InlineData(0.5f - CenterThreshold - 0.01f, -1)] // Just outside center (negative)
    public void Constructor_ThresholdBoundary_CorrectSnapping(float hitZ, int expectedHorizontal)
    {
        // Arrange - Test threshold boundary on X-axis face where horizontal maps to Z
        var position = new BlockPos(0, 0, 0);
        var face = BlockFacing.EAST;
        var hitPosition = new Vector3(1.0f, 0.5f, hitZ);

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(expectedHorizontal, snapping.Horizontal);
    }

    #endregion

    #region ResolvePosition Tests - None Mode

    [Fact]
    public void ResolvePosition_NoneMode_AddsFaceNormalOffset()
    {
        // Arrange
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.EAST;
        var hitPosition = new Vector3(1.0f, 0.5f, 0.5f);
        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.None);

        // Assert - Position should be offset by face normal (1, 0, 0) for EAST
        Assert.Equal(6, resolved.X);
        Assert.Equal(10, resolved.Y);
        Assert.Equal(15, resolved.Z);
    }

    [Fact]
    public void ResolvePosition_ApplyFaceNormalOffset_AddsFaceNormalOffset()
    {
        // Arrange
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.UP;
        var hitPosition = new Vector3(0.5f, 1.0f, 0.5f);
        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.ApplyFaceNormalOffset);

        // Assert - Position should be offset by face normal (0, 1, 0) for UP
        Assert.Equal(5, resolved.X);
        Assert.Equal(11, resolved.Y);
        Assert.Equal(15, resolved.Z);
    }

    #endregion

    #region ResolvePosition Tests - Horizontal Mode

    [Fact]
    public void ResolvePosition_HorizontalMode_XAxisFace_AddsZOffset()
    {
        // Arrange - Hit right side of EAST face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.EAST;
        var hitPosition = new Vector3(1.0f, 0.5f, 0.9f); // High Z

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.Horizontal);

        // Assert - Horizontal snapping on X-axis adds Z offset
        Assert.Equal(5, resolved.X);
        Assert.Equal(10, resolved.Y);
        Assert.Equal(16, resolved.Z); // +1 from horizontal snapping
    }

    [Fact]
    public void ResolvePosition_HorizontalMode_YAxisFace_AddsXOffset()
    {
        // Arrange - Hit right side of UP face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.UP;
        var hitPosition = new Vector3(0.9f, 1.0f, 0.5f); // High X

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.Horizontal);

        // Assert - Horizontal snapping on Y-axis adds X offset
        Assert.Equal(6, resolved.X); // +1 from horizontal snapping
        Assert.Equal(10, resolved.Y);
        Assert.Equal(15, resolved.Z);
    }

    [Fact]
    public void ResolvePosition_HorizontalMode_ZAxisFace_AddsXOffset()
    {
        // Arrange - Hit left side of SOUTH face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.SOUTH;
        var hitPosition = new Vector3(0.1f, 0.5f, 1.0f); // Low X

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.Horizontal);

        // Assert - Horizontal snapping on Z-axis adds X offset
        Assert.Equal(4, resolved.X); // -1 from horizontal snapping
        Assert.Equal(10, resolved.Y);
        Assert.Equal(15, resolved.Z);
    }

    #endregion

    #region ResolvePosition Tests - Vertical Mode

    [Fact]
    public void ResolvePosition_VerticalMode_XAxisFace_AddsYOffset()
    {
        // Arrange - Hit top of EAST face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.EAST;
        var hitPosition = new Vector3(1.0f, 0.9f, 0.5f); // High Y

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.Vertical);

        // Assert - Vertical snapping on X-axis adds Y offset
        Assert.Equal(5, resolved.X);
        Assert.Equal(11, resolved.Y); // +1 from vertical snapping
        Assert.Equal(15, resolved.Z);
    }

    [Fact]
    public void ResolvePosition_VerticalMode_YAxisFace_AddsZOffset()
    {
        // Arrange - Hit top of UP face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.UP;
        var hitPosition = new Vector3(0.5f, 1.0f, 0.9f); // High Z

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.Vertical);

        // Assert - Vertical snapping on Y-axis adds Z offset
        Assert.Equal(5, resolved.X);
        Assert.Equal(10, resolved.Y);
        Assert.Equal(16, resolved.Z); // +1 from vertical snapping
    }

    [Fact]
    public void ResolvePosition_VerticalMode_ZAxisFace_AddsYOffset()
    {
        // Arrange - Hit bottom of SOUTH face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.SOUTH;
        var hitPosition = new Vector3(0.5f, 0.1f, 1.0f); // Low Y

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.Vertical);

        // Assert - Vertical snapping on Z-axis adds Y offset
        Assert.Equal(5, resolved.X);
        Assert.Equal(9, resolved.Y); // -1 from vertical snapping
        Assert.Equal(15, resolved.Z);
    }

    #endregion

    #region ResolvePosition Tests - Combined Modes

    [Fact]
    public void ResolvePosition_HorizontalAndVertical_AddsBothOffsets()
    {
        // Arrange - Hit top-right corner of SOUTH face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.SOUTH;
        var hitPosition = new Vector3(0.9f, 0.9f, 1.0f); // High X and high Y

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical);

        // Assert
        Assert.Equal(6, resolved.X);  // +1 from horizontal snapping
        Assert.Equal(11, resolved.Y); // +1 from vertical snapping
        Assert.Equal(15, resolved.Z);
    }

    [Fact]
    public void ResolvePosition_AllFlags_AddsFaceNormalAndBothOffsets()
    {
        // Arrange - Hit top-right corner of EAST face
        var position = new BlockPos(5, 10, 15);
        var face = BlockFacing.EAST;
        var hitPosition = new Vector3(1.0f, 0.9f, 0.9f); // High Y and high Z

        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var resolved = snapping.ResolvePosition(
            EBuildBrushSnapping.ApplyFaceNormalOffset |
            EBuildBrushSnapping.Horizontal |
            EBuildBrushSnapping.Vertical);

        // Assert
        Assert.Equal(6, resolved.X);  // +1 from face normal
        Assert.Equal(11, resolved.Y); // +1 from vertical snapping
        Assert.Equal(16, resolved.Z); // +1 from horizontal snapping
    }

    #endregion

    #region GetHorizontal Tests

    [Theory]
    [InlineData(EnumAxis.X, 1, 0, 0, 1)]   // X-axis: horizontal maps to Z
    [InlineData(EnumAxis.X, -1, 0, 0, -1)]
    [InlineData(EnumAxis.Y, 1, 1, 0, 0)]   // Y-axis: horizontal maps to X
    [InlineData(EnumAxis.Y, -1, -1, 0, 0)]
    [InlineData(EnumAxis.Z, 1, 1, 0, 0)]   // Z-axis: horizontal maps to X
    [InlineData(EnumAxis.Z, -1, -1, 0, 0)]
    public void GetHorizontal_ReturnsCorrectDirection(EnumAxis axis, int horizontal, int expectedX, int expectedY, int expectedZ)
    {
        // Arrange
        var face = axis switch
        {
            EnumAxis.X => BlockFacing.EAST,
            EnumAxis.Y => BlockFacing.UP,
            EnumAxis.Z => BlockFacing.SOUTH,
            _ => BlockFacing.EAST
        };

        // Create a hit position that results in the desired horizontal direction
        var hitPosition = horizontal switch
        {
            1 => GetHitPositionForPositiveHorizontal(face),
            -1 => GetHitPositionForNegativeHorizontal(face),
            _ => new Vector3(0.5f, 0.5f, 0.5f)
        };

        var position = new BlockPos(0, 0, 0);
        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var result = snapping.GetHorizontal(axis);

        // Assert
        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(expectedZ, result.Z);
    }

    #endregion

    #region GetVertical Tests

    [Theory]
    [InlineData(EnumAxis.X, 1, 0, 1, 0)]   // X-axis: vertical maps to Y
    [InlineData(EnumAxis.X, -1, 0, -1, 0)]
    [InlineData(EnumAxis.Y, 1, 0, 0, 1)]   // Y-axis: vertical maps to Z
    [InlineData(EnumAxis.Y, -1, 0, 0, -1)]
    [InlineData(EnumAxis.Z, 1, 0, 1, 0)]   // Z-axis: vertical maps to Y
    [InlineData(EnumAxis.Z, -1, 0, -1, 0)]
    public void GetVertical_ReturnsCorrectDirection(EnumAxis axis, int vertical, int expectedX, int expectedY, int expectedZ)
    {
        // Arrange
        var face = axis switch
        {
            EnumAxis.X => BlockFacing.EAST,
            EnumAxis.Y => BlockFacing.UP,
            EnumAxis.Z => BlockFacing.SOUTH,
            _ => BlockFacing.EAST
        };

        // Create a hit position that results in the desired vertical direction
        var hitPosition = vertical switch
        {
            1 => GetHitPositionForPositiveVertical(face),
            -1 => GetHitPositionForNegativeVertical(face),
            _ => new Vector3(0.5f, 0.5f, 0.5f)
        };

        var position = new BlockPos(0, 0, 0);
        var selectionRay = new BlockSelectionRay(position, face, hitPosition);
        var snapping = new BrushSnapping(selectionRay);

        // Act
        var result = snapping.GetVertical(axis);

        // Assert
        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(expectedZ, result.Z);
    }

    #endregion

    #region Selection Property Tests

    [Fact]
    public void Selection_PreservesOriginalSelectionRay()
    {
        // Arrange
        var position = new BlockPos(10, 20, 30);
        var face = BlockFacing.NORTH;
        var hitPosition = new Vector3(0.3f, 0.7f, 0.0f);
        var selectionRay = new BlockSelectionRay(position, face, hitPosition);

        // Act
        var snapping = new BrushSnapping(selectionRay);

        // Assert
        Assert.Equal(position.X, snapping.Selection.Position.X);
        Assert.Equal(position.Y, snapping.Selection.Position.Y);
        Assert.Equal(position.Z, snapping.Selection.Position.Z);
        Assert.Equal(face, snapping.Selection.Face);
        Assert.Equal(hitPosition, snapping.Selection.HitPosition);
    }

    #endregion

    #region Helper Methods

    private static Vector3 GetHitPositionForPositiveHorizontal(BlockFacing face)
    {
        return face.Axis switch
        {
            EnumAxis.X => new Vector3(1.0f, 0.5f, 0.9f), // High Z
            EnumAxis.Y => new Vector3(0.9f, 1.0f, 0.5f), // High X
            EnumAxis.Z => new Vector3(0.9f, 0.5f, 1.0f), // High X
            _ => new Vector3(0.5f, 0.5f, 0.5f)
        };
    }

    private static Vector3 GetHitPositionForNegativeHorizontal(BlockFacing face)
    {
        return face.Axis switch
        {
            EnumAxis.X => new Vector3(1.0f, 0.5f, 0.1f), // Low Z
            EnumAxis.Y => new Vector3(0.1f, 1.0f, 0.5f), // Low X
            EnumAxis.Z => new Vector3(0.1f, 0.5f, 1.0f), // Low X
            _ => new Vector3(0.5f, 0.5f, 0.5f)
        };
    }

    private static Vector3 GetHitPositionForPositiveVertical(BlockFacing face)
    {
        return face.Axis switch
        {
            EnumAxis.X => new Vector3(1.0f, 0.9f, 0.5f), // High Y
            EnumAxis.Y => new Vector3(0.5f, 1.0f, 0.9f), // High Z
            EnumAxis.Z => new Vector3(0.5f, 0.9f, 1.0f), // High Y
            _ => new Vector3(0.5f, 0.5f, 0.5f)
        };
    }

    private static Vector3 GetHitPositionForNegativeVertical(BlockFacing face)
    {
        return face.Axis switch
        {
            EnumAxis.X => new Vector3(1.0f, 0.1f, 0.5f), // Low Y
            EnumAxis.Y => new Vector3(0.5f, 1.0f, 0.1f), // Low Z
            EnumAxis.Z => new Vector3(0.5f, 0.1f, 1.0f), // Low Y
            _ => new Vector3(0.5f, 0.5f, 0.5f)
        };
    }

    #endregion
}
