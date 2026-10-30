using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaBuildingExpanded.BuildBrush;

internal interface IBrushSnappingSurfaceShader : IShaderProgram
{
    float[] ProjectionMatrix { set; }
    float[] ModelViewMatrix { set; }
    Vector3 SelectedNormal { set; }
    Vector3 AxisU { set; }
    Vector3 AxisV { set; }
    Vector2 ResolvedDirections { set; }
    float DepthBias { set; }
}

internal interface IBrushSnappingShader : IBrushSnappingSurfaceShader
{
    Vector4 ColorIn { set; }
    float CenterThreshold { set; }
}

internal interface IBrushSnappingChevronShader : IBrushSnappingSurfaceShader
{
    Vector4 ChevronColor { set; }
}

internal abstract class BrushSnappingSurfaceShader : ShaderProgram, IBrushSnappingSurfaceShader
{
    public float[] ProjectionMatrix
    {
        set => UniformMatrix("projectionMatrix", value);
    }

    public float[] ModelViewMatrix
    {
        set => UniformMatrix("modelViewMatrix", value);
    }

    public Vector3 SelectedNormal
    {
        set => Uniform("selectedNormal", value.X, value.Y, value.Z);
    }

    public Vector3 AxisU
    {
        set => Uniform("axisU", value.X, value.Y, value.Z);
    }

    public Vector3 AxisV
    {
        set => Uniform("axisV", value.X, value.Y, value.Z);
    }

    public Vector2 ResolvedDirections
    {
        set => Uniform("resolvedDirections", value.X, value.Y);
    }

    public float DepthBias
    {
        set => Uniform("depthBias", value);
    }
}

internal sealed class BrushSnappingShader : BrushSnappingSurfaceShader, IBrushSnappingShader
{
    public Vector4 ColorIn
    {
        set => Uniform("colorIn", value.X, value.Y, value.Z, value.W);
    }

    public float CenterThreshold
    {
        set => Uniform("centerThreshold", value);
    }
}

internal sealed class BrushSnappingChevronShader : BrushSnappingSurfaceShader, IBrushSnappingChevronShader
{
    public Vector4 ChevronColor
    {
        set => Uniform("chevronColor", value.X, value.Y, value.Z, value.W);
    }
}
