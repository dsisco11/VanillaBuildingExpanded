using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Entity that owns a mini-dimension for the build brush preview.
/// This entity is rendering-only - position and rotation are controlled externally by BuildBrushInstance.
/// </summary>
public class BuildBrushEntity : EntityChunky
{
    /// <summary>
    /// The entity code used for registration.
    /// </summary>
    public const string EntityCode = "vanillabuildingexpanded:buildbrushentity";

    /// <summary>
    /// Whether this entity should be rendered.
    /// </summary>
    public bool ShouldRender { get; set; } = true;

    /// <summary>
    /// Creates a new BuildBrushEntity and links it with a dimension.
    /// </summary>
    /// <param name="sapi">The server API.</param>
    /// <param name="dimension">The mini-dimension to associate with.</param>
    /// <returns>The created entity.</returns>
    public static BuildBrushEntity CreateAndLink(ICoreServerAPI sapi, IMiniDimension dimension)
    {
        BuildBrushEntity entity = (BuildBrushEntity)sapi.World.ClassRegistry.CreateEntity(EntityCode);
        entity.Code = new AssetLocation(EntityCode);
        entity.AssociateWithDimension(dimension);
        return entity;
    }

    public BuildBrushEntity() : base()
    {
    }

    /// <summary>
    /// Updates the entity's world position.
    /// Called externally by BuildBrushInstance when the target selection changes.
    /// </summary>
    /// <param name="position">The new world position.</param>
    public void SetWorldPosition(BlockPos position)
    {
        SetWorldPosition(position.ToVec3d());
    }

    /// <summary>
    /// Updates the entity's world position.
    /// Called externally by BuildBrushInstance when the target selection changes.
    /// </summary>
    /// <param name="position">The new world position.</param>
    public void SetWorldPosition(Vec3d position)
    {
        Pos.SetPos(position);
        ServerPos.SetPos(position);

        // Also update the dimension's current position
        if (blocks?.CurrentPos is not null)
        {
            blocks.CurrentPos.SetPos(position);
            blocks.Dirty = true;
        }
    }

    /// <summary>
    /// Updates the entity's yaw rotation (horizontal rotation).
    /// Called externally by BuildBrushInstance when rotation changes.
    /// </summary>
    /// <param name="yawDegrees">The yaw angle in degrees.</param>
    public void SetYawRotation(float yawDegrees)
    {
        float yawRadians = yawDegrees * GameMath.DEG2RAD;
        Pos.Yaw = yawRadians;
        ServerPos.Yaw = yawRadians;
    }

    public override void OnGameTick(float dt)
    {
        // Simplified tick - we don't need most entity behavior
        // Position is controlled externally by BuildBrushInstance
        if (blocks == null)
        {
            Die(EnumDespawnReason.Removed);
            return;
        }

        if (blocks.subDimensionId == 0)
        {
            Pos.Yaw = 0;
            Pos.Pitch = 0;
            Pos.Roll = 0;
            return;
        }

        // Run behaviors if any
        if (SidedProperties?.Behaviors is not null)
        {
            foreach (EntityBehavior behavior in SidedProperties.Behaviors)
            {
                behavior.OnGameTick(dt);
            }
        }
    }

    public override bool IsInteractable => false;

    public override bool ApplyGravity => false;
}
