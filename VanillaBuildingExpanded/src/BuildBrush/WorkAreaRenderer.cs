using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using VanillaBuildingExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaRenderer : IRenderer
{
    internal const double VisualExpansion = 0.002;
    private readonly ICoreClientAPI api;
    private readonly System.Func<IReadOnlyList<WorkAreaBounds>> getBounds;
    private readonly Action<Exception> onFailure;
    private readonly IOitLineRenderState renderState;
    private readonly Matrixf model = new();
    private MeshRef? mesh;
    private readonly OitLineShader shader;
    private bool failed;
    private bool disposed;
    internal bool IsInCallback { get; private set; }

    public double RenderOrder => 0.51;
    public int RenderRange => int.MaxValue;

    public WorkAreaRenderer(ICoreClientAPI api, System.Func<IReadOnlyList<WorkAreaBounds>> getBounds,
        Action<Exception> onFailure, IOitLineRenderState? renderState = null)
    {
        this.api = api;
        this.getBounds = getBounds;
        this.onFailure = onFailure;
        this.renderState = renderState ?? new OitLineRenderState(api.Render);
        shader = new OitLineShader(api);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || failed || stage != EnumRenderStage.OIT) return;
        var bounds = getBounds();
        if (bounds.Count == 0) return;
        IsInCallback = true;
        try
        {
            var program = shader.GetProgram();
            mesh ??= api.Render.UploadMesh(CreateUnitCube())
                ?? throw new InvalidOperationException("Work area mesh upload returned no mesh.");
            if (!mesh.Initialized || mesh.Disposed)
                throw new InvalidOperationException("The work area mesh is unavailable.");
            var camera = api.World.Player.Entity.CameraPos;
            foreach (var box in bounds)
            {
                SetModelMatrix(model, api.Render.CameraMatrixOriginf, box, camera);
                Draw(program);
            }
        }
        catch (Exception error) when (IsRenderFailure(error))
        {
            failed = true;
            onFailure(error);
        }
        finally { IsInCallback = false; }
    }

    private void Draw(IShaderProgram shader)
    {
        var render = api.Render;
        var previousShader = render.CurrentActiveShader;
        renderState.Capture();
        try
        {
            previousShader?.Stop();
            render.GLEnableDepthTest();
            render.GLDepthMask(false);
            render.LineWidth = 1.6f;
            // Preserve the OIT pass's indexed accumulation/revealage blend settings.
            shader.Use();
            shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", model.Values);
            renderState.SetOccludedDepthTest(false);
            shader.Uniform("colorIn", 1f, 0.75f, 0.15f, 1f);
            render.RenderMesh(mesh!);
            renderState.SetOccludedDepthTest(true);
            shader.Uniform("colorIn", 1f, 0.75f, 0.15f, 0.3f);
            render.RenderMesh(mesh!);
        }
        finally
        {
            try { shader.Stop(); }
            finally
            {
                try { renderState.Restore(); }
                finally { previousShader?.Use(); }
            }
        }
    }

    internal static void SetModelMatrix(Matrixf model, float[] cameraMatrix, WorkAreaBounds bounds, Vec3d camera)
    {
        // Bounds stay local-Y; encode only the draw position to match native CameraPos.
        double dimensionY = (double)bounds.MinCell.Dimension * BlockPos.DimensionBoundary;
        model.Set(cameraMatrix)
            .Translate(bounds.RenderMinX - camera.X - VisualExpansion,
                bounds.RenderMinY + dimensionY - camera.Y - VisualExpansion,
                bounds.RenderMinZ - camera.Z - VisualExpansion)
            .Scale((float)(bounds.SizeX + 2 * VisualExpansion),
                (float)(bounds.SizeY + 2 * VisualExpansion),
                (float)(bounds.SizeZ + 2 * VisualExpansion));
    }

    internal static MeshData CreateUnitCube()
    {
        var data = new MeshData
        {
            xyz = new float[8 * 3],
            Rgba = new byte[8 * 4],
            Indices = new int[24],
            // Separation from block faces comes from VisualExpansion, not a clip-space depth bias.
            Flags = new int[8]
        };
        data.SetMode(EnumDrawMode.Lines);
        LineMeshUtil.AddLineLoop(data, new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0), -1);
        LineMeshUtil.AddLineLoop(data, new(0, 0, 1), new(0, 1, 1), new(1, 1, 1), new(1, 0, 1), -1);
        for (int i = 0; i < 4; i++)
        {
            data.Indices[data.IndicesCount++] = i;
            data.Indices[data.IndicesCount++] = i + 4;
        }
        return data;
    }

    internal static bool IsRenderFailure(Exception error) =>
        // Native CheckGlError throws exactly System.Exception; CheckGlErrorAlways can throw GPU OOM.
        error.GetType() == typeof(Exception)
        || error is InvalidOperationException or ArgumentException or NotSupportedException
            or ExternalException or OutOfMemoryException;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var ownedMesh = mesh;
        mesh = null;
        try { ownedMesh?.Dispose(); }
        finally { shader.Dispose(); }
    }
}
