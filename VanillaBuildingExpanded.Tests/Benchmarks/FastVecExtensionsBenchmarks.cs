using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using VanillaBuildingExpanded.src.Extensions.Math;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Tests.Benchmarks;

/// <summary>
/// Benchmarks comparing FastVec extension methods (AsSpan, ToSNT) vs direct field access
/// and System.Numerics operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class FastVecExtensionsBenchmarks
{
    #region Test Data

    private FastVec2i _fastVec2iA;
    private FastVec2i _fastVec2iB;
    private FastVec3f _fastVec3fA;
    private FastVec3f _fastVec3fB;
    private FastVec3d _fastVec3dA;
    private FastVec3d _fastVec3dB;
    private FastVec3i _fastVec3iA;
    private FastVec3i _fastVec3iB;

    private System.Numerics.Vector3 _sntVec3fA;
    private System.Numerics.Vector3 _sntVec3fB;

    #endregion

    [GlobalSetup]
    public void Setup()
    {
        // Initialize FastVec2i
        _fastVec2iA = new FastVec2i(10, 20);
        _fastVec2iB = new FastVec2i(30, 40);

        // Initialize FastVec3f
        _fastVec3fA = new FastVec3f(1.0f, 2.0f, 3.0f);
        _fastVec3fB = new FastVec3f(4.0f, 5.0f, 6.0f);

        // Initialize FastVec3d
        _fastVec3dA = new FastVec3d(1.0, 2.0, 3.0);
        _fastVec3dB = new FastVec3d(4.0, 5.0, 6.0);

        // Initialize FastVec3i
        _fastVec3iA = new FastVec3i(1, 2, 3);
        _fastVec3iB = new FastVec3i(4, 5, 6);

        // Pre-converted System.Numerics vectors
        _sntVec3fA = new System.Numerics.Vector3(_fastVec3fA.X, _fastVec3fA.Y, _fastVec3fA.Z);
        _sntVec3fB = new System.Numerics.Vector3(_fastVec3fB.X, _fastVec3fB.Y, _fastVec3fB.Z);

        // Setup bulk arrays
        SetupBulk();
    }

    #region AsSpan Conversion Cost Benchmarks

    [Benchmark]
    [BenchmarkCategory("AsSpan", "Conversion")]
    public Span<int> FastVec2i_AsSpan()
    {
        return _fastVec2iA.AsSpan();
    }

    [Benchmark]
    [BenchmarkCategory("AsSpan", "Conversion")]
    public Span<float> FastVec3f_AsSpan()
    {
        return _fastVec3fA.AsSpan();
    }

    [Benchmark]
    [BenchmarkCategory("AsSpan", "Conversion")]
    public Span<double> FastVec3d_AsSpan()
    {
        return _fastVec3dA.AsSpan();
    }

    [Benchmark]
    [BenchmarkCategory("AsSpan", "Conversion")]
    public Span<int> FastVec3i_AsSpan()
    {
        return _fastVec3iA.AsSpan();
    }

    #endregion

    #region ToSNT Conversion Benchmarks

    [Benchmark]
    [BenchmarkCategory("ToSNT", "Conversion")]
    public System.Numerics.Vector3 FastVec3f_ToSNT_ViaAsSpan()
    {
        return _fastVec3fA.ToSNT();
    }

    [Benchmark]
    [BenchmarkCategory("ToSNT", "Conversion")]
    public System.Numerics.Vector3 FastVec3f_ToSNT_DirectConstruction()
    {
        return new System.Numerics.Vector3(_fastVec3fA.X, _fastVec3fA.Y, _fastVec3fA.Z);
    }

    [Benchmark]
    [BenchmarkCategory("ToSNT", "Conversion")]
    public System.Numerics.Vector3 FastVec3f_ToSNT_UnsafeAs()
    {
        // Since FastVec3f and Vector3 both have 3 consecutive floats, we can reinterpret
        return Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref _fastVec3fA);
    }

    #endregion

    #region Element Access: AsSpan vs Direct Field Access

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FastVec3f", "ElementAccess")]
    public float FastVec3f_DirectAccess_SumXYZ()
    {
        return _fastVec3fA.X + _fastVec3fA.Y + _fastVec3fA.Z;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "ElementAccess")]
    public float FastVec3f_AsSpan_SumXYZ()
    {
        var span = _fastVec3fA.AsSpan();
        return span[0] + span[1] + span[2];
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "ElementAccess")]
    public float FastVec3f_Indexer_SumXYZ()
    {
        return _fastVec3fA[0] + _fastVec3fA[1] + _fastVec3fA[2];
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3i", "ElementAccess")]
    public int FastVec3i_DirectAccess_SumXYZ()
    {
        return _fastVec3iA.X + _fastVec3iA.Y + _fastVec3iA.Z;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3i", "ElementAccess")]
    public int FastVec3i_AsSpan_SumXYZ()
    {
        var span = _fastVec3iA.AsSpan();
        return span[0] + span[1] + span[2];
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3i", "ElementAccess")]
    public int FastVec3i_Indexer_SumXYZ()
    {
        return _fastVec3iA[0] + _fastVec3iA[1] + _fastVec3iA[2];
    }

    #endregion

    #region FastVec3f Addition Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Addition")]
    public FastVec3f FastVec3f_Add_Original()
    {
        var copy = _fastVec3fA;
        return copy.Add(_fastVec3fB.X, _fastVec3fB.Y, _fastVec3fB.Z);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Addition")]
    public FastVec3f FastVec3f_Add_AddCopy()
    {
        return _fastVec3fA.AddCopy(_fastVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Addition")]
    public System.Numerics.Vector3 FastVec3f_Add_SNT_PreConverted()
    {
        return _sntVec3fA + _sntVec3fB;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Addition")]
    public System.Numerics.Vector3 FastVec3f_Add_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        var b = _fastVec3fB.ToSNT();
        return a + b;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Addition")]
    public System.Numerics.Vector3 FastVec3f_Add_SNT_UnsafeConversion()
    {
        var a = Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref _fastVec3fA);
        var b = Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref _fastVec3fB);
        return a + b;
    }

    #endregion

    #region FastVec3f Scalar Multiplication Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "ScalarMul")]
    public FastVec3f FastVec3f_ScalarMul_Original()
    {
        var copy = _fastVec3fA;
        return copy.Mul(2.5f);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "ScalarMul")]
    public FastVec3f FastVec3f_ScalarMul_Operator()
    {
        return _fastVec3fA * 2.5f;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "ScalarMul")]
    public System.Numerics.Vector3 FastVec3f_ScalarMul_SNT_PreConverted()
    {
        return _sntVec3fA * 2.5f;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "ScalarMul")]
    public System.Numerics.Vector3 FastVec3f_ScalarMul_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        return a * 2.5f;
    }

    #endregion

    #region FastVec3f Length Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Length")]
    public float FastVec3f_Length_Original()
    {
        return _fastVec3fA.Length();
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Length")]
    public float FastVec3f_Length_SNT_PreConverted()
    {
        return _sntVec3fA.Length();
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Length")]
    public float FastVec3f_Length_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        return a.Length();
    }

    #endregion

    #region FastVec3f Normalize Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Normalize")]
    public FastVec3f FastVec3f_Normalize_Original()
    {
        var copy = _fastVec3fA;
        return copy.Normalize();
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Normalize")]
    public FastVec3f FastVec3f_NormalizedCopy_Original()
    {
        return _fastVec3fA.NormalizedCopy();
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Normalize")]
    public System.Numerics.Vector3 FastVec3f_Normalize_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Normalize(_sntVec3fA);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Normalize")]
    public System.Numerics.Vector3 FastVec3f_Normalize_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        return System.Numerics.Vector3.Normalize(a);
    }

    #endregion

    #region FastVec3f Distance Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Distance")]
    public float FastVec3f_Distance_Original()
    {
        return _fastVec3fA.Distance(_fastVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Distance")]
    public float FastVec3f_Distance_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Distance(_sntVec3fA, _sntVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Distance")]
    public float FastVec3f_Distance_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        var b = _fastVec3fB.ToSNT();
        return System.Numerics.Vector3.Distance(a, b);
    }

    #endregion

    #region FastVec3f Dot Product Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "DotProduct")]
    public float FastVec3f_Dot_Original()
    {
        // FastVec3f.Dot takes Vec3f/Vec3d, so we compute manually
        return _fastVec3fA.X * _fastVec3fB.X + _fastVec3fA.Y * _fastVec3fB.Y + _fastVec3fA.Z * _fastVec3fB.Z;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "DotProduct")]
    public float FastVec3f_Dot_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Dot(_sntVec3fA, _sntVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "DotProduct")]
    public float FastVec3f_Dot_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        var b = _fastVec3fB.ToSNT();
        return System.Numerics.Vector3.Dot(a, b);
    }

    #endregion

    #region FastVec3f Cross Product Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "CrossProduct")]
    public FastVec3f FastVec3f_Cross_Manual()
    {
        // FastVec3f doesn't have Cross, compute manually
        return new FastVec3f(
            _fastVec3fA.Y * _fastVec3fB.Z - _fastVec3fA.Z * _fastVec3fB.Y,
            _fastVec3fA.Z * _fastVec3fB.X - _fastVec3fA.X * _fastVec3fB.Z,
            _fastVec3fA.X * _fastVec3fB.Y - _fastVec3fA.Y * _fastVec3fB.X
        );
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "CrossProduct")]
    public System.Numerics.Vector3 FastVec3f_Cross_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Cross(_sntVec3fA, _sntVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "CrossProduct")]
    public System.Numerics.Vector3 FastVec3f_Cross_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        var b = _fastVec3fB.ToSNT();
        return System.Numerics.Vector3.Cross(a, b);
    }

    #endregion

    #region FastVec3i Addition Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec3i", "Addition")]
    public FastVec3i FastVec3i_Add_Original()
    {
        var copy = _fastVec3iA;
        return copy.Add(_fastVec3iB.X, _fastVec3iB.Y, _fastVec3iB.Z);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3i", "Addition")]
    public FastVec3i FastVec3i_Add_Manual()
    {
        return new FastVec3i(
            _fastVec3iA.X + _fastVec3iB.X,
            _fastVec3iA.Y + _fastVec3iB.Y,
            _fastVec3iA.Z + _fastVec3iB.Z
        );
    }

    #endregion

    #region FastVec2i Addition Benchmarks

    [Benchmark]
    [BenchmarkCategory("FastVec2i", "Addition")]
    public FastVec2i FastVec2i_Add_Operator()
    {
        return _fastVec2iA + _fastVec2iB;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec2i", "Addition")]
    public FastVec2i FastVec2i_Add_Original()
    {
        var copy = _fastVec2iA;
        return copy.Add(_fastVec2iB.X, _fastVec2iB.Y);
    }

    #endregion

    #region Complex Operations: Normalize -> Scale -> Add

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Complex")]
    public FastVec3f FastVec3f_Complex_Original()
    {
        var normalized = _fastVec3fA.NormalizedCopy();
        var scaled = normalized.Mul(5.0f);
        return scaled.AddCopy(_fastVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Complex")]
    public System.Numerics.Vector3 FastVec3f_Complex_SNT_PreConverted()
    {
        var normalized = System.Numerics.Vector3.Normalize(_sntVec3fA);
        var scaled = normalized * 5.0f;
        return scaled + _sntVec3fB;
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Complex")]
    public System.Numerics.Vector3 FastVec3f_Complex_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        var b = _fastVec3fB.ToSNT();
        var normalized = System.Numerics.Vector3.Normalize(a);
        var scaled = normalized * 5.0f;
        return scaled + b;
    }

    #endregion

    #region Lerp (Linear Interpolation)

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Lerp")]
    public FastVec3f FastVec3f_Lerp_Manual()
    {
        // Manual lerp: a + t * (b - a)
        float t = 0.5f;
        return new FastVec3f(
            _fastVec3fA.X + t * (_fastVec3fB.X - _fastVec3fA.X),
            _fastVec3fA.Y + t * (_fastVec3fB.Y - _fastVec3fA.Y),
            _fastVec3fA.Z + t * (_fastVec3fB.Z - _fastVec3fA.Z)
        );
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Lerp")]
    public System.Numerics.Vector3 FastVec3f_Lerp_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Lerp(_sntVec3fA, _sntVec3fB, 0.5f);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Lerp")]
    public System.Numerics.Vector3 FastVec3f_Lerp_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        var b = _fastVec3fB.ToSNT();
        return System.Numerics.Vector3.Lerp(a, b, 0.5f);
    }

    #endregion

    #region Reflection Vector

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Reflection")]
    public FastVec3f FastVec3f_Reflection_Manual()
    {
        // v - 2 * dot(v, n) * n
        var n = _fastVec3fB.NormalizedCopy();
        float dot = _fastVec3fA.X * n.X + _fastVec3fA.Y * n.Y + _fastVec3fA.Z * n.Z;
        float scale = 2.0f * dot;
        return new FastVec3f(
            _fastVec3fA.X - scale * n.X,
            _fastVec3fA.Y - scale * n.Y,
            _fastVec3fA.Z - scale * n.Z
        );
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Reflection")]
    public System.Numerics.Vector3 FastVec3f_Reflection_SNT_PreConverted()
    {
        var n = System.Numerics.Vector3.Normalize(_sntVec3fB);
        return System.Numerics.Vector3.Reflect(_sntVec3fA, n);
    }

    [Benchmark]
    [BenchmarkCategory("FastVec3f", "Reflection")]
    public System.Numerics.Vector3 FastVec3f_Reflection_SNT_WithConversion()
    {
        var a = _fastVec3fA.ToSNT();
        var b = _fastVec3fB.ToSNT();
        var n = System.Numerics.Vector3.Normalize(b);
        return System.Numerics.Vector3.Reflect(a, n);
    }

    #endregion

    #region Bulk Operations via Span

    private readonly float[] _bulkFloatArray = new float[300]; // 100 vectors worth
    private readonly int[] _bulkIntArray = new int[300];

    private void SetupBulk()
    {
        var rand = new Random(42);
        for (int i = 0; i < _bulkFloatArray.Length; i++)
        {
            _bulkFloatArray[i] = (float)rand.NextDouble() * 100f;
            _bulkIntArray[i] = rand.Next(100);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Bulk", "Sum")]
    public float Bulk_Sum_ViaSpan()
    {
        float sum = 0;
        for (int i = 0; i < 100; i++)
        {
            var vec = new FastVec3f(_bulkFloatArray[i * 3], _bulkFloatArray[i * 3 + 1], _bulkFloatArray[i * 3 + 2]);
            var span = vec.AsSpan();
            sum += span[0] + span[1] + span[2];
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Bulk", "Sum")]
    public float Bulk_Sum_DirectFields()
    {
        float sum = 0;
        for (int i = 0; i < 100; i++)
        {
            var vec = new FastVec3f(_bulkFloatArray[i * 3], _bulkFloatArray[i * 3 + 1], _bulkFloatArray[i * 3 + 2]);
            sum += vec.X + vec.Y + vec.Z;
        }
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Bulk", "Sum")]
    public float Bulk_Sum_SNT()
    {
        float sum = 0;
        for (int i = 0; i < 100; i++)
        {
            var vec = new System.Numerics.Vector3(_bulkFloatArray[i * 3], _bulkFloatArray[i * 3 + 1], _bulkFloatArray[i * 3 + 2]);
            sum += vec.X + vec.Y + vec.Z;
        }
        return sum;
    }

    #endregion

    #region Round-Trip Conversion Cost

    [Benchmark]
    [BenchmarkCategory("RoundTrip", "Conversion")]
    public FastVec3f FastVec3f_RoundTrip_ToSNT_BackToFastVec()
    {
        var snt = _fastVec3fA.ToSNT();
        return new FastVec3f(snt.X, snt.Y, snt.Z);
    }

    [Benchmark]
    [BenchmarkCategory("RoundTrip", "Conversion")]
    public FastVec3f FastVec3f_RoundTrip_UnsafeAs_Both()
    {
        var snt = Unsafe.As<FastVec3f, System.Numerics.Vector3>(ref _fastVec3fA);
        return Unsafe.As<System.Numerics.Vector3, FastVec3f>(ref snt);
    }

    #endregion
}
