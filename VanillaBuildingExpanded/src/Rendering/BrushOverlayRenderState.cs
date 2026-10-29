using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Rendering;

internal interface IBrushOverlayRenderState
{
    void Capture();
    void Apply();
    void Restore();
}

internal sealed class BrushOverlayRenderState(IRenderAPI render) : IBrushOverlayRenderState
{
    private bool depthTest, depthWrite, cullFace, polygonOffset;
    private float offsetFactor, offsetUnits;
    private DepthFunction depthFunction;
    private FrontFaceDirection frontFace;

    public void Capture()
    {
        depthTest = GL.IsEnabled(EnableCap.DepthTest);
        depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        cullFace = GL.IsEnabled(EnableCap.CullFace);
        depthFunction = (DepthFunction)GL.GetInteger(GetPName.DepthFunc);
        frontFace = (FrontFaceDirection)GL.GetInteger(GetPName.FrontFace);
        polygonOffset = GL.IsEnabled(EnableCap.PolygonOffsetFill);
        offsetFactor = GL.GetFloat(GetPName.PolygonOffsetFactor);
        offsetUnits = GL.GetFloat(GetPName.PolygonOffsetUnits);
    }

    public void Apply()
    {
        render.GLEnableDepthTest();
        render.GLDepthMask(false);
        GL.DepthFunc(DepthFunction.Lequal);
        // Keep derivative-normal fallback independent of the previous renderer's winding.
        GL.FrontFace(FrontFaceDirection.Ccw);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(-1f, -1f);
    }

    public void Restore()
    {
        try { GL.DepthFunc(depthFunction); }
        finally
        {
            try
            {
                GL.PolygonOffset(offsetFactor, offsetUnits);
                if (polygonOffset) GL.Enable(EnableCap.PolygonOffsetFill);
                else GL.Disable(EnableCap.PolygonOffsetFill);
            }
            finally
            {
                try
                {
                    GL.FrontFace(frontFace);
                }
                finally
                {
                    try
                    {
                        if (cullFace) GL.Enable(EnableCap.CullFace);
                        else GL.Disable(EnableCap.CullFace);
                    }
                    finally
                    {
                        try { render.GLDepthMask(depthWrite); }
                        finally
                        {
                            if (depthTest) render.GLEnableDepthTest();
                            else render.GLDisableDepthTest();
                        }
                    }
                }
            }
        }
    }
}
