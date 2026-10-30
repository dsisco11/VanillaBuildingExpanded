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
    private readonly ICoreClientAPI api;
    private readonly Func<BrushTargetGeometry?> getTarget;
    private readonly IBrushOverlayRenderState state;
    private readonly BrushSnappingShaderOwner<IBrushSnappingShader> shader;
    private readonly BrushSnappingShaderOwner<IBrushSnappingChevronShader> chevronShader;
    private readonly Matrixf model = new();
    private bool failed, disposed;

    internal bool IsInCallback { get; private set; }
    public double RenderOrder => 0.52;
    public int RenderRange => int.MaxValue;

    public BrushSnappingRenderer(ICoreClientAPI api, Func<BrushTargetGeometry?> getTarget,
        IBrushOverlayRenderState? state = null, Func<IBrushSnappingShader>? createShader = null,
        Func<IBrushSnappingChevronShader>? createChevronShader = null)
    {
        this.api = api;
        this.getTarget = getTarget;
        this.state = state ?? new BrushOverlayRenderState(api.Render);
        shader = new(api, "vbe-snapping", createShader ?? (static () => new BrushSnappingShader()));
        chevronShader = new(api, "vbe-snapping-chevron",
            createChevronShader ?? (static () => new BrushSnappingChevronShader()));
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || failed || stage != Stage) return;
        IsInCallback = true;
        try
        {
            var target = getTarget();
            if (target is null || !target.Presentation.ShowsSnappingRegions)
                return;
            var program = shader.GetProgram();
            var mesh = target.Mesh.GetOrUpload();
            var chevrons = target.Mesh.GetChevrons(target.Presentation.Face)?.GetOrUpload();
            var chevronProgram = chevrons is null ? null : chevronShader.GetProgram();
            target.SetModelMatrix(model, api.Render.CameraMatrixOriginf, api.World.Player.Entity.CameraPos);
            Draw(program, chevronProgram, mesh, chevrons, target.Presentation);
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
            chevronShader.Replace();
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

    private void Draw(IBrushSnappingShader program, IBrushSnappingChevronShader? chevronProgram,
        MeshRef mesh, MeshRef? chevrons,
        BrushSnappingPresentation presentation)
    {
        var render = api.Render;
        var previous = render.CurrentActiveShader;
        state.Capture();
        try
        {
            previous?.Stop();
            state.Apply();
            try
            {
                program.Use();
                SetSurfaceUniforms(program, presentation);
                program.ColorIn = new Vector4(1f, 1f, 1f, 0.08f);
                program.CenterThreshold = BrushSnapping.CenterThreshold;
                render.RenderMesh(mesh);
            }
            finally { program.Stop(); }
            if (chevronProgram is not null && chevrons is not null)
            {
                try
                {
                    chevronProgram.Use();
                    SetSurfaceUniforms(chevronProgram, presentation);
                    chevronProgram.ChevronColor = presentation.Result.IsValid
                        ? new Vector4(1f, 0.5f, 0f, 1f) : new Vector4(1f, 0f, 0f, 1f);
                    render.RenderMesh(chevrons);
                }
                finally { chevronProgram.Stop(); }
            }
        }
        finally
        {
            try { state.Restore(); }
            finally { previous?.Use(); }
        }
    }

    private void SetSurfaceUniforms(IBrushSnappingSurfaceShader program, BrushSnappingPresentation presentation)
    {
        program.ProjectionMatrix = api.Render.CurrentProjectionMatrix;
        program.ModelViewMatrix = model.Values;
        BlockFacing face = presentation.Face;
        program.SelectedNormal = new Vector3(face.Normalf.X, face.Normalf.Y, face.Normalf.Z);
        program.AxisU = face.Axis == EnumAxis.X ? Vector3.UnitZ : Vector3.UnitX;
        program.AxisV = face.Axis == EnumAxis.Y ? Vector3.UnitZ : Vector3.UnitY;
        BrushSnappingState resolved = presentation.Result.ResolvedSnapping;
        program.ResolvedDirections = new Vector2(resolved.FaceU, resolved.FaceV);
        program.DepthBias = DepthBias;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { shader.Dispose(); }
        finally { chevronShader.Dispose(); }
    }
}
