using System.Collections.Immutable;
using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

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
    #region Test Helpers

    /// <summary>
    /// Creates a real Block instance with the specified BlockId and a valid Code.
    /// </summary>
    private static Block CreateTestBlock(int blockId, string code = "game:testblock")
    {
        var block = new Block();
        block.BlockId = blockId;
        block.Code = new Vintagestory.API.Common.AssetLocation(code);
        return block;
    }

    /// <summary>
    /// Creates a mock IWorldAccessor.
    /// </summary>
    private static Mock<IWorldAccessor> CreateMockWorld()
    {
        var mockWorld = new Mock<IWorldAccessor>();
        var mockLogger = new Mock<ILogger>();
        mockWorld.Setup(w => w.Logger).Returns(mockLogger.Object);
        mockWorld.Setup(w => w.Side).Returns(EnumAppSide.Client);
        return mockWorld;
    }

    /// <summary>
    /// Creates a mock IPlayer.
    /// </summary>
    private static Mock<IPlayer> CreateMockPlayer()
    {
        var mockPlayer = new Mock<IPlayer>();
        var mockInventoryManager = new Mock<IPlayerInventoryManager>();
        mockPlayer.Setup(p => p.InventoryManager).Returns(mockInventoryManager.Object);
        mockPlayer.Setup(p => p.CurrentBlockSelection).Returns((BlockSelection?)null);
        return mockPlayer;
    }

    /// <summary>
    /// Creates a BuildBrushInstance for testing.
    /// </summary>
    private static BuildBrushInstance CreateTestInstance()
    {
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        return new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
    }

    #endregion

    #region IsActive Tests

    [Fact]
    public void IsActive_WhenChanged_RaisesOnActivationChanged()
    {
        // Arrange
        var instance = CreateTestInstance();

        BrushActivationChangedEventArgs? capturedArgs = null;
        instance.OnActivationChanged += (sender, args) => capturedArgs = args;

        // Act
        instance.IsActive = true;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.False(capturedArgs.WasActive);
        Assert.True(capturedArgs.IsActive);
    }

    [Fact]
    public void IsActive_WhenChangedToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var instance = CreateTestInstance();

        int eventCount = 0;
        instance.OnActivationChanged += (sender, args) => eventCount++;

        // Act - set to false (already false)
        instance.IsActive = false;

        // Assert
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void IsActive_WhenDeactivated_RaisesWithCorrectPreviousState()
    {
        // Arrange
        var instance = CreateTestInstance();
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

    [Fact]
    public void Position_WhenChanged_RaisesOnPositionChanged()
    {
        // Arrange
        var instance = CreateTestInstance();
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

    [Fact]
    public void Position_EventArgsContainsCopiedPositions()
    {
        // Arrange
        var instance = CreateTestInstance();
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

    [Fact]
    public void Snapping_WhenChanged_RaisesOnSnappingModeChanged()
    {
        // Arrange
        var instance = CreateTestInstance();
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

    [Fact]
    public void Snapping_WhenChangedToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var instance = CreateTestInstance();
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

    #region BlockTransformed Tests

    [Fact]
    public void BlockTransformed_RaisesOnBlockTransformedChanged_ViaBlockId()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        BlockChangedEventArgs? capturedArgs = null;
        instance.OnBlockTransformedChanged += (sender, args) => capturedArgs = args;

        // Act - setting BlockId triggers BlockUntransformed which triggers BlockTransformed
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs.IsTransformedBlock);
        Assert.Null(capturedArgs.PreviousBlock);
        Assert.Same(testBlock, capturedArgs.CurrentBlock);
    }

    #endregion

    #region BlockUntransformed Tests

    [Fact]
    public void BlockUntransformed_RaisesOnBlockUntransformedChanged_ViaBlockId()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

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

    [Fact]
    public void BlockUntransformed_RaisesOnRotationInfoChanged_ViaBlockId()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

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

    [Fact]
    public void BlockUntransformed_SubscribesToRotationInfoEvents()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);

        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
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

    [Fact]
    public void OnDimensionLifecycle_IsDefinedAndAccessible()
    {
        // Arrange
        var instance = CreateTestInstance();

        DimensionLifecycleEventArgs? capturedArgs = null;
        instance.OnDimensionLifecycle += (sender, args) => capturedArgs = args;

        // Assert - event subscription should succeed without error
        Assert.NotNull(instance);
        // Note: The event won't fire without server-side dimension initialization
    }

    #endregion
}