using System.Threading;

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

    // Async tessellation state
    private CancellationTokenSource? tessellationCts;
    private volatile bool isTessellating;

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
        tessellator = new MiniDimensionTessellator(api);
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();

        // Subscribe to block change events
        var brushInstance = BrushInstance;
        if (brushInstance is not null)
        {
            // Subscribe to dimension dirty events
            if (brushInstance.Dimension is not null)
            {
                brushInstance.Dimension.OnDirty += Dimension_OnDirty;
            }
        }

        RebuildMesh();
    }

    /// <summary>
    /// Called when the dimension is marked dirty and needs mesh rebuild.
    /// </summary>
    private void Dimension_OnDirty(object? sender, DimensionDirtyEventArgs e)
    {
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
        BuildBrushDimension? brushDimension = BrushInstance?.Dimension;

        if (dimension is null || brushDimension is null)
        {
            DisposeMesh();
            return;
        }

        // Get the block to check if it changed
        BlockPos originPos = new(0, 0, 0, Dimensions.MiniDimensions);
        Block? block = dimension.GetBlock(originPos);
        if (block is null || block.BlockId == 0)
        {
            block = brushEntity?.BrushInstance?.BlockTransformed;
        }

        if (block is null || block.BlockId == 0)
        {
            DisposeMesh();
            return;
        }

        // Get active bounds from the dimension
        if (!brushDimension.GetActiveBounds(out BlockPos min, out BlockPos max))
        {
            DisposeMesh();
            return;
        }

        // Cancel any pending tessellation
        CancelPendingTessellation();

        // Start async tessellation
        isTessellating = true;
        tessellationCts = new CancellationTokenSource();
        CancellationToken token = tessellationCts.Token;

        capi.Event.EnqueueMainThreadTask(() =>
        {
            MeshData ? meshData = tessellator.Tessellate(dimension, min, max);
            _upload_mesh(meshData);
        }, $"{nameof(BuildBrushEntityRenderer)}.{nameof(RebuildMesh)}");

        //tessellator.TessellateAsync(dimension, min, max, token)
        //    .ContinueWith(task =>
        //    {
        //        // Check if cancelled or faulted
        //        if (task.IsCanceled || task.IsFaulted || token.IsCancellationRequested)
        //        {
        //            isTessellating = false;
        //            return;
        //        }

        //        MeshData? meshData = task.Result;
        //        _upload_mesh(meshData);
        //    }, token);

        void _upload_mesh(MeshData? meshData)
        {
            if (meshData is null || meshData.VerticesCount == 0)
            {
                isTessellating = false;
                return;
            }

            // Marshal back to main thread for GPU upload
            //capi.Event.EnqueueMainThreadTask(() =>
            //{
                // Double-check we weren't cancelled while waiting
                //if (token.IsCancellationRequested)
                //{
                //    isTessellating = false;
                //    return;
                //}

                // Dispose old mesh and upload new one

                DisposeMesh();
                meshRef = capi.Render.UploadMultiTextureMesh(meshData);
                isTessellating = false;
            //}, $"{nameof(BuildBrushEntityRenderer)}.{nameof(RebuildMesh)}");
        }
    }

    /// <summary>
    /// Cancels any pending tessellation operation.
    /// </summary>
    private void CancelPendingTessellation()
    {
        try
        {
            tessellationCts?.Cancel();
            tessellationCts?.Dispose();
        }
        catch { }
        finally
        {
            tessellationCts = null;
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

        BuildBrushInstance? brush = BrushInstance;
        if (brush is null)
            return;

        if (meshRef is null)
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
        // Cancel any pending tessellation
        CancelPendingTessellation();

        // Unsubscribe from events
        var brushInstance = BrushInstance;
        if (brushInstance is not null)
        {
            // Unsubscribe from dimension dirty events
            if (brushInstance.Dimension is not null)
            {
                brushInstance.Dimension.OnDirty -= Dimension_OnDirty;
            }
        }

        DisposeMesh();
    }
}
