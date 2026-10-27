using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Gizmos;

internal sealed class GizmoRenderAdapter : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly GizmoManager manager;
    private readonly Func<GizmoContext> capture;
    private readonly ILineRenderState state;
    private IShaderProgram? shader;
    private readonly Dictionary<GizmoShape, MeshRef> meshes = [];
    private GizmoContext? context;
    private GizmoContext? drawnContext;
    private bool desired;
    private bool opaqueRegistered;
    private bool overlayRegistered;
    private bool failed;
    private bool disposed;
    private bool queued;
    private int callbackDepth;
    private bool drawing;
    public GizmoContext? Context => drawnContext;
    public bool IsAvailable => !failed && !disposed;
    internal bool IsInCallback => callbackDepth != 0;
    public double RenderOrder => 0.49;
    public int RenderRange => int.MaxValue;
    public event Action<GizmoContext>? ContextReady;

    public GizmoRenderAdapter(ICoreClientAPI api, GizmoManager manager,
        Func<GizmoContext>? capture = null, ILineRenderState? state = null)
    {
        this.api = api;
        this.manager = manager;
        this.capture = capture ?? (() => GizmoCamera.Capture(api));
        this.state = state ?? new GizmoRenderState(api.Render);
    }

    public void SetActive(bool active)
    {
        if (disposed && active) throw new ObjectDisposedException(nameof(GizmoRenderAdapter));
        desired = active;
        if (!active)
        {
            context = null;
            drawnContext = null;
            manager.InvalidateFrame();
        }
        RequestMembership();
    }

    private void RequestMembership()
    {
        if (callbackDepth == 0) { ReconcileMembership(); return; }
        if (queued) return;
        queued = true;
        api.Event.EnqueueMainThreadTask(() =>
        {
            queued = false;
            ReconcileMembership();
        }, "vbe-gizmo-render-membership");
    }

    private void ReconcileMembership()
    {
        if (desired && !disposed && !failed)
        {
            try
            {
                if (!opaqueRegistered)
                {
                    api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vbe-gizmos");
                    opaqueRegistered = true;
                }
                if (!overlayRegistered)
                {
                    api.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "vbe-gizmos-overlay");
                    overlayRegistered = true;
                }
            }
            catch (Exception error) when (IsRenderFailure(error)) { Fail(error); }
            return;
        }
        try
        {
            if (opaqueRegistered)
            {
                api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
                opaqueRegistered = false;
            }
        }
        finally
        {
            try
            {
                if (overlayRegistered)
                {
                    api.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
                    overlayRegistered = false;
                }
            }
            finally { if (disposed) DisposeResources(); }
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || failed || !desired) return;
        callbackDepth++;
        try
        {
            if (stage == EnumRenderStage.Opaque)
            {
                var next = capture();
                if (next.Camera is null) throw new InvalidOperationException("Gizmo camera capture returned no camera.");
                if (context is { Camera: { } old } && (context.Dimension != next.Dimension
                    || old.Width != next.Camera.Width || old.Height != next.Camera.Height
                    || old.MouseGrabbed != next.Camera.MouseGrabbed || old.CameraMode != next.Camera.CameraMode))
                {
                    drawnContext = null;
                    manager.InvalidateFrame();
                }
                context = next;
                ContextReady?.Invoke(next);
            }
            else if (stage == EnumRenderStage.AfterBlit && context is { } current)
            {
                drawing = true;
                try
                {
                    manager.Render(current);
                    if (!disposed && !failed && desired) drawnContext = current;
                }
                finally { drawing = false; }
            }
        }
        catch (Exception error) when (IsRenderFailure(error)) { Fail(error); }
        finally { callbackDepth--; }
    }

    public void Draw(PositionGizmoDraw draw)
    {
        if (!drawing || disposed || failed || !ReferenceEquals(draw.Context, context))
            throw new InvalidOperationException("Gizmos draw only inside their session's overlay callback.");
        var render = api.Render;
        var shader = GetShader();
        var previous = render.CurrentActiveShader;
        state.Capture();
        try
        {
            previous?.Stop();
            render.GLDisableDepthTest();
            render.GLDepthMask(false);
            shader.Use();
            shader.UniformMatrix("projectionMatrix", ToFloat(draw.Context.Camera!.Projection));
            foreach (var part in draw.Geometry.Parts)
            {
                if (disposed || failed || !desired || !draw.Gizmo.IsEligible(draw.Context.Dimension)) break;
                if (!meshes.TryGetValue(part.Shape, out var mesh))
                {
                    mesh = render.UploadMesh(CreateMesh(part.Shape))
                        ?? throw new InvalidOperationException("Gizmo mesh upload returned no mesh.");
                    meshes.Add(part.Shape, mesh);
                }
                if (mesh.Disposed || !mesh.Initialized) throw new InvalidOperationException("Gizmo mesh is unavailable.");
                bool emphasized = draw.Gizmo.CapturedHandle == part.Handle || draw.Gizmo.HoveredHandle == part.Handle;
                render.LineWidth = emphasized ? 2.8f : 1.6f;
                var color = part.Handle.Value switch
                {
                    1 => (1f, 0.2f, 0.2f), 2 => (0.2f, 1f, 0.2f), 3 => (0.25f, 0.5f, 1f),
                    _ => (1f, 0.85f, 0.25f)
                };
                shader.Uniform("colorIn", color.Item1, color.Item2, color.Item3, emphasized ? 1f : 0.8f);
                shader.UniformMatrix("modelViewMatrix", ModelView(draw.Context.Camera!, part));
                render.RenderMesh(mesh);
            }
        }
        finally
        {
            try { shader.Stop(); }
            finally
            {
                try { state.Restore(); }
                finally { previous?.Use(); }
            }
        }
    }

    private IShaderProgram GetShader()
    {
        if (shader is null || shader.Disposed)
        {
            shader = api.Shader.NewShaderProgram()
                ?? throw new InvalidOperationException("Gizmo shader creation returned no program.");
            var vertex = api.Shader.NewShader(EnumShaderType.VertexShader)
                ?? throw new InvalidOperationException("Gizmo vertex shader creation failed.");
            vertex.Code = """
                #version 330 core
                layout(location = 0) in vec3 vertexPositionIn;
                uniform mat4 projectionMatrix;
                uniform mat4 modelViewMatrix;
                void main() {
                    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertexPositionIn, 1.0);
                }
                """;
            shader.VertexShader = vertex;
            var fragment = api.Shader.NewShader(EnumShaderType.FragmentShader)
                ?? throw new InvalidOperationException("Gizmo fragment shader creation failed.");
            fragment.Code = """
                #version 330 core
                uniform vec4 colorIn;
                layout(location = 0) out vec4 outColor;
                void main() {
                    outColor = colorIn;
                }
                """;
            shader.FragmentShader = fragment;
            api.Shader.RegisterMemoryShaderProgram("vbe-gizmo-overlay", shader);
            if (!shader.Compile()) throw new InvalidOperationException("The unlit gizmo shader failed to compile.");
        }
        if (shader.LoadError) throw new InvalidOperationException("The unlit gizmo shader is unavailable.");
        return shader;
    }

    internal static float[] ModelView(GizmoCamera camera, PositionGizmoPart part)
    {
        var offset = part.Origin - camera.ReferencePosition;
        var model = new double[16];
        for (int i = 0; i < 3; i++)
        {
            model[i] = part.BasisX[i];
            model[4 + i] = part.BasisY[i];
            model[8 + i] = part.BasisZ[i];
            model[12 + i] = offset[i];
        }
        model[15] = 1;
        var view = camera.View.ToArray();
        return ToFloat(Mat4d.Multiply(new double[16], view, model));
    }

    internal static MeshData CreateMesh(GizmoShape shape)
    {
        var vertices = GizmoShapes.Vertices(shape);
        var indices = GizmoShapes.Indices(shape);
        var mesh = new MeshData
        {
            xyz = new float[vertices.Length * 3], Rgba = new byte[vertices.Length * 4],
            Flags = new int[vertices.Length], Indices = indices.ToArray(),
            VerticesCount = vertices.Length, IndicesCount = indices.Length
        };
        mesh.SetMode(EnumDrawMode.Lines);
        Array.Fill(mesh.Rgba, (byte)255);
        for (int i = 0; i < vertices.Length; i++)
            for (int axis = 0; axis < 3; axis++) mesh.xyz[3 * i + axis] = (float)vertices[i][axis];
        return mesh;
    }

    private static float[] ToFloat(ReadOnlySpan<double> matrix)
    {
        var result = new float[16];
        for (int i = 0; i < result.Length; i++) result[i] = (float)matrix[i];
        return result;
    }

    private void Fail(Exception error)
    {
        if (failed || disposed) return;
        failed = true;
        desired = false;
        context = null;
        drawnContext = null;
        try { manager.InvalidateFrame(); }
        finally
        {
            try
            {
                api.Logger.Error("[Gizmo] In-world controls unavailable for this session: {0}", error);
                string code = "vanillabuildingexpanded:gizmo-render-unavailable";
                api.TriggerIngameError(this, code, Lang.Get(code));
            }
            finally { RequestMembership(); }
        }
    }

    private static bool IsRenderFailure(Exception error) => error.GetType() == typeof(Exception)
        || error is InvalidOperationException or ArgumentException or NotSupportedException
            or ExternalException or OutOfMemoryException;

    private void DisposeResources()
    {
        List<Exception> errors = [];
        foreach (var mesh in meshes.Values)
        {
            try { mesh.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        meshes.Clear();
        try { if (shader is { Disposed: false }) shader.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        shader = null;
        if (errors.Count != 0) throw new AggregateException("Gizmo GPU cleanup failed.", errors);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        desired = false;
        context = null;
        drawnContext = null;
        ContextReady = null;
        try { manager.InvalidateFrame(); }
        finally { RequestMembership(); }
    }
}
