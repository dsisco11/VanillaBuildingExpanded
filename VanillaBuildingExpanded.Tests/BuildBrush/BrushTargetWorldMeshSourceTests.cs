using System.Numerics;
using System.Runtime.CompilerServices;

using HarmonyLib;
using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;

using Fixture = VanillaBuildingExpanded.Tests.BuildBrush.BrushTargetMeshTests.Fixture;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

[Collection("Native snapping capture")]
public sealed class BrushTargetWorldMeshSourceTests
{
    [Fact]
    public void NativeHooksInstallAgainstTheInstalledEngineWithoutReplacingItsMethods()
    {
        const string id = "vbe.tests.snapping.capture";
        Harmony harmony = new(id);
        try
        {
            var patched = harmony.CreateClassProcessor(typeof(BrushTargetNativeMeshCapture)).Patch();
            Assert.Equal(3, patched.Count);
            var originals = Harmony.GetAllPatchedMethods()
                .Where(method => method.DeclaringType == typeof(ChunkTesselator)
                    && Harmony.GetPatchInfo(method).Owners.Contains(id)).ToArray();
            Assert.Equal(3, originals.Length);
            Assert.All(originals, method => Assert.DoesNotContain(
                Harmony.GetPatchInfo(method).Prefixes, patch => patch.owner == id && patch.PatchMethod.ReturnType == typeof(bool)));
        }
        finally { harmony.UnpatchAll(id); }
    }

    [Fact]
    public void ActualPoolDeltasPreserveNativeTransformsAndIgnoreOtherBlocksAndMaterialAttributes()
    {
        using Fixture f = new();
        using var source = new BrushTargetWorldMeshSource(f.Api.Object);
        source.GetMesh(f.Block, f.Position);
        var request = Assert.IsType<BrushTargetWorldMeshSource.Request>(
            BrushTargetWorldMeshSource.FindRequest(0, 0, 0));
        BrushTargetMeshCapture capture = new(request);
        MeshData pool = BrushTargetMeshTests.CreateCuboid(Vector3.Zero, Vector3.One);
        MeshData[][][] pools = [[[pool]]];
        var before = BrushTargetMeshCapture.Snapshot(pools);
        MeshData expected = BrushTargetMeshTests.CreateCuboid(Vector3.Zero, new Vector3(1, 0.5f, 1));
        expected.Translate(0.13f, 0.2f, -0.07f);
        MeshData native = expected.Clone().Translate(f.Position.X, f.Position.Y, f.Position.Z);
        pool.AddMeshData(native);
        capture.Append(before);
        MeshData result = Assert.IsType<MeshData>(capture.Finish());
        Assert.Equal(expected.Indices, result.Indices);
        for (int i = 0; i < expected.VerticesCount * 3; i++)
            Assert.Equal(expected.xyz[i], result.xyz[i], 5);
        Assert.Equal(expected.Flags.Select(flags =>
            flags & (VertexFlags.NormalBitMask | VertexFlags.ZOffsetBitMask)), result.Flags);
        Assert.Null(result.Normals);
        Assert.Null(result.Uv);
        Assert.Null(result.Rgba);
        Assert.Null(result.CustomFloats);
        Assert.Null(result.CustomShorts);
        Assert.Null(result.CustomInts);
        Assert.Null(result.CustomBytes);
        Assert.NotSame(pool.xyz, result.xyz);
        Assert.Equal(48, pool.VerticesCount);
        using BrushTargetMeshCache cache = new(f.Api.Object, (_, _) => result);
        var target = Assert.IsType<BrushTargetGeometry>(cache.GetTarget(f.Presentation()));
        target.Mesh.GetOrUpload();
        Assert.Equal(1, f.Uploads);
    }

    [Theory]
    [InlineData(EnumChunkRenderPass.Opaque)]
    [InlineData(EnumChunkRenderPass.OpaqueNoCull)]
    [InlineData(EnumChunkRenderPass.BlendNoCull)]
    [InlineData(EnumChunkRenderPass.Transparent)]
    [InlineData(EnumChunkRenderPass.Liquid)]
    [InlineData(EnumChunkRenderPass.TopSoil)]
    [InlineData(EnumChunkRenderPass.Meta)]
    [InlineData(EnumChunkRenderPass.OpaqueWaterPlant)]
    [InlineData(EnumChunkRenderPass.Decor)]
    public void CaptureRetainsNativeNormalsAndDepthOffsetsWithTheirActualPass(EnumChunkRenderPass pass)
    {
        using Fixture f = new();
        using var source = new BrushTargetWorldMeshSource(f.Api.Object);
        source.GetMesh(f.Block, f.Position);
        BrushTargetMeshCapture capture = new(BrushTargetWorldMeshSource.FindRequest(0, 0, 0)!);
        MeshData pool = f.Borrowed.Clone();
        pool.Clear();
        MeshData[][] passes = new MeshData[9][];
        passes[(int)pass] = [pool];
        var before = BrushTargetMeshCapture.Snapshot([passes]);
        MeshData native = f.Borrowed.Clone().Translate(f.Position.X, f.Position.Y, f.Position.Z);
        const int upNormal = 7 << 18;
        const int depthOffset = 2 << 8;
        Array.Fill(native.Flags, upNormal | depthOffset | 0xFF | (1 << 11));
        pool.AddMeshData(native);
        capture.Append(before);
        MeshData result = Assert.IsType<MeshData>(capture.Finish());
        Assert.All(result.Flags, flags => Assert.Equal(upNormal | depthOffset | (int)pass, flags));
        Assert.Null(result.Normals);
        Assert.Null(result.Uv);
        Assert.Null(result.Rgba);
        string vertex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets",
            "vanillabuildingexpanded", "shaders", "vbe-snapping.vsh"));
        // Native UploadMesh assigns consecutive slots only for populated attributes.
        Assert.Contains("layout(location = 0) in vec3 vertexPositionIn;", vertex);
        Assert.Contains("layout(location = 1) in int renderFlagsIn;", vertex);
        using BrushTargetMeshCache cache = new(f.Api.Object, (_, _) => result);
        var target = Assert.IsType<BrushTargetGeometry>(cache.GetTarget(f.Presentation()));
        Assert.NotSame(result.Flags, target.Mesh.Data.Flags);
        Assert.Equal(result.Flags, target.Mesh.Data.Flags);
        target.Mesh.GetOrUpload();
        Assert.Equal(1, f.Uploads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AllCloseRangePoolsAreRetainedButTheFarReplacementIsExcluded(bool detail, bool common)
    {
        using Fixture f = new();
        using var source = new BrushTargetWorldMeshSource(f.Api.Object);
        source.GetMesh(f.Block, f.Position);
        var request = BrushTargetWorldMeshSource.FindRequest(0, 0, 0)!;
        BrushTargetMeshCapture capture = new(request);
        MeshData near = f.Borrowed.Clone(), shared = f.Borrowed.Clone();
        MeshData normal = f.Borrowed.Clone(), far = f.Borrowed.Clone();
        near.Clear();
        shared.Clear();
        normal.Clear();
        far.Clear();
        MeshData[][][] pools = [[[near]], [[shared]], [[normal]], [[far]]];
        var before = BrushTargetMeshCapture.Snapshot(pools);
        if (detail) near.AddMeshData(f.Borrowed);
        if (common) shared.AddMeshData(f.Borrowed);
        normal.AddMeshData(f.Borrowed);
        far.AddMeshData(f.Borrowed);
        capture.Append(before);
        MeshData result = Assert.IsType<MeshData>(capture.Finish());
        Assert.Equal(f.Borrowed.IndicesCount * (1 + (detail ? 1 : 0) + (common ? 1 : 0)), result.IndicesCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachedDecorOwnershipDoesNotDependOnNativeParityFlags(bool nativeDecorFlag)
    {
        using Fixture f = new();
        using var source = new BrushTargetWorldMeshSource(f.Api.Object);
        source.GetMesh(f.Block, f.Position);
        BrushTargetMeshCapture capture = new(BrushTargetWorldMeshSource.FindRequest(0, 0, 0)!);
        TCTCache vars = (TCTCache)RuntimeHelpers.GetUninitializedObject(typeof(TCTCache));
        vars.index3d = ((f.Position.Y & 31) * 32 + (f.Position.Z & 31)) * 32 + (f.Position.X & 31);
        vars.isDecorTesselate = nativeDecorFlag;
        Block decor = new() { BlockId = 20 };
        Assert.False(capture.Matches(vars, decor));
        capture.IsDecorPass = true;
        Assert.True(capture.Matches(vars, decor));
        vars.index3d++;
        Assert.False(capture.Matches(vars, decor));
        capture.IsDecorPass = false;
        vars.index3d--;
        Assert.False(capture.Matches(vars, decor));
        Assert.True(capture.Matches(vars, f.Block));
    }

    [Fact]
    public void CaptureRequestsArePositionAndDimensionSpecificAndDoNotRepeatEveryFrame()
    {
        using Fixture f = new();
        using var source = new BrushTargetWorldMeshSource(f.Api.Object);
        for (int i = 0; i < 10; i++) Assert.Null(source.GetMesh(f.Block, f.Position));
        f.Blocks.Verify(b => b.MarkBlockDirty(It.IsAny<BlockPos>(), (IPlayer)null!), Times.Once);
        Assert.NotNull(BrushTargetWorldMeshSource.FindRequest(0, 0, 0));
        Assert.Null(BrushTargetWorldMeshSource.FindRequest(0, 0, 0, skipChunkCenter: true));
        Assert.Null(BrushTargetWorldMeshSource.FindRequest(0, 1024, 0));
        BlockPos next = new(35, 40, 37, 1);
        source.GetMesh(f.Block, next);
        Assert.Null(BrushTargetWorldMeshSource.FindRequest(0, 0, 0));
        Assert.NotNull(BrushTargetWorldMeshSource.FindRequest(1, 1025, 1));
    }

    [Fact]
    public void QueuedStaleCapturesAndDisposedSessionsCannotPublishOrRetainTheirMeshes()
    {
        using Fixture f = new();
        Mock<IClientEventAPI> events = new();
        f.Api.SetupGet(a => a.Event).Returns(events.Object);
        Queue<Action> queue = new();
        events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => queue.Enqueue(action));
        using var source = new BrushTargetWorldMeshSource(f.Api.Object);
        source.GetMesh(f.Block, f.Position);
        var old = BrushTargetWorldMeshSource.FindRequest(0, 0, 0)!;
        MeshData stale = f.Borrowed.Clone();
        source.Publish(old, stale, null);
        source.Invalidate();
        queue.Dequeue()();
        Assert.Null(stale.xyz);
        Assert.Null(source.GetMesh(f.Block, f.Position));
        var current = BrushTargetWorldMeshSource.FindRequest(0, 0, 0)!;
        MeshData captured = f.Borrowed.Clone();
        source.Publish(current, captured, null);
        queue.Dequeue()();
        Assert.Same(captured, source.GetMesh(f.Block, f.Position));
        MeshData same = captured.Clone();
        source.Publish(current, same, null);
        queue.Dequeue()();
        Assert.Null(same.xyz);
        Assert.Same(captured, source.GetMesh(f.Block, f.Position));
        MeshData changedFlags = captured.Clone();
        changedFlags.Flags[0] ^= VertexFlags.ZOffsetBitMask;
        source.Publish(current, changedFlags, null);
        queue.Dequeue()();
        Assert.Null(captured.xyz);
        Assert.Same(changedFlags, source.GetMesh(f.Block, f.Position));
        source.Dispose();
        Assert.Null(changedFlags.xyz);
        MeshData late = f.Borrowed.Clone();
        source.Publish(current, late, null);
        Assert.Null(late.xyz);
        Assert.Empty(queue);
        Assert.Null(BrushTargetWorldMeshSource.FindRequest(0, 0, 0));
    }
}
