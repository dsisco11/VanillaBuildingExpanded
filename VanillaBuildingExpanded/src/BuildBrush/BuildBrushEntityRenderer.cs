using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Custom renderer for the build brush entity that renders the mini-dimension blocks.
/// </summary>
public class BuildBrushEntityRenderer : EntityRenderer
{
    private readonly IMiniDimension? dimension;
    private MeshRef? meshRef;
    private readonly Matrixf modelMat = new();

    // Render colors
    private static readonly Vec4f ColorNormal = ColorUtil.WhiteArgbVec;
    private static readonly Vec4f ColorInvalid = new(1f, 0.2f, 0.2f, 0.5f);
    private static readonly Vec4f ColorGlow = new(1f, 1f, 1f, 0.1f);

    public BuildBrushEntityRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
        // Get the dimension from the entity's watched attributes
        int dimId = entity.WatchedAttributes.GetInt("dim", -1);
        if (dimId >= 0)
        {
            dimension = api.World.GetOrCreateDimension(dimId, entity.Pos.XYZ);
        }
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        // Rebuild mesh when entity is fully loaded
        RebuildMesh();
    }

    /// <summary>
    /// Rebuilds the mesh from the dimension's blocks.
    /// </summary>
    public void RebuildMesh()
    {
        meshRef?.Dispose();
        meshRef = null;

        if (dimension == null)
            return;

        // IMiniDimension is an IBlockAccessor, so we can use it directly
        IBlockAccessor blockAccessor = dimension;

        // Build mesh from dimension blocks
        MeshData combinedMesh = new();

        // The dimension typically has blocks around (0,0,0) in dimension-local coordinates
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    BlockPos pos = new(x, y, z, Dimensions.MiniDimensions);
                    Block block = blockAccessor.GetBlock(pos);
                    if (block != null && block.BlockId != 0)
                    {
                        MeshData? blockMesh = capi.TesselatorManager.GetDefaultBlockMesh(block);
                        if (blockMesh != null)
                        {
                            MeshData clone = blockMesh.Clone();
                            clone.Translate(x, y, z);
                            combinedMesh.AddMeshData(clone);
                        }
                    }
                }
            }
        }

        if (combinedMesh.VerticesCount > 0)
        {
            meshRef = capi.Render.UploadMesh(combinedMesh);
        }
    }

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        if (meshRef == null || isShadowPass)
            return;

        IRenderAPI rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;

        // Use entity position for rendering
        Vec3d entityPos = entity.Pos.XYZ;

        modelMat.Identity();
        modelMat.Translate(
            (float)(entityPos.X - camPos.X),
            (float)(entityPos.Y - camPos.Y),
            (float)(entityPos.Z - camPos.Z)
        );
        modelMat.RotateYDeg(entity.Pos.Yaw * GameMath.RAD2DEG);

        IStandardShaderProgram shader = rapi.StandardShader;
        shader.Use();
        shader.RgbaTint = ColorNormal;
        shader.DontWarpVertices = 0;
        shader.AddRenderFlags = 0;
        shader.ExtraGlow = 32;
        shader.RgbaGlowIn = ColorGlow;
        shader.RgbaAmbientIn = rapi.AmbientColor;
        shader.RgbaFogIn = rapi.FogColor;
        shader.FogMinIn = rapi.FogMin;
        shader.FogDensityIn = rapi.FogDensity;
        shader.NormalShaded = 1;
        shader.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        shader.ViewMatrix = rapi.CameraMatrixOriginf;
        shader.ModelMatrix = modelMat.Values;

        rapi.RenderMesh(meshRef);

        shader.ExtraGlow = 0;
        shader.Stop();
    }

    public override void Dispose()
    {
        meshRef?.Dispose();
        meshRef = null;
    }
}
