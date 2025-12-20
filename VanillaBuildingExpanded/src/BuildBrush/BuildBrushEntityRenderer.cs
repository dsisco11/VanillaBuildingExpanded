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
    private ItemRenderInfo? renderInfo;
    //private MultiTextureMeshRef? meshRef;
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
    private MultiTextureMeshRef? meshRef => renderInfo?.ModelRef;
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
            brushInstance.OnBlockChanged += OnBrushBlockChanged;
        }

        RebuildMesh();
    }

    /// <summary>
    /// Called when the brush block changes.
    /// </summary>
    private void OnBrushBlockChanged(BuildBrushInstance instance, Block? block)
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
            //meshRef?.Dispose();
            //meshRef = null;
            renderInfo = null;
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
            //meshRef?.Dispose();
            //meshRef = null;
            renderInfo = null;
            currentBlockId = 0;
            return;
        }

        if (block.BlockId == currentBlockId && meshRef is not null)
            return;

        currentBlockId = block.BlockId;
        //meshRef?.Dispose();

        renderInfo = capi.Render.GetItemStackRenderInfo(BrushInstance?.DummySlot, EnumItemRenderTarget.Ground, 0);
        if (renderInfo?.ModelRef is null)
            return;

        // Use the TesselatorManager to get the pre-tessellated mesh
        //MeshData? blockMesh = capi.TesselatorManager.GetDefaultBlockMesh(block);
        //var blockMesh = capi.TesselatorManager.GetDefaultBlockMeshRef(block);
        //if (blockMesh is null)
        //{
        //    meshRef = null;
        //    return;
        //}

        //// Clone it so we don't modify the cached version
        //MeshData mesh = blockMesh.Clone();

        //// Upload to GPU
        //meshRef = capi.Render.UploadMesh(mesh);
        //meshRef = blockMesh;
    }

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        if (isShadowPass)
            return;

        if (renderInfo is null)
            return;

        BuildBrushInstance? brush = BrushInstance;
        if (brush is null)
            return;

        IRenderAPI rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        //Vec3d entityPos = entity.Pos.XYZ;
        Vec3d entityPos = brush.Position?.ToVec3d() ?? Vec3d.Zero;

        // Build model matrix
        modelMat.Identity();
        // TODO: Shouldnt need to subtract the camera position here, the view matrix theoretically should have already been inverse offset by the camera position...
        modelMat.Translate(
            (float)(entityPos.X - camPos.X),
            (float)(entityPos.Y - camPos.Y),
            (float)(entityPos.Z - camPos.Z)
        );

        // Get validity state
        bool isValid = brush.IsValidPlacement; // entity.WatchedAttributes.GetBool("isValid", true);

        // Setup the shader
        IStandardShaderProgram shader = rapi.StandardShader;
        shader.Use();
        // settings
        shader.OverlayOpacity = renderInfo.OverlayOpacity;
        shader.NormalShaded = renderInfo.NormalShaded ? 1 : 0;
        shader.AlphaTest =renderInfo.AlphaTest;
        shader.ExtraZOffset = 0.00001f;
        shader.DontWarpVertices = 1;
        shader.AddRenderFlags = 0;
        // colors
        //shader.RgbaTint = ColorUtil.WhiteArgbVec;
        //shader.RgbaLightIn = isValid ? ColorValid : ColorInvalid;
        shader.RgbaTint = isValid ? ColorValid : ColorInvalid;
        shader.ExtraGlow = 64;
        shader.RgbaGlowIn = RenderGlow;
        // lighting
        shader.RgbaLightIn = ColorUtil.WhiteArgbVec;
        shader.RgbaAmbientIn = rapi.AmbientColor;
        shader.RgbaFogIn = rapi.FogColor;
        shader.FogMinIn = rapi.FogMin;
        shader.FogDensityIn = rapi.FogDensity;
        // matricies
        shader.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        shader.ViewMatrix = rapi.CameraMatrixOriginf;
        shader.ModelMatrix = modelMat.Values;
        // texturing
        if (renderInfo.OverlayTexture is not null && renderInfo.OverlayOpacity > 0f)
        {
            shader.Tex2dOverlay2D = renderInfo.OverlayTexture.TextureId;
            shader.OverlayTextureSize = new Vec2f(renderInfo.OverlayTexture.Width, renderInfo.OverlayTexture.Height);
            shader.BaseTextureSize = new Vec2f(renderInfo.TextureSize.Width, renderInfo.TextureSize.Height);
            TextureAtlasPosition texPos = rapi.GetTextureAtlasPosition(brush.ItemStack);
            shader.BaseUvOrigin = new Vec2f(texPos.x1, texPos.y1);
        }

        // Render the mesh
        if (!renderInfo.CullFaces)
        {
            rapi.GlDisableCullFace();
        }
        // Render the mesh
        rapi.RenderMultiTextureMesh(renderInfo.ModelRef, "tex");
        // Reset state
        if (!renderInfo.CullFaces)
        {
            rapi.GlEnableCullFace();
        }

        shader.ExtraGlow = 0;
        shader.RgbaGlowIn = ColorUtil.WhiteArgbVec;
        shader.RgbaLightIn = ColorUtil.WhiteArgbVec;
        shader.AddRenderFlags = 0;
        shader.DamageEffect = 0f;
        shader.Stop();
    }

    public override void Dispose()
    {
        // Unsubscribe from events
        var brushInstance = BrushInstance;
        if (brushInstance is not null)
        {
            brushInstance.OnBlockChanged -= OnBrushBlockChanged;
        }

        //meshRef?.Dispose();
        //meshRef = null;
    }
}
