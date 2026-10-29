using System;
using System.Numerics;

using VanillaBuildingExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushSnappingRenderer : IRenderer
{
    internal const EnumRenderStage Stage = EnumRenderStage.OIT;
    internal const float DepthBias = 0.00001f;
    internal const float MarkingWidth = 0.025f;
    private readonly ICoreClientAPI api;
    private readonly Func<BrushTargetGeometry?> getTarget;
    private readonly IBrushOverlayRenderState state;
    private readonly BrushSnappingShader shader;
    private readonly Matrixf model = new();
    private bool failed, disposed;

    public double RenderOrder => 0.52;
    public int RenderRange => int.MaxValue;

    public BrushSnappingRenderer(ICoreClientAPI api, Func<BrushTargetGeometry?> getTarget,
        IBrushOverlayRenderState? state = null)
    {
        this.api = api;
        this.getTarget = getTarget;
        this.state = state ?? new BrushOverlayRenderState(api.Render);
        shader = new BrushSnappingShader(api);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || failed || stage != Stage) return;
        try
        {
            var target = getTarget();
            if (target is null || target.Presentation.Result.Outcome != BrushPlacementOutcome.AcceptedSnapping)
                return;
            var program = shader.GetProgram();
            var mesh = target.Mesh.GetOrUpload();
            target.SetModelMatrix(model, api.Render.CameraMatrixOriginf, api.World.Player.Entity.CameraPos);
            Draw(program, mesh, target.Presentation);
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            Fail(error);
        }
    }

    public void ReloadShader()
    {
        if (disposed) return;
        try
        {
            shader.Replace();
            failed = false;
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            Fail(error);
        }
    }

    private void Fail(Exception error)
    {
        failed = true;
        api.Logger.Error("[BuildBrush] Snapping indicator unavailable until shader reload: {0}", error);
    }

    private void Draw(IShaderProgram program, MeshRef mesh, BrushSnappingPresentation presentation)
    {
        var render = api.Render;
        var previous = render.CurrentActiveShader;
        state.Capture();
        try
        {
            previous?.Stop();
            state.Apply();
            program.Use();
            program.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            program.UniformMatrix("modelViewMatrix", model.Values);
            BlockFacing face = presentation.Face;
            Vector3 u = face.Axis == EnumAxis.X ? Vector3.UnitZ : Vector3.UnitX;
            Vector3 v = face.Axis == EnumAxis.Y ? Vector3.UnitZ : Vector3.UnitY;
            program.Uniform("selectedNormal", face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
            program.Uniform("axisU", u.X, u.Y, u.Z);
            program.Uniform("axisV", v.X, v.Y, v.Z);
            BrushSnappingState applied = presentation.Result.AppliedSnapping;
            program.Uniform("appliedDirections", (float)applied.FaceU, (float)applied.FaceV);
            program.Uniform("colorIn", 0.2f, 0.85f, 1f, 0.7f);
            program.Uniform("markingWidth", MarkingWidth);
            program.Uniform("depthBias", DepthBias);
            render.RenderMesh(mesh);
        }
        finally
        {
            try { program.Stop(); }
            finally
            {
                try { state.Restore(); }
                finally { previous?.Use(); }
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        shader.Dispose();
    }
}
