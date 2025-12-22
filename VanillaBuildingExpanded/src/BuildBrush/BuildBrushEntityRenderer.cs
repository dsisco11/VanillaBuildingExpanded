using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Custom renderer for the build brush entity.
/// Uses the TesselatorManager to get pre-tessellated block meshes for rendering.
/// </summary>
public class BuildBrushEntityRenderer : EntityRenderer
{
    private MultiTextureMeshRef? meshRef;
    private int currentBlockId;
    private readonly Matrixf modelMat = new();
    private readonly BuildBrushEntity brushEntity;

    #region Constants
    // Render colors
    private static readonly Vec4f ColorValid = ColorUtil.WhiteArgbVec;
    private static readonly Vec4f ColorInvalid = new(1f, .2f, .2f, 0.1f);
    protected static readonly Vec4f RenderGlow = new(1f, 1f, 1f, .1f);
    #endregion

    #region Accessors
    private BuildBrushInstance? BrushInstance => brushEntity?.BrushInstance;
    #endregion

    public BuildBrushEntityRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
        brushEntity = (BuildBrushEntity)entity;
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();

        // Subscribe to block change events
        var brushInstance = BrushInstance;
        if (brushInstance is not null)
        {
            brushInstance.OnBlockChanged += BrushInstance_OnBrushBlockChanged;
            brushInstance.OnOrientationChanged += BrushInstance_OnOrientationChanged;
        }

        RebuildMesh();
    }

    private void BrushInstance_OnOrientationChanged(BuildBrushInstance arg1, int arg2, BlockOrientationDefinition arg3)
    {
        RebuildMesh();
    }

    /// <summary>
    /// Called when the brush block changes.
    /// </summary>
    private void BrushInstance_OnBrushBlockChanged(BuildBrushInstance instance, Block? block)
    {
        RebuildMesh();
    }

    /// <summary>
    /// Rebuilds the mesh from the block in the dimension.
    /// </summary>
    public void RebuildMesh()
    {
        IMiniDimension? dimension = brushEntity.Dimension;
        if (dimension is null)
        {
            DisposeMesh();
            currentBlockId = 0;
            return;
        }

        // Get the block from the dimension at origin (0,0,0)
        BlockPos originPos = new(0, 0, 0, Dimensions.MiniDimensions);
        Block? block = dimension.GetBlock(originPos);
        if (block is null || block.BlockId == 0)
        {
            // get block from the build brush instance instead
            block = brushEntity?.BrushInstance?.BlockTransformed;
        }

        if (block is null || block.BlockId == 0)
        {
            DisposeMesh();
            currentBlockId = 0;
            return;
        }

        // Skip if block hasn't changed
        if (block.BlockId == currentBlockId && meshRef is not null)
            return;

        currentBlockId = block.BlockId;

        // Dispose old mesh before creating new one
        DisposeMesh();

        // Tesselate the block using the game's tesselator
        capi.Tesselator.TesselateBlock(block, out MeshData meshData);

        // Upload to GPU as multi-texture mesh
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

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        if (isShadowPass)
            return;

        if (meshRef is null)
            return;

        BuildBrushInstance? brush = BrushInstance;
        if (brush is null)
            return;

        IRenderAPI rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        Vec3d entityPos = brush.Position?.ToVec3d() ?? entity?.Pos.XYZ ?? Vec3d.Zero;

        // Build model matrix - translate to world position relative to camera
        modelMat.Identity();
        modelMat.Translate(
            (float)(entityPos.X - camPos.X),
            (float)(entityPos.Y - camPos.Y),
            (float)(entityPos.Z - camPos.Z)
        );

        // Get validity state
        bool isValid = brush.IsValidPlacement;

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
        shader.RgbaGlowIn = ColorUtil.WhiteArgbVec;
        shader.RgbaLightIn = ColorUtil.WhiteArgbVec;
        shader.DamageEffect = 0f;
        shader.Stop();
    }

    public override void Dispose()
    {
        // Unsubscribe from events
        var brushInstance = BrushInstance;
        if (brushInstance is not null)
        {
            brushInstance.OnBlockChanged -= BrushInstance_OnBrushBlockChanged;
            brushInstance.OnOrientationChanged -= BrushInstance_OnOrientationChanged;
        }

        DisposeMesh();
    }
}
