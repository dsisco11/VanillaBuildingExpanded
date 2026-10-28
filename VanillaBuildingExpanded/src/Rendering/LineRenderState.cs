using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Rendering;

internal interface ILineRenderState
{
    void Capture();
    void Restore();
}

internal class LineRenderState(IRenderAPI render) : ILineRenderState
{
    protected bool depthTest;
    protected bool depthWrite;
    protected float lineWidth;

    public virtual void Capture()
    {
        depthTest = GL.IsEnabled(EnableCap.DepthTest);
        depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        lineWidth = GL.GetFloat(GetPName.LineWidth);
    }

    public virtual void Restore()
    {
        try { render.LineWidth = lineWidth; }
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
