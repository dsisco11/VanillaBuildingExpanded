using System.Threading;
using System.Threading.Tasks;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildHammer.Tessellation;

/// <summary>
/// Interface for tessellation services that produce mesh data from mini-dimensions.
/// Allows for dependency injection and easier testing of components that depend on tessellation.
/// </summary>
public interface ITessellationService
{
    /// <summary>
    /// Asynchronously tessellates all blocks within the specified bounds of a mini-dimension.
    /// </summary>
    /// <param name="dimension">The mini-dimension to tessellate.</param>
    /// <param name="min">Minimum corner of the bounds (in dimension-local coordinates).</param>
    /// <param name="max">Maximum corner of the bounds (in dimension-local coordinates).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The combined mesh data, or null if cancelled or no blocks found.</returns>
    Task<MeshData?> TessellateAsync(
        IMiniDimension dimension,
        BlockPos min,
        BlockPos max,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously tessellates all blocks within the specified bounds.
    /// </summary>
    /// <param name="dimension">The mini-dimension to tessellate.</param>
    /// <param name="min">Minimum corner of the bounds (in dimension-local coordinates).</param>
    /// <param name="max">Maximum corner of the bounds (in dimension-local coordinates).</param>
    /// <returns>The combined mesh data, or null if no blocks found.</returns>
    MeshData? Tessellate(
        IMiniDimension dimension,
        BlockPos min,
        BlockPos max);
}
