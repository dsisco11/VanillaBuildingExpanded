using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Rendering;

internal sealed class GizmoRenderState(IRenderAPI render) : ILineRenderState
{
    private readonly LineRenderState lines = new(render);
    private bool blend;
    private int sourceRgb, destinationRgb, sourceAlpha, destinationAlpha, equationRgb, equationAlpha;

    public void Capture()
    {
        lines.Capture();
        blend = GL.IsEnabled(IndexedEnableCap.Blend, 0);
        // The binding omits indexed enum aliases for these valid blend-state queries.
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, 0, out sourceRgb);
        GL.GetInteger((GetIndexedPName)GetPName.BlendDstRgb, 0, out destinationRgb);
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcAlpha, 0, out sourceAlpha);
        GL.GetInteger((GetIndexedPName)GetPName.BlendDstAlpha, 0, out destinationAlpha);
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, 0, out equationRgb);
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationAlpha, 0, out equationAlpha);
        try
        {
            // Only the overlay's color output is affected; other MRT blend state stays untouched.
            GL.Enable(IndexedEnableCap.Blend, 0);
            GL.BlendEquationSeparate(0, BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            // Label textures retain Cairo's premultiplied alpha.
            GL.BlendFuncSeparate(0, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public void Restore()
    {
        try
        {
            GL.BlendEquationSeparate(0, (BlendEquationMode)equationRgb, (BlendEquationMode)equationAlpha);
            GL.BlendFuncSeparate(0, (BlendingFactorSrc)sourceRgb, (BlendingFactorDest)destinationRgb,
                (BlendingFactorSrc)sourceAlpha, (BlendingFactorDest)destinationAlpha);
            if (blend) GL.Enable(IndexedEnableCap.Blend, 0);
            else GL.Disable(IndexedEnableCap.Blend, 0);
        }
        finally { lines.Restore(); }
    }
}
