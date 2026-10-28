using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Gizmos;

internal sealed partial class GizmoRenderAdapter : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly GizmoManager manager;
    private readonly Func<GizmoContext> capture;
    private readonly IOitLineRenderState state;
    private readonly ILineRenderState labelState;
    private readonly List<GizmoHandleDraw> labelDraws = [];
    private readonly OitLineShader shader;
    private readonly Dictionary<GizmoShape, MeshRef> meshes = [];
    private GizmoContext? context;
    private GizmoContext? drawnContext;
    private bool desired;
    private bool oitRegistered;
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
        Func<GizmoContext>? capture = null, IOitLineRenderState? state = null,
        ILineRenderState? labelState = null)
    {
        this.api = api;
        this.manager = manager;
        this.capture = capture ?? (() => GizmoCamera.Capture(api));
        this.state = state ?? new OitLineRenderState(api.Render);
        this.labelState = labelState ?? new GizmoRenderState(api.Render);
        shader = new OitLineShader(api);
    }

    public void SetActive(bool active)
    {
        if (disposed && active) throw new ObjectDisposedException(nameof(GizmoRenderAdapter));
        desired = active;
        if (!active)
        {
            labelDraws.Clear();
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
                if (!oitRegistered)
                {
                    api.Event.RegisterRenderer(this, EnumRenderStage.OIT, "vbe-gizmos");
                    oitRegistered = true;
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
            if (oitRegistered)
            {
                api.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
                oitRegistered = false;
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
            if (stage == EnumRenderStage.OIT)
            {
                labelDraws.Clear();
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
                if (disposed || failed || !desired) return;
                drawing = true;
                try
                {
                    manager.Render(next);
                    if (!disposed && !failed && desired) drawnContext = next;
                }
                finally { drawing = false; }
            }
            else if (stage == EnumRenderStage.AfterBlit && drawnContext is not null)
            {
                DrawLabels();
                PruneLabelTextures();
            }
        }
        catch (Exception error) when (IsRenderFailure(error)) { Fail(error); }
        finally { callbackDepth--; }
    }

    /// <summary>Draws the resolved snapshot shared with picking; handles retain no native resources.</summary>
    public void Draw(GizmoHandleDraw draw)
    {
        var geometry = draw.Geometry;
        var current = geometry.Context;
        if (!drawing || disposed || failed || !ReferenceEquals(current, context))
            throw new InvalidOperationException("Gizmo lines draw only inside their session's OIT callback.");
        if (!CanDraw(geometry)) return;
        var render = api.Render;
        var shader = this.shader.GetProgram();
        var previous = render.CurrentActiveShader;
        state.Capture();
        try
        {
            previous?.Stop();
            render.GLEnableDepthTest();
            render.GLDepthMask(false);
            shader.Use();
            shader.UniformMatrix("projectionMatrix", ToFloat(current.Camera!.Projection));
            if (!meshes.TryGetValue(geometry.Shape, out var mesh))
            {
                mesh = render.UploadMesh(CreateMesh(geometry.Shape))
                    ?? throw new InvalidOperationException("Gizmo mesh upload returned no mesh.");
                meshes.Add(geometry.Shape, mesh);
            }
            if (mesh.Disposed || !mesh.Initialized) throw new InvalidOperationException("Gizmo mesh is unavailable.");
            render.LineWidth = draw.Appearance.LineWidth;
            var color = draw.Appearance.Color;
            shader.UniformMatrix("modelViewMatrix", ModelView(current.Camera!, geometry));
            state.SetOccludedDepthTest(false);
            shader.Uniform("colorIn", color.X, color.Y, color.Z, color.W);
            if (CanDraw(geometry)) render.RenderMesh(mesh);
            state.SetOccludedDepthTest(true);
            shader.Uniform("colorIn", color.X, color.Y, color.Z, color.W * 0.3f);
            if (CanDraw(geometry)) render.RenderMesh(mesh);
            if (CanDraw(geometry) && geometry.Handle.Label is not null) labelDraws.Add(draw);
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

    private bool CanDraw(GizmoHandleGeometry geometry) => !disposed && !failed && desired
        && !geometry.Handle.IsDisposed && geometry.Handle.Visible
        && ReferenceEquals(geometry.Handle.Owner, geometry.Owner)
        && geometry.Owner.Owner == manager && geometry.Owner.IsEligible(geometry.Context.Dimension);

    internal static float[] ModelView(GizmoCamera camera, GizmoHandleGeometry part)
    {
        var offset = part.Frame.Origin - camera.ReferencePosition;
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
        labelDraws.Clear();
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
        || error is InvalidOperationException or ArgumentException or NotSupportedException or OverflowException
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
        foreach (var cached in labelTextures.Values)
        {
            try { cached.Texture.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        labelTextures.Clear();
        try { labelQuad?.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        labelQuad = null;
        try { if (labelShader is { Disposed: false }) labelShader.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        labelShader = null;
        try { shader.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Gizmo GPU cleanup failed.", errors);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        desired = false;
        labelDraws.Clear();
        context = null;
        drawnContext = null;
        ContextReady = null;
        try { manager.InvalidateFrame(); }
        finally { RequestMembership(); }
    }
}
