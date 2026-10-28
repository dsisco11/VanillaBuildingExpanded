using OpenTK.Graphics.OpenGL;

using VanillaBuildingExpanded.Rendering;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal interface IWorkAreaRenderState : ILineRenderState
{
    void SetOccludedDepthTest(bool occluded);
}

internal sealed class WorkAreaRenderState(IRenderAPI render) : LineRenderState(render), IWorkAreaRenderState
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
