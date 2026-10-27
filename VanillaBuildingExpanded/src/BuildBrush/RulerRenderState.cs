using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal interface IRulerRenderState
{
    void Capture();
    void Restore();
}

internal sealed class RulerRenderState(IRenderAPI render) : IRulerRenderState
{
    private bool depthTest;
    private bool depthWrite;
    private float lineWidth;

    public void Capture()
    {
        // The public render API exposes setters only; query the engine's own GL binding.
        depthTest = GL.IsEnabled(EnableCap.DepthTest);
        depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        lineWidth = GL.GetFloat(GetPName.LineWidth);
    }

    public void Restore()
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
