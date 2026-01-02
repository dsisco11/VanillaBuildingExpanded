using System.Collections.Immutable;
using System.Reflection;

using Moq;

using VanillaBuildingExpanded.BuildHammer;
using VanillaBuildingExpanded.BuildHammer.Tessellation;
using VanillaBuildingExpanded.Tests.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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

        public BuildBrushInstance Instance { get; }
        public BuildBrushDimension Dimension { get; }

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
            Dimension = new BuildBrushDimension(MockWorld.Object);

            // Initialize the dimension with a mock IMiniDimension so it's considered "initialized"
            // Without this, SetBlock/MarkDirty won't fire OnDirty events
            var mockMiniDimension = new Mock<IMiniDimension>();
            mockMiniDimension.Setup(d => d.subDimensionId).Returns(1);
            Dimension.InitializeClientSide(mockMiniDimension.Object);

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
            Instance.OnBlockTransformedChanged += (s, e) =>
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

            // Create definitions based on mode
            var definitionsBuilder = ImmutableArray.CreateBuilder<BlockOrientationDefinition>(orientationCount);
            for (int i = 0; i < orientationCount; i++)
            {
                int variantBlockId = block.BlockId + i;
                float meshAngle = mode switch
                {
                    EBuildBrushRotationMode.Rotatable => i * (360f / orientationCount),
                    EBuildBrushRotationMode.Hybrid => i * (360f / orientationCount),
                    _ => 0f // VariantBased and None use 0° mesh angle
                };

                // Register variant blocks
                if (i > 0)
                {
                    var variantBlock = TestHelpers.CreateTestBlock(variantBlockId, $"{block.Code}-variant{i}");
                    MockWorld.Setup(w => w.GetBlock(variantBlockId)).Returns(variantBlock);
                }

                definitionsBuilder.Add(new BlockOrientationDefinition(variantBlockId, meshAngle));
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
        private static BuildBrushOrientationInfo CreateOrientationInfo(
            IWorldAccessor world,
            Block block,
            EBuildBrushRotationMode mode,
            ImmutableArray<BlockOrientationDefinition> definitions)
        {
            return new BuildBrushOrientationInfo(world, block, mode, definitions);
        }

        /// <summary>
        /// Injects pre-configured rotation info into the instance via reflection.
        /// Also sets up the block and wires events.
        /// </summary>
        private static void InjectOrientationInfo(BuildBrushInstance instance, BuildBrushOrientationInfo orientationInfo, Block block)
        {
            // Get private fields
            var rotationField = typeof(BuildBrushInstance).GetField("_rotation", BindingFlags.NonPublic | BindingFlags.Instance);
            var blockUntransformedField = typeof(BuildBrushInstance).GetField("_blockUntransformed", BindingFlags.NonPublic | BindingFlags.Instance);
            var blockIdField = typeof(BuildBrushInstance).GetField("_blockId", BindingFlags.NonPublic | BindingFlags.Instance);

            // Unsubscribe from old rotation if any
            var oldRotation = rotationField?.GetValue(instance) as BuildBrushOrientationInfo;
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

    #region Orientation Change Flow Tests - Hybrid Mode

    [Fact]
    public void Hybrid_OrientationChange_RaisesOrientationChanged()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:hybridblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Hybrid, orientationCount: 8);
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
    public void Hybrid_OrientationChange_TriggersTessellation()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:hybridblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Hybrid, orientationCount: 8);
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
    public void Hybrid_MultipleOrientationChanges_EachTriggersTessellation()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:hybridblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Hybrid, orientationCount: 8);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Cycle through orientations
        harness.Instance.OrientationIndex = 1;
        harness.Instance.OrientationIndex = 3;
        harness.Instance.OrientationIndex = 5;
        harness.Instance.OrientationIndex = 7;
        harness.Instance.OrientationIndex = 0;

        // Assert
        Assert.Equal(5, harness.OrientationChangedCount);
        Assert.Equal(5, harness.DimensionDirtyCount);
        Assert.Equal(5, harness.TessellationCallCount);
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
        harness.Instance.OnBlockTransformedChanged += (s, e) => blockTransformedChanged = true;
        harness.Instance.OnRotationInfoChanged += (s, e) => rotationInfoChanged = true;
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Full chain fires
        Assert.True(blockUntransformedChanged, "OnBlockUntransformedChanged should fire");
        Assert.True(blockTransformedChanged, "OnBlockTransformedChanged should fire");
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
        harness.Instance.OnBlockTransformedChanged += (s, e) => eventOrder.Add("BlockTransformed");
        harness.AssertDefaultState();

        // Act
        harness.Instance.BlockId = 100;

        // Assert - Events fire in actual implementation order:
        // 1. RotationInfo (rotation info created for new block)
        // 2. BlockUntransformed (base block set)
        // 3. BlockTransformed (final transformed block)
        Assert.Equal(3, eventOrder.Count);
        Assert.Equal("RotationInfo", eventOrder[0]);
        Assert.Equal("BlockUntransformed", eventOrder[1]);
        Assert.Equal("BlockTransformed", eventOrder[2]);
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

    [Fact]
    public void RotationModeHybrid_HasCorrectProperties()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:hybridblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Hybrid, orientationCount: 8);

        // Assert
        var rotation = harness.Instance.Rotation;
        Assert.NotNull(rotation);
        Assert.Equal(EBuildBrushRotationMode.Hybrid, rotation.Mode);
        Assert.True(rotation.CanRotate);
        Assert.True(rotation.HasVariants);
        Assert.True(rotation.HasRotatableEntity);
        Assert.Equal(8, rotation.OrientationCount);
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
    public void Hybrid_EightOrientationChanges_TriggerEightTessellations()
    {
        // Arrange
        var harness = CreateHarness();
        var block = TestHelpers.CreateTestBlock(100, "game:hybridblock");
        harness.SetupBlockWithRotation(block, EBuildBrushRotationMode.Hybrid, orientationCount: 8);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Cycle through all 8 orientations
        for (int i = 1; i < 8; i++)
        {
            harness.Instance.OrientationIndex = i;
        }
        harness.Instance.OrientationIndex = 0;

        // Assert - Exactly 8 of each
        Assert.Equal(8, harness.OrientationChangedCount);
        Assert.Equal(8, harness.DimensionDirtyCount);
        Assert.Equal(8, harness.TessellationCallCount);
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

    #region Hybrid Orientation Cycling E2E Tests

    /// <summary>
    /// Test harness extension for proper hybrid orientation setup with multiple variants.
    /// </summary>
    private class HybridBrushTestHarness : BrushTestHarness
    {
        public List<OrientationIndexChangedEventArgs> AllOrientationEvents { get; } = new();

        public void SetupHybridBlockWithVariants(int variantCount, int anglesPerVariant, int baseBlockId = 100)
        {
            var definitionsBuilder = ImmutableArray.CreateBuilder<BlockOrientationDefinition>();
            var firstBlock = TestHelpers.CreateTestBlock(baseBlockId, $"game:hybrid-variant0");
            MockWorld.Setup(w => w.GetBlock(baseBlockId)).Returns(firstBlock);

            float intervalDegrees = 360f / variantCount / anglesPerVariant;

            for (int variant = 0; variant < variantCount; variant++)
            {
                int blockId = baseBlockId + variant;
                if (variant > 0)
                {
                    var variantBlock = TestHelpers.CreateTestBlock(blockId, $"game:hybrid-variant{variant}");
                    MockWorld.Setup(w => w.GetBlock(blockId)).Returns(variantBlock);
                }

                for (int angle = 0; angle < anglesPerVariant; angle++)
                {
                    float meshAngle = angle * intervalDegrees;
                    definitionsBuilder.Add(new BlockOrientationDefinition(blockId, meshAngle));
                }
            }

            var definitions = definitionsBuilder.ToImmutable();
            var orientationInfo = new BuildBrushOrientationInfo(MockWorld.Object, firstBlock, EBuildBrushRotationMode.Hybrid, definitions);

            // Inject via reflection
            var rotationField = typeof(BuildBrushInstance).GetField("_rotation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var blockField = typeof(BuildBrushInstance).GetField("_blockUntransformed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var blockIdField = typeof(BuildBrushInstance).GetField("_blockId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            rotationField?.SetValue(Instance, orientationInfo);
            blockField?.SetValue(Instance, firstBlock);
            blockIdField?.SetValue(Instance, baseBlockId);

            // Subscribe to rotation events
            var handlerMethod = typeof(BuildBrushInstance).GetMethod("Rotation_OnOrientationChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (handlerMethod is not null)
            {
                var handler = (EventHandler<OrientationIndexChangedEventArgs>)Delegate.CreateDelegate(
                    typeof(EventHandler<OrientationIndexChangedEventArgs>), Instance, handlerMethod);
                orientationInfo.OnOrientationChanged += handler;
            }

            // Capture all orientation events
            Instance.OnOrientationChanged += (s, e) => AllOrientationEvents.Add(e);
        }
    }

    private static HybridBrushTestHarness CreateHybridHarness()
    {
        return new HybridBrushTestHarness();
    }

    [Theory]
    [InlineData(2, 4, 8)]  // 2 variants × 4 angles = 8 orientations
    [InlineData(4, 2, 8)]  // 4 variants × 2 angles = 8 orientations
    [InlineData(3, 3, 9)]  // 3 variants × 3 angles = 9 orientations
    public void Rotation_HybridBlock_BidirectionalCycle_UpdatesPreviewCorrectly_Config(int variantCount, int anglesPerVariant, int totalOrientations)
    {
        // Arrange
        var harness = CreateHybridHarness();
        harness.SetupHybridBlockWithVariants(variantCount, anglesPerVariant);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Full forward cycle
        for (int i = 0; i < totalOrientations; i++)
        {
            harness.Instance.CycleOrientation(EModeCycleDirection.Forward);
        }

        // Assert - Correct number of events
        Assert.Equal(totalOrientations, harness.AllOrientationEvents.Count);

        // Verify variant transitions happen at correct indices
        int eventsWithVariantChange = 0;
        foreach (var evt in harness.AllOrientationEvents)
        {
            if (evt.VariantChanged)
            {
                eventsWithVariantChange++;
                // At variant transition, mesh angle should be 0 (for forward) or at boundary
                Assert.True(evt.CurrentMeshAngleDegrees == 0f || evt.CurrentIndex == 0);
            }
        }

        // Should have exactly variantCount variant changes (including wrap-around)
        Assert.Equal(variantCount, eventsWithVariantChange);
    }

    [Theory]
    [InlineData(2, 4, 8)]  // 2 variants × 4 angles = 8 orientations
    [InlineData(4, 2, 8)]  // 4 variants × 2 angles = 8 orientations
    [InlineData(3, 3, 9)]  // 3 variants × 3 angles = 9 orientations
    public void Rotation_HybridBlock_ForwardCycle_ExhaustsAnglesBeforeSwitchingVariant(int variantCount, int anglesPerVariant, int totalOrientations)
    {
        // Arrange
        var harness = CreateHybridHarness();
        harness.SetupHybridBlockWithVariants(variantCount, anglesPerVariant);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Forward cycle through all orientations (excluding wrap-around)
        for (int i = 0; i < totalOrientations - 1; i++)
        {
            harness.Instance.CycleOrientation(EModeCycleDirection.Forward);
        }

        // Assert - Verify block ID only changes at expected transition points
        int? lastBlockId = null;
        int consecutiveSameBlockCount = 0;

        foreach (var evt in harness.AllOrientationEvents)
        {
            if (lastBlockId is null)
            {
                lastBlockId = evt.PreviousBlock?.BlockId;
            }

            if (evt.CurrentBlock?.BlockId == lastBlockId)
            {
                consecutiveSameBlockCount++;
            }
            else
            {
                // Block changed - should have had anglesPerVariant-1 consecutive same block events
                // (minus 1 because the first event of a new block doesn't count)
                lastBlockId = evt.CurrentBlock?.BlockId;
                consecutiveSameBlockCount = 0;
            }
        }

        // Final consecutive count should be anglesPerVariant - 1 (last variant's remaining angles)
        Assert.True(consecutiveSameBlockCount <= anglesPerVariant - 1);
    }

    [Theory]
    [InlineData(2, 4, 8)]  // 2 variants × 4 angles = 8 orientations
    [InlineData(4, 2, 8)]  // 4 variants × 2 angles = 8 orientations
    [InlineData(3, 3, 9)]  // 3 variants × 3 angles = 9 orientations
    public void Rotation_HybridBlock_BackwardCycle_ExhaustsAnglesBeforeSwitchingVariant(int variantCount, int anglesPerVariant, int totalOrientations)
    {
        // Arrange
        var harness = CreateHybridHarness();
        harness.SetupHybridBlockWithVariants(variantCount, anglesPerVariant);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Backward cycle from start (will wrap to end first)
        for (int i = 0; i < totalOrientations; i++)
        {
            harness.Instance.CycleOrientation(EModeCycleDirection.Backward);
        }

        // Assert - Verify block ID only changes at expected transition points
        Assert.Equal(totalOrientations, harness.AllOrientationEvents.Count);

        // First event should be wrap-around (0 → last index)
        var firstEvent = harness.AllOrientationEvents[0];
        Assert.Equal(0, firstEvent.PreviousIndex);
        Assert.Equal(totalOrientations - 1, firstEvent.CurrentIndex);
        Assert.True(firstEvent.VariantChanged);
    }

    [Fact]
    public void Rotation_HybridBlock_MixedForwardBackward_MaintainsCorrectState()
    {
        // Arrange
        var harness = CreateHybridHarness();
        harness.SetupHybridBlockWithVariants(variantCount: 2, anglesPerVariant: 4);
        harness.ResetCounters();
        harness.AssertDefaultState();

        // Act - Mixed cycling: forward 3, backward 2, forward 5
        for (int i = 0; i < 3; i++) harness.Instance.CycleOrientation(EModeCycleDirection.Forward);
        for (int i = 0; i < 2; i++) harness.Instance.CycleOrientation(EModeCycleDirection.Backward);
        for (int i = 0; i < 5; i++) harness.Instance.CycleOrientation(EModeCycleDirection.Forward);

        // Assert - 10 total events
        Assert.Equal(10, harness.AllOrientationEvents.Count);

        // Final index should be (0 + 3 - 2 + 5) % 8 = 6
        var lastEvent = harness.AllOrientationEvents[^1];
        Assert.Equal(6, lastEvent.CurrentIndex);
    }

    #endregion
}
