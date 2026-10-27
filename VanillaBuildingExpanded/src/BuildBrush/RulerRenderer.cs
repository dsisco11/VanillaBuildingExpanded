using System;
using System.Runtime.InteropServices;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerRenderer : IRenderer
{
    internal const double VisualExpansion = 0.002;
    private readonly ICoreClientAPI api;
    private readonly System.Func<RulerBounds?> getBounds;
    private readonly Action<Exception> onFailure;
    private readonly IRulerRenderState renderState;
    private readonly Matrixf model = new();
    private MeshRef? mesh;
    private bool failed;
    private bool disposed;

    public double RenderOrder => 0.51;
    public int RenderRange => int.MaxValue;

    public RulerRenderer(ICoreClientAPI api, System.Func<RulerBounds?> getBounds,
        Action<Exception> onFailure, IRulerRenderState? renderState = null)
    {
        this.api = api;
        this.getBounds = getBounds;
        this.onFailure = onFailure;
        this.renderState = renderState ?? new RulerRenderState(api.Render);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || failed || stage != EnumRenderStage.Opaque || getBounds() is not { } bounds) return;
        try
        {
            var shader = api.Shader.GetProgram((int)EnumShaderProgram.Wireframe);
            if (shader is null || shader.Disposed || shader.LoadError)
                throw new InvalidOperationException("The engine wireframe shader is unavailable.");
            mesh ??= api.Render.UploadMesh(CreateUnitCube())
                ?? throw new InvalidOperationException("Ruler mesh upload returned no mesh.");
            if (!mesh.Initialized || mesh.Disposed)
                throw new InvalidOperationException("The ruler mesh is unavailable.");
            var camera = api.World.Player.Entity.CameraPos;
            SetModelMatrix(model, api.Render.CameraMatrixOriginf, bounds, camera);
            Draw(shader);
        }
        catch (Exception error) when (IsRenderFailure(error))
        {
            failed = true;
            onFailure(error);
        }
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
            // Opaque color needs no blend change, preserving even indexed MRT blend settings.
            shader.Use();
            shader.Uniform("origin", 0f, 0f, 0f);
            shader.Uniform("extraGlow", 0f);
            shader.Uniform("colorIn", 1f, 0.75f, 0.15f, 1f);
            shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", model.Values);
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

    internal static void SetModelMatrix(Matrixf model, float[] cameraMatrix, RulerBounds bounds, Vec3d camera)
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
            // Zero flags avoid wind and extra Z-offset; the native shader already biases W by 0.0014.
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
        ownedMesh?.Dispose();
    }
}
