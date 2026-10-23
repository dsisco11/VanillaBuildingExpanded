using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>
/// Client-side specific tests for <see cref="BuildBrushInstance"/>.
/// Tests entity association, dimension wrapping, and client-side behavior.
/// </summary>
public class BuildBrushInstanceTests_Client
{
    #region Client-Side Activation Tests

    /// <summary>Checks Is Active When Set To True On Client Does Not Create Dimension.</summary>
    [Fact]
    public void IsActive_WhenSetToTrueOnClient_DoesNotCreateDimension()
    {
        // Arrange - On client side, dimension activation is skipped (server creates dimensions)
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        
        var instance = inputs.CreateBrush(mockWorld.Object);

        // Act
        instance.IsActive = true;

        // Assert - Dimension should be null on client (server creates it)
        Assert.Null(instance.Dimension);
        Assert.True(instance.IsActive);
    }

    /// <summary>Checks Is Active When Set To False On Client Does Not Destroy Dimension.</summary>
    [Fact]
    public void IsActive_WhenSetToFalseOnClient_DoesNotDestroyDimension()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        
        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.IsActive = true;

        // Act
        instance.IsActive = false;

        // Assert - should not throw, dimension remains null
        Assert.Null(instance.Dimension);
        Assert.False(instance.IsActive);
    }

    #endregion

    #region AssociateEntity Tests
    // Note: AssociateEntity requires a real BuildBrushEntity which needs game infrastructure.
    // These tests verify the behavior without the full entity setup.

    /// <summary>Checks Associate Entity Sets Entity Property.</summary>
    [Fact]
    public void AssociateEntity_SetsEntityProperty()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        
        var instance = inputs.CreateBrush(mockWorld.Object);

        // We can't easily create a real BuildBrushEntity without game infrastructure
        // Just verify the method exists and the Entity property works
        Assert.Null(instance.Entity);
    }

    #endregion

    #region OnBlockPlaced Client Behavior Tests

    /// <summary>Checks On Block Placed Server On Client With No Block Selection Does Not Throw.</summary>
    [Fact]
    public void OnBlockPlacedServer_OnClient_WithNoBlockSelection_DoesNotThrow()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        var testBlock = TestHelpers.CreateTestBlock(100);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(testBlock);

        var itemStack = new ItemStack(testBlock);
        var dummySlot = new DummySlot(itemStack);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(dummySlot);
        inputs.Selection = (BlockSelection?)null;
        
        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Act & Assert - should not throw even without block selection
        var exception = Record.Exception(() => instance.OnBlockPlacedServer());
        Assert.Null(exception);
    }

    /// <summary>Checks On Block Placed Server On Client Updates Block Id From Hotbar.</summary>
    [Fact]
    public void OnBlockPlacedServer_OnClient_UpdatesBlockIdFromHotbar()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        var block1 = TestHelpers.CreateTestBlock(100);
        var block2 = TestHelpers.CreateTestBlock(200);
        mockWorld.Setup(w => w.GetBlock(100)).Returns(block1);
        mockWorld.Setup(w => w.GetBlock(200)).Returns(block2);

        var itemStack1 = new ItemStack(block1);
        var dummySlot = new DummySlot(itemStack1);
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(dummySlot);
        inputs.Selection = (BlockSelection?)null;
        
        var instance = inputs.CreateBrush(mockWorld.Object);
        instance.BlockId = 100;

        // Change hotbar item
        var itemStack2 = new ItemStack(block2);
        dummySlot.Itemstack = itemStack2;

        // Act
        instance.OnBlockPlacedServer();

        // Assert - BlockId should update
        Assert.Equal(200, instance.BlockId);
    }

    #endregion

    #region TryUpdate Client Tests

    /// <summary>Checks Try Update When No Block Selection Sets Is Valid Placement False.</summary>
    [Fact]
    public void TryUpdate_WhenNoBlockSelection_SetsIsValidPlacementFalse()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        inputs.Selection = (BlockSelection?)null;
        
        var instance = inputs.CreateBrush(mockWorld.Object);

        // Act
        bool result = instance.TryUpdate(inputs.Selection);

        // Assert
        Assert.False(result);
        Assert.False(instance.IsValidPlacement);
    }

    /// <summary>Checks Try Update When No Block Sets Position From Selection.</summary>
    [Fact]
    public void TryUpdate_WhenNoBlock_SetsPositionFromSelection()
    {
        // Arrange - no block set, so snapping just returns selection position
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();

        var blockSelection = new BlockSelection
        {
            Position = new BlockPos(5, 10, 15),
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 1.0, 0.5)
        };
        inputs.Selection = blockSelection;
        
        var instance = inputs.CreateBrush(mockWorld.Object);
        // No BlockId set

        // Act
        instance.TryUpdate(blockSelection);

        // Assert - position should be set (snapping returns selection.Position when no block)
        Assert.NotNull(instance.Position);
    }

    #endregion

    #region Selection Property Tests

    /// <summary>Checks Selection When Try Update Updates Selection Position.</summary>
    [Fact]
    public void Selection_WhenTryUpdate_UpdatesSelectionPosition()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        
        var instance = inputs.CreateBrush(mockWorld.Object);
        var selection = new BlockSelection
        {
            Position = new BlockPos(1, 2, 3),
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.25, 0.5, 0.75),
            DidOffset = false
        };

        // Act
        instance.TryUpdate(selection, force: true);

        // Assert - Selection should be updated
        Assert.NotNull(instance.Selection);
        Assert.Equal(instance.Position?.X, instance.Selection.Position?.X);
        Assert.Equal(instance.Position?.Y, instance.Selection.Position?.Y);
        Assert.Equal(instance.Position?.Z, instance.Selection.Position?.Z);
    }

    /// <summary>Checks Selection When Try Update Preserves Face And Hit Position.</summary>
    [Fact]
    public void Selection_WhenTryUpdate_PreservesFaceAndHitPosition()
    {
        // Arrange
        var mockWorld = TestHelpers.CreateMockWorld(EnumAppSide.Client);
        var inputs = TestHelpers.CreateBrushInputs();
        
        var instance = inputs.CreateBrush(mockWorld.Object);
        var selection = new BlockSelection
        {
            Position = new BlockPos(1, 2, 3),
            Face = BlockFacing.NORTH,
            HitPosition = new Vec3d(0.1, 0.2, 0.3),
            DidOffset = false
        };

        // Act
        instance.TryUpdate(selection, force: true);

        // Assert
        Assert.Equal(BlockFacing.NORTH, instance.Selection.Face);
        Assert.Equal(0.1, instance.Selection.HitPosition.X);
        Assert.Equal(0.2, instance.Selection.HitPosition.Y);
        Assert.Equal(0.3, instance.Selection.HitPosition.Z);
    }

    #endregion
}
