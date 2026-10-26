using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

using VanillaBuildingExpanded.BuildBrush.Tessellation;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>
/// Custom renderer for the build brush entity.
/// Uses the MiniDimensionTessellator to tessellate blocks including block entities.
/// </summary>
public class BuildBrushEntityRenderer : EntityRenderer
{
    private MultiTextureMeshRef? meshRef;
    private readonly Matrixf modelMat = new();
    private readonly BuildBrushEntity brushEntity;
    private readonly MiniDimensionTessellator tessellator;
    private readonly Vec4f RgbaGlowClear = new(1, 1, 1, 0);

    private int lastSeenDirtyCounter = -1;
    private int lastSeenBlockId = -1;
    private bool rebuildQueued;
    private bool disposed;

    #region Constants
    // Render colors
    private static readonly Vec4f ColorValid = ColorUtil.WhiteArgbVec;
    private static readonly Vec4f ColorInvalid = new(1f, .2f, .2f, 0.1f);
    protected static readonly Vec4f RenderGlow = new(1f, 1f, 1f, .1f);
    #endregion

    public BuildBrushEntityRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
        brushEntity = (BuildBrushEntity)entity;
        tessellator = new MiniDimensionTessellator(api);
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();

        RebuildMesh();
    }

    private void RebuildMeshIfChanged()
    {
        GetPreviewBounds(out BlockPos min, out _);
        int dirtyCounter = brushEntity.WatchedAttributes.GetInt(BuildBrushEntity.BrushDirtyCounterKey);
        int blockId = brushEntity.Dimension?.GetBlock(min)?.BlockId ?? 0;
        if (dirtyCounter == lastSeenDirtyCounter && blockId == lastSeenBlockId)
        {
            return;
        }

        RebuildMesh();
    }

    private void GetPreviewBounds(out BlockPos min, out BlockPos max)
    {
        if (!brushEntity.TryGetPreviewBounds(out min, out max))
        {
            min = new BlockPos(0, 0, 0, Dimensions.MiniDimensions);
            max = min;
        }
    }

    /// <summary>
    /// Queues a main-thread mesh rebuild, coalescing requests until it runs.
    /// </summary>
    public void RebuildMesh()
    {
        if (disposed || rebuildQueued || capi.World.Side != EnumAppSide.Client)
            return;

        rebuildQueued = true;
        capi.Event.EnqueueMainThreadTask(() =>
        {
            rebuildQueued = false;
            if (!disposed)
                RebuildMeshNow();
        }, $"{nameof(BuildBrushEntityRenderer)}.{nameof(RebuildMesh)}");
    }

    private void RebuildMeshNow()
    {
        IMiniDimension? dimension = brushEntity.Dimension;
        GetPreviewBounds(out BlockPos min, out BlockPos max);
        lastSeenDirtyCounter = brushEntity.WatchedAttributes.GetInt(BuildBrushEntity.BrushDirtyCounterKey);
        lastSeenBlockId = dimension?.GetBlock(min)?.BlockId ?? 0;

        if (dimension is null || lastSeenBlockId == 0)
        {
            DisposeMesh();
            return;
        }

        MeshData? meshData = tessellator.Tessellate(dimension, min, max);
        if (meshData is null || meshData.VerticesCount == 0)
        {
            return;
        }

        DisposeMesh();
        meshRef = capi.Render.UploadMultiTextureMesh(meshData);
    }

    /// <summary>
    /// Disposes the current mesh and frees GPU resources.
    /// </summary>
    private void DisposeMesh()
    {
        meshRef?.Dispose();
        meshRef = null;
    }

    /// <summary>Renders an available preview, hiding it immediately when the local brush is disabled.</summary>
    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        if (isShadowPass || brushEntity.BrushInstance?.IsDisabled == true
            || !brushEntity.WatchedAttributes.GetBool(BuildBrushEntity.BrushVisibleKey))
            return;

        // Chunks and watched attributes arrive independently; neither must be the only rebuild signal.
        RebuildMeshIfChanged();

        if (meshRef is null)
            return;

        IRenderAPI rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        Vec3d entityPos = entity?.Pos.XYZ ?? Vec3d.Zero;

        // Build model matrix - translate to world position relative to camera
        modelMat.Identity();
        modelMat.Translate(
            (float)(entityPos.X - camPos.X),
            (float)(entityPos.Y - camPos.Y),
            (float)(entityPos.Z - camPos.Z)
        );

        // Get validity state from watched attributes
        bool isValid = brushEntity.WatchedAttributes.GetBool(BuildBrushEntity.BrushIsValidKey);

        // Setup the shader
        IStandardShaderProgram shader = rapi.StandardShader;
        shader.Use();
        // settings
        shader.OverlayOpacity = 0;
        shader.NormalShaded = 1;
        shader.AlphaTest = 0.05f;
        shader.ExtraZOffset = 0.00001f; // to prevent z-fighting
        shader.DontWarpVertices = 1;
        shader.AddRenderFlags = 0;
        // colors
        shader.RgbaTint = isValid ? ColorValid : ColorInvalid;
        shader.ExtraGlow = 32;
        shader.RgbaGlowIn = RenderGlow;
        // lighting
        shader.RgbaLightIn = ColorUtil.WhiteArgbVec;
        shader.RgbaAmbientIn = rapi.AmbientColor;
        shader.RgbaFogIn = rapi.FogColor;
        shader.FogMinIn = rapi.FogMin;
        shader.FogDensityIn = rapi.FogDensity;
        // matrices
        shader.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        shader.ViewMatrix = rapi.CameraMatrixOriginf;
        shader.ModelMatrix = modelMat.Values;

        // Render the mesh
        rapi.RenderMultiTextureMesh(meshRef, "tex");

        // Reset shader state
        shader.ExtraGlow = 0;
        shader.RgbaGlowIn = RgbaGlowClear;
        shader.RgbaLightIn = ColorUtil.WhiteArgbVec;
        shader.DamageEffect = 0f;
        shader.Stop();
    }

    public override void Dispose()
    {
        disposed = true;
        DisposeMesh();
    }
}
