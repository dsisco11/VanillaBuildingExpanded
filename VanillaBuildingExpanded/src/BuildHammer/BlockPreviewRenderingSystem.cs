using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;
internal class BuildPreviewRenderer : IRenderer, IDisposable
{
    #region Fields
    public double RenderOrder => 0.55;
    public int RenderRange => 256;
    private readonly ICoreClientAPI api;
    /// <summary> The tint color applied to the preview model. </summary>
    protected static readonly Vec4f RenderColor_Normal = ColorUtil.WhiteArgbVec;
    protected static readonly Vec4f RenderColor_Invalid = new(1f, .2f, .2f, 0.1f);
    protected static readonly Vec4f RenderGlow = new(1f, 1f, 1f, .1f);
    protected Matrixf ModelMat = new();
    protected readonly BuildBrushSystem_Client brushManager;
    #endregion

    #region Properties
    #endregion

    #region Lifecycle
    public BuildPreviewRenderer(ICoreClientAPI api, BuildBrushSystem_Client brushManager)
    {
        this.api = api;
        this.brushManager = brushManager;
    }

    public void Dispose()
    {
    }
    #endregion

    #region Rendering Logic
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var player = api.World.Player;
        BuildBrushInstance? brush = brushManager.GetBrush(player);
        if (brush is null || brush.IsDisabled || brush.ItemStack is null) 
            return;

        IRenderAPI rapi = api.Render;
        ItemRenderInfo renderInfo = rapi.GetItemStackRenderInfo(brush.DummySlot, EnumItemRenderTarget.Ground, deltaTime);
        if (renderInfo.ModelRef is null)
            return;

        UpdateModelMatrix(brush, renderInfo);

        // Setup the shader
        const string textureSampleName = "tex";
        IStandardShaderProgram shader = rapi.StandardShader;
        shader.Use();
        shader.RgbaTint = ColorUtil.WhiteArgbVec;
        shader.DontWarpVertices = 0;
        shader.AlphaTest = renderInfo.AlphaTest;
        shader.AddRenderFlags = 0;
        shader.ExtraZOffset = 0.00001f;

        shader.OverlayOpacity = renderInfo.OverlayOpacity;
        if (renderInfo.OverlayTexture is not null && renderInfo.OverlayOpacity > 0f)
        {
            shader.Tex2dOverlay2D = renderInfo.OverlayTexture.TextureId;
            shader.OverlayTextureSize = new Vec2f(renderInfo.OverlayTexture.Width, renderInfo.OverlayTexture.Height);
            shader.BaseTextureSize = new Vec2f(renderInfo.TextureSize.Width, renderInfo.TextureSize.Height);
            TextureAtlasPosition texPos = rapi.GetTextureAtlasPosition(brush.ItemStack);
            shader.BaseUvOrigin = new Vec2f(texPos.x1, texPos.y1);
        }

        shader.RgbaLightIn = brush.IsValidPlacement ? RenderColor_Normal : RenderColor_Invalid;
        shader.ExtraGlow = 64;
        shader.RgbaGlowIn = RenderGlow;
        shader.RgbaAmbientIn = rapi.AmbientColor;
        shader.RgbaFogIn = rapi.FogColor;
        shader.FogMinIn = rapi.FogMin;
        shader.FogDensityIn = 0;
        shader.ExtraGodray = 0f;
        shader.NormalShaded = renderInfo.NormalShaded ? 1 : 0;
        shader.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        shader.ViewMatrix = rapi.CameraMatrixOriginf;
        shader.ModelMatrix = ModelMat.Values;

        if (!renderInfo.CullFaces)
        {
            rapi.GlDisableCullFace();
        }
        // Render the mesh
        rapi.RenderMultiTextureMesh(renderInfo.ModelRef, textureSampleName);
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
    #endregion

    protected void UpdateModelMatrix(in BuildBrushInstance brush, in ItemRenderInfo renderInfo)
    {
        //ModelTransform itemTransform = renderInfo.Transform ?? ModelTransform.NoTransform;
        Vec3d pos = brush.Position?.ToVec3d() ?? Vec3d.Zero;
        Vec3d camPos = api.World.Player.Entity.CameraPos;

        ModelMat.Identity();
        ModelMat.Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z);
    }
}
