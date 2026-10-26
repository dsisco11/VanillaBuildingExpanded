using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BuildBrushEntityRendererTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceivedVariant_RebuildsWithoutWaitingForDirtyCounter(bool counterArrivesFirst)
    {
        var capi = TestHelpers.CreateMockCapi();
        capi.SetupGet(c => c.World.Side).Returns(EnumAppSide.Client);
        var render = new Mock<IRenderAPI>();
        capi.SetupGet(c => c.Render).Returns(render.Object);
        capi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(It.IsAny<Block>()))
            .Returns(TestHelpers.CreateTestCubeMesh());
        var events = new Mock<IClientEventAPI>();
        capi.SetupGet(c => c.Event).Returns(events.Object);
        Queue<Action> rebuilds = [];
        events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => rebuilds.Enqueue(action));
        var dimension = TestHelpers.CreateMockDimension();
        dimension.SetupGet(d => d.CurrentPos).Returns(new EntityPos());
        BlockPos origin = TestHelpers.CreateMiniDimensionPos(100, 100, 100);
        Block block = TestHelpers.CreateTestBlock(100, "game:fence-ew");
        dimension.Setup(d => d.GetBlock(It.Is<BlockPos>(pos => pos == origin))).Returns(() => block);
        var entity = new BuildBrushEntity();
        entity.AssociateWithDimension(dimension.Object);
        entity.SetPreviewBounds(origin, origin);
        entity.WatchedAttributes.SetBool(BuildBrushEntity.BrushVisibleKey, true);
        var renderer = new BuildBrushEntityRenderer(entity, capi.Object);

        renderer.OnEntityLoaded();
        entity.RefreshClientPreview();
        Assert.Single(rebuilds);
        rebuilds.Dequeue()();
        render.Verify(r => r.UploadMultiTextureMesh(It.Is<MeshData>(mesh => mesh.VerticesCount == 24)), Times.Once);
        entity.RefreshClientPreview();
        Assert.Empty(rebuilds);

        if (counterArrivesFirst)
        {
            entity.IncrementBrushDirtyCounter();
            entity.RefreshClientPreview();
            Assert.Single(rebuilds);
            rebuilds.Dequeue()();
        }

        block = TestHelpers.CreateTestBlock(101, "game:fence-n");
        renderer.DoRender3DOpaque(0, false);
        Assert.Empty(rebuilds);
        entity.RefreshClientPreview();
        Assert.Single(rebuilds);
        rebuilds.Dequeue()();
        Mock.Get(capi.Object.TesselatorManager).Verify(t => t.GetDefaultBlockMesh(block), Times.Once);
        render.Verify(r => r.UploadMultiTextureMesh(It.Is<MeshData>(mesh => mesh.VerticesCount == 24)),
            Times.Exactly(counterArrivesFirst ? 3 : 2));
        entity.RefreshClientPreview();
        Assert.Empty(rebuilds);

        // Same-ID updates still use the counter for entity properties and mesh rotation.
        entity.IncrementBrushDirtyCounter();
        entity.RefreshClientPreview();
        Assert.Single(rebuilds);
        renderer.Dispose();
        rebuilds.Dequeue()();
        entity.IncrementBrushDirtyCounter();
        entity.RefreshClientPreview();
        Assert.Empty(rebuilds);
        render.Verify(r => r.UploadMultiTextureMesh(It.IsAny<MeshData>()),
            Times.Exactly(counterArrivesFirst ? 3 : 2));
    }

    [Fact]
    public void InitiallyMissingChunk_RebuildsWhenPreviewBlockArrives()
    {
        var capi = TestHelpers.CreateMockCapi();
        capi.SetupGet(c => c.World.Side).Returns(EnumAppSide.Client);
        var events = new Mock<IClientEventAPI>();
        capi.SetupGet(c => c.Event).Returns(events.Object);
        Queue<Action> rebuilds = [];
        events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => rebuilds.Enqueue(action));
        var dimension = TestHelpers.CreateMockDimension();
        dimension.SetupGet(d => d.CurrentPos).Returns(new EntityPos());
        BlockPos origin = TestHelpers.CreateMiniDimensionPos(100, 100, 100);
        Block? block = null;
        dimension.Setup(d => d.GetBlock(It.Is<BlockPos>(pos => pos == origin))).Returns(() => block!);
        var entity = new BuildBrushEntity();
        entity.AssociateWithDimension(dimension.Object);
        entity.SetPreviewBounds(origin, origin);
        entity.WatchedAttributes.SetBool(BuildBrushEntity.BrushVisibleKey, true);
        var renderer = new BuildBrushEntityRenderer(entity, capi.Object);

        renderer.OnEntityLoaded();
        entity.RefreshClientPreview();
        Assert.Single(rebuilds);
        rebuilds.Dequeue()();
        entity.RefreshClientPreview();
        Assert.Empty(rebuilds);

        block = TestHelpers.CreateTestBlock(100);
        entity.RefreshClientPreview();
        Assert.Single(rebuilds);
        renderer.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedRebuild_CoalescesRequestsAndUsesLatestStateUnlessDisposed(bool disposeBeforeRebuild)
    {
        var capi = TestHelpers.CreateMockCapi();
        capi.SetupGet(c => c.World.Side).Returns(EnumAppSide.Client);
        var render = new Mock<IRenderAPI>();
        capi.SetupGet(c => c.Render).Returns(render.Object);
        capi.Setup(c => c.TesselatorManager.GetDefaultBlockMesh(It.IsAny<Block>()))
            .Returns(TestHelpers.CreateTestCubeMesh());
        var events = new Mock<IClientEventAPI>();
        capi.SetupGet(c => c.Event).Returns(events.Object);
        Queue<Action> rebuilds = [];
        events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => rebuilds.Enqueue(action));
        var dimension = TestHelpers.CreateMockDimension();
        dimension.SetupGet(d => d.CurrentPos).Returns(new EntityPos());
        BlockPos firstPosition = TestHelpers.CreateMiniDimensionPos(100, 100, 100);
        BlockPos latestPosition = firstPosition.EastCopy();
        Block firstBlock = TestHelpers.CreateTestBlock(100);
        Block latestBlock = TestHelpers.CreateTestBlock(101);
        dimension.Setup(d => d.GetBlock(It.Is<BlockPos>(pos => pos == firstPosition))).Returns(firstBlock);
        dimension.Setup(d => d.GetBlock(It.Is<BlockPos>(pos => pos == latestPosition))).Returns(latestBlock);
        var entity = new BuildBrushEntity();
        entity.AssociateWithDimension(dimension.Object);
        entity.SetPreviewBounds(firstPosition, firstPosition);
        entity.WatchedAttributes.SetBool(BuildBrushEntity.BrushVisibleKey, true);
        var renderer = new BuildBrushEntityRenderer(entity, capi.Object);

        renderer.OnEntityLoaded();
        entity.RefreshClientPreview();
        entity.SetPreviewBounds(latestPosition, latestPosition);
        entity.IncrementBrushDirtyCounter();
        renderer.RebuildMesh();
        entity.RefreshClientPreview();
        Assert.Single(rebuilds);

        if (disposeBeforeRebuild)
            renderer.Dispose();
        rebuilds.Dequeue()();

        Mock.Get(capi.Object.TesselatorManager).Verify(t => t.GetDefaultBlockMesh(firstBlock), Times.Never);
        Mock.Get(capi.Object.TesselatorManager).Verify(t => t.GetDefaultBlockMesh(latestBlock),
            disposeBeforeRebuild ? Times.Never() : Times.Once());
        render.Verify(r => r.UploadMultiTextureMesh(It.IsAny<MeshData>()),
            disposeBeforeRebuild ? Times.Never() : Times.Once());
        entity.RefreshClientPreview();
        Assert.Empty(rebuilds);
        renderer.Dispose();
    }
}
