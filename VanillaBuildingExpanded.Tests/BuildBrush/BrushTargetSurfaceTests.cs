using System.Numerics;

using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using Xunit;

using Fixture = VanillaBuildingExpanded.Tests.BuildBrush.BrushTargetMeshTests.Fixture;
using TestMeshRef = VanillaBuildingExpanded.Tests.BuildBrush.BrushTargetMeshTests.TestMeshRef;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushTargetSurfaceTests
{
    [Theory]
    [InlineData("wedge")]
    [InlineData("multi-element")]
    [InlineData("recessed")]
    [InlineData("alternate-triangulation")]
    public void ActualStaticMesh_IsPreservedThroughQualificationReuseUploadAndInvalidation(string shape)
    {
        using Fixture f = new();
        f.Borrowed = Shape(shape);
        float[] positions = (float[])f.Borrowed.xyz.Clone();
        int[] indices = (int[])f.Borrowed.Indices.Clone();
        double height = shape switch { "wedge" => 0.9, "recessed" => 0.6, _ => 1 };
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, height)));
        Assert.NotSame(f.Borrowed, target.Mesh.Data);
        Assert.Equal(positions, target.Mesh.Data.xyz);
        Assert.Equal(indices, target.Mesh.Data.Indices);
        Assert.Equal(f.Borrowed.VerticesCount, target.Mesh.Data.VerticesCount);
        Assert.Equal(f.Borrowed.IndicesCount, target.Mesh.Data.IndicesCount);
        TestMeshRef gpu = Assert.IsType<TestMeshRef>(target.Mesh.GetOrUpload());
        f.Render.Verify(x => x.UploadMesh(target.Mesh.Data), Times.Once);

        f.Position = new BlockPos(100, 200, 300);
        var next = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(
            f.Presentation(BlockFacing.UP, 0.8, 0.1, shape == "wedge" ? 0.8 : height)));
        Assert.Same(target.Mesh, next.Mesh);
        Assert.Same(gpu, next.Mesh.GetOrUpload());
        Matrixf model = new();
        next.SetModelMatrix(model, new Matrixf().Values, new Vec3d());
        Assert.Equal(100, model.Values[12]);
        Assert.Equal(200, model.Values[13]);
        Assert.Equal(300, model.Values[14]);
        Assert.Equal(1, f.Uploads);

        f.Cache.InvalidateGeometry(f.Block.Id);
        Assert.Equal(1, gpu.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => target.Mesh.GetOrUpload());
        Assert.Equal(positions, f.Borrowed.xyz);
        Assert.Equal(indices, f.Borrowed.Indices);
        var rebuilt = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, height)));
        Assert.NotSame(target.Mesh, rebuilt.Mesh);
        Assert.Equal(positions, rebuilt.Mesh.Data.xyz);
        Assert.NotSame(gpu, rebuilt.Mesh.GetOrUpload());
        Assert.Equal(2, f.Uploads);
    }

    [Fact]
    public void RecessedSurface_DoesNotRequireAHitOnItsPlaneOrTheOuterBounds()
    {
        using Fixture f = new();
        f.Borrowed = Shape("recessed");
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(
            f.Presentation(BlockFacing.UP, 0.9, 0.1, 0.6)));
        Assert.Equal(1, target.Mesh.Bounds.Max.Y);
        Assert.Equal(0.6, target.Presentation.TargetHitPosition.Y);
        Assert.NotNull(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, 0.8)));
        Assert.NotNull(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, 1)));
    }

    [Theory]
    [InlineData("multi-element")]
    [InlineData("wedge")]
    public void SelectionBoxHit_NeedNotIntersectRenderedGeometry(string shape)
    {
        using Fixture f = new();
        f.Borrowed = Shape(shape);
        Assert.NotNull(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.5, 0.1, 1)));
    }

    [Fact]
    public void ReversedWinding_IsPassedToTheShaderRatherThanRejectingTheTarget()
    {
        using Fixture f = new();
        f.Borrowed = Shape("wedge");
        for (int i = 0; i < 6; i += 3)
            (f.Borrowed.Indices[i + 1], f.Borrowed.Indices[i + 2]) =
                (f.Borrowed.Indices[i + 2], f.Borrowed.Indices[i + 1]);
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, 0.9)));
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        Assert.Equal(0, f.Uploads);
    }

    [Fact]
    public void DirectionalCoverage_IsClippedByTheShaderWithoutInventingGeometry()
    {
        using Fixture f = new();
        f.Borrowed = Mesh([0, 0, 0, 0, 0, 0.2f, 0.2f, 0, 0, 1, 1, 1], [0, 1, 2]);
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, 1)));
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        Assert.NotNull(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.1, 0.1, 1)));
    }

    [Fact]
    public void IndependentDirections_CanHaveAreaOnDifferentSelectedFacingTriangles()
    {
        using Fixture f = new();
        f.Borrowed = Mesh(
            [0.7f, 0, 0.45f, 0.7f, 0, 0.55f, 1, 0, 0.45f,
             0.45f, 0.5f, 0.7f, 0.45f, 0.5f, 1, 0.55f, 0.5f, 0.7f],
            [0, 1, 2, 3, 4, 5]);
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.9, 1)));
        Assert.Equal(1, target.Presentation.Result.AppliedSnapping.FaceU);
        Assert.Equal(1, target.Presentation.Result.AppliedSnapping.FaceV);
    }

    [Theory]
    [InlineData(0.1, 0.1)]
    [InlineData(0.1, 0.5)]
    [InlineData(0.1, 0.9)]
    [InlineData(0.5, 0.1)]
    [InlineData(0.5, 0.9)]
    [InlineData(0.9, 0.1)]
    [InlineData(0.9, 0.5)]
    [InlineData(0.9, 0.9)]
    public void TopSlab_AxisAndDiagonalSnappingBothReachTheShader(double u, double v)
    {
        using Fixture f = new();
        f.Borrowed = BrushTargetMeshTests.CreateCuboid(new Vector3(0, 0.5f, 0), Vector3.One);
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, u, v, 1)));
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        target.Mesh.GetOrUpload();
        Assert.Equal(1, f.Uploads);
    }

    [Fact]
    public void DiagonalSnapping_DoesNotRequireBothBandsToIntersectTheMesh()
    {
        using Fixture f = new();
        f.Borrowed = Mesh([0.7f, 0, 0.45f, 0.7f, 0, 0.55f, 1, 0, 0.45f], [0, 1, 2]);
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.9, 1)));
        Assert.Equal(1, target.Presentation.Result.AppliedSnapping.FaceU);
        Assert.Equal(1, target.Presentation.Result.AppliedSnapping.FaceV);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
    }

    [Theory]
    [InlineData("negative-index")]
    [InlineData("incomplete-triangle")]
    [InlineData("short-positions")]
    [InlineData("degenerate")]
    [InlineData("nonfinite")]
    public void MalformedGeneralTriangles_AreNotUploaded(string defect)
    {
        using Fixture f = new();
        f.Borrowed = Shape("wedge");
        switch (defect)
        {
            case "negative-index": f.Borrowed.Indices[0] = -1; break;
            case "incomplete-triangle": f.Borrowed.IndicesCount--; break;
            case "short-positions": f.Borrowed.xyz = [0, 0, 0]; break;
            case "degenerate": Array.Fill(f.Borrowed.Indices, 0); break;
            case "nonfinite": f.Borrowed.xyz[0] = float.PositiveInfinity; break;
            default: throw new InvalidOperationException();
        }
        Assert.Null(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, 0.9)));
        Assert.Equal(0, f.Uploads);
    }

    [Fact]
    public void NativeCapturedGeometry_IsAuthoritativeRegardlessOfDeclaredDrawType()
    {
        using Fixture f = new();
        f.Block.DrawType = EnumDrawType.Cube;
        f.Borrowed = Shape("wedge");
        Assert.NotNull(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, 0.9)));
    }

    [Fact]
    public void HarmlessDegenerateTriangles_DoNotRejectOtherRenderedSurfaces()
    {
        using Fixture f = new();
        f.Borrowed.Indices[1] = f.Borrowed.Indices[0];
        Assert.NotNull(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1)));
    }

    [Fact]
    public void BakedGeometryOutsideTheCell_IsNotClampedOrRescaledIntoACube()
    {
        using Fixture f = new();
        f.Borrowed = BrushTargetMeshTests.CreateCuboid(new Vector3(-0.2f, 0, 0), new Vector3(1.2f, 0.8f, 1));
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, 0.8)));
        Assert.Equal(-0.2f, target.Mesh.Bounds.Min.X);
        Assert.Equal(1.2f, target.Mesh.Bounds.Max.X);
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
    }

    [Fact]
    public void CenteredNarrowSurface_RetainsActualGeometryForTheCenterRegionFill()
    {
        using Fixture f = new();
        f.Borrowed = BrushTargetMeshTests.CreateCuboid(new Vector3(0.45f, 0, 0.45f), new Vector3(0.55f, 1, 0.55f));
        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.5, 0.5, 1)));
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
    }

    private static MeshData Shape(string name)
    {
        if (name == "wedge")
            return Mesh(
                [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1],
                [0, 5, 2, 0, 3, 5, 0, 2, 1, 3, 4, 5, 1, 2, 5, 1, 5, 4, 0, 1, 4, 0, 4, 3]);
        if (name == "alternate-triangulation")
        {
            MeshData cube = BrushTargetMeshTests.CreateCuboid(Vector3.Zero, Vector3.One);
            for (int quad = 0; quad < 6; quad++)
            {
                int[] diagonal = [0, 1, 3, 1, 2, 3];
                for (int i = 0; i < 6; i++) cube.Indices[quad * 6 + i] = quad * 4 + diagonal[i];
            }
            return cube;
        }
        MeshData first;
        MeshData second;
        if (name == "recessed")
        {
            first = BrushTargetMeshTests.CreateCuboid(Vector3.Zero, new Vector3(1, 0.6f, 1));
            second = BrushTargetMeshTests.CreateCuboid(new Vector3(0, 0.6f, 0.8f), Vector3.One);
        }
        else if (name == "multi-element")
        {
            first = BrushTargetMeshTests.CreateCuboid(Vector3.Zero, new Vector3(0.2f, 1, 1));
            second = BrushTargetMeshTests.CreateCuboid(new Vector3(0.8f, 0, 0), Vector3.One);
        }
        else throw new ArgumentOutOfRangeException(nameof(name));
        return Mesh([.. first.xyz, .. second.xyz],
            [.. first.Indices, .. second.Indices.Select(i => i + first.VerticesCount)]);
    }

    private static MeshData Mesh(float[] positions, int[] indices) => new(positions.Length / 3, indices.Length)
    {
        xyz = positions, VerticesCount = positions.Length / 3,
        Indices = indices, IndicesCount = indices.Length
    };
}
