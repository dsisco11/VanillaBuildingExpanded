using Moq;

using VanillaBuildingExpanded.BuildHammer;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Server-side specific tests for <see cref="BuildBrushInstance"/>.
/// Tests dimension activation events and server-side behavior.
/// Note: Full entity spawning requires game infrastructure and is tested in E2E tests.
/// </summary>
public class BuildBrushInstanceTests_Server
{
    #region Dimension Activation Event Tests

    [Fact]
    public void IsActive_WhenSetToTrueOnServer_AttemptsToActivateDimension()
    {
        // Arrange - Server-side mock without full entity infrastructure
        // The activation will attempt to create dimension but may fail on entity spawn
        // We verify the activation flow starts correctly
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Server);
        var mockPlayer = TestHelpers.CreateMockPlayer();
        
        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        bool activationChanged = false;
        instance.OnActivationChanged += (sender, args) =>
        {
            activationChanged = true;
            Assert.False(args.WasActive);
            Assert.True(args.IsActive);
        };

        // Act
        instance.IsActive = true;

        // Assert - activation event should fire regardless of dimension success
        Assert.True(activationChanged);
        Assert.True(instance.IsActive);
    }

    [Fact]
    public void IsActive_WhenSetToFalseOnServer_RaisesDeactivationEvent()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Server);
        var mockPlayer = TestHelpers.CreateMockPlayer();
        
        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.IsActive = true; // Activate first (will fail dimension but set flag)

        bool deactivationFired = false;
        instance.OnActivationChanged += (sender, args) =>
        {
            if (!args.IsActive)
            {
                deactivationFired = true;
                Assert.True(args.WasActive);
                Assert.False(args.IsActive);
            }
        };

        // Act
        instance.IsActive = false;

        // Assert
        Assert.True(deactivationFired);
        Assert.False(instance.IsActive);
    }

    #endregion

    #region Server-Side Behavior Tests

    [Fact]
    public void OnBlockPlacedServer_OnServer_DoesNotCallTryUpdate()
    {
        // Arrange - OnBlockPlaced only calls TryUpdate on client
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Server);
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var itemStack = new ItemStack(testBlock);
        var dummySlot = new DummySlot(itemStack);
        mockPlayer.Setup(p => p.InventoryManager.ActiveHotbarSlot).Returns(dummySlot);
        
        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        instance.BlockId = 100;

        // Set up position tracking
        instance.Position = new BlockPos(5, 5, 5);
        int positionChanges = 0;
        instance.OnPositionChanged += (_, _) => positionChanges++;

        // Act
        instance.OnBlockPlacedServer();

        // Assert - position should not change (TryUpdate not called on server)
        Assert.Equal(0, positionChanges);
    }

    #endregion

    #region DestroyDimension Tests

    [Fact]
    public void DestroyDimension_WhenNoDimension_DoesNotThrow()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Server);
        var mockPlayer = TestHelpers.CreateMockPlayer();
        
        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);
        // No dimension created

        // Act & Assert - should not throw
        var exception = Record.Exception(() => instance.DestroyDimension());
        Assert.Null(exception);
    }

    [Fact]
    public void DestroyDimension_SetsEntityAndDimensionToNull()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Server);
        var mockPlayer = TestHelpers.CreateMockPlayer();
        
        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        // Act
        instance.DestroyDimension();

        // Assert
        Assert.Null(instance.Dimension);
        Assert.Null(instance.Entity);
    }

    #endregion

    #region Dimension Block Update Tests

    [Fact]
    public void BlockId_WhenChangedOnServer_MarksInstanceDirty()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Server);
        var mockPlayer = TestHelpers.CreateMockPlayer();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);
        
        var instance = new BuildBrushInstance(mockPlayer.Object, mockWorld.Object);

        // Act
        instance.BlockId = 100;

        // Assert - IsDirty should be true after block change
        Assert.True(instance.IsDirty);
    }

    #endregion
}
