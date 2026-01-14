using System.Collections.Generic;
using System.Collections.Immutable;

using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Tests for rotatable orientation cycling behavior.
/// Verifies that rotating a rotatable block cycles through mesh angles on the same block-id.
/// </summary>
public class RotatableOrientationCyclingTests
{
    private static (Dictionary<int, Block> blocks, ImmutableArray<BlockOrientation> definitions) CreateRotatableSetup(
        int baseBlockId,
        float intervalDegrees,
        int stepCount)
    {
        var blocks = new Dictionary<int, Block>
        {
            [baseBlockId] = new Block { BlockId = baseBlockId }
        };

        var builder = ImmutableArray.CreateBuilder<BlockOrientation>(stepCount);
        for (int i = 0; i < stepCount; i++)
        {
            builder.Add(new BlockOrientation(baseBlockId, i * intervalDegrees));
        }

        return (blocks, builder.MoveToImmutable());
    }

    private static IWorldAccessor CreateMockWorld(Dictionary<int, Block> blocks)
    {
        var mockWorld = new Mock<IWorldAccessor>();
        mockWorld.Setup(w => w.GetBlock(It.IsAny<int>()))
            .Returns((int id) => blocks.TryGetValue(id, out var block) ? block : null);
        return mockWorld.Object;
    }

    [Theory]
    [InlineData(90f, 4)]
    [InlineData(45f, 8)]
    public void Rotatable_CycleForward_WrapsAndKeepsBlockId(float intervalDegrees, int stepCount)
    {
        // Arrange
        var (blocks, definitions) = CreateRotatableSetup(baseBlockId: 100, intervalDegrees, stepCount);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = new BrushOrientation(world, originalBlock, EBuildBrushRotationMode.Rotatable, definitions);

        // Act & Assert
        for (int i = 0; i < stepCount; i++)
        {
            Assert.Equal(100, info.Current.BlockId);
            Assert.Equal(i * intervalDegrees, info.Current.MeshAngleDegrees);
            info.Rotate(EModeCycleDirection.Forward);
        }

        // After full cycle, should wrap back to start
        Assert.Equal(0, info.CurrentIndex);
        Assert.Equal(100, info.Current.BlockId);
        Assert.Equal(0f, info.Current.MeshAngleDegrees);
    }

    [Theory]
    [InlineData(90f, 4)]
    [InlineData(45f, 8)]
    public void Rotatable_CycleBackward_FromStart_WrapsToLast(float intervalDegrees, int stepCount)
    {
        // Arrange
        var (blocks, definitions) = CreateRotatableSetup(baseBlockId: 100, intervalDegrees, stepCount);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = new BrushOrientation(world, originalBlock, EBuildBrushRotationMode.Rotatable, definitions);

        // Act
        info.Rotate(EModeCycleDirection.Backward);

        // Assert
        Assert.Equal(stepCount - 1, info.CurrentIndex);
        Assert.Equal(100, info.Current.BlockId);
        Assert.Equal((stepCount - 1) * intervalDegrees, info.Current.MeshAngleDegrees);
    }

    [Fact]
    public void Rotatable_OrientationChangedEvents_NeverFlagVariantChanged()
    {
        // Arrange
        const float intervalDegrees = 90f;
        const int stepCount = 4;

        var (blocks, definitions) = CreateRotatableSetup(baseBlockId: 100, intervalDegrees, stepCount);
        var world = CreateMockWorld(blocks);
        var originalBlock = blocks[100];
        var info = new BrushOrientation(world, originalBlock, EBuildBrushRotationMode.Rotatable, definitions);

        var captured = new List<OrientationIndexChangedEventArgs>();
        info.OnOrientationChanged += (sender, args) => captured.Add(args);

        // Act
        for (int i = 0; i < stepCount - 1; i++)
        {
            info.Rotate(EModeCycleDirection.Forward);
        }

        // Assert
        Assert.Equal(stepCount - 1, captured.Count);
        foreach (var evt in captured)
        {
            Assert.False(evt.VariantChanged);
            Assert.True(evt.MeshAngleOnlyChanged);
        }
    }
}
