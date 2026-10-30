using System.Numerics;

using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushSnappingChevronMeshTests
{
    private const int Stride = 21;
    [Theory]
    [InlineData("north")]
    [InlineData("east")]
    [InlineData("south")]
    [InlineData("west")]
    [InlineData("up")]
    [InlineData("down")]
    public void SeparateGeometryCoversAllEightRegionsAndRetainsNativeAttributeSlots(string code)
    {
        BlockFacing face = BlockFacing.FromCode(code);
        MeshData source = Plane(face);
        float[] original = (float[])source.xyz.Clone();
        MeshData glyphs = Assert.IsType<MeshData>(BrushSnappingChevronMesh.Build(source, face));
        Assert.Equal(original, source.xyz);
        Assert.Null(source.CustomFloats);
        Assert.Null(glyphs.Uv);
        Assert.Null(glyphs.Rgba);
        Assert.Null(glyphs.Normals);
        Assert.Equal(new[] { 3, 3, 3, 3, 3, 3, 3 }, glyphs.CustomFloats.InterleaveSizes);
        Assert.Equal(new[] { 0, 12, 24, 36, 48, 60, 72 }, glyphs.CustomFloats.InterleaveOffsets);
        Assert.Equal(84, glyphs.CustomFloats.InterleaveStride);
        Assert.Equal(glyphs.VerticesCount * Stride, glyphs.CustomFloats.Count);
        Assert.Equal(glyphs.VerticesCount, glyphs.Flags.Length);
        Assert.True(BrushTargetBounds.TryRead(glyphs, out _));
        var directions = Enumerable.Range(0, glyphs.VerticesCount)
            .Select(i => new Vector2(glyphs.CustomFloats.Values[i * Stride], glyphs.CustomFloats.Values[i * Stride + 1]))
            .Distinct().ToArray();
        Assert.Equal(8, directions.Length);
        Assert.DoesNotContain(Vector2.Zero, directions);
        foreach (Vector2 direction in directions)
        {
            Vector2 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            for (int i = 0; i < glyphs.VerticesCount; i++)
            {
                var data = glyphs.CustomFloats.Values;
                if (data[i * Stride] != direction.X || data[i * Stride + 1] != direction.Y) continue;
                Vector3 position = Position(glyphs, i);
                Vector2 uv = face.ToAB(position);
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
                Vector2 boundsU = Bounds(direction.X), boundsV = Bounds(direction.Y);
                Assert.InRange(uv.X, boundsU.X - 0.000001f, boundsU.Y + 0.000001f);
                Assert.InRange(uv.Y, boundsV.X - 0.000001f, boundsV.Y + 0.000001f);
                Assert.InRange(data[i * Stride + 2], 0, 1);
                Assert.Equal(source.Flags[0], glyphs.Flags[i]);
                Assert.InRange(Offset(glyphs, i), 5.99999f, 6.00001f);
            }
            float span = direction.X != 0 && direction.Y != 0 ? 0.32f : 0.27f;
            Assert.True(max.X - min.X >= span - 0.00001f);
            Assert.True(max.Y - min.Y >= span - 0.00001f);
        }
        Assert.Contains(Enumerable.Range(0, glyphs.VerticesCount), i => glyphs.CustomFloats.Values[i * Stride + 2] == 0);
        Assert.Contains(Enumerable.Range(0, glyphs.VerticesCount), i => glyphs.CustomFloats.Values[i * Stride + 2] == 1);
        for (int i = 0; i < glyphs.IndicesCount; i += 3)
        {
            Vector3 a = Position(glyphs, glyphs.Indices[i]), b = Position(glyphs, glyphs.Indices[i + 1]),
                c = Position(glyphs, glyphs.Indices[i + 2]);
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), face.Normalf.ToSNT()) >= -0.000001f);
        }
    }

    [Fact]
    public void GeometryIsProjectedOntoTheActualSlopeWithInterpolatedNativeNormalsAndOffsets()
    {
        MeshData source = Plane(BlockFacing.UP);
        for (int i = 0; i < source.VerticesCount; i++)
        {
            source.xyz[i * 3 + 1] = 0.2f + source.xyz[i * 3] * 0.6f;
            int offset = (int)(source.xyz[i * 3] * 2 + source.xyz[i * 3 + 2] * 4);
            source.Flags[i] = (source.Flags[i] & ~VertexFlags.ZOffsetBitMask) | (offset << 8);
        }
        MeshData glyphs = Assert.IsType<MeshData>(BrushSnappingChevronMesh.Build(source, BlockFacing.UP));
        for (int i = 0; i < glyphs.VerticesCount; i++)
        {
            Vector3 point = Position(glyphs, i);
            Assert.InRange(point.Y - (0.2f + point.X * 0.6f), -0.000001f, 0.000001f);
            Assert.InRange(Offset(glyphs, i) - (point.X * 2 + point.Z * 4),
                -0.00001f, 0.00001f);
            Assert.InRange(glyphs.CustomFloats.Values[i * Stride + 4], 0.99999f, 1.00001f);
            var data = glyphs.CustomFloats.Values;
            int offset = i * Stride;
            Vector3 a = new(data[offset + 6], data[offset + 7], data[offset + 8]);
            Vector3 b = new(data[offset + 9], data[offset + 10], data[offset + 11]);
            Vector3 c = new(data[offset + 12], data[offset + 13], data[offset + 14]);
            Vector3 reconstructed = a * data[offset + 15] + b * data[offset + 16] + c * data[offset + 17];
            Assert.InRange(Vector3.Distance(reconstructed, point), 0, 0.000001f);
            Assert.InRange(data[offset + 15] + data[offset + 16] + data[offset + 17], 0.999999f, 1.000001f);
        }
    }

    [Fact]
    public void MissingSurfaceDoesNotCreateAChevronPlane()
    {
        MeshData source = Plane(BlockFacing.NORTH);
        for (int i = 0; i < source.VerticesCount; i++)
        {
            source.xyz[i * 3] = 0.45f + source.xyz[i * 3] * 0.1f;
            source.xyz[i * 3 + 1] = 0.45f + source.xyz[i * 3 + 1] * 0.1f;
        }
        Assert.Null(BrushSnappingChevronMesh.Build(source, BlockFacing.NORTH));
    }

    [Fact]
    public void FaceMeshesAreCachedAcrossDirectionsAndRetiredWithTheTarget()
    {
        using var fixture = new BrushTargetMeshTests.Fixture();
        var first = Assert.IsType<BrushTargetGeometry>(fixture.Cache.GetTarget(fixture.Presentation()));
        var glyphs = Assert.IsType<BrushTargetMesh>(first.Mesh.GetChevrons(BlockFacing.NORTH));
        var gpu = Assert.IsType<BrushTargetMeshTests.TestMeshRef>(glyphs.GetOrUpload());
        var next = Assert.IsType<BrushTargetGeometry>(fixture.Cache.GetTarget(fixture.Presentation(u: 0.1)));
        Assert.Same(glyphs, next.Mesh.GetChevrons(BlockFacing.NORTH));
        Assert.Same(gpu, glyphs.GetOrUpload());
        Assert.Single(fixture.ChevronUploaded);
        fixture.Cache.Invalidate();
        Assert.Equal(1, gpu.DisposeCount);
        Assert.Null(glyphs.Data.xyz);
        Assert.Throws<ObjectDisposedException>(() => glyphs.GetOrUpload());
        Assert.Throws<ObjectDisposedException>(() => first.Mesh.GetChevrons(BlockFacing.NORTH));
    }

    private static MeshData Plane(BlockFacing face)
    {
        Vector3[] positions = [Point(0, 0), Point(1, 0), Point(1, 1), Point(0, 1)];
        bool reverse = Vector3.Dot(Vector3.Cross(positions[1] - positions[0], positions[2] - positions[0]),
            face.Normalf.ToSNT()) < 0;
        return new MeshData(false)
        {
            xyz = positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            Flags = Enumerable.Repeat(VertexFlags.PackNormal(face.Normali) | (6 << 8) | 3, 4).ToArray(),
            Indices = reverse ? [0, 2, 1, 0, 3, 2] : [0, 1, 2, 0, 2, 3],
            VerticesCount = 4, VerticesMax = 4, IndicesCount = 6, IndicesMax = 6,
            mode = EnumDrawMode.Triangles
        };
        Vector3 Point(float u, float v) => face.Axis switch
        {
            EnumAxis.X => new(face.Negative ? 0 : 1, v, u),
            EnumAxis.Y => new(u, face.Negative ? 0 : 1, v),
            _ => new(u, v, face.Negative ? 0 : 1)
        };
    }

    private static Vector2 Bounds(float direction) => direction < 0
        ? new(0, 0.5f - BrushSnapping.CenterThreshold)
        : direction > 0 ? new(0.5f + BrushSnapping.CenterThreshold, 1)
        : new(0.5f - BrushSnapping.CenterThreshold, 0.5f + BrushSnapping.CenterThreshold);
    private static Vector3 Position(MeshData data, int index) =>
        new(data.xyz[index * 3], data.xyz[index * 3 + 1], data.xyz[index * 3 + 2]);
    private static float Offset(MeshData data, int index)
    {
        int offset = index * Stride;
        var attributes = data.CustomFloats.Values;
        return attributes[offset + 15] * attributes[offset + 18]
            + attributes[offset + 16] * attributes[offset + 19]
            + attributes[offset + 17] * attributes[offset + 20];
    }
}
