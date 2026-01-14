using System.Collections.Immutable;
using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildHammer;
using VanillaBuildingExpanded.BuildHammer.Tessellation;
using VanillaBuildingExpanded.Tests.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;

using Xunit;

namespace VanillaBuildingExpanded.Tests.E2E;

/// <summary>
/// End-to-end tests that verify the complete flow from user actions
/// (like rotate, set block) through to tessellation being triggered.
/// These tests wire up real components with mocked Vintage Story APIs.
/// </summary>
public class BrushToTessellationFlowTests
{
    #region Test Harness

    /// <summary>
    /// Test harness that wires together Instance, Dimension, and tracks events.
    /// Models the dimension as already created (no server initialization).
    /// </summary>
    private class BrushTestHarness
    {
        public Mock<IWorldAccessor> MockWorld { get; }
        public Mock<IPlayer> MockPlayer { get; }
        public Mock<ICoreClientAPI> MockCapi { get; }
        public Mock<ITesselatorManager> MockTesselatorManager { get; }
        public Mock<ITessellationService> MockTessellationService { get; }
        public Mock<IMiniDimension> MockMiniDimension { get; }

        public BuildBrushInstance Instance { get; }
        public BrushDimension Dimension { get; }

        // Event tracking
        public int BlockTransformedChangedCount { get; private set; }
        public int OrientationChangedCount { get; private set; }
        public int DimensionDirtyCount { get; private set; }
        public int TessellationCallCount { get; private set; }

        public BlockChangedEventArgs? LastBlockTransformedArgs { get; private set; }
        public OrientationIndexChangedEventArgs? LastOrientationArgs { get; private set; }
        public DimensionDirtyEventArgs? LastDimensionDirtyArgs { get; private set; }

        public BrushTestHarness()
        {
            // Create mocks
            MockWorld = TestHelpers.CreateMockWorld();
            MockPlayer = TestHelpers.CreateMockPlayer();
            MockCapi = TestHelpers.CreateMockCapi();
            MockTesselatorManager = new Mock<ITesselatorManager>();
            MockTessellationService = new Mock<ITessellationService>();

            // Setup capi to return tesselator manager
            MockCapi.Setup(c => c.TesselatorManager).Returns(MockTesselatorManager.Object);

            // Create real components
            Instance = new BuildBrushInstance(MockPlayer.Object, MockWorld.Object);
            Dimension = new BrushDimension(MockWorld.Object);

            // Initialize the dimension with a mock IMiniDimension so it's considered "initialized"
            // Without this, SetBlock/MarkDirty won't fire OnDirty events
            MockMiniDimension = new Mock<IMiniDimension>();
            MockMiniDimension.Setup(d => d.subDimensionId).Returns(1);

            int placedBlockId = 0;
            BlockEntity? placedBlockEntity = null;

            MockMiniDimension
                .Setup(d => d.AdjustPosForSubDimension(It.IsAny<BlockPos>()))
                .Callback<BlockPos>(_ => { });

            MockMiniDimension
                .Setup(d => d.SetBlock(It.IsAny<int>(), It.IsAny<BlockPos>()))
                .Callback<int, BlockPos>((blockId, _) => placedBlockId = blockId);

            MockMiniDimension
                .Setup(d => d.GetBlock(It.IsAny<BlockPos>()))
                .Returns(() => placedBlockId == 0 ? null! : MockWorld.Object.GetBlock(placedBlockId));

            MockMiniDimension
                .Setup(d => d.GetBlockEntity(It.IsAny<BlockPos>()))
                .Returns(() =>
                {
                    // In these tests we only ever place a single preview block at the origin.
                    // If a block has been placed, ensure a rotatable BE exists so rotatable mode can apply mesh-angle changes.
                    if (placedBlockId != 0 && placedBlockEntity is null)
                    {
                        placedBlockEntity = new TestRotatableBlockEntity();
                    }

                    return placedBlockEntity;
                });

            Dimension.InitializeClientSide(MockMiniDimension.Object);

            // Wire up dimension subscription (dimension subscribes to instance events)
            Dimension.SubscribeTo(Instance);

            // Wire up instance to receive dimension dirty events
            // (In the real system, this happens when SetDimension is called with an IMiniDimension,
            // but for testing we manually wire up the connection)
            Dimension.OnDirty += (s, e) =>
            {
                DimensionDirtyCount++;
                LastDimensionDirtyArgs = e;

                // Simulate renderer behavior: when dimension is dirty, call tessellation
                // This models what BuildBrushEntityRenderer does when it receives OnDimensionDirty
                var mockDimension = new Mock<IMiniDimension>();
                var min = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
                var max = TestHelpers.CreateMiniDimensionPos(0, 0, 0);
                MockTessellationService.Object.Tessellate(mockDimension.Object, min, max);
            };

            // Track events
            Instance.OnPlacementBlockChanged += (s, e) =>
            {
                BlockTransformedChangedCount++;
                LastBlockTransformedArgs = e;
            };

            Instance.OnOrientationChanged += (s, e) =>
            {
                OrientationChangedCount++;
                LastOrientationArgs = e;
            };

            // Track tessellation calls
            MockTessellationService.Setup(t => t.Tessellate(
                It.IsAny<IMiniDimension>(),
                It.IsAny<BlockPos>(),
                It.IsAny<BlockPos>()))
                .Callback(() => TessellationCallCount++)
                .Returns(TestHelpers.CreateTestMeshData());
        }

        /// <summary>
        /// Registers a block with the mock world and optionally sets up tessellation.
        /// </summary>
        public Block RegisterBlock(int blockId, string code = "game:testblock")
        {
            var block = TestHelpers.CreateTestBlock(blockId, code);
            MockWorld.Setup(w => w.GetBlock(blockId)).Returns(block);

            // Setup tessellation for this block
            var mesh = TestHelpers.CreateTestMeshData();
            MockTesselatorManager.Setup(t => t.GetDefaultBlockMesh(block)).Returns(mesh);

            return block;
        }

        /// <summary>
        /// Resets all event counters.
        /// </summary>
        public void ResetCounters()
        {
            BlockTransformedChangedCount = 0;
            OrientationChangedCount = 0;
            DimensionDirtyCount = 0;
            TessellationCallCount = 0;
            LastBlockTransformedArgs = null;
            LastOrientationArgs = null;
            LastDimensionDirtyArgs = null;
        }

        /// <summary>
        /// Asserts that all tracking values are at their default state.
        /// Call this before the Act stage to ensure test isolation.
        /// </summary>
        public void AssertDefaultState()
        {
            Assert.Equal(0, BlockTransformedChangedCount);
            Assert.Equal(0, OrientationChangedCount);
            Assert.Equal(0, DimensionDirtyCount);
            Assert.Equal(0, TessellationCallCount);
            Assert.Null(LastBlockTransformedArgs);
            Assert.Null(LastOrientationArgs);
            Assert.Null(LastDimensionDirtyArgs);
        }

        /// <summary>
        /// Sets up the instance with a pre-configured BuildBrushOrientationInfo using reflection.
        /// This allows testing specific rotation modes without needing real block variant detection.
        /// </summary>
        /// <param name="block">The block to set.</param>
        /// <param name="mode">The rotation mode to configure.</param>
        /// <param name="orientationCount">Number of orientations (creates definitions with incrementing block IDs).</param>
        public void SetupBlockWithRotation(Block block, EBuildBrushRotationMode mode, int orientationCount)
        {
            // Register the block and its variants in the world
            MockWorld.Setup(w => w.GetBlock(block.BlockId)).Returns(block);

            if (mode == EBuildBrushRotationMode.Rotatable)
            {
                // Ensure the preview dimension spawns a BE so rotatable mesh-angle changes mark the dimension dirty.
                block.EntityClass = "TestRotatable";
            }

            // Create definitions based on mode
            var definitionsBuilder = ImmutableArray.CreateBuilder<BlockOrientation>(orientationCount);
            for (int i = 0; i < orientationCount; i++)
            {
                int definitionBlockId = mode == EBuildBrushRotationMode.Rotatable
                    ? block.BlockId
                    : block.BlockId + i;

                float meshAngle = mode == EBuildBrushRotationMode.Rotatable
                    ? i * (360f / orientationCount)
                    : 0f; // VariantBased and None use 0° mesh angle

                // Register variant blocks
                if (definitionBlockId != block.BlockId)
                {
                    var variantBlock = TestHelpers.CreateTestBlock(definitionBlockId, $"{block.Code}-variant{i}");
                    MockWorld.Setup(w => w.GetBlock(definitionBlockId)).Returns(variantBlock);
                }

                definitionsBuilder.Add(new BlockOrientation(definitionBlockId, meshAngle));
            }

            var definitions = definitionsBuilder.MoveToImmutable();

            // Create the orientation info via reflection
            var orientationInfo = CreateOrientationInfo(MockWorld.Object, block, mode, definitions);

            // Inject into the instance via reflection
            InjectOrientationInfo(Instance, orientationInfo, block);
        }

        /// <summary>
        /// Creates a BuildBrushOrientationInfo directly using the internal constructor.
        /// </summary>
        private static BrushOrientation CreateOrientationInfo(
            IWorldAccessor world,
            Block block,
            EBuildBrushRotationMode mode,
            ImmutableArray<BlockOrientation> definitions)
        {
            return new BrushOrientation(world, block, mode, definitions);
        }

        /// <summary>
        /// Injects pre-configured rotation info into the instance via reflection.
        /// Also sets up the block and wires events.
        /// </summary>
        private static void InjectOrientationInfo(BuildBrushInstance instance, BrushOrientation orientationInfo, Block block)
        {
            // Get private fields
            var rotationField = typeof(BuildBrushInstance).GetField("_rotation", BindingFlags.NonPublic | BindingFlags.Instance);
            var blockUntransformedField = typeof(BuildBrushInstance).GetField("_blockUntransformed", BindingFlags.NonPublic | BindingFlags.Instance);
            var blockIdField = typeof(BuildBrushInstance).GetField("_blockId", BindingFlags.NonPublic | BindingFlags.Instance);

            // Unsubscribe from old rotation if any
            var oldRotation = rotationField?.GetValue(instance) as BrushOrientation;
            if (oldRotation is not null)
            {
                // Need to unsubscribe via reflection - get the event handler field
                var eventHandlerField = typeof(BuildBrushInstance).GetMethod("Rotation_OnOrientationChanged", BindingFlags.NonPublic | BindingFlags.Instance);
                if (eventHandlerField is not null)
                {
                    var handler = (EventHandler<OrientationIndexChangedEventArgs>)Delegate.CreateDelegate(
                        typeof(EventHandler<OrientationIndexChangedEventArgs>), instance, eventHandlerField);
                    oldRotation.OnOrientationChanged -= handler;
                }
            }

            // Set fields
            rotationField?.SetValue(instance, orientationInfo);
            blockUntransformedField?.SetValue(instance, block);
            blockIdField?.SetValue(instance, block.BlockId);

            // Subscribe to new rotation events
            var newEventHandlerMethod = typeof(BuildBrushInstance).GetMethod("Rotation_OnOrientationChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            if (newEventHandlerMethod is not null)
            {
                var newHandler = (EventHandler<OrientationIndexChangedEventArgs>)Delegate.CreateDelegate(
                    typeof(EventHandler<OrientationIndexChangedEventArgs>), instance, newEventHandlerMethod);
                orientationInfo.OnOrientationChanged += newHandler;
            }
        }

        private sealed class TestRotatableBlockEntity : BlockEntity, IRotatable
        {
            private float _meshAngleRadians;

            public void OnTransformed(
                IWorldAccessor worldAccessor,
                ITreeAttribute tree,
                int degreeRotation,
                System.Collections.Generic.Dictionary<int, AssetLocation> oldBlockIdMapping,
                System.Collections.Generic.Dictionary<int, AssetLocation> oldItemIdMapping,
                EnumAxis? flipAxis)
            {
                // For tests, any tree mutation is sufficient to simulate a successful rotation.
                _meshAngleRadians = degreeRotation * GameMath.DEG2RAD;
                tree.SetFloat("meshAngle", _meshAngleRadians);
            }

            public override void ToTreeAttributes(ITreeAttribute tree)
            {
                tree.SetFloat("meshAngle", _meshAngleRadians);
            }

            public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessor)
            {
                _meshAngleRadians = tree.GetFloat("meshAngle", _meshAngleRadians);
            }

        }
    }

    /// <summary>
    /// Creates a test harness with all components wired together.
    /// </summary>
    private static BrushTestHarness CreateHarness()
    {
        return new BrushTestHarness();
    }

    #endregion

    #region Block Change Flow Tests

    [Fact]
    public void SetBlockId_RaisesBlockTransformedChanged()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert
        Assert.Equal(1, harness.BlockTransformedChangedCount);
        Assert.NotNull(harness.LastBlockTransformedArgs);
        Assert.Equal(100, harness.LastBlockTransformedArgs.CurrentBlock?.BlockId);
    }

    [Fact]
    public void SetBlockId_MultipleTimes_RaisesEventEachTime()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100, "game:block1");
        harness.RegisterBlock(200, "game:block2");
        harness.RegisterBlock(300, "game:block3");
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;
        harness.Instance.BlockId = 200;
        harness.Instance.BlockId = 300;

        // Assert - 3 distinct block changes
        Assert.Equal(3, harness.BlockTransformedChangedCount);
    }

    [Fact]
    public void SetBlockId_SameBlock_DoesNotRaiseEvent()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);

        harness.Instance.BlockId = 100;
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - set same block again
        harness.Instance.BlockId = 100;

        // Assert
        Assert.Equal(0, harness.BlockTransformedChangedCount);
    }

    [Fact]
    public void ClearBlock_RaisesBlockTransformedChanged_WithNullBlock()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);
        harness.Instance.BlockId = 100;
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 0;

        // Assert
        Assert.Equal(1, harness.BlockTransformedChangedCount);
        Assert.Null(harness.LastBlockTransformedArgs?.CurrentBlock);
    }

    #endregion

    #region Orientation Change Flow Tests - VariantBased Mode

    [Fact]
    public void VariantBased_OrientationChange_RaisesOrientationChanged()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:variantblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.VariantBased, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act
        harness.Instance.OrientationIndex = 1;

        // Assert
        Assert.Equal(1, harness.OrientationChangedCount);
        Assert.NotNull(harness.LastOrientationArgs);
        Assert.Equal(0, harness.LastOrientationArgs.PreviousIndex);
        Assert.Equal(1, harness.LastOrientationArgs.CurrentIndex);
    }

    [Fact]
    public void VariantBased_OrientationChange_TriggersTessellation()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:variantblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.VariantBased, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act
        harness.Instance.OrientationIndex = 1;

        // Assert
        Assert.Equal(1, harness.OrientationChangedCount);
        Assert.Equal(1, harness.DimensionDirtyCount);
        Assert.Equal(1, harness.TessellationCallCount);
    }

    [Fact]
    public void VariantBased_MultipleOrientationChanges_EachTriggersTessellation()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:variantblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.VariantBased, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Cycle through all 4 orientations
        harness.Instance.OrientationIndex = 1;
        harness.Instance.OrientationIndex = 2;
        harness.Instance.OrientationIndex = 3;
        harness.Instance.OrientationIndex = 0;

        // Assert
        Assert.Equal(4, harness.OrientationChangedCount);
        Assert.Equal(4, harness.DimensionDirtyCount);
        Assert.Equal(4, harness.TessellationCallCount);
    }

    #endregion

    #region Orientation Change Flow Tests - Rotatable Mode

    [Fact]
    public void Rotatable_OrientationChange_RaisesOrientationChanged()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:rotatableblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Rotatable, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act
        harness.Instance.OrientationIndex = 1;

        // Assert
        Assert.Equal(1, harness.OrientationChangedCount);
        Assert.NotNull(harness.LastOrientationArgs);
        Assert.Equal(0, harness.LastOrientationArgs.PreviousIndex);
        Assert.Equal(1, harness.LastOrientationArgs.CurrentIndex);
    }

    [Fact]
    public void Rotatable_OrientationChange_TriggersTessellation()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:rotatableblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Rotatable, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act
        harness.Instance.OrientationIndex = 1;

        // Assert
        Assert.Equal(1, harness.OrientationChangedCount);
        Assert.Equal(1, harness.DimensionDirtyCount);
        Assert.Equal(1, harness.TessellationCallCount);
    }

    [Fact]
    public void Rotatable_MultipleOrientationChanges_EachTriggersTessellation()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:rotatableblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Rotatable, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Cycle through all 4 orientations
        harness.Instance.OrientationIndex = 1;
        harness.Instance.OrientationIndex = 2;
        harness.Instance.OrientationIndex = 3;
        harness.Instance.OrientationIndex = 0;

        // Assert
        Assert.Equal(4, harness.OrientationChangedCount);
        Assert.Equal(4, harness.DimensionDirtyCount);
        Assert.Equal(4, harness.TessellationCallCount);
    }

    #endregion

    #region Orientation Change Flow Tests - None Mode

    [Fact]
    public void None_SingleOrientation_CannotRotate()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:staticblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.None, orientationCount: 1);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Assert - CanRotate should be false
        Assert.False(harness.Instance.Rotation?.CanRotate);
        Assert.Equal(1, harness.Instance.Rotation?.OrientationCount);
    }

    [Fact]
    public void None_SetSameOrientationIndex_DoesNotRaiseEvent()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:staticblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.None, orientationCount: 1);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Attempt to set same index (only has 1 orientation)
        harness.Instance.OrientationIndex = 0;

        // Assert
        Assert.Equal(0, harness.OrientationChangedCount);
        Assert.Equal(0, harness.DimensionDirtyCount);
        Assert.Equal(0, harness.TessellationCallCount);
    }

    #endregion

    #region Same Orientation Index Tests

    [Fact]
    public void SetSameOrientationIndex_DoesNotRaiseEvent()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:rotatableblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Rotatable, orientationCount: 4);

        // First change to index 1
        harness.Instance.OrientationIndex = 1;
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - set to same index (1)
        harness.Instance.OrientationIndex = 1;

        // Assert
        Assert.Equal(0, harness.OrientationChangedCount);
        Assert.Equal(0, harness.DimensionDirtyCount);
        Assert.Equal(0, harness.TessellationCallCount);
    }

    #endregion

    #region Dimension Subscription Flow Tests

    [Fact]
    public void Dimension_ReceivesBlockTransformedChanges()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Dimension should be subscribed and receive the event
        Assert.Same(harness.Instance, harness.Dimension.SubscribedInstance);
    }

    [Fact]
    public void Dimension_Unsubscribed_DoesNotReceiveEvents()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);

        harness.Dimension.Unsubscribe();
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Event still fires on instance, but dimension is not subscribed
        Assert.Equal(1, harness.BlockTransformedChangedCount);
        Assert.Null(harness.Dimension.SubscribedInstance);
    }

    [Fact]
    public void SwitchingDimensionSubscription_OldDimensionStopsReceiving()
    {
        // Arrange
        var harness = CreateHarness();
        var dimension2 = TestHelpers.CreateTestDimension(harness.MockWorld);
        harness.RegisterBlock(100);
        harness.RegisterBlock(200);

        // Switch subscription to dimension2
        harness.Dimension.Unsubscribe();
        dimension2.SubscribeTo(harness.Instance);
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert
        Assert.Null(harness.Dimension.SubscribedInstance);
        Assert.Same(harness.Instance, dimension2.SubscribedInstance);
    }

    #endregion

    #region Full Event Chain Tests

    [Fact]
    public void BlockChange_TriggersFullEventChain()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);

        // Track all events
        bool blockUntransformedChanged = false;
        bool blockTransformedChanged = false;
        bool rotationInfoChanged = false;

        harness.Instance.OnBlockUntransformedChanged += (s, e) => blockUntransformedChanged = true;
        harness.Instance.OnPlacementBlockChanged += (s, e) => blockTransformedChanged = true;
        harness.Instance.OnRotationInfoChanged += (s, e) => rotationInfoChanged = true;
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Full chain fires
        Assert.True(blockUntransformedChanged, "OnBlockUntransformedChanged should fire");
        Assert.True(blockTransformedChanged, "OnPlacementBlockChanged should fire");
        Assert.True(rotationInfoChanged, "OnRotationInfoChanged should fire");
    }

    [Fact]
    public void EventChain_EventsFireInCorrectOrder()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);

        var eventOrder = new List<string>();

        harness.Instance.OnBlockUntransformedChanged += (s, e) => eventOrder.Add("BlockUntransformed");
        harness.Instance.OnRotationInfoChanged += (s, e) => eventOrder.Add("RotationInfo");
        harness.Instance.OnPlacementBlockChanged += (s, e) => eventOrder.Add("PlacementBlock");
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Events fire in actual implementation order:
        // 1. RotationInfo (rotation info created for new block)
        // 2. BlockUntransformed (base block set)
        // 3. PlacementBlock (final placement block)
        Assert.Equal(3, eventOrder.Count);
        Assert.Equal("RotationInfo", eventOrder[0]);
        Assert.Equal("BlockUntransformed", eventOrder[1]);
        Assert.Equal("PlacementBlock", eventOrder[2]);
    }

    #endregion

    #region Rotation Mode Property Tests

    [Fact]
    public void RotationModeNone_HasCorrectProperties()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:staticblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.None, orientationCount: 1);

        // Assert
        var rotation = harness.Instance.Rotation;
        Assert.NotNull(rotation);
        Assert.Equal(EBuildBrushRotationMode.None, rotation.Mode);
        Assert.False(rotation.CanRotate);
        Assert.Equal(1, rotation.OrientationCount);
    }

    [Fact]
    public void RotationModeVariantBased_HasCorrectProperties()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:variantblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.VariantBased, orientationCount: 4);

        // Assert
        var rotation = harness.Instance.Rotation;
        Assert.NotNull(rotation);
        Assert.Equal(EBuildBrushRotationMode.VariantBased, rotation.Mode);
        Assert.True(rotation.CanRotate);
        Assert.True(rotation.HasVariants);
        Assert.False(rotation.HasRotatableEntity);
        Assert.Equal(4, rotation.OrientationCount);
    }

    [Fact]
    public void RotationModeRotatable_HasCorrectProperties()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:rotatableblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Rotatable, orientationCount: 4);

        // Assert
        var rotation = harness.Instance.Rotation;
        Assert.NotNull(rotation);
        Assert.Equal(EBuildBrushRotationMode.Rotatable, rotation.Mode);
        Assert.True(rotation.CanRotate);
        Assert.False(rotation.HasVariants);
        Assert.True(rotation.HasRotatableEntity);
        Assert.Equal(4, rotation.OrientationCount);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void NullBlockId_HandledGracefully()
    {
        // Arrange
        var harness = CreateHarness();
        harness.AssertDefaultState();

        // Act & Assert - Should not throw
        harness.Instance.BlockId = 0;
        Assert.Null(harness.Instance.BlockUntransformed);
    }

    [Fact]
    public void UnregisteredBlockId_HandledGracefully()
    {
        // Arrange
        var harness = CreateHarness();
        // Don't register block 999
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 999;

        // Assert - BlockUntransformed should be null for unregistered block
        Assert.Null(harness.Instance.BlockUntransformed);
    }

    [Fact]
    public void RapidBlockChanges_AllEventsFireCorrectly()
    {
        // Arrange
        var harness = CreateHarness();
        for (int i = 1; i <= 10; i++)
        {
            harness.RegisterBlock(i * 100, $"game:block{i}");
        }
        harness.AssertDefaultState();

        // Act - Rapid changes
        for (int i = 1; i <= 10; i++)
        {
            harness.Instance.BlockId = i * 100;
        }

        // Assert
        Assert.Equal(10, harness.BlockTransformedChangedCount);
    }

    #endregion

    #region Tessellation Trigger Tests - Block Changes

    [Fact]
    public void BlockChange_TriggersSingleTessellation()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Exactly 1 dimension dirty, 1 tessellation
        Assert.Equal(1, harness.BlockTransformedChangedCount);
        Assert.Equal(1, harness.DimensionDirtyCount);
        Assert.Equal(1, harness.TessellationCallCount);
    }

    [Fact]
    public void ThreeBlockChanges_TriggerThreeTessellations()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100, "game:block1");
        harness.RegisterBlock(200, "game:block2");
        harness.RegisterBlock(300, "game:block3");
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;
        harness.Instance.BlockId = 200;
        harness.Instance.BlockId = 300;

        // Assert - Exactly 3 of each
        Assert.Equal(3, harness.BlockTransformedChangedCount);
        Assert.Equal(3, harness.DimensionDirtyCount);
        Assert.Equal(3, harness.TessellationCallCount);
    }

    [Fact]
    public void TenBlockChanges_TriggerTenTessellations()
    {
        // Arrange
        var harness = CreateHarness();
        for (int i = 1; i <= 10; i++)
        {
            harness.RegisterBlock(i * 100, $"game:block{i}");
        }
        harness.AssertDefaultState();

        // Act
        for (int i = 1; i <= 10; i++)
        {
            harness.Instance.BlockId = i * 100;
        }

        // Assert - Exactly 10 of each
        Assert.Equal(10, harness.BlockTransformedChangedCount);
        Assert.Equal(10, harness.DimensionDirtyCount);
        Assert.Equal(10, harness.TessellationCallCount);
    }

    #endregion

    #region Tessellation Trigger Tests - Orientation Changes

    [Fact]
    public void VariantBased_FourOrientationChanges_TriggerFourTessellations()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:variantblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.VariantBased, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Cycle through all 4 orientations
        harness.Instance.OrientationIndex = 1;
        harness.Instance.OrientationIndex = 2;
        harness.Instance.OrientationIndex = 3;
        harness.Instance.OrientationIndex = 0;

        // Assert - Exactly 4 of each
        Assert.Equal(4, harness.OrientationChangedCount);
        Assert.Equal(4, harness.DimensionDirtyCount);
        Assert.Equal(4, harness.TessellationCallCount);
    }

    [Fact]
    public void Rotatable_FourOrientationChanges_TriggerFourTessellations()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:rotatableblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Rotatable, orientationCount: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Cycle through all 4 orientations
        harness.Instance.OrientationIndex = 1;
        harness.Instance.OrientationIndex = 2;
        harness.Instance.OrientationIndex = 3;
        harness.Instance.OrientationIndex = 0;

        // Assert - Exactly 4 of each
        Assert.Equal(4, harness.OrientationChangedCount);
        Assert.Equal(4, harness.DimensionDirtyCount);
        Assert.Equal(4, harness.TessellationCallCount);
    }

    [Fact]
    public void None_ZeroOrientationChanges_ZeroTessellations()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:staticblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.None, orientationCount: 1);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Try to change orientation (should have no effect since only 1 orientation)
        harness.Instance.OrientationIndex = 0;

        // Assert - Zero changes
        Assert.Equal(0, harness.OrientationChangedCount);
        Assert.Equal(0, harness.DimensionDirtyCount);
        Assert.Equal(0, harness.TessellationCallCount);
    }

    #endregion

    #region Tessellation Service Parameter Tests

    [Fact]
    public void TessellationService_ReceivedValidParameters()
    {
        // Arrange
        var harness = CreateHarness();
        harness.RegisterBlock(100);

        IMiniDimension? capturedDimension = null;
        BlockPos? capturedMin = null;
        BlockPos? capturedMax = null;

        // Reconfigure mock to capture parameters while still incrementing counter
        harness.MockTessellationService.Setup(t => t.Tessellate(
            It.IsAny<IMiniDimension>(),
            It.IsAny<BlockPos>(),
            It.IsAny<BlockPos>()))
            .Callback<IMiniDimension, BlockPos, BlockPos>((dim, min, max) =>
            {
                capturedDimension = dim;
                capturedMin = min;
                capturedMax = max;
            })
            .Returns(TestHelpers.CreateTestMeshData());

        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Dimension dirty event fired
        Assert.Equal(1, harness.DimensionDirtyCount);

        // Parameters should have been captured by the mock (not by TessellationCallCount 
        // since the mock setup was replaced and won't increment the harness counter)
        Assert.NotNull(capturedDimension);
        Assert.NotNull(capturedMin);
        Assert.NotNull(capturedMax);
    }

    #endregion
}
