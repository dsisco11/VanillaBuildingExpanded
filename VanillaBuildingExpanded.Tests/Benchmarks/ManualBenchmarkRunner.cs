using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using VanillaBuildingExpanded.src.Extensions.Math;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Tests.Benchmarks;

/// <summary>
/// Manual benchmark runner that avoids BenchmarkDotNet's auto-generated project issues
/// with external (non-NuGet) references like Vintagestory.
/// </summary>
public static class ManualBenchmarkRunner
{
    private const int WarmupIterations = 100;
    private const int BenchmarkIterations = 1_000_000;

    public static void RunAllBenchmarks()
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("VECTOR PERFORMANCE BENCHMARKS");
        Console.WriteLine($"Warmup: {WarmupIterations:N0} iterations, Benchmark: {BenchmarkIterations:N0} iterations");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        RunVec2fBenchmarks();
        RunVec3fBenchmarks();
        RunFastVec3fBenchmarks();
        RunVec3dBenchmarks();
        RunConversionBenchmarks();
        RunComplexOperationBenchmarks();
    }

    private static void RunVec2fBenchmarks()
    {
        Console.WriteLine("--- Vec2f Benchmarks ---");

        var vec2fA = new Vec2f(1.5f, 2.5f);
        var vec2fB = new Vec2f(3.0f, 4.0f);
        var sntVec2A = new System.Numerics.Vector2(vec2fA.X, vec2fA.Y);
        var sntVec2B = new System.Numerics.Vector2(vec2fB.X, vec2fB.Y);

        // Addition
        Benchmark("Vec2f Addition (Original operator)", () => { var r = vec2fA + vec2fB; });
        Benchmark("Vec2f Addition (SNT PreConverted)", () => { var r = sntVec2A + sntVec2B; });
        Benchmark("Vec2f Addition (SNT w/ Conversion)", () =>
        {
            var a = vec2fA.ToSNT();
            var b = vec2fB.ToSNT();
            var r = a + b;
        });

        // Scalar multiplication
        Benchmark("Vec2f ScalarMul (Original operator)", () => { var r = vec2fA * 2.5f; });
        Benchmark("Vec2f ScalarMul (SNT PreConverted)", () => { var r = sntVec2A * 2.5f; });

        Console.WriteLine();
    }

    private static void RunVec3fBenchmarks()
    {
        Console.WriteLine("--- Vec3f Benchmarks ---");

        var vec3fA = new Vec3f(1.0f, 2.0f, 3.0f);
        var vec3fB = new Vec3f(4.0f, 5.0f, 6.0f);
        var sntVec3fA = new System.Numerics.Vector3(vec3fA.X, vec3fA.Y, vec3fA.Z);
        var sntVec3fB = new System.Numerics.Vector3(vec3fB.X, vec3fB.Y, vec3fB.Z);

        // Addition
        Benchmark("Vec3f Addition (Original Clone+Add)", () => { var r = vec3fA.Clone().Add(vec3fB); });
        Benchmark("Vec3f Addition (SNT PreConverted)", () => { var r = sntVec3fA + sntVec3fB; });
        Benchmark("Vec3f Addition (SNT w/ Conversion)", () =>
        {
            var a = vec3fA.ToSNT();
            var b = vec3fB.ToSNT();
            var r = a + b;
        });

        // Dot product
        Benchmark("Vec3f Dot (Original)", () => { var r = vec3fA.Dot(vec3fB); });
        Benchmark("Vec3f Dot (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Dot(sntVec3fA, sntVec3fB); });
        Benchmark("Vec3f Dot (SNT w/ Conversion)", () =>
        {
            var a = vec3fA.ToSNT();
            var b = vec3fB.ToSNT();
            var r = System.Numerics.Vector3.Dot(a, b);
        });

        // Cross product
        Benchmark("Vec3f Cross (Original)", () => { var r = vec3fA.Cross(vec3fB); });
        Benchmark("Vec3f Cross (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Cross(sntVec3fA, sntVec3fB); });
        Benchmark("Vec3f Cross (SNT w/ Conversion)", () =>
        {
            var a = vec3fA.ToSNT();
            var b = vec3fB.ToSNT();
            var r = System.Numerics.Vector3.Cross(a, b);
        });

        // Normalize
        Benchmark("Vec3f Normalize (Original Clone)", () => { var r = vec3fA.Clone().Normalize(); });
        Benchmark("Vec3f Normalize (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Normalize(sntVec3fA); });

        // Length
        Benchmark("Vec3f Length (Original)", () => { var r = vec3fA.Length(); });
        Benchmark("Vec3f Length (SNT PreConverted)", () => { var r = sntVec3fA.Length(); });

        // Distance
        Benchmark("Vec3f Distance (Original)", () => { var r = vec3fA.DistanceTo(vec3fB); });
        Benchmark("Vec3f Distance (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Distance(sntVec3fA, sntVec3fB); });

        // Lerp
        Benchmark("Vec3f Lerp (Manual)", () =>
        {
            float t = 0.5f;
            var diff = vec3fB.Clone().Sub(vec3fA);
            diff.Mul(t);
            var r = vec3fA.Clone().Add(diff);
        });
        Benchmark("Vec3f Lerp (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Lerp(sntVec3fA, sntVec3fB, 0.5f); });

        Console.WriteLine();
    }

    private static void RunFastVec3fBenchmarks()
    {
        Console.WriteLine("--- FastVec3f Benchmarks ---");

        var fastVec3fA = new FastVec3f(1.0f, 2.0f, 3.0f);
        var fastVec3fB = new FastVec3f(4.0f, 5.0f, 6.0f);
        var sntVec3fA = new System.Numerics.Vector3(fastVec3fA.X, fastVec3fA.Y, fastVec3fA.Z);
        var sntVec3fB = new System.Numerics.Vector3(fastVec3fB.X, fastVec3fB.Y, fastVec3fB.Z);

        // Element Access
        Benchmark("FastVec3f Element (Direct fields)", () => { var r = fastVec3fA.X + fastVec3fA.Y + fastVec3fA.Z; });
        Benchmark("FastVec3f Element (Indexer)", () => { var r = fastVec3fA[0] + fastVec3fA[1] + fastVec3fA[2]; });
        Benchmark("FastVec3f Element (AsSpan)", () =>
        {
            var span = fastVec3fA.AsSpan();
            var r = span[0] + span[1] + span[2];
        });

        // ToSNT conversion methods
        Benchmark("FastVec3f ToSNT (via AsSpan)", () => { var r = fastVec3fA.ToSNT(); });
        Benchmark("FastVec3f ToSNT (Direct ctor)", () => { var r = new System.Numerics.Vector3(fastVec3fA.X, fastVec3fA.Y, fastVec3fA.Z); });
        Benchmark("FastVec3f ToSNT (Unsafe.As)", () => { var r = Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref fastVec3fA); });

        // Addition
        Benchmark("FastVec3f Addition (AddCopy)", () => { var r = fastVec3fA.AddCopy(fastVec3fB); });
        Benchmark("FastVec3f Addition (SNT PreConverted)", () => { var r = sntVec3fA + sntVec3fB; });
        Benchmark("FastVec3f Addition (SNT w/ ToSNT)", () =>
        {
            var a = fastVec3fA.ToSNT();
            var b = fastVec3fB.ToSNT();
            var r = a + b;
        });
        Benchmark("FastVec3f Addition (SNT Unsafe)", () =>
        {
            var a = Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref fastVec3fA);
            var b = Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref fastVec3fB);
            var r = a + b;
        });

        // Normalize
        Benchmark("FastVec3f Normalize (Original)", () =>
        {
            var copy = fastVec3fA;
            copy.Normalize();
        });
        Benchmark("FastVec3f NormalizedCopy (Original)", () => { var r = fastVec3fA.NormalizedCopy(); });
        Benchmark("FastVec3f Normalize (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Normalize(sntVec3fA); });

        // Length
        Benchmark("FastVec3f Length (Original)", () => { var r = fastVec3fA.Length(); });
        Benchmark("FastVec3f Length (SNT PreConverted)", () => { var r = sntVec3fA.Length(); });

        // Distance
        Benchmark("FastVec3f Distance (Original)", () => { var r = fastVec3fA.Distance(fastVec3fB); });
        Benchmark("FastVec3f Distance (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Distance(sntVec3fA, sntVec3fB); });

        // Dot (manual)
        Benchmark("FastVec3f Dot (Manual)", () =>
        {
            var r = fastVec3fA.X * fastVec3fB.X + fastVec3fA.Y * fastVec3fB.Y + fastVec3fA.Z * fastVec3fB.Z;
        });
        Benchmark("FastVec3f Dot (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Dot(sntVec3fA, sntVec3fB); });

        Console.WriteLine();
    }

    private static void RunVec3dBenchmarks()
    {
        Console.WriteLine("--- Vec3d Benchmarks ---");

        var vec3dA = new Vec3d(1.0, 2.0, 3.0);
        var vec3dB = new Vec3d(4.0, 5.0, 6.0);
        var sntVec3dA = new System.Numerics.Vector3((float)vec3dA.X, (float)vec3dA.Y, (float)vec3dA.Z);
        var sntVec3dB = new System.Numerics.Vector3((float)vec3dB.X, (float)vec3dB.Y, (float)vec3dB.Z);

        // Addition
        Benchmark("Vec3d Addition (Original Clone)", () => { var r = vec3dA.Clone().Add(vec3dB); });
        Benchmark("Vec3d Addition (SNT PreConverted)", () => { var r = sntVec3dA + sntVec3dB; });
        Benchmark("Vec3d Addition (SNT w/ Conversion)", () =>
        {
            var a = vec3dA.ToSNT();
            var b = vec3dB.ToSNT();
            var r = a + b;
        });

        // Dot
        Benchmark("Vec3d Dot (Original)", () => { var r = vec3dA.Dot(vec3dB); });
        Benchmark("Vec3d Dot (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Dot(sntVec3dA, sntVec3dB); });

        // Cross
        Benchmark("Vec3d Cross (Original)", () => { var r = vec3dA.Cross(vec3dB); });
        Benchmark("Vec3d Cross (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Cross(sntVec3dA, sntVec3dB); });

        // Normalize
        Benchmark("Vec3d Normalize (Original Clone)", () => { var r = vec3dA.Clone().Normalize(); });
        Benchmark("Vec3d Normalize (SNT PreConverted)", () => { var r = System.Numerics.Vector3.Normalize(sntVec3dA); });

        // Length
        Benchmark("Vec3d Length (Original)", () => { var r = vec3dA.Length(); });
        Benchmark("Vec3d Length (SNT PreConverted)", () => { var r = sntVec3dA.Length(); });

        Console.WriteLine();
    }

    private static void RunConversionBenchmarks()
    {
        Console.WriteLine("--- Conversion Cost Benchmarks ---");

        var vec2f = new Vec2f(1.5f, 2.5f);
        var vec3f = new Vec3f(1.0f, 2.0f, 3.0f);
        var vec3d = new Vec3d(1.0, 2.0, 3.0);
        var fastVec3f = new FastVec3f(1.0f, 2.0f, 3.0f);
        var sntVec3 = new System.Numerics.Vector3(1.0f, 2.0f, 3.0f);

        Benchmark("Vec2f -> SNT Vector2", () => { var r = vec2f.ToSNT(); });
        Benchmark("Vec3f -> SNT Vector3", () => { var r = vec3f.ToSNT(); });
        Benchmark("Vec3d -> SNT Vector3", () => { var r = vec3d.ToSNT(); });
        Benchmark("FastVec3f -> SNT (ToSNT)", () => { var r = fastVec3f.ToSNT(); });
        Benchmark("FastVec3f -> SNT (Unsafe.As)", () => { var r = Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref fastVec3f); });
        Benchmark("SNT -> FastVec3f (ToSlowVecf)", () => { var r = sntVec3.ToSlowVecf(); });
        Benchmark("SNT -> FastVec3i (ToSlowVeci)", () => { var r = sntVec3.ToSlowVeci(); });

        Console.WriteLine();
    }

    private static void RunComplexOperationBenchmarks()
    {
        Console.WriteLine("--- Complex Operation Benchmarks ---");

        var vec3fA = new Vec3f(1.0f, 2.0f, 3.0f);
        var vec3fB = new Vec3f(4.0f, 5.0f, 6.0f);
        var fastVec3fA = new FastVec3f(1.0f, 2.0f, 3.0f);
        var fastVec3fB = new FastVec3f(4.0f, 5.0f, 6.0f);
        var sntVec3fA = new System.Numerics.Vector3(vec3fA.X, vec3fA.Y, vec3fA.Z);
        var sntVec3fB = new System.Numerics.Vector3(vec3fB.X, vec3fB.Y, vec3fB.Z);

        // Normalize -> Scale -> Add chain
        Benchmark("Complex (Vec3f Original)", () =>
        {
            var normalized = vec3fA.Clone().Normalize();
            normalized.Mul(5.0f);
            var result = normalized.Add(vec3fB);
        });
        Benchmark("Complex (FastVec3f Original)", () =>
        {
            var normalized = fastVec3fA.NormalizedCopy();
            var scaled = normalized.Mul(5.0f);
            var result = scaled.AddCopy(fastVec3fB);
        });
        Benchmark("Complex (SNT PreConverted)", () =>
        {
            var normalized = System.Numerics.Vector3.Normalize(sntVec3fA);
            var scaled = normalized * 5.0f;
            var result = scaled + sntVec3fB;
        });
        Benchmark("Complex (SNT w/ Conversion)", () =>
        {
            var a = vec3fA.ToSNT();
            var b = vec3fB.ToSNT();
            var normalized = System.Numerics.Vector3.Normalize(a);
            var scaled = normalized * 5.0f;
            var result = scaled + b;
        });

        // Reflection: v - 2 * dot(v, n) * n
        Benchmark("Reflection (Vec3f Manual)", () =>
        {
            var n = vec3fB.Clone().Normalize();
            var dotProduct = vec3fA.Dot(n);
            var scaled = n.Clone().Mul(2.0f * dotProduct);
            var result = vec3fA.Clone().Sub(scaled);
        });
        Benchmark("Reflection (SNT PreConverted)", () =>
        {
            var n = System.Numerics.Vector3.Normalize(sntVec3fB);
            var result = System.Numerics.Vector3.Reflect(sntVec3fA, n);
        });

        Console.WriteLine();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Benchmark(string name, Action action)
    {
        // Warmup
        for (int i = 0; i < WarmupIterations; i++)
        {
            action();
        }

        // Benchmark
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < BenchmarkIterations; i++)
        {
            action();
        }
        sw.Stop();

        double nsPerOp = (double)sw.ElapsedTicks / Stopwatch.Frequency * 1_000_000_000 / BenchmarkIterations;
        Console.WriteLine($"  {name,-45} {nsPerOp,10:F2} ns/op");
    }
}
