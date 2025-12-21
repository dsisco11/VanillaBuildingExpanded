namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Immutable struct representing a single orientation state for a block.
/// Combines a block-id (for variant-based rotation) with a mesh-angle offset (for IRotatable blocks).
/// </summary>
/// <param name="BlockId">The block ID for this orientation state.</param>
/// <param name="MeshAngleDegrees">The mesh angle offset in degrees (0-359).</param>
public readonly record struct BlockOrientationDefinition(int BlockId, float MeshAngleDegrees)
{
    /// <summary>
    /// Returns a string representation of this orientation definition.
    /// </summary>
    public override string ToString() => $"BlockOrientationDefinition(BlockId={BlockId}, MeshAngle={MeshAngleDegrees}°)";
}
