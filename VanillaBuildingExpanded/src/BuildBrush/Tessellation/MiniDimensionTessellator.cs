using System;
using System.Threading;
using System.Threading.Tasks;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer.Tessellation;

/// <summary>
/// Provides tessellation services for mini-dimensions, producing combined mesh data
/// from all blocks and block entities within specified bounds.
/// </summary>
public class MiniDimensionTessellator
{
    private readonly ICoreClientAPI capi;

    public MiniDimensionTessellator(ICoreClientAPI capi)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
    }

    /// <summary>
    /// Asynchronously tessellates all blocks within the specified bounds of a mini-dimension.
    /// </summary>
    /// <param name="dimension">The mini-dimension to tessellate.</param>
    /// <param name="min">Minimum corner of the bounds (in dimension-local coordinates).</param>
    /// <param name="max">Maximum corner of the bounds (in dimension-local coordinates).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The combined mesh data, or null if cancelled or no blocks found.</returns>
    public Task<MeshData?> TessellateAsync(
        IMiniDimension dimension,
        BlockPos min,
        BlockPos max,
        CancellationToken cancellationToken = default)
    {
        if (dimension is null)
            return Task.FromResult<MeshData?>(null);

        // Capture values for the background task
        int minX = min.X, minY = min.Y, minZ = min.Z;
        int maxX = max.X, maxY = max.Y, maxZ = max.Z;

        return Task.Run(() => TessellateInternal(dimension, minX, minY, minZ, maxX, maxY, maxZ, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Synchronously tessellates all blocks within the specified bounds.
    /// </summary>
    public MeshData? Tessellate(
        IMiniDimension dimension,
        BlockPos min,
        BlockPos max)
    {
        if (dimension is null)
            return null;

        return TessellateInternal(dimension, min.X, min.Y, min.Z, max.X, max.Y, max.Z, CancellationToken.None);
    }

    /// <summary>
    /// Internal tessellation implementation.
    /// </summary>
    private MeshData? TessellateInternal(
        IMiniDimension dimension,
        int minX, int minY, int minZ,
        int maxX, int maxY, int maxZ,
        CancellationToken ct)
    {
        try
        {
            MiniDimensionMeshPool meshPool = new();
            BlockPos tempPos = new(0, 0, 0, Dimensions.MiniDimensions);

            // Iterate through all positions in the bounds
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        // Check for cancellation each iteration
                        ct.ThrowIfCancellationRequested();

                        tempPos.Set(x, y, z);

                        // Get the block at this position
                        Block? block = dimension.GetBlock(tempPos);
                        if (block is null || block.BlockId == 0)
                            continue;

                        // Calculate the offset from the min corner (so mesh is centered at origin)
                        float offsetX = x - minX;
                        float offsetY = y - minY;
                        float offsetZ = z - minZ;

                        // Check for block entity with custom tessellation
                        bool skipDefaultMesh = false;
                        BlockEntity? blockEntity = dimension.GetBlockEntity(tempPos);

                        if (blockEntity is not null)
                        {
                            // Create an offset mesh pool that translates meshes to the correct position
                            // and tracks how many vertices were added
                            var offsetPool = new OffsetMeshPoolWrapper(meshPool, offsetX, offsetY, offsetZ);
                            
                            // Call block entity tessellation
                            // Note: This should be called from main thread for thread safety,
                            // but we're following the game's pattern where OnTesselation is called from tess thread
                            skipDefaultMesh = blockEntity.OnTesselation(offsetPool, capi.Tesselator);

                            // If OnTesselation skipped the default mesh but added zero vertices,
                            // it likely relies on a custom IRenderer that won't run in our preview.
                            // Add the fallback block mesh so the BE isn't invisible.
                            if (skipDefaultMesh && offsetPool.VerticesAdded == 0)
                            {
                                MeshData? fallbackMesh = GetBlockMesh(block);
                                if (fallbackMesh is not null)
                                {
                                    meshPool.AddMeshDataWithOffset(fallbackMesh, offsetX, offsetY, offsetZ);
                                    capi.Logger.Debug(
                                        "[MiniDimensionTessellator] Added fallback mesh for {0} at ({1},{2},{3}) - OnTesselation skipped default but added no vertices",
                                        blockEntity.GetType().Name, x, y, z);
                                }
                            }
                        }

                        // Add default block mesh if not skipped by block entity
                        if (!skipDefaultMesh)
                        {
                            MeshData? blockMesh = GetBlockMesh(block);
                            if (blockMesh is not null)
                            {
                                meshPool.AddMeshDataWithOffset(blockMesh, offsetX, offsetY, offsetZ);
                            }
                        }
                    }
                }
            }

            return meshPool.TakeCombinedMesh();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            capi.Logger.Error("MiniDimensionTessellator: Error during tessellation: {0}", ex);
            return null;
        }
    }

    /// <summary>
    /// Gets the mesh data for a block using the tessellator manager.
    /// </summary>
    private MeshData? GetBlockMesh(Block block)
    {
        try
        {
            // Use the tessellator manager to get the pre-cached block mesh
            // This is the same mesh used during chunk tessellation
            return capi.TesselatorManager.GetDefaultBlockMesh(block);
        }
        catch
        {
            // Fallback: tessellate the block directly
            try
            {
                capi.Tesselator.TesselateBlock(block, out MeshData mesh);
                return mesh;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Wrapper that applies a position offset to all mesh data added to the pool
    /// and tracks the total vertices added.
    /// </summary>
    private class OffsetMeshPoolWrapper : ITerrainMeshPool
    {
        private readonly MiniDimensionMeshPool innerPool;
        private readonly float offsetX, offsetY, offsetZ;
        private readonly Matrixf translateMatrix = new();

        /// <summary>
        /// Total number of vertices added to this pool via AddMeshData calls.
        /// </summary>
        public int VerticesAdded { get; private set; }

        public OffsetMeshPoolWrapper(MiniDimensionMeshPool innerPool, float offsetX, float offsetY, float offsetZ)
        {
            this.innerPool = innerPool;
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            this.offsetZ = offsetZ;

            // Pre-compute translation matrix
            translateMatrix.Identity();
            translateMatrix.Translate(offsetX, offsetY, offsetZ);
        }

        public void AddMeshData(MeshData data, int lodLevel = 1)
        {
            if (data is null || data.VerticesCount == 0)
                return;

            VerticesAdded += data.VerticesCount;
            innerPool.AddMeshDataWithOffset(data, offsetX, offsetY, offsetZ);
        }

        public void AddMeshData(MeshData data, float[] tfMatrix, int lodLevel = 1)
        {
            if (data is null || data.VerticesCount == 0)
                return;

            VerticesAdded += data.VerticesCount;

            // Apply the provided transform, then our offset translation
            MeshData transformed = data.Clone();
            transformed.MatrixTransform(tfMatrix);
            innerPool.AddMeshDataWithOffset(transformed, offsetX, offsetY, offsetZ);
        }

        public void AddMeshData(MeshData data, ColorMapData colorMapData, int lodLevel = 1)
        {
            // Ignore color map data for preview rendering
            AddMeshData(data, lodLevel);
        }
    }
}
