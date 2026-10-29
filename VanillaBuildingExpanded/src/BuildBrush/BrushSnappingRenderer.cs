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
    private readonly BrushSnappingShaderOwner shader;
    private readonly Matrixf model = new();
    private bool failed, disposed;

    internal bool IsInCallback { get; private set; }
    public double RenderOrder => 0.52;
    public int RenderRange => int.MaxValue;

    public BrushSnappingRenderer(ICoreClientAPI api, Func<BrushTargetGeometry?> getTarget,
        IBrushOverlayRenderState? state = null, Func<IBrushSnappingShader>? createShader = null)
    {
        this.api = api;
        this.getTarget = getTarget;
        this.state = state ?? new BrushOverlayRenderState(api.Render);
        shader = new BrushSnappingShaderOwner(api, createShader);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || failed || stage != Stage) return;
        IsInCallback = true;
        try
        {
            var target = getTarget();
            if (target is null || !target.Presentation.HasTangentialSnapping)
                return;
            var program = shader.GetProgram();
            var mesh = target.Mesh.GetOrUpload();
            target.SetModelMatrix(model, api.Render.CameraMatrixOriginf, api.World.Player.Entity.CameraPos);
            Draw(program, mesh, target.Presentation);
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            Disable(error);
        }
        finally { IsInCallback = false; }
    }

    public bool ReloadShader()
    {
        if (disposed) return false;
        try
        {
            shader.Replace();
            failed = false;
            return true;
        }
        catch (Exception error) when (WorkAreaRenderer.IsRenderFailure(error))
        {
            Disable(error);
            return false;
        }
    }

    internal void Disable(Exception error)
    {
        failed = true;
        api.Logger.Error("[BuildBrush] Snapping indicator unavailable until shader reload: {0}", error);
    }

    private void Draw(IBrushSnappingShader program, MeshRef mesh, BrushSnappingPresentation presentation)
    {
        var render = api.Render;
        var previous = render.CurrentActiveShader;
        state.Capture();
        try
        {
            previous?.Stop();
            state.Apply();
            program.Use();
            program.ProjectionMatrix = render.CurrentProjectionMatrix;
            program.ModelViewMatrix = model.Values;
            BlockFacing face = presentation.Face;
            Vector3 u = face.Axis == EnumAxis.X ? Vector3.UnitZ : Vector3.UnitX;
            Vector3 v = face.Axis == EnumAxis.Y ? Vector3.UnitZ : Vector3.UnitY;
            program.SelectedNormal = new Vector3(face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
            program.AxisU = u;
            program.AxisV = v;
            BrushSnappingState resolved = presentation.Result.ResolvedSnapping;
            program.ResolvedDirections = new Vector2(resolved.FaceU, resolved.FaceV);
            program.ColorIn = presentation.Result.IsValid
                ? new Vector4(0.2f, 0.85f, 1f, 0.7f) : new Vector4(1f, 0f, 0f, 0.7f);
            program.MarkingWidth = MarkingWidth;
            program.DepthBias = DepthBias;
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
