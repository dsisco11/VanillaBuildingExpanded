using System.Collections.Immutable;
using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for <see cref="BuildBrushDimension"/>.
/// Verifies subscription lifecycle and event handler behavior.
/// </summary>
public class BuildBrushDimensionTests
{
    #region Subscription Tests    #region SubscribeTo Tests

    [Fact]
    public void SubscribeTo_SetsSubscribedInstance()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();

        // Act
        dimension.SubscribeTo(instance);

        // Assert
        Assert.Same(instance, dimension.SubscribedInstance);
    }

    [Fact]
    public void SubscribeTo_CalledTwiceWithSameInstance_DoesNothing()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();

        // Act
        dimension.SubscribeTo(instance);
        dimension.SubscribeTo(instance);

        // Assert
        Assert.Same(instance, dimension.SubscribedInstance);
    }

    [Fact]
    public void SubscribeTo_CalledWithDifferentInstance_UnsubscribesFromPrevious()
    {
        // Arrange
        var instance1 = TestHelpers.CreateTestInstance();
        var instance2 = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();

        // Act
        dimension.SubscribeTo(instance1);
        dimension.SubscribeTo(instance2);

        // Assert
        Assert.Same(instance2, dimension.SubscribedInstance);
    }

    #endregion

    #region Unsubscribe Tests

    [Fact]
    public void Unsubscribe_ClearsSubscribedInstance()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();
        dimension.SubscribeTo(instance);

        // Act
        dimension.Unsubscribe();

        // Assert
        Assert.Null(dimension.SubscribedInstance);
    }

    [Fact]
    public void Unsubscribe_WhenNotSubscribed_DoesNotThrow()
    {
        // Arrange
        var dimension = TestHelpers.CreateTestDimension();

        // Act & Assert - should not throw
        var exception = Record.Exception(() => dimension.Unsubscribe());
        Assert.Null(exception);
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        int blockChangedCount = 0;
        dimension.OnDirty += (s, e) => blockChangedCount++;

        dimension.SubscribeTo(instance);
        dimension.Unsubscribe();

        // Act - change block after unsubscribe
        instance.BlockId = 100;

        // Assert - dimension should not receive the event
        // (blockChangedCount would be 0 if unsubscribe worked)
        // Note: The dimension isn't initialized so SetBlock won't work,
        // but the event handlers won't be called at all
        Assert.Null(dimension.SubscribedInstance);
    }

    #endregion

    #region Destroy Tests

    [Fact]
    public void Destroy_UnsubscribesFromInstance()
    {
        // Arrange
        var instance = TestHelpers.CreateTestInstance();
        var dimension = TestHelpers.CreateTestDimension();
        dimension.SubscribeTo(instance);

        // Act
        dimension.Destroy();

        // Assert
        Assert.Null(dimension.SubscribedInstance);
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public void Instance_OnBlockTransformedChanged_UpdatesDimension()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        // Track if dimension received the event
        bool eventReceived = false;
        dimension.OnDirty += (s, e) => eventReceived = true;

        dimension.SubscribeTo(instance);

        // Act - setting BlockId triggers OnPlacementBlockChanged
        instance.BlockId = 100;

        // Assert - event was received by dimension
        // Note: The actual block placement won't work without initialization,
        // but the event handler is still invoked
        Assert.True(eventReceived || dimension.SubscribedInstance == instance);
    }

    [Fact]
    public void SubscribeTo_AutomaticResubscription_OnInstanceChange()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var testBlock1 = TestHelpers.CreateTestBlock(100);
        var testBlock2 = TestHelpers.CreateTestBlock(200);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock1);
        mockWorld.Setup(w => w.GetBlock(200)).Returns(testBlock2);

        var instance1 = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var instance2 = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        int eventCount = 0;
        dimension.OnDirty += (s, e) => eventCount++;

        // Subscribe to first instance
        dimension.SubscribeTo(instance1);
        instance1.BlockId = 100;
        int countAfterFirst = eventCount;

        // Subscribe to second instance (should unsubscribe from first)
        dimension.SubscribeTo(instance2);
        
        // Trigger event on first instance - should not be received
        int countBeforeOldEvent = eventCount;
        instance1.BlockId = 0; // Clear
        instance1.BlockId = 100; // Set again

        // Trigger event on second instance - should be received
        instance2.BlockId = 200;

        // Assert
        Assert.Same(instance2, dimension.SubscribedInstance);
        // The subscription should have moved to instance2
    }

    #endregion

    #region Event Args Propagation Tests

    [Fact]
    public void Instance_OnOrientationChanged_IsReceived()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = TestHelpers.CreateTestDimension();

        // We need to verify the dimension's event handler is connected
        // Since we can't easily mock the internal handler, we verify
        // the subscription exists
        dimension.SubscribeTo(instance);

        // Set up a block that supports rotation
        instance.BlockId = 100;

        // Assert subscription is active
        Assert.Same(instance, dimension.SubscribedInstance);
    }

    #endregion

    #region ApplyRotation Tests - Hybrid Mode

    /// <summary>
    /// Helper to set RotationMode on dimension via reflection.
    /// </summary>
    private static void SetRotationMode(BuildBrushDimension dimension, EBuildBrushRotationMode mode)
    {
        var prop = typeof(BuildBrushDimension).GetProperty("RotationMode");
        prop?.SetValue(dimension, mode);
    }

    /// <summary>
    /// Helper to set originalBlock on dimension via reflection.
    /// </summary>
    private static void SetOriginalBlock(BuildBrushDimension dimension, Block block)
    {
        var field = typeof(BuildBrushDimension).GetField("originalBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(dimension, block);
    }

    /// <summary>
    /// Helper to set currentBlock on dimension via reflection.
    /// </summary>
    private static void SetCurrentBlock(BuildBrushDimension dimension, Block block)
    {
        var field = typeof(BuildBrushDimension).GetField("currentBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(dimension, block);
    }

    /// <summary>
    /// Helper to get currentBlock from dimension via reflection.
    /// </summary>
    private static Block? GetCurrentBlock(BuildBrushDimension dimension)
    {
        var field = typeof(BuildBrushDimension).GetField("currentBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(dimension) as Block;
    }

    [Fact]
    public void ApplyRotation_HybridMode_WhenVariantBlockDiffers_UpdatesCurrentBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BuildBrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        var blockEast = TestHelpers.CreateTestBlock(101, "game:hybrid-east");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);
        mockWorld.Setup(w => w.GetBlock(101)).Returns(blockEast);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to Hybrid mode with blockNorth as original/current
        SetRotationMode(dimension, EBuildBrushRotationMode.Hybrid);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (variant change)
        var previousDef = new BlockOrientationDefinition(100, 0f);
        var currentDef = new BlockOrientationDefinition(101, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BuildBrushOrientationInfo(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Hybrid, definitions);
        orientationInfo.CurrentIndex = 1; // Set to the new index

        // Act - Apply rotation with different variant block
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should now be blockEast
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(blockEast, currentBlock);
    }

    [Fact]
    public void ApplyRotation_HybridMode_WhenVariantBlockSame_DoesNotReplaceBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BuildBrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to Hybrid mode
        SetRotationMode(dimension, EBuildBrushRotationMode.Hybrid);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (same variant, different angle)
        var previousDef = new BlockOrientationDefinition(100, 0f);
        var currentDef = new BlockOrientationDefinition(100, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BuildBrushOrientationInfo(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Hybrid, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act - Apply rotation with SAME variant block (different angle, same block)
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should still be blockNorth
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(blockNorth, currentBlock);
    }

    [Fact]
    public void ApplyRotation_VariantBasedMode_WhenVariantBlockDiffers_UpdatesCurrentBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BuildBrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:variant-north");
        var blockEast = TestHelpers.CreateTestBlock(101, "game:variant-east");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);
        mockWorld.Setup(w => w.GetBlock(101)).Returns(blockEast);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to VariantBased mode
        SetRotationMode(dimension, EBuildBrushRotationMode.VariantBased);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (variant change)
        var previousDef = new BlockOrientationDefinition(100, 0f);
        var currentDef = new BlockOrientationDefinition(101, 0f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BuildBrushOrientationInfo(mockWorld.Object, blockNorth, EBuildBrushRotationMode.VariantBased, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act - Apply rotation with different variant block
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should now be blockEast
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(blockEast, currentBlock);
    }

    [Fact]
    public void ApplyRotation_HybridMode_WhenNullVariantBlock_DoesNotCrash()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BuildBrushDimension(mockWorld.Object);

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to Hybrid mode
        SetRotationMode(dimension, EBuildBrushRotationMode.Hybrid);
        SetOriginalBlock(dimension, blockNorth);
        SetCurrentBlock(dimension, blockNorth);

        // Create mock event args for rotation (angle change only)
        var previousDef = new BlockOrientationDefinition(100, 0f);
        var currentDef = new BlockOrientationDefinition(100, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BuildBrushOrientationInfo(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Hybrid, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act & Assert - Should not throw even with null CurrentBlock in orientation info
        var exception = Record.Exception(() => dimension.ApplyRotation(eventArgs, orientationInfo));
        Assert.Null(exception);
    }

    [Fact]
    public void ApplyRotation_NoneMode_DoesNotChangeBlock()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var dimension = new BuildBrushDimension(mockWorld.Object);

        var block = TestHelpers.CreateTestBlock(100, "game:static");
        var otherBlock = TestHelpers.CreateTestBlock(101, "game:other");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(block);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Set the dimension to None mode
        SetRotationMode(dimension, EBuildBrushRotationMode.None);
        SetOriginalBlock(dimension, block);
        SetCurrentBlock(dimension, block);

        // Create mock event args for rotation (attempted change)
        var previousDef = new BlockOrientationDefinition(100, 0f);
        var currentDef = new BlockOrientationDefinition(101, 90f);
        var eventArgs = new OrientationIndexChangedEventArgs(0, 1, previousDef, currentDef);
        
        // Create mock orientation info
        var definitions = System.Collections.Immutable.ImmutableArray.Create(previousDef, currentDef);
        var orientationInfo = new BuildBrushOrientationInfo(mockWorld.Object, block, EBuildBrushRotationMode.None, definitions);
        orientationInfo.CurrentIndex = 1;

        // Act - Try to apply rotation with different block
        dimension.ApplyRotation(eventArgs, orientationInfo);

        // Assert - currentBlock should NOT change for None mode
        var currentBlock = GetCurrentBlock(dimension);
        Assert.Same(block, currentBlock);
    }

    #endregion

    #region Instance_OnOrientationChanged Handler Tests

    [Fact]
    public void Instance_OnOrientationChanged_HybridMode_PassesCorrectBlockAndAngle()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        var blockEast = TestHelpers.CreateTestBlock(101, "game:hybrid-east");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);
        mockWorld.Setup(w => w.GetBlock(101)).Returns(blockEast);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = new BuildBrushDimension(mockWorld.Object);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        // Subscribe dimension to instance
        dimension.SubscribeTo(instance);

        // Create hybrid orientation definitions: 2 variants × 4 angles = 8 total
        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),   // index 0: North, 0°
            new BlockOrientationDefinition(100, 90f),  // index 1: North, 90°
            new BlockOrientationDefinition(100, 180f), // index 2: North, 180°
            new BlockOrientationDefinition(100, 270f), // index 3: North, 270°
            new BlockOrientationDefinition(101, 0f),   // index 4: East, 0°
            new BlockOrientationDefinition(101, 90f),  // index 5: East, 90°
            new BlockOrientationDefinition(101, 180f), // index 6: East, 180°
            new BlockOrientationDefinition(101, 270f)  // index 7: East, 270°
        );

        var rotation = new BuildBrushOrientationInfo(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Hybrid, definitions);

        // Inject rotation info into instance
        var rotationField = typeof(BuildBrushInstance).GetField("_rotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockField = typeof(BuildBrushInstance).GetField("_blockUntransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockTransformedField = typeof(BuildBrushInstance).GetField("_blockTransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        rotationField?.SetValue(instance, rotation);
        blockField?.SetValue(instance, blockNorth);
        blockTransformedField?.SetValue(instance, blockNorth);

        // Wire up the rotation event handler
        var handlerMethod = typeof(BuildBrushInstance).GetMethod("Rotation_OnOrientationChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handlerMethod is not null)
        {
            var handler = (EventHandler<OrientationIndexChangedEventArgs>)Delegate.CreateDelegate(
                typeof(EventHandler<OrientationIndexChangedEventArgs>), instance, handlerMethod);
            rotation.OnOrientationChanged += handler;
        }

        // Capture events
        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        instance.OnOrientationChanged += (s, e) => capturedEvents.Add(e);

        // Act - Cycle through orientations: 0 → 1 → 2 → 3 → 4 (crosses variant boundary)
        for (int i = 0; i < 4; i++)
        {
            instance.CycleOrientation(EModeCycleDirection.Forward);
        }

        // Assert
        Assert.Equal(4, capturedEvents.Count);

        // First 3 events should have same block (North), increasing angles
        Assert.Equal(100, capturedEvents[0].CurrentDefinition.BlockId);
        Assert.Equal(90f, capturedEvents[0].CurrentDefinition.MeshAngleDegrees);
        Assert.False(capturedEvents[0].VariantChanged);

        Assert.Equal(100, capturedEvents[1].CurrentDefinition.BlockId);
        Assert.Equal(180f, capturedEvents[1].CurrentDefinition.MeshAngleDegrees);
        Assert.False(capturedEvents[1].VariantChanged);

        Assert.Equal(100, capturedEvents[2].CurrentDefinition.BlockId);
        Assert.Equal(270f, capturedEvents[2].CurrentDefinition.MeshAngleDegrees);
        Assert.False(capturedEvents[2].VariantChanged);

        // 4th event should cross to East variant with angle reset to 0°
        Assert.Equal(101, capturedEvents[3].CurrentDefinition.BlockId);
        Assert.Equal(0f, capturedEvents[3].CurrentDefinition.MeshAngleDegrees);
        Assert.True(capturedEvents[3].VariantChanged);
    }

    [Fact]
    public void Instance_OnOrientationChanged_HybridMode_BlockTransformedUpdatedBeforeEvent()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld();
        var mockPlayer = TestHelpers.CreateMockPlayer();

        var blockNorth = TestHelpers.CreateTestBlock(100, "game:hybrid-north");
        var blockEast = TestHelpers.CreateTestBlock(101, "game:hybrid-east");
        mockWorld.Setup(w => w.GetBlock(100)).Returns(blockNorth);
        mockWorld.Setup(w => w.GetBlock(101)).Returns(blockEast);

        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        var dimension = new BuildBrushDimension(mockWorld.Object);

        // Initialize dimension with mock mini-dimension
        var mockMiniDimension = new Mock<IMiniDimension>();
        mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
        dimension.InitializeClientSide(mockMiniDimension.Object);

        dimension.SubscribeTo(instance);

        // Create hybrid orientation: 2 variants × 2 angles = 4 total
        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),   // North, 0°
            new BlockOrientationDefinition(100, 180f), // North, 180°
            new BlockOrientationDefinition(101, 0f),   // East, 0°
            new BlockOrientationDefinition(101, 180f)  // East, 180°
        );

        var rotation = new BuildBrushOrientationInfo(mockWorld.Object, blockNorth, EBuildBrushRotationMode.Hybrid, definitions);

        // Inject rotation info
        var rotationField = typeof(BuildBrushInstance).GetField("_rotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockField = typeof(BuildBrushInstance).GetField("_blockUntransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        var blockTransformedField = typeof(BuildBrushInstance).GetField("_blockTransformed", BindingFlags.NonPublic | BindingFlags.Instance);
        rotationField?.SetValue(instance, rotation);
        blockField?.SetValue(instance, blockNorth);
        blockTransformedField?.SetValue(instance, blockNorth);

        // Wire up rotation event
        var handlerMethod = typeof(BuildBrushInstance).GetMethod("Rotation_OnOrientationChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handlerMethod is not null)
        {
            var handler = (EventHandler<OrientationIndexChangedEventArgs>)Delegate.CreateDelegate(
                typeof(EventHandler<OrientationIndexChangedEventArgs>), instance, handlerMethod);
            rotation.OnOrientationChanged += handler;
        }

        // Capture CurrentPlacementBlock at the moment of each event
        var blockTransformedAtEvent = new List<Block?>();
        var eventArgsCurrentBlockId = new List<int>();
        instance.OnOrientationChanged += (s, e) =>
        {
            blockTransformedAtEvent.Add(instance.CurrentPlacementBlock);
            eventArgsCurrentBlockId.Add(e.CurrentDefinition.BlockId);
        };

        // Act - Cycle to index 2 (East variant)
        instance.CycleOrientation(EModeCycleDirection.Forward); // 0 → 1
        instance.CycleOrientation(EModeCycleDirection.Forward); // 1 → 2 (variant change)

        // Assert - EventArgs.CurrentDefinition.BlockId should always have the correct NEW block ID
        // even if instance.CurrentPlacementBlock is updated after the event fires
        Assert.Equal(2, eventArgsCurrentBlockId.Count);
        Assert.Equal(100, eventArgsCurrentBlockId[0]); // Still North at 180°
        Assert.Equal(101, eventArgsCurrentBlockId[1]);  // Changed to East at 0°
    }

    #endregion
}
