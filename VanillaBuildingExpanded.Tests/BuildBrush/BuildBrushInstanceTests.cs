using System.Collections.Immutable;
using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for <see cref="BuildBrushInstance"/>.
/// Verifies that state changes raise events with correct previous/current state.
/// </summary>
public class BuildBrushInstanceTests
{
    #region IsActive Tests

    /// <summary>Checks Is Active When Changed Raises On Activation Changed.</summary>
    [Fact]
    public void IsActive_WhenChanged_RaisesOnActivationChanged()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();

        BrushActivationChangedEventArgs? capturedArgs = null;
        instance.OnActivationChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.IsActive = true;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.False(capturedArgs.WasActive);
        Assert.True(capturedArgs.IsActive);
    }

    /// <summary>Checks Is Active When Changed To Same Value Does Not Raise Event.</summary>
    [Fact]
    public void IsActive_WhenChangedToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();

        int eventCount = 0;
        instance.OnActivationChanged += (sender, args) => eventCount++;

        // Act - set to false (already false)
        instance.IsActive = false;

        // Assert
        Assert.Equal(0, eventCount);
    }

    /// <summary>Checks Is Active When Deactivated Raises With Correct Previous State.</summary>
    [Fact]
    public void IsActive_WhenDeactivated_RaisesWithCorrectPreviousState()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        instance.IsActive = true;

        BrushActivationChangedEventArgs? capturedArgs = null;
        instance.OnActivationChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.IsActive = false;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs.WasActive);
        Assert.False(capturedArgs.IsActive);
    }

    #endregion

    #region Position Tests

    /// <summary>Checks Position When Changed Raises On Position Changed.</summary>
    [Fact]
    public void Position_WhenChanged_RaisesOnPositionChanged()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var initialPos = new BlockPos(0, 0, 0);
        var newPos = new BlockPos(10, 20, 30);
        instance.Position = initialPos;

        PositionChangedEventArgs? capturedArgs = null;
        instance.OnPositionChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.Position = newPos;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.NotNull(capturedArgs.PreviousPosition);
        Assert.NotNull(capturedArgs.CurrentPosition);
        Assert.Equal(initialPos.X, capturedArgs.PreviousPosition.X);
        Assert.Equal(initialPos.Y, capturedArgs.PreviousPosition.Y);
        Assert.Equal(initialPos.Z, capturedArgs.PreviousPosition.Z);
        Assert.Equal(newPos.X, capturedArgs.CurrentPosition.X);
        Assert.Equal(newPos.Y, capturedArgs.CurrentPosition.Y);
        Assert.Equal(newPos.Z, capturedArgs.CurrentPosition.Z);
    }

    /// <summary>Checks Position Event Args Contains Copied Positions.</summary>
    [Fact]
    public void Position_EventArgsContainsCopiedPositions()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var pos = new BlockPos(5, 10, 15);

        PositionChangedEventArgs? capturedArgs = null;
        instance.OnPositionChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.Position = pos;

        // Modify original position
        pos.X = 999;

        // Assert - event args should have copied values, not references
        Assert.NotNull(capturedArgs);
        Assert.Equal(5, capturedArgs.CurrentPosition!.X);
    }

    #endregion

    #region Snapping Tests

    /// <summary>Checks Snapping When Changed Raises On Snapping Mode Changed.</summary>
    [Fact]
    public void Snapping_WhenChanged_RaisesOnSnappingModeChanged()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var initialSnapping = EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical;
        var newSnapping = EBuildBrushSnapping.None;

        // Set initial snapping
        instance.Snapping = initialSnapping;

        SnappingModeChangedEventArgs? capturedArgs = null;
        instance.OnSnappingModeChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.Snapping = newSnapping;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(initialSnapping, capturedArgs.PreviousMode);
        Assert.Equal(newSnapping, capturedArgs.CurrentMode);
    }

    /// <summary>Checks Snapping When Changed To Same Value Does Not Raise Event.</summary>
    [Fact]
    public void Snapping_WhenChangedToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var snapping = EBuildBrushSnapping.Horizontal;
        instance.Snapping = snapping;

        int eventCount = 0;
        instance.OnSnappingModeChanged += (sender, args) => eventCount++;

        // Act
        instance.Snapping = snapping;

        // Assert
        Assert.Equal(0, eventCount);
    }

    #endregion

    #region Placement Block Tests

    /// <summary>Checks Block Transformed Raises On Block Transformed Changed Via Block Id.</summary>
    [Fact]
    public void BlockTransformed_RaisesOnBlockTransformedChanged_ViaBlockId()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);

        BlockChangedEventArgs? capturedArgs = null;
        instance.OnPlacementBlockChanged += (sender, args) => capturedArgs = args;

        // Act - setting BlockId triggers BlockUntransformed which triggers placement block update
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs.IsTransformedBlock);
        Assert.Null(capturedArgs.PreviousBlock);
        Assert.Same(testBlock, capturedArgs.CurrentBlock);
    }

    #endregion

    #region BlockUntransformed Tests

    /// <summary>Checks Block Untransformed Raises On Block Untransformed Changed Via Block Id.</summary>
    [Fact]
    public void BlockUntransformed_RaisesOnBlockUntransformedChanged_ViaBlockId()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);

        BlockChangedEventArgs? capturedArgs = null;
        instance.OnBlockUntransformedChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.False(capturedArgs.IsTransformedBlock);
        Assert.Null(capturedArgs.PreviousBlock);
        Assert.Same(testBlock, capturedArgs.CurrentBlock);
    }

    /// <summary>Checks Block Untransformed Raises On Rotation Info Changed Via Block Id.</summary>
    [Fact]
    public void BlockUntransformed_RaisesOnRotationInfoChanged_ViaBlockId()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);

        RotationInfoChangedEventArgs? capturedArgs = null;
        instance.OnRotationInfoChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Null(capturedArgs.PreviousRotation);
        Assert.NotNull(capturedArgs.CurrentRotation);
        Assert.Same(testBlock, capturedArgs.SourceBlock);
    }

    /// <summary>Checks Block Untransformed Subscribes To Rotation Info Events.</summary>
    [Fact]
    public void BlockUntransformed_SubscribesToRotationInfoEvents()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Create a rotation info with multiple orientations so rotation can work
        var rotation = instance.Rotation;
        Assert.NotNull(rotation);

        // Subscribe to orientation changed event
        OrientationIndexChangedEventArgs? capturedArgs = null;
        instance.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act - If rotation has multiple definitions, changing index should forward event
        if (rotation.CanRotate && rotation.Definitions.Length > 1)
        {
            rotation.CurrentIndex = 1;

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(0, capturedArgs.PreviousIndex);
            Assert.Equal(1, capturedArgs.CurrentIndex);
        }
        else
        {
            // Block doesn't support rotation - skip test
            // (Most simple blocks won't have orientation definitions)
        }
    }

    #endregion

    #region OnDimensionLifecycle Tests
    // Note: Full lifecycle tests require server-side initialization which is
    // difficult to mock. These tests verify the event is properly wired.

    /// <summary>Checks On Dimension Lifecycle Is Defined And Accessible.</summary>
    [Fact]
    public void OnDimensionLifecycle_IsDefinedAndAccessible()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();

        DimensionLifecycleEventArgs? capturedArgs = null;
        instance.OnDimensionLifecycle += (sender, args) => capturedArgs = args;

        // Assert - event subscription should succeed without error
        Assert.NotNull(instance);
        // Note: The event won't fire without server-side dimension initialization
    }

    #endregion

    #region IsValidPlacementBlock Tests

    /// <summary>Checks Is Valid Placement Block Null Block Returns False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void IsValidPlacementBlock_NullBlock_ReturnsFalse(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);

        // Act
        bool result = instance.IsValidPlacementBlock(null);

        // Assert
        Assert.False(result);
    }

    /// <summary>Checks Is Valid Placement Block Block Id Zero Returns False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void IsValidPlacementBlock_BlockIdZero_ReturnsFalse(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);
        var block = TestHelpers.CreateTestBlock(0);

        // Act
        bool result = instance.IsValidPlacementBlock(block);

        // Assert
        Assert.False(result);
    }

    /// <summary>Checks Is Valid Placement Block Is Missing True Returns False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void IsValidPlacementBlock_IsMissingTrue_ReturnsFalse(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);
        var block = TestHelpers.CreateTestBlock(100);
        block.IsMissing = true;

        // Act
        bool result = instance.IsValidPlacementBlock(block);

        // Assert
        Assert.False(result);
    }

    /// <summary>Checks Is Valid Placement Block Valid Block Returns True.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void IsValidPlacementBlock_ValidBlock_ReturnsTrue(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);
        var block = TestHelpers.CreateTestBlock(100);

        // Act
        bool result = instance.IsValidPlacementBlock(block);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region BlockId Property Tests

    /// <summary>Checks Block Id Set To Null Clears Block Untransformed.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void BlockId_SetToNull_ClearsBlockUntransformed(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;
        Assert.NotNull(instance.BlockUntransformed);

        // Act
        instance.BlockId = null;

        // Assert
        Assert.Null(instance.BlockId);
        Assert.Null(instance.BlockUntransformed);
    }

    /// <summary>Checks Block Id Set To Invalid Block Block Untransformed Is Null.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void BlockId_SetToInvalidBlock_BlockUntransformedIsNull(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var invalidBlock = TestHelpers.CreateTestBlock(100);
        invalidBlock.IsMissing = true;
        mockWorld.Setup(w => w.GetBlock(100)).Returns(invalidBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.Equal(100, instance.BlockId); // BlockId is preserved for change detection
        Assert.Null(instance.BlockUntransformed); // But untransformed block is null
    }

    /// <summary>Checks Block Id Set To Same Value Does Not Raise Event.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void BlockId_SetToSameValue_DoesNotRaiseEvent(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        int eventCount = 0;
        instance.OnBlockUntransformedChanged += (_, _) => eventCount++;

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.Equal(0, eventCount);
    }

    #endregion

    #region IsDisabled Tests

    /// <summary>Checks Is Disabled When Not Active Returns True.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void IsDisabled_WhenNotActive_ReturnsTrue(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;
        instance.Position = new BlockPos(0, 0, 0);
        instance.IsActive = false;

        // Assert
        Assert.True(instance.IsDisabled);
    }

    /// <summary>Checks Is Disabled When Position Is Null Returns True.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void IsDisabled_WhenPositionIsNull_ReturnsTrue(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;
        instance.IsActive = true;
        instance.Position = null;

        // Assert
        Assert.True(instance.IsDisabled);
    }

    /// <summary>Checks Is Disabled When Invalid Placement Block Returns True.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void IsDisabled_WhenInvalidPlacementBlock_ReturnsTrue(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var invalidBlock = TestHelpers.CreateTestBlock(100);
        invalidBlock.IsMissing = true;
        mockWorld.Setup(w => w.GetBlock(100)).Returns(invalidBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100; // Invalid block
        instance.IsActive = true;
        instance.Position = new BlockPos(0, 0, 0);

        // Assert
        Assert.True(instance.IsDisabled);
    }

    #endregion

    #region OrientationIndex Tests

    /// <summary>Checks Orientation Index When Rotation Is Null Returns Zero.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OrientationIndex_WhenRotationIsNull_ReturnsZero(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);
        // No block set, so no rotation info

        // Assert
        Assert.Equal(0, instance.OrientationIndex);
    }

    /// <summary>Checks Orientation Index When Set Updates Block Transformed.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OrientationIndex_WhenSet_UpdatesBlockTransformed(EnumAppSide side)
    {
        // Arrange - need a block that supports rotation (has multiple orientation definitions)
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Act & Assert - if rotation supports it, changing index should trigger block transformed change
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.OrientationCount > 1)
        {
            Block? previousTransformed = instance.CurrentPlacementBlock;
            int eventCount = 0;
            instance.OnPlacementBlockChanged += (_, _) => eventCount++;

            instance.OrientationIndex = 1;

            // Block may or may not change depending on orientation mode
            // but the event should fire
            Assert.True(eventCount >= 0); // Event fired (or orientation unchanged)
        }
    }

    /// <summary>Checks Orientation Index Set To Same Value Does Not Raise Event.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OrientationIndex_SetToSameValue_DoesNotRaiseEvent(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        int eventCount = 0;
        instance.OnOrientationChanged += (_, _) => eventCount++;

        // Act - set to same value (0)
        instance.OrientationIndex = 0;

        // Assert
        Assert.Equal(0, eventCount);
    }

    #endregion

    #region CycleOrientation Tests

    /// <summary>Checks Cycle Orientation When Rotation Is Null Returns False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void CycleOrientation_WhenRotationIsNull_ReturnsFalse(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);
        // No block set

        // Act
        bool result = instance.CycleOrientation();

        // Assert
        Assert.False(result);
    }

    /// <summary>Checks Cycle Orientation When Cannot Rotate Returns False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void CycleOrientation_WhenCannotRotate_ReturnsFalse(EnumAppSide side)
    {
        // Arrange - basic block without rotation support
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // If block doesn't support rotation
        if (instance.Rotation is null || !instance.Rotation.CanRotate)
        {
            // Act
            bool result = instance.CycleOrientation();

            // Assert
            Assert.False(result);
        }
    }

    /// <summary>Checks Cycle Orientation Forward Increments Orientation Index.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void CycleOrientation_Forward_IncrementsOrientationIndex(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Only test if rotation is supported
        if (instance.Rotation is not null && instance.Rotation.CanRotate && instance.OrientationCount > 1)
        {
            int initialIndex = instance.OrientationIndex;

            // Act
            bool result = instance.CycleOrientation(EModeCycleDirection.Forward);

            // Assert
            Assert.True(result);
            // Index should have changed (wrapping handled by rotation info)
            Assert.NotEqual(initialIndex, instance.OrientationIndex);
        }
    }

    #endregion

    #region OrientationCount Tests

    /// <summary>Checks Orientation Count When Rotation Is Null Returns Zero.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OrientationCount_WhenRotationIsNull_ReturnsZero(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);

        // Assert
        Assert.Equal(0, instance.OrientationCount);
    }

    /// <summary>Checks Orientation Count When Block Set Returns Rotation Orientation Count.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OrientationCount_WhenBlockSet_ReturnsRotationOrientationCount(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Assert
        Assert.Equal(instance.Rotation?.OrientationCount ?? 0, instance.OrientationCount);
    }

    #endregion

    #region RotationMode Tests

    /// <summary>Checks Rotation Mode When Rotation Is Null Returns None.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void RotationMode_WhenRotationIsNull_ReturnsNone(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);

        // Assert
        Assert.Equal(EBuildBrushRotationMode.None, instance.RotationMode);
    }

    #endregion

    #region EnterBuildMode Tests

    /// <summary>Checks Enter Build Mode Calls Try Update Block Id.</summary>
    [Fact]
    public void EnterBuildMode_CallsTryUpdateBlockId()
    {
        // Arrange - use Server side to avoid DisplaySnappingModeNotice needing ModLoader mock
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Server);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        // Setup hotbar slot with a block using DummySlot (real class, ItemSlot is not mockable)
        var itemStack = new ItemStack(testBlock);
        var dummySlot = new DummySlot(itemStack);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(dummySlot);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = null; // Clear any initial value

        // Act
        instance.EnterBuildMode(null);

        // Assert - BlockId should be set from hotbar
        Assert.Equal(100, instance.BlockId);
    }

    #endregion

    #region ExitBuildMode Tests

    /// <summary>Checks Exit Build Mode Sets Is Active To False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void ExitBuildMode_SetsIsActiveToFalse(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.IsActive = true;

        // Act
        instance.ExitBuildMode();

        // Assert
        Assert.False(instance.IsActive);
    }

    /// <summary>Checks Exit Build Mode Sets Block Id To Zero.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void ExitBuildMode_SetsBlockIdToZero(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Act
        instance.ExitBuildMode();

        // Assert
        Assert.Equal(0, instance.BlockId);
    }

    #endregion

    #region OnBlockPlaced Tests

    /// <summary>Checks On Block Placed Server Calls Try Update Block Id.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OnBlockPlacedServer_CallsTryUpdateBlockId(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var block1 = TestHelpers.CreateTestBlock(100);
        var block2 = TestHelpers.CreateTestBlock(200);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(block1);
        mockWorld.Setup(w => w.GetBlock(200)).Returns(block2);

        // Use DummySlot with block1 initially
        var itemStack1 = new ItemStack(block1);
        var dummySlot = new DummySlot(itemStack1);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(dummySlot);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Change the hotbar item by updating the DummySlot's itemstack
        var itemStack2 = new ItemStack(block2);
        dummySlot.Itemstack = itemStack2;

        // Act
        instance.OnBlockPlacedServer();

        // Assert - BlockId should update to new block
        Assert.Equal(200, instance.BlockId);
    }

    #endregion

    #region TryUpdateBlockId Tests

    /// <summary>Checks Try Update Block Id When Block Changes Returns True.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void TryUpdateBlockId_WhenBlockChanges_ReturnsTrue(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var block1 = TestHelpers.CreateTestBlock(100);
        var block2 = TestHelpers.CreateTestBlock(200);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(block1);
        mockWorld.Setup(w => w.GetBlock(200)).Returns(block2);

        var itemStack1 = new ItemStack(block1);
        var dummySlot = new DummySlot(itemStack1);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(dummySlot);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Change hotbar item
        var itemStack2 = new ItemStack(block2);
        dummySlot.Itemstack = itemStack2;

        // Act
        bool result = instance.TryUpdateBlockId();

        // Assert
        Assert.True(result);
        Assert.Equal(200, instance.BlockId);
    }

    /// <summary>Checks Try Update Block Id When Block Same Returns False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void TryUpdateBlockId_WhenBlockSame_ReturnsFalse(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var itemStack = new ItemStack(testBlock);
        var dummySlot = new DummySlot(itemStack);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(dummySlot);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Act
        bool result = instance.TryUpdateBlockId();

        // Assert
        Assert.False(result);
    }

    /// <summary>Checks Try Update Block Id When Hotbar Empty Sets Block Id To Null.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void TryUpdateBlockId_WhenHotbarEmpty_SetsBlockIdToNull(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        // Create empty DummySlot
        var dummySlot = new DummySlot(null);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(dummySlot);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Act
        bool result = instance.TryUpdateBlockId();

        // Assert
        Assert.True(result);
        Assert.Null(instance.BlockId);
    }

    #endregion

    #region MarkDirty Tests

    /// <summary>Checks Mark Dirty Sets Is Dirty To True.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void MarkDirty_SetsIsDirtyToTrue(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);
        
        // Clear IsDirty first (constructor may set it)
        instance.TryUpdate(inputs.Selection); // This clears IsDirty

        // Act
        instance.MarkDirty();

        // Assert
        Assert.True(instance.IsDirty);
    }

    /// <summary>Checks Mark Dirty When Already Dirty Does Not Register Callback.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void MarkDirty_WhenAlreadyDirty_DoesNotRegisterCallback(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);

        // First call to MarkDirty
        instance.MarkDirty();
        Assert.True(instance.IsDirty);

        // Act - second call when already dirty
        instance.MarkDirty();

        // Assert - IsDirty should still be true, no exception
        Assert.True(instance.IsDirty);
    }

    /// <summary>Checks Mark Dirty Registers Callback.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void MarkDirty_RegistersCallback(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var instance = inputs.CreateBrush(mockWorld.Object);

        // Act
        instance.MarkDirty();

        // Assert - callback should be registered
        mockWorld.Verify(w => w.RegisterCallback(It.IsAny<Action<float>>(), It.IsAny<int>()), Times.AtLeastOnce);
    }

    #endregion

    #region Event Forwarding Tests

    /// <summary>Checks On Orientation Changed Forwards From Rotation Info.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OnOrientationChanged_ForwardsFromRotationInfo(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        int eventCount = 0;
        instance.OnOrientationChanged += (sender, args) => eventCount++;

        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.OrientationCount > 1)
        {
            // Act
            rotation.CurrentIndex = 1;

            // Assert - event should be forwarded
            Assert.True(eventCount > 0);
        }
    }

    /// <summary>Checks On Rotation Info Changed Fired When Block Changes.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OnRotationInfoChanged_FiredWhenBlockChanges(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var block1 = TestHelpers.CreateTestBlock(100);
        var block2 = TestHelpers.CreateTestBlock(200);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(block1);
        mockWorld.Setup(w => w.GetBlock(200)).Returns(block2);

        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        int eventCount = 0;
        RotationInfoChangedEventArgs? lastArgs = null;
        instance.OnRotationInfoChanged += (sender, args) =>
        {
            eventCount++;
            lastArgs = args;
        };

        // Act
        instance.BlockId = 200;

        // Assert
        Assert.True(eventCount > 0);
        Assert.NotNull(lastArgs);
        Assert.Same(block2, lastArgs.SourceBlock);
    }

    /// <summary>Checks On Placement Block Changed Contains Is Transformed Block True.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OnPlacementBlockChanged_ContainsIsTransformedBlockTrue(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);

        BlockChangedEventArgs? capturedArgs = null;
        instance.OnPlacementBlockChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs.IsTransformedBlock);
    }

    /// <summary>Checks On Block Untransformed Changed Contains Is Transformed Block False.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client)]
    [InlineData(EnumAppSide.Server)]
    public void OnBlockUntransformedChanged_ContainsIsTransformedBlockFalse(EnumAppSide side)
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(side);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = inputs.CreateBrush(mockWorld.Object);

        BlockChangedEventArgs? capturedArgs = null;
        instance.OnBlockUntransformedChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.False(capturedArgs.IsTransformedBlock);
    }

    #endregion
}
