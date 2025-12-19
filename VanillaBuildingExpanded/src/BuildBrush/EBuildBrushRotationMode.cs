namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Describes how a block supports rotation when used with the build brush.
/// </summary>
public enum EBuildBrushRotationMode
{
    /// <summary>
    /// Block cannot be rotated.
    /// </summary>
    None,

    /// <summary>
    /// Block uses pre-defined rotation variants (e.g., "rot", "horizontalorientation").
    /// Rotation is applied by swapping to a different block variant.
    /// </summary>
    VariantBased,

    /// <summary>
    /// Block entity implements IRotatable interface.
    /// Rotation is applied via <see cref="Vintagestory.API.Common.IRotatable.OnTransformed"/>.
    /// </summary>
    Rotatable,

    /// <summary>
    /// Block has both variant-based rotation AND an IRotatable block entity.
    /// Both mechanisms are applied when rotating.
    /// </summary>
    Hybrid
}
