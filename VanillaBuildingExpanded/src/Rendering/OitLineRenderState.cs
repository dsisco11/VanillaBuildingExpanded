using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Rendering;

internal interface IOitLineRenderState : ILineRenderState
{
    void SetOccludedDepthTest(bool occluded);
}

internal sealed class OitLineRenderState(IRenderAPI render) : LineRenderState(render), IOitLineRenderState
{
    private DepthFunction depthFunction;

    public override void Capture()
    {
        depthFunction = (DepthFunction)GL.GetInteger(GetPName.DepthFunc);
        base.Capture();
    }

    public void SetOccludedDepthTest(bool occluded) =>
        GL.DepthFunc(occluded ? DepthFunction.Greater : DepthFunction.Lequal);

    public override void Restore()
    {
        try { GL.DepthFunc(depthFunction); }
        finally { base.Restore(); }
    }
}
