using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Tests.Benchmarks;

/// <summary>
/// Benchmarks comparing System.Numerics vector operations vs Vintage Story API vector operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class VecExtensionsBenchmarks
{
    #region Test Data

    private Vec2f _vec2fA = null!;
    private Vec2f _vec2fB = null!;
    private Vec3f _vec3fA = null!;
    private Vec3f _vec3fB = null!;
    private Vec3d _vec3dA = null!;
    private Vec3d _vec3dB = null!;

    private System.Numerics.Vector2 _sntVec2A;
    private System.Numerics.Vector2 _sntVec2B;
    private System.Numerics.Vector3 _sntVec3fA;
    private System.Numerics.Vector3 _sntVec3fB;
    private System.Numerics.Vector3 _sntVec3dA;
    private System.Numerics.Vector3 _sntVec3dB;

    #endregion

    [GlobalSetup]
    public void Setup()
    {
        // Initialize Vec2f
        _vec2fA = new Vec2f(1.5f, 2.5f);
        _vec2fB = new Vec2f(3.0f, 4.0f);

        // Initialize Vec3f
        _vec3fA = new Vec3f(1.0f, 2.0f, 3.0f);
        _vec3fB = new Vec3f(4.0f, 5.0f, 6.0f);

        // Initialize Vec3d
        _vec3dA = new Vec3d(1.0, 2.0, 3.0);
        _vec3dB = new Vec3d(4.0, 5.0, 6.0);

        // Initialize System.Numerics vectors (pre-converted)
        _sntVec2A = new System.Numerics.Vector2(_vec2fA.X, _vec2fA.Y);
        _sntVec2B = new System.Numerics.Vector2(_vec2fB.X, _vec2fB.Y);

        _sntVec3fA = new System.Numerics.Vector3(_vec3fA.X, _vec3fA.Y, _vec3fA.Z);
        _sntVec3fB = new System.Numerics.Vector3(_vec3fB.X, _vec3fB.Y, _vec3fB.Z);

        _sntVec3dA = new System.Numerics.Vector3((float)_vec3dA.X, (float)_vec3dA.Y, (float)_vec3dA.Z);
        _sntVec3dB = new System.Numerics.Vector3((float)_vec3dB.X, (float)_vec3dB.Y, (float)_vec3dB.Z);
    }

    #region Vec2f Addition Benchmarks

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Vec2f", "Addition")]
    public Vec2f Vec2f_Add_Original()
    {
        return _vec2fA + _vec2fB;
    }

    [Benchmark]
    [BenchmarkCategory("Vec2f", "Addition")]
    public System.Numerics.Vector2 Vec2f_Add_SNT_PreConverted()
    {
        return _sntVec2A + _sntVec2B;
    }

    [Benchmark]
    [BenchmarkCategory("Vec2f", "Addition")]
    public System.Numerics.Vector2 Vec2f_Add_SNT_WithConversion()
    {
        var a = _vec2fA.ToSNT();
        var b = _vec2fB.ToSNT();
        return a + b;
    }

    #endregion

    #region Vec2f Multiplication Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec2f", "Multiplication")]
    public Vec2f Vec2f_Multiply_Original()
    {
        return _vec2fA * 2.5f;
    }

    [Benchmark]
    [BenchmarkCategory("Vec2f", "Multiplication")]
    public System.Numerics.Vector2 Vec2f_Multiply_SNT_PreConverted()
    {
        return _sntVec2A * 2.5f;
    }

    [Benchmark]
    [BenchmarkCategory("Vec2f", "Multiplication")]
    public System.Numerics.Vector2 Vec2f_Multiply_SNT_WithConversion()
    {
        var a = _vec2fA.ToSNT();
        return a * 2.5f;
    }

    #endregion

    #region Vec3f Addition Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Addition")]
    public Vec3f Vec3f_Add_Original()
    {
        // Vec3f.Add modifies the original, so we clone first
        return _vec3fA.Clone().Add(_vec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Addition")]
    public System.Numerics.Vector3 Vec3f_Add_SNT_PreConverted()
    {
        return _sntVec3fA + _sntVec3fB;
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Addition")]
    public System.Numerics.Vector3 Vec3f_Add_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        var b = _vec3fB.ToSNT();
        return a + b;
    }

    #endregion

    #region Vec3f Dot Product Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3f", "DotProduct")]
    public float Vec3f_Dot_Original()
    {
        return _vec3fA.Dot(_vec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "DotProduct")]
    public float Vec3f_Dot_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Dot(_sntVec3fA, _sntVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "DotProduct")]
    public float Vec3f_Dot_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        var b = _vec3fB.ToSNT();
        return System.Numerics.Vector3.Dot(a, b);
    }

    #endregion

    #region Vec3f Cross Product Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3f", "CrossProduct")]
    public Vec3f Vec3f_Cross_Original()
    {
        return _vec3fA.Cross(_vec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "CrossProduct")]
    public System.Numerics.Vector3 Vec3f_Cross_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Cross(_sntVec3fA, _sntVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "CrossProduct")]
    public System.Numerics.Vector3 Vec3f_Cross_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        var b = _vec3fB.ToSNT();
        return System.Numerics.Vector3.Cross(a, b);
    }

    #endregion

    #region Vec3f Normalize Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Normalize")]
    public Vec3f Vec3f_Normalize_Original()
    {
        return _vec3fA.Clone().Normalize();
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Normalize")]
    public System.Numerics.Vector3 Vec3f_Normalize_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Normalize(_sntVec3fA);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Normalize")]
    public System.Numerics.Vector3 Vec3f_Normalize_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        return System.Numerics.Vector3.Normalize(a);
    }

    #endregion

    #region Vec3f Length Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Length")]
    public float Vec3f_Length_Original()
    {
        return _vec3fA.Length();
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Length")]
    public float Vec3f_Length_SNT_PreConverted()
    {
        return _sntVec3fA.Length();
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Length")]
    public float Vec3f_Length_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        return a.Length();
    }

    #endregion

    #region Vec3f Scalar Multiplication Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3f", "ScalarMul")]
    public Vec3f Vec3f_ScalarMul_Original()
    {
        return _vec3fA.Clone().Mul(2.5f);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "ScalarMul")]
    public System.Numerics.Vector3 Vec3f_ScalarMul_SNT_PreConverted()
    {
        return _sntVec3fA * 2.5f;
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "ScalarMul")]
    public System.Numerics.Vector3 Vec3f_ScalarMul_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        return a * 2.5f;
    }

    #endregion

    #region Vec3d Addition Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Addition")]
    public Vec3d Vec3d_Add_Original()
    {
        return _vec3dA.Clone().Add(_vec3dB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Addition")]
    public System.Numerics.Vector3 Vec3d_Add_SNT_PreConverted()
    {
        return _sntVec3dA + _sntVec3dB;
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Addition")]
    public System.Numerics.Vector3 Vec3d_Add_SNT_WithConversion()
    {
        var a = _vec3dA.ToSNT();
        var b = _vec3dB.ToSNT();
        return a + b;
    }

    #endregion

    #region Vec3d Dot Product Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3d", "DotProduct")]
    public double Vec3d_Dot_Original()
    {
        return _vec3dA.Dot(_vec3dB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "DotProduct")]
    public float Vec3d_Dot_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Dot(_sntVec3dA, _sntVec3dB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "DotProduct")]
    public float Vec3d_Dot_SNT_WithConversion()
    {
        var a = _vec3dA.ToSNT();
        var b = _vec3dB.ToSNT();
        return System.Numerics.Vector3.Dot(a, b);
    }

    #endregion

    #region Vec3d Cross Product Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3d", "CrossProduct")]
    public Vec3d Vec3d_Cross_Original()
    {
        return _vec3dA.Cross(_vec3dB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "CrossProduct")]
    public System.Numerics.Vector3 Vec3d_Cross_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Cross(_sntVec3dA, _sntVec3dB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "CrossProduct")]
    public System.Numerics.Vector3 Vec3d_Cross_SNT_WithConversion()
    {
        var a = _vec3dA.ToSNT();
        var b = _vec3dB.ToSNT();
        return System.Numerics.Vector3.Cross(a, b);
    }

    #endregion

    #region Vec3d Normalize Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Normalize")]
    public Vec3d Vec3d_Normalize_Original()
    {
        return _vec3dA.Clone().Normalize();
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Normalize")]
    public System.Numerics.Vector3 Vec3d_Normalize_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Normalize(_sntVec3dA);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Normalize")]
    public System.Numerics.Vector3 Vec3d_Normalize_SNT_WithConversion()
    {
        var a = _vec3dA.ToSNT();
        return System.Numerics.Vector3.Normalize(a);
    }

    #endregion

    #region Vec3d Length Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Length")]
    public double Vec3d_Length_Original()
    {
        return _vec3dA.Length();
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Length")]
    public float Vec3d_Length_SNT_PreConverted()
    {
        return _sntVec3dA.Length();
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "Length")]
    public float Vec3d_Length_SNT_WithConversion()
    {
        var a = _vec3dA.ToSNT();
        return a.Length();
    }

    #endregion

    #region Vec3d Scalar Multiplication Benchmarks

    [Benchmark]
    [BenchmarkCategory("Vec3d", "ScalarMul")]
    public Vec3d Vec3d_ScalarMul_Original()
    {
        return _vec3dA.Clone().Mul(2.5);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "ScalarMul")]
    public System.Numerics.Vector3 Vec3d_ScalarMul_SNT_PreConverted()
    {
        return _sntVec3dA * 2.5f;
    }

    [Benchmark]
    [BenchmarkCategory("Vec3d", "ScalarMul")]
    public System.Numerics.Vector3 Vec3d_ScalarMul_SNT_WithConversion()
    {
        var a = _vec3dA.ToSNT();
        return a * 2.5f;
    }

    #endregion

    #region Complex Operations - Combined Math

    /// <summary>
    /// Simulates a complex operation: normalize, then scale, then add.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Vec3f", "Complex")]
    public Vec3f Vec3f_Complex_Original()
    {
        var normalized = _vec3fA.Clone().Normalize();
        normalized.Mul(5.0f);
        return normalized.Add(_vec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Complex")]
    public System.Numerics.Vector3 Vec3f_Complex_SNT_PreConverted()
    {
        var normalized = System.Numerics.Vector3.Normalize(_sntVec3fA);
        var scaled = normalized * 5.0f;
        return scaled + _sntVec3fB;
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Complex")]
    public System.Numerics.Vector3 Vec3f_Complex_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        var b = _vec3fB.ToSNT();
        var normalized = System.Numerics.Vector3.Normalize(a);
        var scaled = normalized * 5.0f;
        return scaled + b;
    }

    /// <summary>
    /// Simulates computing a reflection vector: v - 2 * dot(v, n) * n
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Vec3f", "Reflection")]
    public Vec3f Vec3f_Reflection_Original()
    {
        var n = _vec3fB.Clone().Normalize();
        var dotProduct = _vec3fA.Dot(n);
        var scaled = n.Clone().Mul(2.0f * dotProduct);
        return _vec3fA.Clone().Sub(scaled);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Reflection")]
    public System.Numerics.Vector3 Vec3f_Reflection_SNT_PreConverted()
    {
        var n = System.Numerics.Vector3.Normalize(_sntVec3fB);
        return System.Numerics.Vector3.Reflect(_sntVec3fA, n);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Reflection")]
    public System.Numerics.Vector3 Vec3f_Reflection_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        var b = _vec3fB.ToSNT();
        var n = System.Numerics.Vector3.Normalize(b);
        return System.Numerics.Vector3.Reflect(a, n);
    }

    #endregion

    #region Distance Calculations

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Distance")]
    public float Vec3f_Distance_Original()
    {
        return _vec3fA.DistanceTo(_vec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Distance")]
    public float Vec3f_Distance_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Distance(_sntVec3fA, _sntVec3fB);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Distance")]
    public float Vec3f_Distance_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        var b = _vec3fB.ToSNT();
        return System.Numerics.Vector3.Distance(a, b);
    }

    #endregion

    #region Lerp (Linear Interpolation)

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Lerp")]
    public Vec3f Vec3f_Lerp_Original()
    {
        // Manual lerp: a + t * (b - a)
        float t = 0.5f;
        var diff = _vec3fB.Clone().Sub(_vec3fA);
        diff.Mul(t);
        return _vec3fA.Clone().Add(diff);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Lerp")]
    public System.Numerics.Vector3 Vec3f_Lerp_SNT_PreConverted()
    {
        return System.Numerics.Vector3.Lerp(_sntVec3fA, _sntVec3fB, 0.5f);
    }

    [Benchmark]
    [BenchmarkCategory("Vec3f", "Lerp")]
    public System.Numerics.Vector3 Vec3f_Lerp_SNT_WithConversion()
    {
        var a = _vec3fA.ToSNT();
        var b = _vec3fB.ToSNT();
        return System.Numerics.Vector3.Lerp(a, b, 0.5f);
    }

    #endregion

    #region Conversion Only (Baseline for conversion cost)

    [Benchmark]
    [BenchmarkCategory("Conversion")]
    public System.Numerics.Vector2 Vec2f_ToSNT_Conversion()
    {
        return _vec2fA.ToSNT();
    }

    [Benchmark]
    [BenchmarkCategory("Conversion")]
    public System.Numerics.Vector3 Vec3f_ToSNT_Conversion()
    {
        return _vec3fA.ToSNT();
    }

    [Benchmark]
    [BenchmarkCategory("Conversion")]
    public System.Numerics.Vector3 Vec3d_ToSNT_Conversion()
    {
        return _vec3dA.ToSNT();
    }

    [Benchmark]
    [BenchmarkCategory("Conversion")]
    public FastVec3f SNT_ToSlowVecf_Conversion()
    {
        return _sntVec3fA.ToSlowVecf();
    }

    [Benchmark]
    [BenchmarkCategory("Conversion")]
    public FastVec3i SNT_ToSlowVeci_Conversion()
    {
        return _sntVec3fA.ToSlowVeci();
    }

    #endregion
}
