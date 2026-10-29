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
    private bool depthTest, depthWrite, cullFace;
    private DepthFunction depthFunction;
    private FrontFaceDirection frontFace;

    public void Capture()
    {
        depthTest = GL.IsEnabled(EnableCap.DepthTest);
        depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        cullFace = GL.IsEnabled(EnableCap.CullFace);
        depthFunction = (DepthFunction)GL.GetInteger(GetPName.DepthFunc);
        frontFace = (FrontFaceDirection)GL.GetInteger(GetPName.FrontFace);
    }

    public void Apply()
    {
        render.GLEnableDepthTest();
        render.GLDepthMask(false);
        GL.DepthFunc(DepthFunction.Lequal);
        // gl_FrontFacing must recover the same indexed winding as CPU qualification.
        GL.FrontFace(FrontFaceDirection.Ccw);
        // Geometry qualification includes static no-cull surfaces.
        GL.Disable(EnableCap.CullFace);
    }

    public void Restore()
    {
        try { GL.DepthFunc(depthFunction); }
        finally
        {
            try { GL.FrontFace(frontFace); }
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
