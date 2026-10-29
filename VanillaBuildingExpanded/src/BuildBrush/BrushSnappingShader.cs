using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaBuildingExpanded.BuildBrush;

internal interface IBrushSnappingShader : IShaderProgram
{
    float[] ProjectionMatrix { set; }
    float[] ModelViewMatrix { set; }
    Vector3 SelectedNormal { set; }
    Vector3 AxisU { set; }
    Vector3 AxisV { set; }
    Vector2 ResolvedDirections { set; }
    Vector4 ColorIn { set; }
    float MarkingWidth { set; }
    float DepthBias { set; }
}

internal sealed class BrushSnappingShader : ShaderProgram, IBrushSnappingShader
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

    public Vector4 ColorIn
    {
        set => Uniform("colorIn", value.X, value.Y, value.Z, value.W);
    }

    public float MarkingWidth
    {
        set => Uniform("markingWidth", value);
    }

    public float DepthBias
    {
        set => Uniform("depthBias", value);
    }
}
