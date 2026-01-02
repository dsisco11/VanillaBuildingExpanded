using System.Collections.Generic;
using System.Collections.Immutable;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for hybrid orientation cycling behavior.
/// Verifies that rotating a hybrid block cycles through all mesh angles
/// for a variant before switching to the next variant block.
/// </summary>
public class HybridOrientationCyclingTests
{
    #region Test Data

    /// <summary>
    /// Test configuration representing different hybrid orientation setups.
    /// </summary>
    public record HybridTestConfig(
        string Name,
        int VariantCount,
        float IntervalDegrees,
        int TotalOrientations,
        int[] ForwardTransitionIndices,
        int[] BackwardTransitionIndices);

    /// <summary>
    /// Test configurations for different hybrid scenarios.
    /// </summary>
    public static IEnumerable<object[]> HybridConfigurations()
    {
        // Config A: 2 variants × 4 angles (0°, 90°, 180°, 270°) = 8 total
        // Forward transitions at index 4 (variant1→variant2) and wrap-around at 0
        // Backward transitions at index 3 (variant2→variant1) and wrap-around at 7
        yield return new object[]
        {
            new HybridTestConfig(
                "2Variants_4Angles",
                VariantCount: 2,
                IntervalDegrees: 90f,
                TotalOrientations: 8,
                ForwardTransitionIndices: [4],
                BackwardTransitionIndices: [3])
        };

        // Config B: 4 variants × 2 angles (0°, 180°) = 8 total
        // Forward transitions at indices 2, 4, 6
        // Backward transitions at indices 1, 3, 5
        yield return new object[]
        {
            new HybridTestConfig(
                "4Variants_2Angles",
                VariantCount: 4,
                IntervalDegrees: 180f,
                TotalOrientations: 8,
                ForwardTransitionIndices: [2, 4, 6],
                BackwardTransitionIndices: [1, 3, 5])
        };

        // Config C: 3 variants × 3 angles (0°, 120°, 240°) = 9 total
        // Forward transitions at indices 3, 6
        // Backward transitions at indices 2, 5
        yield return new object[]
        {
            new HybridTestConfig(
                "3Variants_3Angles",
                VariantCount: 3,
                IntervalDegrees: 120f,
                TotalOrientations: 9,
                ForwardTransitionIndices: [3, 6],
                BackwardTransitionIndices: [2, 5])
        };
    }

    /// <summary>
    /// Creates blocks and definitions for a hybrid configuration.
    /// </summary>
    private static (Dictionary<int, Block> blocks, ImmutableArray<BlockOrientationDefinition> definitions)
        CreateHybridSetup(HybridTestConfig config, int baseBlockId = 100)
    {
        var blocks = new Dictionary<int, Block>();
        var definitionsBuilder = ImmutableArray.CreateBuilder<BlockOrientationDefinition>();

        int anglesPerVariant = config.TotalOrientations / config.VariantCount;

        for (int variant = 0; variant < config.VariantCount; variant++)
        {
            int blockId = baseBlockId + variant;
            var block = new Block { BlockId = blockId };
            blocks[blockId] = block;

            for (int angleStep = 0; angleStep < anglesPerVariant; angleStep++)
            {
                float meshAngle = angleStep * config.IntervalDegrees;
                definitionsBuilder.Add(new BlockOrientationDefinition(blockId, meshAngle));
            }
        }

        return (blocks, definitionsBuilder.ToImmutable());
    }

    /// <summary>
    /// Creates a mock world that returns blocks from a dictionary.
    /// </summary>
    private static IWorldAccessor CreateMockWorld(Dictionary<int, Block> blocks)
    {
        var mockWorld = new Mock<IWorldAccessor>();
        mockWorld.Setup(w => w.GetBlock(It.IsAny<int>()))
            .Returns((int id) => blocks.TryGetValue(id, out var block) ? block : null);
        return mockWorld.Object;
    }

    /// <summary>
    /// Creates a BuildBrushOrientationInfo with the given configuration.
    /// </summary>
    private static BuildBrushOrientationInfo CreateOrientationInfo(
        IWorldAccessor world,
        Block originalBlock,
        ImmutableArray<BlockOrientationDefinition> definitions)
    {
        return new BuildBrushOrientationInfo(world, originalBlock, EBuildBrushRotationMode.Hybrid, definitions);
    }

    #endregion

    #region Forward Cycling Tests

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_CycleForward_CyclesThroughAllMeshAngles_BeforeSwitchingBlockId(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        int anglesPerVariant = config.TotalOrientations / config.VariantCount;

        // Act & Assert - Cycle through all orientations and verify each step
        for (int expectedIndex = 0; expectedIndex < config.TotalOrientations; expectedIndex++)
        {
            // Calculate expected variant and angle
            int expectedVariant = expectedIndex / anglesPerVariant;
            int expectedAngleStep = expectedIndex % anglesPerVariant;
            int expectedBlockId = 100 + expectedVariant;
            float expectedAngle = expectedAngleStep * config.IntervalDegrees;

            Assert.Equal(expectedIndex, info.CurrentIndex);
            Assert.Equal(expectedBlockId, info.Current.BlockId);
            Assert.Equal(expectedAngle, info.Current.MeshAngleDegrees);

            // Advance to next (if not at end)
            if (expectedIndex < config.TotalOrientations - 1)
            {
                info.Rotate(EModeCycleDirection.Forward);
            }
        }
    }

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_CycleForward_BlockIdOnlyChangesAtTransitionPoints(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - Cycle through all orientations
        for (int i = 0; i < config.TotalOrientations - 1; i++)
        {
            info.Rotate(EModeCycleDirection.Forward);
        }

        // Assert - VariantChanged should only be true at transition points
        var transitionSet = new HashSet<int>(config.ForwardTransitionIndices);
        for (int i = 0; i < capturedEvents.Count; i++)
        {
            var evt = capturedEvents[i];
            bool expectedVariantChanged = transitionSet.Contains(evt.CurrentIndex);
            Assert.Equal(expectedVariantChanged, evt.VariantChanged);
        }
    }

    #endregion

    #region Backward Cycling Tests

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_CycleBackward_CyclesThroughAllMeshAngles_BeforeSwitchingBlockId(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        // Start at the last index
        info.CurrentIndex = config.TotalOrientations - 1;

        int anglesPerVariant = config.TotalOrientations / config.VariantCount;

        // Act & Assert - Cycle backward through all orientations
        for (int step = 0; step < config.TotalOrientations; step++)
        {
            int expectedIndex = config.TotalOrientations - 1 - step;
            int expectedVariant = expectedIndex / anglesPerVariant;
            int expectedAngleStep = expectedIndex % anglesPerVariant;
            int expectedBlockId = 100 + expectedVariant;
            float expectedAngle = expectedAngleStep * config.IntervalDegrees;

            Assert.Equal(expectedIndex, info.CurrentIndex);
            Assert.Equal(expectedBlockId, info.Current.BlockId);
            Assert.Equal(expectedAngle, info.Current.MeshAngleDegrees);

            // Go backward (if not at start)
            if (step < config.TotalOrientations - 1)
            {
                info.Rotate(EModeCycleDirection.Backward);
            }
        }
    }

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_CycleBackward_BlockIdOnlyChangesAtTransitionPoints(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        // Start at the last index
        info.CurrentIndex = config.TotalOrientations - 1;

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - Cycle backward through all orientations
        for (int i = 0; i < config.TotalOrientations - 1; i++)
        {
            info.Rotate(EModeCycleDirection.Backward);
        }

        // Assert - VariantChanged should only be true at backward transition points
        var transitionSet = new HashSet<int>(config.BackwardTransitionIndices);
        for (int i = 0; i < capturedEvents.Count; i++)
        {
            var evt = capturedEvents[i];
            bool expectedVariantChanged = transitionSet.Contains(evt.CurrentIndex);
            Assert.Equal(expectedVariantChanged, evt.VariantChanged);
        }
    }

    #endregion

    #region Wrap-Around Tests

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_WrapAroundForward_TransitionsFromLastVariantToFirst(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        // Start at the last index
        info.CurrentIndex = config.TotalOrientations - 1;

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act - Rotate forward to wrap around
        info.Rotate(EModeCycleDirection.Forward);

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(config.TotalOrientations - 1, capturedArgs.PreviousIndex);
        Assert.Equal(0, capturedArgs.CurrentIndex);
        Assert.Equal(0f, capturedArgs.CurrentMeshAngleDegrees);
        Assert.Equal(100, capturedArgs.CurrentBlock?.BlockId); // First variant
        Assert.True(capturedArgs.VariantChanged);
    }

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_WrapAroundBackward_TransitionsFromFirstVariantToLast(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        // Start at index 0
        Assert.Equal(0, info.CurrentIndex);

        OrientationIndexChangedEventArgs? capturedArgs = null;
        info.OnOrientationChanged += (sender, args) => capturedArgs = args;

        // Act - Rotate backward to wrap around
        info.Rotate(EModeCycleDirection.Backward);

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(0, capturedArgs.PreviousIndex);
        Assert.Equal(config.TotalOrientations - 1, capturedArgs.CurrentIndex);

        // Should be at last variant's last angle
        int lastVariantBlockId = 100 + config.VariantCount - 1;
        int anglesPerVariant = config.TotalOrientations / config.VariantCount;
        float lastAngle = (anglesPerVariant - 1) * config.IntervalDegrees;

        Assert.Equal(lastVariantBlockId, capturedArgs.CurrentBlock?.BlockId);
        Assert.Equal(lastAngle, capturedArgs.CurrentMeshAngleDegrees);
        Assert.True(capturedArgs.VariantChanged);
    }

    #endregion

    #region Mesh Angle Reset Tests

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_MeshAngleResetsToZero_WhenSwitchingBlockIdForward(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - Cycle through all orientations
        for (int i = 0; i < config.TotalOrientations - 1; i++)
        {
            info.Rotate(EModeCycleDirection.Forward);
        }

        // Assert - At every forward transition point, mesh angle should be 0°
        foreach (var transitionIndex in config.ForwardTransitionIndices)
        {
            var evt = capturedEvents.Find(e => e.CurrentIndex == transitionIndex);
            Assert.NotNull(evt);
            Assert.Equal(0f, evt.CurrentMeshAngleDegrees);
        }
    }

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_MeshAngleSetsToMaxAngle_WhenSwitchingBlockIdBackward(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        // Start at the last index
        info.CurrentIndex = config.TotalOrientations - 1;

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - Cycle backward through all orientations
        for (int i = 0; i < config.TotalOrientations - 1; i++)
        {
            info.Rotate(EModeCycleDirection.Backward);
        }

        // Assert - At every backward transition point, mesh angle should be the last angle of that variant
        int anglesPerVariant = config.TotalOrientations / config.VariantCount;
        float lastAngleOfVariant = (anglesPerVariant - 1) * config.IntervalDegrees;

        foreach (var transitionIndex in config.BackwardTransitionIndices)
        {
            var evt = capturedEvents.Find(e => e.CurrentIndex == transitionIndex);
            Assert.NotNull(evt);
            Assert.Equal(lastAngleOfVariant, evt.CurrentMeshAngleDegrees);
        }
    }

    #endregion

    #region Full Cycle Event Verification Tests

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_FullCycleForward_RaisesCorrectEventsAtEachStep(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - Full forward cycle (including wrap-around)
        for (int i = 0; i < config.TotalOrientations; i++)
        {
            info.Rotate(EModeCycleDirection.Forward);
        }

        // Assert - Exactly TotalOrientations events raised
        Assert.Equal(config.TotalOrientations, capturedEvents.Count);

        // Verify each event has correct index progression
        for (int i = 0; i < capturedEvents.Count; i++)
        {
            var evt = capturedEvents[i];
            int expectedPrevious = i;
            int expectedCurrent = (i + 1) % config.TotalOrientations;

            Assert.Equal(expectedPrevious, evt.PreviousIndex);
            Assert.Equal(expectedCurrent, evt.CurrentIndex);
        }

        // Verify VariantChanged is correct for all events
        var transitionSet = new HashSet<int>(config.ForwardTransitionIndices);
        transitionSet.Add(0); // Wrap-around also changes variant

        foreach (var evt in capturedEvents)
        {
            bool expectedVariantChanged = transitionSet.Contains(evt.CurrentIndex);
            Assert.Equal(expectedVariantChanged, evt.VariantChanged);
        }
    }

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_FullCycleBackward_RaisesCorrectEventsAtEachStep(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - Full backward cycle (including wrap-around)
        for (int i = 0; i < config.TotalOrientations; i++)
        {
            info.Rotate(EModeCycleDirection.Backward);
        }

        // Assert - Exactly TotalOrientations events raised
        Assert.Equal(config.TotalOrientations, capturedEvents.Count);

        // Verify each event has correct index progression (going backward)
        for (int i = 0; i < capturedEvents.Count; i++)
        {
            var evt = capturedEvents[i];
            int expectedPrevious = (config.TotalOrientations - i) % config.TotalOrientations;
            int expectedCurrent = (config.TotalOrientations - i - 1 + config.TotalOrientations) % config.TotalOrientations;

            Assert.Equal(expectedPrevious, evt.PreviousIndex);
            Assert.Equal(expectedCurrent, evt.CurrentIndex);
        }

        // Verify VariantChanged is correct for all events
        var transitionSet = new HashSet<int>(config.BackwardTransitionIndices);
        transitionSet.Add(config.TotalOrientations - 1); // Wrap-around also changes variant

        foreach (var evt in capturedEvents)
        {
            bool expectedVariantChanged = transitionSet.Contains(evt.CurrentIndex);
            Assert.Equal(expectedVariantChanged, evt.VariantChanged);
        }
    }

    [Theory]
    [MemberData(nameof(HybridConfigurations))]
    public void Hybrid_MeshAngleOnlyChanged_TrueWhenSameBlockDifferentAngle(HybridTestConfig config)
    {
        // Arrange
        var (blocks, definitions) = CreateHybridSetup(config);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = CreateOrientationInfo(world, originalBlock, definitions);

        var capturedEvents = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => capturedEvents.Add(args);

        // Act - Full forward cycle
        for (int i = 0; i < config.TotalOrientations; i++)
        {
            info.Rotate(EModeCycleDirection.Forward);
        }

        // Assert - MeshAngleOnlyChanged should be true when VariantChanged is false
        foreach (var evt in capturedEvents)
        {
            // MeshAngleOnlyChanged should be the opposite of VariantChanged
            Assert.Equal(!evt.VariantChanged, evt.MeshAngleOnlyChanged);
        }
    }

    #endregion
}
