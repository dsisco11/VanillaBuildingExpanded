using System.Numerics;

using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

using Fixture = VanillaBuildingExpanded.Tests.BuildBrush.BrushTargetMeshTests.Fixture;
using TestMeshRef = VanillaBuildingExpanded.Tests.BuildBrush.BrushTargetMeshTests.TestMeshRef;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushTargetMeshCacheTests
{
    [Theory]
    [InlineData("replace")]
    [InlineData("geometry")]
    [InlineData("world")]
    [InlineData("resource")]
    [InlineData("shader")]
    [InlineData("dispose")]
    public void InvalidationAndReplacement_ReleaseOnlyOwnedResources_AndCannotReuseOldData(string reason)
    {
        using Fixture f = new();
        MeshData borrowed = f.Borrowed;
        float[] borrowedPositions = borrowed.xyz;
        float[] expected = (float[])borrowedPositions.Clone();
        BrushTargetGeometry target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        MeshData owned = target.Mesh.Data;
        TestMeshRef gpu = Assert.IsType<TestMeshRef>(target.Mesh.GetOrUpload());
        owned.xyz[0] += 0.1f;
        Assert.Equal(expected, borrowed.xyz);

        if (reason == "replace")
        {
            f.Borrowed = BrushTargetMeshTests.CreateCuboid(Vector3.Zero, new Vector3(1, 0.5f, 1));
            Assert.NotSame(target.Mesh,
                Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, plane: 0.5))).Mesh);
        }
        else if (reason == "geometry") f.Cache.InvalidateGeometry(f.Block.Id);
        else if (reason == "dispose") f.Cache.Dispose();
        else f.Cache.Invalidate();

        Assert.Equal(1, gpu.DisposeCount);
        Assert.Null(owned.xyz);
        Assert.Same(borrowedPositions, borrowed.xyz);
        Assert.Equal(expected, borrowed.xyz);
        Assert.Throws<ObjectDisposedException>(() => target.Mesh.GetOrUpload());
        if (reason != "dispose")
        {
            var replacement = Assert.IsType<BrushTargetGeometry>(
                f.Cache.GetTarget(f.Presentation(BlockFacing.UP, plane: reason == "replace" ? 0.5 : 1)));
            Assert.NotSame(target.Mesh, replacement.Mesh);
            Assert.NotSame(gpu, replacement.Mesh.GetOrUpload());
            Assert.Equal(2, f.Uploads);
        }
        f.Cache.Dispose();
        f.Cache.Dispose();
        Assert.Equal(1, gpu.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => f.Cache.GetTarget(f.Presentation()));
        Assert.Throws<ObjectDisposedException>(() => f.Cache.Invalidate());
    }

    [Fact]
    public void UnrelatedGeometryInvalidation_DoesNotRebuildOrUpload()
    {
        using Fixture f = new();
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        MeshRef gpu = target.Mesh.GetOrUpload();
        f.Cache.InvalidateGeometry(999);
        var reused = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        Assert.Same(target.Mesh, reused.Mesh);
        Assert.Same(gpu, reused.Mesh.GetOrUpload());
        Assert.Equal(1, f.Uploads);
    }

    [Fact]
    public void UnsupportedTargetDoesNotExposePreviousTarget_AndReturningToValidTargetReusesGeometry()
    {
        using Fixture f = new();
        var original = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        f.Api.Setup(x => x.TesselatorManager.GetDefaultBlockMesh(f.Block)).Returns((MeshData)null!);
        Assert.Null(f.Cache.GetTarget(f.Presentation()));
        f.Api.Setup(x => x.TesselatorManager.GetDefaultBlockMesh(f.Block)).Returns(() => f.Borrowed);
        var next = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        Assert.NotSame(original.Mesh, next.Mesh);
        Assert.NotSame(original.Presentation, next.Presentation);
    }

    [Fact]
    public void InPlaceGeometryChangeRequiresExplicitInvalidation_IncludingNegativeQualification()
    {
        using Fixture f = new();
        int firstIndex = f.Borrowed.Indices[0];
        f.Borrowed.Indices[0] = -1;
        Assert.Null(f.Cache.GetTarget(f.Presentation()));
        f.Borrowed.Indices[0] = firstIndex;
        Assert.Null(f.Cache.GetTarget(f.Presentation()));
        f.Cache.InvalidateGeometry(f.Block.Id);
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        f.Borrowed.xyz[0] = float.NaN;
        f.Cache.Invalidate();
        Assert.Null(f.Cache.GetTarget(f.Presentation()));
        Assert.Throws<ObjectDisposedException>(() => target.Mesh.GetOrUpload());
    }

    [Fact]
    public void TextureAlphaChanges_DoNotRequalifyOrUploadGeometry()
    {
        using Fixture f = new();
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        MeshRef gpu = target.Mesh.GetOrUpload();
        f.Bitmap.TexturePixels[0] = 0;
        f.Texture.Alpha = 0;
        f.Block.RenderPass = EnumChunkRenderPass.BlendNoCull;
        var next = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        Assert.Same(target.Mesh, next.Mesh);
        Assert.Same(gpu, next.Mesh.GetOrUpload());
        Assert.Equal(1, f.Uploads);
        f.Api.VerifyGet(x => x.BlockTextureAtlas, Times.Never);
    }

    [Fact]
    public void NoDefaultMesh_OmitsTargetAndReleasesPriorOwnedResources()
    {
        using Fixture f = new();
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        TestMeshRef gpu = Assert.IsType<TestMeshRef>(target.Mesh.GetOrUpload());
        f.Api.Setup(x => x.TesselatorManager.GetDefaultBlockMesh(f.Block)).Returns((MeshData)null!);
        Assert.Null(f.Cache.GetTarget(f.Presentation()));
        Assert.Equal(1, gpu.DisposeCount);
        Assert.NotNull(f.Borrowed.xyz);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("w-only")]
    [InlineData("invalid-none")]
    [InlineData("invalid-w-only")]
    public void NonTangentialOutcomes_DoNotAcquireGeometryRegardlessOfValidity(string reason)
    {
        using Fixture f = new();
        if (reason is "invalid-none" or "invalid-w-only")
            f.Inputs.Validator.Setup(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(),
                ref It.Ref<string>.IsAny)).Returns(false);
        var presentation = reason switch
        {
            "none" or "invalid-none" => f.Presentation(mode: EBuildBrushSnapping.None),
            "w-only" or "invalid-w-only" => f.Presentation(u: 0.5, v: 0.5),
            _ => f.Presentation()
        };
        Assert.Null(f.Cache.GetTarget(presentation));
        f.Api.Verify(x => x.TesselatorManager.GetDefaultBlockMesh(It.IsAny<Block>()), Times.Never);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("load-failure")]
    public void UnavailableOrMalformedBitmaps_DoNotParticipateInGeometryQualification(string failure)
    {
        using Fixture f = new();
        if (failure == "missing")
            f.Atlas.Setup(x => x.LoadCompositeBitmap(It.IsAny<AssetLocationAndSource>())).Returns((IBitmap)null!);
        else if (failure == "malformed") f.Bitmap.Width = 2;
        else f.Atlas.Setup(x => x.LoadCompositeBitmap(It.IsAny<AssetLocationAndSource>()))
                .Throws(new InvalidOperationException("load failed"));
        Assert.NotNull(f.Cache.GetTarget(f.Presentation()));
        Assert.NotNull(f.Borrowed.xyz);
        f.Api.VerifyGet(x => x.BlockTextureAtlas, Times.Never);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("uninitialized")]
    [InlineData("exception")]
    public void UploadFailures_AreExplicitWithoutBorrowedDisposal_AndCanRetry(string failure)
    {
        using Fixture f = new();
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        TestMeshRef failed = new() { Ready = false };
        if (failure == "null") f.Render.Setup(x => x.UploadMesh(It.IsAny<MeshData>())).Returns((MeshRef)null!);
        else if (failure == "uninitialized") f.Render.Setup(x => x.UploadMesh(It.IsAny<MeshData>())).Returns(failed);
        else f.Render.Setup(x => x.UploadMesh(It.IsAny<MeshData>())).Throws(new InvalidOperationException("upload failed"));
        Assert.Throws<InvalidOperationException>(() => target.Mesh.GetOrUpload());
        Assert.Equal(failure == "uninitialized" ? 1 : 0, failed.DisposeCount);
        Assert.NotNull(f.Borrowed.xyz);
        Assert.NotNull(target.Mesh.Data.xyz);
        TestMeshRef valid = new();
        f.Render.Setup(x => x.UploadMesh(It.IsAny<MeshData>())).Returns(valid);
        Assert.Same(valid, target.Mesh.GetOrUpload());
        Assert.Same(valid, target.Mesh.GetOrUpload());
        f.Cache.Dispose();
        Assert.Equal(1, valid.DisposeCount);
    }

    [Fact]
    public void MeshResources_RejectAccessFromAnotherThread()
    {
        using Fixture f = new();
        var presentation = f.Presentation();
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(presentation));
        Exception?[] errors = new Exception?[5];
        Thread thread = new(() =>
        {
            errors[0] = Record.Exception(() => f.Cache.GetTarget(presentation));
            errors[1] = Record.Exception(() => f.Cache.InvalidateGeometry(f.Block.Id));
            errors[2] = Record.Exception(() => f.Cache.Invalidate());
            errors[3] = Record.Exception(() => target.Mesh.GetOrUpload());
            errors[4] = Record.Exception(() => f.Cache.Dispose());
        });
        thread.Start();
        thread.Join();
        Assert.All(errors, error => Assert.IsType<InvalidOperationException>(error));
        Assert.Equal(0, f.Uploads);
        Assert.NotNull(f.Cache.GetTarget(presentation));
    }

    [Fact]
    public void CapturedHitAccessor_IsDefensive_AndNonfiniteHitsCannotQualifyGeometry()
    {
        using Fixture f = new();
        var presentation = f.Presentation();
        presentation.TargetHitPosition.Set(0, 0, 0);
        Assert.Equal(0.9, presentation.TargetHitPosition.X);
        var nan = f.Presentation(plane: double.NaN);
        Assert.Null(f.Cache.GetTarget(nan));
        Assert.Null(f.Cache.GetTarget(f.Presentation(plane: double.PositiveInfinity)));
    }
}
