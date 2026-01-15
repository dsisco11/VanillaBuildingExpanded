using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

using VanillaBuildingExpanded.BuildHammer.Tessellation;

namespace VanillaBuildingExpanded.BuildHammer;

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

        lastSeenDirtyCounter = brushEntity.WatchedAttributes.GetInt(BuildBrushEntity.BrushDirtyCounterKey);
        RebuildMesh();
    }

    private void RebuildMeshIfDirtyCounterChanged()
    {
        int dirtyCounter = brushEntity.WatchedAttributes.GetInt(BuildBrushEntity.BrushDirtyCounterKey);
        if (dirtyCounter == lastSeenDirtyCounter)
        {
            return;
        }

        lastSeenDirtyCounter = dirtyCounter;
        RebuildMesh();
    }

    /// <summary>
    /// Rebuilds the mesh from the blocks in the dimension using async tessellation.
    /// </summary>
    public void RebuildMesh()
    {
        if (capi.World.Side != EnumAppSide.Client)
            return;

        IMiniDimension? dimension = brushEntity.Dimension;

        if (dimension is null)
        {
            DisposeMesh();
            return;
        }

        // Get the block to check if it changed
        BlockPos originPos = new(0, 0, 0, Dimensions.MiniDimensions);
        Block? block = dimension.GetBlock(originPos);
        if (block is null || block.BlockId == 0)
        {
            block = brushEntity?.BrushInstance?.CurrentPlacementBlock;
        }

        if (block is null || block.BlockId == 0)
        {
            DisposeMesh();
            return;
        }

        // Get active bounds from watched attrs, falling back to origin for single-block previews.
        if (!brushEntity.TryGetPreviewBounds(out BlockPos min, out BlockPos max))
        {
            BlockPos originFallback = new(0, 0, 0, Dimensions.MiniDimensions);
            Block? originBlock = dimension.GetBlock(originFallback);
            if (originBlock is null || originBlock.BlockId == 0)
            {
                DisposeMesh();
                return;
            }

            min = originFallback;
            max = originFallback;
        }

        capi.Event.EnqueueMainThreadTask(() =>
        {
            MeshData ? meshData = tessellator.Tessellate(dimension, min, max);
            _upload_mesh(meshData);
        }, $"{nameof(BuildBrushEntityRenderer)}.{nameof(RebuildMesh)}");

        void _upload_mesh(MeshData? meshData)
        {
            if (meshData is null || meshData.VerticesCount == 0)
            {
                return;
            }

            // Dispose old mesh and upload new one
            DisposeMesh();
            meshRef = capi.Render.UploadMultiTextureMesh(meshData);
        }
    }

    /// <summary>
    /// Disposes the current mesh and frees GPU resources.
    /// </summary>
    private void DisposeMesh()
    {
        meshRef?.Dispose();
        meshRef = null;
    }

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        if (isShadowPass)
            return;

        // Server-side preview updates are signaled via the watched dirty counter.
        // This avoids mesh rebuild on cursor movement (translation-only updates).
        RebuildMeshIfDirtyCounterChanged();

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
        DisposeMesh();
    }
}
