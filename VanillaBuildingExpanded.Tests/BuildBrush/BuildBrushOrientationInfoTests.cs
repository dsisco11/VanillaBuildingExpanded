using System.Collections.Immutable;
using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for <see cref="BuildBrushOrientationInfo"/>.
/// Verifies that orientation changes raise events with correct previous/current state.
/// </summary>
public class BuildBrushOrientationInfoTests
{
    #region Test Helpers

    /// <summary>
    /// Creates a BuildBrushOrientationInfo via reflection since the constructor is private.
    /// </summary>
    private static BuildBrushOrientationInfo CreateOrientationInfo(
        IWorldAccessor world,
        Block originalBlock,
        EBuildBrushRotationMode mode,
        ImmutableArray<BlockOrientationDefinition> definitions)
    {
        // Use reflection to access private constructor
        var ctor = typeof(BuildBrushOrientationInfo).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(IWorldAccessor), typeof(Block), typeof(EBuildBrushRotationMode), typeof(ImmutableArray<BlockOrientationDefinition>)],
            null);

        return (BuildBrushOrientationInfo)ctor!.Invoke([world, originalBlock, mode, definitions]);
    }

    /// <summary>
    /// Creates a real Block instance with the specified BlockId.
    /// Block.BlockId is a public field, so we can set it directly.
    /// </summary>
    private static Block CreateTestBlock(int blockId)
    {
        var block = new Block();
        block.BlockId = blockId;
        return block;
    }

    /// <summary>
    /// Creates a mock IWorldAccessor that returns blocks from a dictionary.
    /// </summary>
    private static IWorldAccessor CreateMockWorld(Dictionary<int, Block> blocks)
    {
        var mockWorld = new Mock<IWorldAccessor>();
        mockWorld.Setup(w => w.GetBlock(It.IsAny<int>()))
            .Returns((int id) => blocks.TryGetValue(id, out var block) ? block : null);
        return mockWorld.Object;
    }

    #endregion

    #region CurrentIndex Setter Tests

    [Fact]
    public void CurrentIndex_WhenChanged_RaisesOnOrientationChanged()
    {
        // Arrange
        var block1 = CreateTestBlock(100);
        var block2 = CreateTestBlock(101);
        var blocks = new Dictionary<int, Block> { { 100, block1 }, { 101, block2 } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 90f)
        );

        var info = CreateOrientationInfo(world, block1, EBuildBrushRotationMode.VariantBased, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act
        info.CurrentIndex = 1;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.PreviousIndex);
        Assert.Equal(1, capturedArgs.CurrentIndex);
    }

    [Fact]
    public void CurrentIndex_WhenChangedToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 90f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.VariantBased, definitions);

        int eventCount = 0;
        info.OnOrientationChanged += (sender, args) => eventCount++;

        // Act - set to same value (0)
        info.CurrentIndex = 0;

        // Assert
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void CurrentIndex_WhenChanged_ProvidesPreviousBlock()
    {
        // Arrange
        var block1 = CreateTestBlock(100);
        var block2 = CreateTestBlock(101);
        var blocks = new Dictionary<int, Block> { { 100, block1 }, { 101, block2 } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 90f)
        );

        var info = CreateOrientationInfo(world, block1, EBuildBrushRotationMode.VariantBased, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act
        info.CurrentIndex = 1;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Same(block1, capturedArgs.PreviousBlock);
        Assert.Same(block2, capturedArgs.CurrentBlock);
    }

    [Fact]
    public void CurrentIndex_WhenChanged_ProvidesMeshAngleDegrees()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(100, 90f),
            new BlockOrientationDefinition(100, 180f),
            new BlockOrientationDefinition(100, 270f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.Rotatable, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act
        info.CurrentIndex = 2;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(0f, capturedArgs.PreviousMeshAngleDegrees);
        Assert.Equal(180f, capturedArgs.CurrentMeshAngleDegrees);
    }

    [Fact]
    public void CurrentIndex_WithNegativeValue_WrapsAroundAndRaisesEvent()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(100, 90f),
            new BlockOrientationDefinition(100, 180f),
            new BlockOrientationDefinition(100, 270f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.Rotatable, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act - negative index should wrap to end
        info.CurrentIndex = -1;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.PreviousIndex);
        Assert.Equal(3, capturedArgs.CurrentIndex); // -1 wraps to last index
        Assert.Equal(270f, capturedArgs.CurrentMeshAngleDegrees);
    }

    [Fact]
    public void CurrentIndex_WithOverflowValue_WrapsAroundAndRaisesEvent()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(100, 90f),
            new BlockOrientationDefinition(100, 180f),
            new BlockOrientationDefinition(100, 270f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.Rotatable, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act - overflow index should wrap to beginning
        info.CurrentIndex = 5;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.PreviousIndex);
        Assert.Equal(1, capturedArgs.CurrentIndex); // 5 % 4 = 1
        Assert.Equal(90f, capturedArgs.CurrentMeshAngleDegrees);
    }

    #endregion

    #region Rotate() Tests

    [Fact]
    public void Rotate_Forward_RaisesOnOrientationChanged()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(100, 90f),
            new BlockOrientationDefinition(100, 180f),
            new BlockOrientationDefinition(100, 270f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.Rotatable, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act
        info.Rotate(EModeCycleDirection.Forward);

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.PreviousIndex);
        Assert.Equal(1, capturedArgs.CurrentIndex);
    }

    [Fact]
    public void Rotate_Backward_RaisesOnOrientationChangedWithWraparound()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(100, 90f),
            new BlockOrientationDefinition(100, 180f),
            new BlockOrientationDefinition(100, 270f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.Rotatable, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act - backward from 0 should wrap to 3
        info.Rotate(EModeCycleDirection.Backward);

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.PreviousIndex);
        Assert.Equal(3, capturedArgs.CurrentIndex);
    }

    #endregion

    #region TrySetIndexForBlockId() Tests

    [Fact]
    public void TrySetIndexForBlockId_WhenFound_RaisesOnOrientationChanged()
    {
        // Arrange
        var block1 = CreateTestBlock(100);
        var block2 = CreateTestBlock(101);
        var block3 = CreateTestBlock(102);
        var blocks = new Dictionary<int, Block> { { 100, block1 }, { 101, block2 }, { 102, block3 } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 0f),
            new BlockOrientationDefinition(102, 0f)
        );

        var info = CreateOrientationInfo(world, block1, EBuildBrushRotationMode.VariantBased, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act
        bool result = info.TrySetIndexForBlockId(102);

        // Assert
        Assert.True(result);
        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.PreviousIndex);
        Assert.Equal(2, capturedArgs.CurrentIndex);
        Assert.Same(block1, capturedArgs.PreviousBlock);
        Assert.Same(block3, capturedArgs.CurrentBlock);
    }

    [Fact]
    public void TrySetIndexForBlockId_WhenNotFound_DoesNotRaiseEvent()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 0f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.VariantBased, definitions);

        int eventCount = 0;
        info.OnOrientationChanged += (sender, args) => eventCount++;

        // Act
        bool result = info.TrySetIndexForBlockId(999); // Non-existent block ID

        // Assert
        Assert.False(result);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void TrySetIndexForBlockId_WhenAlreadyAtIndex_DoesNotRaiseEvent()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 0f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.VariantBased, definitions);

        int eventCount = 0;
        info.OnOrientationChanged += (sender, args) => eventCount++;

        // Act - set to block ID that's already at index 0
        bool result = info.TrySetIndexForBlockId(100);

        // Assert
        Assert.True(result);
        Assert.Equal(0, eventCount); // No event because index didn't change
    }

    #endregion

    #region Event Args Property Tests

    [Fact]
    public void EventArgs_VariantChanged_IsTrueWhenBlockIdsDiffer()
    {
        // Arrange
        var block1 = CreateTestBlock(100);
        var block2 = CreateTestBlock(101);
        var blocks = new Dictionary<int, Block> { { 100, block1 }, { 101, block2 } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(101, 0f)
        );

        var info = CreateOrientationInfo(world, block1, EBuildBrushRotationMode.VariantBased, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act
        info.CurrentIndex = 1;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs.VariantChanged);
        Assert.False(capturedArgs.MeshAngleOnlyChanged);
    }

    [Fact]
    public void EventArgs_MeshAngleOnlyChanged_IsTrueWhenSameBlockDifferentAngle()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),
            new BlockOrientationDefinition(100, 90f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.Rotatable, definitions);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act
        info.CurrentIndex = 1;

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.False(capturedArgs.VariantChanged);
        Assert.True(capturedArgs.MeshAngleOnlyChanged);
    }

    [Fact]
    public void EventArgs_HybridRotation_CorrectlyIdentifiesVariantChange()
    {
        // Arrange - Hybrid rotation with different blocks and angles
        var blockNorth = CreateTestBlock(100);
        var blockEast = CreateTestBlock(101);
        var blocks = new Dictionary<int, Block> { { 100, blockNorth }, { 101, blockEast } };
        var world = CreateMockWorld(blocks);

        // Hybrid: 2 variants × 2 angles = 4 total orientations
        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f),   // North, 0°
            new BlockOrientationDefinition(100, 90f),  // North, 90°
            new BlockOrientationDefinition(101, 0f),   // East, 0°
            new BlockOrientationDefinition(101, 90f)   // East, 90°
        );

        var info = CreateOrientationInfo(world, blockNorth, EBuildBrushRotationMode.Hybrid, definitions);

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - rotate through all positions
        info.CurrentIndex = 1; // North 0° → North 90° (mesh angle only)
        info.CurrentIndex = 2; // North 90° → East 0° (variant change)
        info.CurrentIndex = 3; // East 0° → East 90° (mesh angle only)

        // Assert
        Assert.Equal(3, capturedEvents.Count);

        // First rotation: same block, different angle
        Assert.False(capturedEvents[0].VariantChanged);
        Assert.True(capturedEvents[0].MeshAngleOnlyChanged);

        // Second rotation: different block (variant change)
        Assert.True(capturedEvents[1].VariantChanged);
        Assert.False(capturedEvents[1].MeshAngleOnlyChanged);

        // Third rotation: same block, different angle
        Assert.False(capturedEvents[2].VariantChanged);
        Assert.True(capturedEvents[2].MeshAngleOnlyChanged);
    }

    #endregion

    #region Empty/Edge Case Tests

    [Fact]
    public void CurrentIndex_WithEmptyDefinitions_DoesNotRaiseEvent()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.None, ImmutableArray<BlockOrientationDefinition>.Empty);

        int eventCount = 0;
        info.OnOrientationChanged += (sender, args) => eventCount++;

        // Act
        info.CurrentIndex = 5; // Should be ignored

        // Assert
        Assert.Equal(0, eventCount);
        Assert.Equal(0, info.CurrentIndex);
    }

    [Fact]
    public void CurrentIndex_WithSingleDefinition_DoesNotRaiseEventOnWraparound()
    {
        // Arrange
        var block = CreateTestBlock(100);
        var blocks = new Dictionary<int, Block> { { 100, block } };
        var world = CreateMockWorld(blocks);

        var definitions = ImmutableArray.Create(
            new BlockOrientationDefinition(100, 0f)
        );

        var info = CreateOrientationInfo(world, block, EBuildBrushRotationMode.None, definitions);

        int eventCount = 0;
        info.OnOrientationChanged += (sender, args) => eventCount++;

        // Act - any value wraps to 0, which is already the current index
        info.CurrentIndex = 5;

        // Assert
        Assert.Equal(0, eventCount);
        Assert.Equal(0, info.CurrentIndex);
    }

    #endregion
}
