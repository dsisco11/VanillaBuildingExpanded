using System;
using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal readonly record struct BrushTargetBounds(Vector3 Min, Vector3 Max)
{
    private const float Tolerance = 0.00001f;

    public bool CanMark(BrushSnappingPresentation presentation, MeshData data)
    {
        Vec3d hit = presentation.TargetHitPosition;
        if (!double.IsFinite(hit.X) || !double.IsFinite(hit.Y) || !double.IsFinite(hit.Z))
            return false;

        BlockFacing face = presentation.Face;
        BrushSnappingState applied = presentation.Result.AppliedSnapping;
        bool markU = applied.FaceU == 0;
        bool markV = applied.FaceV == 0;
        // Raycast hits can lie on selection boxes rather than the rendered triangles.
        for (int index = 0; index < data.IndicesCount; index += 3)
        {
            Vector3 a = ReadVertex(data, data.Indices[index]);
            Vector3 b = ReadVertex(data, data.Indices[index + 1]);
            Vector3 c = ReadVertex(data, data.Indices[index + 2]);
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            if (Component(normal, face.Axis) * (face.Negative ? -1 : 1) <= 0)
                continue;

            Vector2 uvA = face.ToAB(a);
            Vector2 uvB = face.ToAB(b);
            Vector2 uvC = face.ToAB(c);
            Vector2 min = Vector2.Min(uvA, Vector2.Min(uvB, uvC));
            Vector2 max = Vector2.Max(uvA, Vector2.Max(uvB, uvC));
            markU |= IntersectsRegion(applied.FaceU, min.X, max.X);
            markV |= IntersectsRegion(applied.FaceV, min.Y, max.Y);
            if (markU && markV) return true;
        }
        return false;
    }

    private static bool IntersectsRegion(int direction, float min, float max) =>
        direction == 0 || (direction < 0 ? min < 0.35f : max > 0.65f);

    public static bool TryRead(MeshData data, bool requireUnitCube, out BrushTargetBounds bounds)
    {
        bounds = default;
        if (data.mode != EnumDrawMode.Triangles || data.VerticesCount < 3 || data.IndicesCount < 3
            || data.IndicesCount % 3 != 0 || data.xyz is null || data.xyz.LongLength < (long)data.VerticesCount * 3
            || data.Indices is null || data.Indices.Length < data.IndicesCount
            || data.HasAnyWindModeSet || data.CustomFloats is not null || data.CustomInts is not null
            || data.CustomBytes is not null || data.CustomShorts is not null)
            return false;

        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        for (int vertex = 0; vertex < data.VerticesCount; vertex++)
        {
            Vector3 point = ReadVertex(data, vertex);
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                return false;
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        for (int index = 0; index < data.IndicesCount; index++)
            if (data.Indices[index] < 0 || data.Indices[index] >= data.VerticesCount) return false;
        for (int index = 0; index < data.IndicesCount; index += 3)
        {
            Vector3 a = ReadVertex(data, data.Indices[index]);
            Vector3 b = ReadVertex(data, data.Indices[index + 1]);
            Vector3 c = ReadVertex(data, data.Indices[index + 2]);
            float areaSquared = Vector3.Cross(b - a, c - a).LengthSquared();
            if (!float.IsFinite(areaSquared) || areaSquared <= 0) return false;
        }

        if (data.Flags is not null)
        {
            if (data.Flags.Length < data.VerticesCount) return false;
            for (int vertex = 0; vertex < data.VerticesCount; vertex++)
                if ((data.Flags[vertex] & (VertexFlags.WindModeBitsMask | VertexFlags.ZOffsetBitMask)) != 0)
                    return false;
        }
        if (data.RenderPassesAndExtraBits is not null)
        {
            foreach (short pass in data.RenderPassesAndExtraBits)
                if (pass != -1 && !BrushTargetMeshSource.IsSupportedRenderPass((EnumChunkRenderPass)pass)) return false;
        }

        if (requireUnitCube && !MatchesNativeCube(data, min, max)) return false;
        bounds = new BrushTargetBounds(min, max);
        return true;
    }

    private static bool MatchesNativeCube(MeshData data, Vector3 min, Vector3 max)
    {
        // Native cube drawing uses unit faces, not the block's arbitrary JSON mesh.
        if (data.VerticesCount != 24 || data.IndicesCount != 36
            || Vector3.DistanceSquared(min, Vector3.Zero) > Tolerance * Tolerance
            || Vector3.DistanceSquared(max, Vector3.One) > Tolerance * Tolerance)
            return false;
        int faces = 0;
        for (int quad = 0; quad < 6; quad++)
        {
            int first = quad * 4;
            for (int index = 0; index < 6; index++)
            {
                if (data.Indices[quad * 6 + index] != first + CubeMeshUtil.BaseCubeVertexIndices[index])
                    return false;
            }
            Vector3 a = ReadVertex(data, first);
            Vector3 b = ReadVertex(data, first + 1);
            Vector3 c = ReadVertex(data, first + 2);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            BlockFacing? face = GetFacing(normal);
            if (face is null || (faces & face.Flag) != 0) return false;
            faces |= face.Flag;

            Vector3 plane = face.Negative ? min : max;
            float expectedPlane = Component(plane, face.Axis);
            Vector2 minUv = face.ToAB(min);
            Vector2 maxUv = face.ToAB(max);
            int corners = 0;
            int firstCorner = 0;
            for (int vertex = 0; vertex < 4; vertex++)
            {
                Vector3 point = ReadVertex(data, first + vertex);
                if (!Near(Component(point, face.Axis), expectedPlane)) return false;
                Vector2 uv = face.ToAB(point);
                int u = Near(uv.X, minUv.X) ? 0 : Near(uv.X, maxUv.X) ? 1 : -1;
                int v = Near(uv.Y, minUv.Y) ? 0 : Near(uv.Y, maxUv.Y) ? 1 : -1;
                if (u < 0 || v < 0) return false;
                int corner = u | v << 1;
                if ((corners & 1 << corner) != 0) return false;
                corners |= 1 << corner;
                if (vertex == 0) firstCorner = corner;
                if (vertex == 2 && (firstCorner ^ corner) != 3) return false;
            }
        }
        return faces == 63;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) <= Tolerance;
    private static float Component(Vector3 vector, EnumAxis axis) =>
        axis switch { EnumAxis.X => vector.X, EnumAxis.Y => vector.Y, _ => vector.Z };
    private static Vector3 ReadVertex(MeshData data, int index) =>
        new(data.xyz[index * 3], data.xyz[index * 3 + 1], data.xyz[index * 3 + 2]);

    private static BlockFacing? GetFacing(Vector3 normal)
    {
        if (Math.Abs(normal.X) > Tolerance && Near(normal.Y, 0) && Near(normal.Z, 0))
            return normal.X < 0 ? BlockFacing.WEST : BlockFacing.EAST;
        if (Math.Abs(normal.Y) > Tolerance && Near(normal.X, 0) && Near(normal.Z, 0))
            return normal.Y < 0 ? BlockFacing.DOWN : BlockFacing.UP;
        if (Math.Abs(normal.Z) > Tolerance && Near(normal.X, 0) && Near(normal.Y, 0))
            return normal.Z < 0 ? BlockFacing.NORTH : BlockFacing.SOUTH;
        return null;
    }
}
