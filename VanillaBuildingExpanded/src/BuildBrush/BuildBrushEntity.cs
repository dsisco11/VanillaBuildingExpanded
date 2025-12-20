using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Custom entity for the build brush that uses our custom renderer.
/// Extends EntityChunky to maintain mini-dimension functionality.
/// </summary>
public class BuildBrushEntity : EntityChunky
{
    /// <summary>
    /// The entity code for spawning (domain:code format).
    /// </summary>
    public const string EntityCode = "vanillabuildingexpanded:buildbrushentity";

    /// <summary>
    /// The entity class name for registration (must match JSON "class" field).
    /// </summary>
    public const string ClassName = "BuildBrushEntity";

    /// <summary>
    /// The renderer class name for registration.
    /// </summary>
    public const string RendererClassName = "BuildBrushRenderer";

    /// <summary>
    /// Creates a new BuildBrushEntity and links it with a dimension.
    /// </summary>
    public static BuildBrushEntity CreateAndLink(ICoreServerAPI sapi, IMiniDimension dimension)
    {
        BuildBrushEntity entity = (BuildBrushEntity)sapi.World.ClassRegistry.CreateEntity(ClassName);
        entity.Code = new AssetLocation(EntityCode);
        entity.AssociateWithDimension(dimension);
        return entity;
    }

    public BuildBrushEntity() : base()
    {
    }
}
