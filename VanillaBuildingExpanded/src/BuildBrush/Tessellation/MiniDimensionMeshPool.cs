using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer.Tessellation;

/// <summary>
/// Implementation of ITerrainMeshPool that accumulates mesh data from multiple sources
/// into a single combined MeshData for mini-dimension rendering.
/// </summary>
public class MiniDimensionMeshPool : ITerrainMeshPool
{
    private MeshData? combinedMesh;
    private readonly Matrixf tempMatrix = new();

    /// <summary>
    /// Gets the combined mesh data accumulated from all AddMeshData calls.
    /// </summary>
    public MeshData? CombinedMesh => combinedMesh;

    /// <summary>
    /// Gets whether any mesh data has been added.
    /// </summary>
    public bool HasMeshData => combinedMesh is not null && combinedMesh.VerticesCount > 0;

    /// <summary>
    /// Adds mesh data to the pool. The data is cloned to prevent mutation issues.
    /// </summary>
    /// <param name="data">The mesh data to add.</param>
    /// <param name="lodLevel">LOD level (ignored for mini-dimension rendering).</param>
    public void AddMeshData(MeshData data, int lodLevel = 1)
    {
        if (data is null || data.VerticesCount == 0)
            return;

        EnsureCombinedMeshInitialized();
        combinedMesh!.AddMeshData(data.Clone());
    }

    /// <summary>
    /// Adds mesh data with a transform matrix applied.
    /// </summary>
    /// <param name="data">The mesh data to add.</param>
    /// <param name="tfMatrix">The 4x4 transformation matrix to apply.</param>
    /// <param name="lodLevel">LOD level (ignored for mini-dimension rendering).</param>
    public void AddMeshData(MeshData data, float[] tfMatrix, int lodLevel = 1)
    {
        if (data is null || data.VerticesCount == 0)
            return;

        // Clone and transform the mesh
        MeshData transformed = data.Clone();
        transformed.MatrixTransform(tfMatrix);

        EnsureCombinedMeshInitialized();
        combinedMesh!.AddMeshData(transformed);
    }

    /// <summary>
    /// Adds mesh data with color map data. Color map is ignored for preview rendering.
    /// </summary>
    /// <param name="data">The mesh data to add.</param>
    /// <param name="colorMapData">Color map data (ignored).</param>
    /// <param name="lodLevel">LOD level (ignored for mini-dimension rendering).</param>
    public void AddMeshData(MeshData data, ColorMapData colorMapData, int lodLevel = 1)
    {
        // For preview rendering, we ignore color map data and just add the mesh
        AddMeshData(data, lodLevel);
    }

    /// <summary>
    /// Adds mesh data with a position offset applied.
    /// </summary>
    /// <param name="data">The mesh data to add.</param>
    /// <param name="xOffset">X position offset.</param>
    /// <param name="yOffset">Y position offset.</param>
    /// <param name="zOffset">Z position offset.</param>
    public void AddMeshDataWithOffset(MeshData data, float xOffset, float yOffset, float zOffset)
    {
        if (data is null || data.VerticesCount == 0)
            return;

        EnsureCombinedMeshInitialized();
        combinedMesh!.AddMeshData(data, xOffset, yOffset, zOffset);
    }

    /// <summary>
    /// Clears all accumulated mesh data for reuse.
    /// </summary>
    public void Clear()
    {
        combinedMesh?.Clear();
    }

    /// <summary>
    /// Gets the combined mesh and transfers ownership to the caller.
    /// The pool is reset after this call.
    /// </summary>
    /// <returns>The combined mesh data, or null if no data was added.</returns>
    public MeshData? TakeCombinedMesh()
    {
        MeshData? result = combinedMesh;
        combinedMesh = null;
        return result;
    }

    /// <summary>
    /// Ensures the combined mesh is initialized and ready to receive data.
    /// </summary>
    private void EnsureCombinedMeshInitialized()
    {
        if (combinedMesh is null)
        {
            combinedMesh = new MeshData(24, 36, withNormals: false, withUv: true, withRgba: true, withFlags: true);
            combinedMesh.SetMode(EnumDrawMode.Triangles);
        }
    }
}
