using System;
using System.Collections.Generic;
using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal static class BrushSnappingChevronMesh
{
    internal const float Width = 0.01f;
    private readonly record struct Point(Vector2 Position, float Coverage);
    private readonly record struct Triangle(Point A, Point B, Point C, Vector2 Direction);

    public static MeshData? Build(MeshData source, BlockFacing face)
    {
        List<Triangle> glyphs = [];
        for (int u = -1; u <= 1; u++)
        for (int v = -1; v <= 1; v++)
            if (u != 0 || v != 0) AddChevron(glyphs, new Vector2(u, v));

        List<float> positions = [], attributes = [];
        List<int> flags = [], indices = [];
        float[] normal = new float[3];
        List<Point> polygon = new(8), scratch = new(8);
        for (int index = 0; index < source.IndicesCount; index += 3)
        {
            int ia = source.Indices[index], ib = source.Indices[index + 1], ic = source.Indices[index + 2];
            Vector3 a = Position(source, ia), b = Position(source, ib), c = Position(source, ic);
            Vector2 pa = face.ToAB(a), pb = face.ToAB(b), pc = face.ToAB(c);
            float area = Cross(pb - pa, pc - pa);
            if (area == 0) continue;
            Vector2 min = Vector2.Min(pa, Vector2.Min(pb, pc));
            Vector2 max = Vector2.Max(pa, Vector2.Max(pb, pc));
            int fa = Flag(source, ia), fb = Flag(source, ib), fc = Flag(source, ic);
            Vector3 na = Normal(fa, normal), nb = Normal(fb, normal), nc = Normal(fc, normal);
            foreach (Triangle glyph in glyphs)
            {
                Vector2 gmin = Vector2.Min(glyph.A.Position, Vector2.Min(glyph.B.Position, glyph.C.Position));
                Vector2 gmax = Vector2.Max(glyph.A.Position, Vector2.Max(glyph.B.Position, glyph.C.Position));
                if (gmax.X < min.X || gmax.Y < min.Y || gmin.X > max.X || gmin.Y > max.Y) continue;
                polygon.Clear();
                polygon.Add(glyph.A);
                polygon.Add(glyph.B);
                polygon.Add(glyph.C);
                Clip(ref polygon, ref scratch, pa, pb, MathF.Sign(area));
                Clip(ref polygon, ref scratch, pb, pc, MathF.Sign(area));
                Clip(ref polygon, ref scratch, pc, pa, MathF.Sign(area));
                for (int vertex = 1; vertex + 1 < polygon.Count; vertex++)
                {
                    Point p0 = polygon[0], p1 = polygon[vertex], p2 = polygon[vertex + 1];
                    float clippedArea = Cross(p1.Position - p0.Position, p2.Position - p0.Position);
                    if (clippedArea == 0) continue;
                    if (MathF.Sign(clippedArea) != MathF.Sign(area)) (p1, p2) = (p2, p1);
                    AddVertex(p0);
                    AddVertex(p1);
                    AddVertex(p2);
                }

                void AddVertex(Point point)
                {
                    float wb = Cross(point.Position - pa, pc - pa) / area;
                    float wc = Cross(pb - pa, point.Position - pa) / area;
                    float wa = 1 - wb - wc;
                    Vector3 position = a * wa + b * wb + c * wc;
                    Vector3 surfaceNormal = na * wa + nb * wb + nc * wc;
                    positions.Add(position.X);
                    positions.Add(position.Y);
                    positions.Add(position.Z);
                    flags.Add(fa);
                    attributes.Add(glyph.Direction.X);
                    attributes.Add(glyph.Direction.Y);
                    attributes.Add(Math.Clamp(point.Coverage, 0, 1));
                    attributes.Add(surfaceNormal.X);
                    attributes.Add(surfaceNormal.Y);
                    attributes.Add(surfaceNormal.Z);
                    Append(attributes, a);
                    Append(attributes, b);
                    Append(attributes, c);
                    Append(attributes, new Vector3(wa, wb, wc));
                    Append(attributes, new Vector3((fa >> 8) & 7, (fb >> 8) & 7, (fc >> 8) & 7));
                    indices.Add(indices.Count);
                }
            }
        }
        if (indices.Count == 0) return null;
        return new MeshData(false)
        {
            xyz = positions.ToArray(), Flags = flags.ToArray(), Indices = indices.ToArray(),
            VerticesCount = flags.Count, VerticesMax = flags.Count,
            IndicesCount = indices.Count, IndicesMax = indices.Count,
            mode = EnumDrawMode.Triangles,
            CustomFloats = new CustomMeshDataPartFloat
            {
                Values = attributes.ToArray(), Count = attributes.Count,
                InterleaveSizes = [3, 3, 3, 3, 3, 3, 3],
                InterleaveOffsets = [0, 12, 24, 36, 48, 60, 72], InterleaveStride = 84
            }
        };
    }

    private static void AddChevron(List<Triangle> glyphs, Vector2 direction)
    {
        Vector2 forward = Vector2.Normalize(direction), across = new(-forward.Y, forward.X);
        Vector2 tip = forward, upper = -forward + across, lower = -forward - across;
        Vector2 min = Vector2.Min(tip, Vector2.Min(upper, lower));
        Vector2 max = Vector2.Max(tip, Vector2.Max(upper, lower));
        Vector2 center = (min + max) * 0.5f;
        Vector2 bu = Bounds(direction.X), bv = Bounds(direction.Y);
        float halfSize = MathF.Min(bu.Y - bu.X, bv.Y - bv.X) * 0.5f - 0.015f;
        float scale = 2 * halfSize / MathF.Max(max.X - min.X, max.Y - min.Y);
        Vector2 regionCenter = new((bu.X + bu.Y) * 0.5f, (bv.X + bv.Y) * 0.5f);
        upper = (upper - center) * scale + regionCenter;
        tip = (tip - center) * scale + regionCenter;
        lower = (lower - center) * scale + regionCenter;
        Vector2 first = Vector2.Normalize(tip - upper), second = Vector2.Normalize(lower - tip);
        Vector2 n1 = new(-first.Y, first.X), n2 = new(-second.Y, second.X);
        Vector2 miter = Vector2.Normalize(n1 + n2);
        float miterScale = 1 / Vector2.Dot(miter, n1);
        Vector2[] outer = Outline(Width * 0.5f, upper, lower);
        Vector2[] inner = Outline(Width * 0.25f, upper + first * Width * 0.25f,
            lower - second * Width * 0.25f);
        for (int i = 0; i < outer.Length; i++)
        {
            int next = (i + 1) % outer.Length;
            Quad(new(outer[i], 0), new(outer[next], 0), new(inner[next], 1), new(inner[i], 1));
        }
        Quad(new(inner[0], 1), new(inner[1], 1), new(inner[4], 1), new(inner[5], 1));
        Quad(new(inner[1], 1), new(inner[2], 1), new(inner[3], 1), new(inner[4], 1));

        Vector2[] Outline(float halfWidth, Vector2 start, Vector2 end) =>
        [
            start + n1 * halfWidth, tip + miter * (halfWidth * miterScale),
            end + n2 * halfWidth, end - n2 * halfWidth,
            tip - miter * (halfWidth * miterScale), start - n1 * halfWidth
        ];
        void Quad(Point a, Point b, Point c, Point d)
        {
            glyphs.Add(new(a, b, c, direction));
            glyphs.Add(new(a, c, d, direction));
        }
    }

    private static void Clip(ref List<Point> polygon, ref List<Point> scratch,
        Vector2 a, Vector2 b, float sign)
    {
        scratch.Clear();
        if (polygon.Count != 0)
        {
            Point previous = polygon[^1];
            float previousDistance = Cross(b - a, previous.Position - a) * sign;
            foreach (Point current in polygon)
            {
                float distance = Cross(b - a, current.Position - a) * sign;
                if ((distance >= 0) != (previousDistance >= 0))
                {
                    float t = previousDistance / (previousDistance - distance);
                    scratch.Add(new(Vector2.Lerp(previous.Position, current.Position, t),
                        previous.Coverage + (current.Coverage - previous.Coverage) * t));
                }
                if (distance >= 0) scratch.Add(current);
                previous = current;
                previousDistance = distance;
            }
        }
        (polygon, scratch) = (scratch, polygon);
    }

    private static Vector2 Bounds(float direction) => direction < 0
        ? new(0, 0.5f - BrushSnapping.CenterThreshold)
        : direction > 0 ? new(0.5f + BrushSnapping.CenterThreshold, 1)
        : new(0.5f - BrushSnapping.CenterThreshold, 0.5f + BrushSnapping.CenterThreshold);

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static void Append(List<float> values, Vector3 vector)
    {
        values.Add(vector.X);
        values.Add(vector.Y);
        values.Add(vector.Z);
    }
    private static Vector3 Position(MeshData data, int index) =>
        new(data.xyz[index * 3], data.xyz[index * 3 + 1], data.xyz[index * 3 + 2]);
    private static int Flag(MeshData data, int index) =>
        data.Flags is not null && index < data.Flags.Length ? data.Flags[index] : 0;
    private static Vector3 Normal(int flags, float[] buffer)
    {
        VertexFlags.UnpackNormal(flags, buffer);
        return new(buffer[0], buffer[1], buffer[2]);
    }
}
