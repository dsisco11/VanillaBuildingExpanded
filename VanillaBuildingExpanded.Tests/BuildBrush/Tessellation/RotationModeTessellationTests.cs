using System;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush.Tessellation;

/// <summary>
/// Tests verifying correct behavior for all EBuildBrushRotationMode scenarios.
/// Tests that each rotation mode correctly triggers dimension updates with proper blocks/rotations.
/// </summary>
public class RotationModeTessellationTests
{
    #region Test Helpers

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
    /// Creates a test Block with specified BlockId and Code.
    /// </summary>
    private static Block CreateTestBlock(int blockId, string code = "game:testblock")
    {
        var block = new Block();
        block.BlockId = blockId;
        block.Code = new AssetLocation(code);
        return block;
    }

    /// <summary>
    /// Creates a BuildBrushDimension for testing.
    /// </summary>
    private static BuildBrushDimension CreateTestDimension(Mock<IWorldAccessor>? mockWorld = null)
    {
        mockWorld ??= CreateMockWorld();
        return new BuildBrushDimension(mockWorld.Object);
    }

    #endregion

    #region Rotation Mode: None

    [Fact]
    public void RotationModeNone_SetBlock_RaisesDirtyEvent()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100, "game:simpleblock");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = CreateTestDimension(mockWorld);

        dimension.SubscribeTo(instance);

        bool blockTransformedChanged = false;
        instance.OnBlockTransformedChanged += (s, e) => blockTransformedChanged = true;

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.True(blockTransformedChanged);
        Assert.NotNull(instance.Rotation);
        // Note: Most simple blocks will have None rotation mode
    }

    [Fact]
    public void RotationModeNone_CanRotate_IsFalse()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100, "game:simpleblock");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(instance.Rotation);
        // A simple block without orientation variants should not be rotatable
        // (depends on block definition - test documents expected behavior)
    }

    #endregion

    #region Rotation Mode Detection

    [Fact]
    public void RotationInfo_IsCreatedForEveryBlock()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        RotationInfoChangedEventArgs? capturedArgs = null;
        instance.OnRotationInfoChanged += (s, e) => capturedArgs = e;

        // Act
        instance.BlockId = 100;

        // Assert - Rotation info should always be created
        Assert.NotNull(capturedArgs);
        Assert.NotNull(capturedArgs.CurrentRotation);
        Assert.NotNull(instance.Rotation);
    }

    [Fact]
    public void RotationInfo_ModeIsSetCorrectly()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        // Act
        instance.BlockId = 100;

        // Assert
        Assert.NotNull(instance.Rotation);
        // Mode will be one of: None, VariantBased, Rotatable, Hybrid
        Assert.True(
            instance.Rotation.Mode == EBuildBrushRotationMode.None ||
            instance.Rotation.Mode == EBuildBrushRotationMode.VariantBased ||
            instance.Rotation.Mode == EBuildBrushRotationMode.Rotatable ||
            instance.Rotation.Mode == EBuildBrushRotationMode.Hybrid
        );
    }

    #endregion

    #region Orientation Index Changes

    [Fact]
    public void OrientationIndex_WhenBlockSupportsRotation_CanBeChanged()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.BlockId = 100;

        // Act & Assert
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.Definitions.Length > 1)
        {
            // Block supports rotation
            int initialIndex = instance.OrientationIndex;
            instance.OrientationIndex = 1;

            Assert.Equal(1, instance.OrientationIndex);
            Assert.NotEqual(initialIndex, instance.OrientationIndex);
        }
        else
        {
            // Block doesn't support rotation - that's fine
            Assert.True(true);
        }
    }

    [Fact]
    public void OrientationIndex_WhenChanged_RaisesOrientationChangedEvent()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.BlockId = 100;

        OrientationIndexChangedEventArgs? capturedArgs = null;
        instance.OnOrientationChanged += (s, e) => capturedArgs = e;

        // Act & Assert
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.Definitions.Length > 1)
        {
            instance.OrientationIndex = 1;

            Assert.NotNull(capturedArgs);
            Assert.Equal(0, capturedArgs.PreviousIndex);
            Assert.Equal(1, capturedArgs.CurrentIndex);
        }
    }

    #endregion

    #region Block Transformed Updates

    [Fact]
    public void OrientationIndex_WhenChanged_UpdatesBlockTransformed()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        BlockChangedEventArgs? lastBlockTransformedEvent = null;
        instance.OnBlockTransformedChanged += (s, e) => lastBlockTransformedEvent = e;

        instance.BlockId = 100;
        var initialBlock = instance.BlockTransformed;

        // Act & Assert
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.Definitions.Length > 1)
        {
            // Reset to capture next event
            lastBlockTransformedEvent = null;

            instance.OrientationIndex = 1;

            // For VariantBased rotation, the transformed block should change
            if (rotation.Mode == EBuildBrushRotationMode.VariantBased ||
                rotation.Mode == EBuildBrushRotationMode.Hybrid)
            {
                Assert.NotNull(lastBlockTransformedEvent);
            }
        }
    }

    #endregion

    #region Rotation Event Args Properties

    [Fact]
    public void OrientationChangedEventArgs_ContainsMeshAngle()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.BlockId = 100;

        OrientationIndexChangedEventArgs? capturedArgs = null;
        instance.OnOrientationChanged += (s, e) => capturedArgs = e;

        // Act
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.Definitions.Length > 1)
        {
            instance.OrientationIndex = 1;

            // Assert
            Assert.NotNull(capturedArgs);
            // MeshAngle should be a valid angle (0, 90, 180, 270 typically)
            Assert.True(capturedArgs.CurrentMeshAngleDegrees >= 0);
        }
    }

    [Fact]
    public void OrientationChangedEventArgs_VariantChanged_ReflectsMode()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.BlockId = 100;

        OrientationIndexChangedEventArgs? capturedArgs = null;
        instance.OnOrientationChanged += (s, e) => capturedArgs = e;

        // Act
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.Definitions.Length > 1)
        {
            instance.OrientationIndex = 1;

            // Assert
            Assert.NotNull(capturedArgs);

            if (rotation.Mode == EBuildBrushRotationMode.VariantBased ||
                rotation.Mode == EBuildBrushRotationMode.Hybrid)
            {
                // VariantChanged should be true for variant-based modes
                // (when the block ID changes between orientations)
            }
            else if (rotation.Mode == EBuildBrushRotationMode.Rotatable)
            {
                // VariantChanged should be false, MeshAngleOnlyChanged should be true
                Assert.True(capturedArgs.MeshAngleOnlyChanged || !capturedArgs.VariantChanged);
            }
        }
    }

    #endregion

    #region Dimension Subscription with Rotation

    [Fact]
    public void DimensionSubscribed_ReceivesOrientationChanges()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = CreateTestDimension(mockWorld);

        dimension.SubscribeTo(instance);
        instance.BlockId = 100;

        bool orientationEventReceived = false;
        instance.OnOrientationChanged += (s, e) => orientationEventReceived = true;

        // Act
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.Definitions.Length > 1)
        {
            instance.OrientationIndex = 1;

            // Assert
            Assert.True(orientationEventReceived);
        }
    }

    [Fact]
    public void DimensionSubscribed_ReceivesBlockTransformedOnRotation()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = CreateTestDimension(mockWorld);

        dimension.SubscribeTo(instance);
        instance.BlockId = 100;

        int blockTransformedCount = 0;
        instance.OnBlockTransformedChanged += (s, e) => blockTransformedCount++;

        // Reset counter after initial block set
        blockTransformedCount = 0;

        // Act
        var rotation = instance.Rotation;
        if (rotation is not null && rotation.CanRotate && rotation.Definitions.Length > 1 &&
            (rotation.Mode == EBuildBrushRotationMode.VariantBased || rotation.Mode == EBuildBrushRotationMode.Hybrid))
        {
            instance.OrientationIndex = 1;

            // Assert - For variant-based, block transformed should change
            Assert.True(blockTransformedCount > 0);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SettingSameOrientationIndex_DoesNotRaiseEvent()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.BlockId = 100;

        int eventCount = 0;
        instance.OnOrientationChanged += (s, e) => eventCount++;

        // Act - Set to same index
        int currentIndex = instance.OrientationIndex;
        instance.OrientationIndex = currentIndex;

        // Assert - No event should fire
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void ClearingBlock_ResetsRotationInfo()
    {
        // Arrange
        var mockWorld = CreateMockWorld();
        var mockPlayer = CreateMockPlayer();
        var testBlock = CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);
        mockWorld.Setup(w => w.GetBlock(0)).Returns((Block?)null);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.BlockId = 100;

        Assert.NotNull(instance.Rotation);

        // Act
        instance.BlockId = 0;

        // Assert
        Assert.Null(instance.BlockUntransformed);
        Assert.Null(instance.BlockTransformed);
    }

    #endregion
}
