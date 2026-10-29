using System;
using System.Numerics;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal readonly record struct BrushTargetBounds(Vector3 Min, Vector3 Max)
{
    public static bool TryRead(MeshData data, out BrushTargetBounds bounds)
    {
        bounds = default;
        if (data.mode != EnumDrawMode.Triangles || data.VerticesCount < 3 || data.IndicesCount < 3
            || data.IndicesCount % 3 != 0 || data.xyz is null || data.xyz.LongLength < (long)data.VerticesCount * 3
            || data.Indices is null || data.Indices.Length < data.IndicesCount)
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
        bool hasSurface = false;
        for (int index = 0; index < data.IndicesCount; index += 3)
        {
            Vector3 a = ReadVertex(data, data.Indices[index]);
            Vector3 b = ReadVertex(data, data.Indices[index + 1]);
            Vector3 c = ReadVertex(data, data.Indices[index + 2]);
            float areaSquared = Vector3.Cross(b - a, c - a).LengthSquared();
            if (!float.IsFinite(areaSquared)) return false;
            hasSurface |= areaSquared > 0;
        }
        if (!hasSurface) return false;

        bounds = new BrushTargetBounds(min, max);
        return true;
    }

    private static Vector3 ReadVertex(MeshData data, int index) =>
        new(data.xyz[index * 3], data.xyz[index * 3 + 1], data.xyz[index * 3 + 2]);
}
