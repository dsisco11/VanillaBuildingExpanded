using System;

using Vintagestory.API.Client;
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
    /// The watched attribute key for the owning player's UID.
    /// </summary>
    public const string OwnerPlayerUidKey = "ownerPlayerUid";

    /// <summary>
    /// Weak reference to the owning brush instance.
    /// </summary>
    private WeakReference<BuildBrushInstance>? brushInstanceRef;

    /// <summary>
    /// Exposes the dimension for the renderer to access.
    /// </summary>
    public IMiniDimension? Dimension => blocks;

    /// <summary>
    /// Gets the brush instance if still alive.
    /// </summary>
    public BuildBrushInstance? BrushInstance
    {
        get
        {
            if (brushInstanceRef?.TryGetTarget(out var instance) == true)
                return instance;
            return null;
        }
    }

    /// <summary>
    /// Creates a new BuildBrushEntity and links it with a dimension.
    /// Sets the owning player's UID as a watched attribute.
    /// </summary>
    public static BuildBrushEntity CreateAndLink(ICoreServerAPI sapi, IMiniDimension dimension, string ownerPlayerUid)
    {
        BuildBrushEntity entity = (BuildBrushEntity)sapi.World.ClassRegistry.CreateEntity(ClassName);
        entity.Code = new AssetLocation(EntityCode);
        entity.AssociateWithDimension(dimension);
        entity.WatchedAttributes.SetString(OwnerPlayerUidKey, ownerPlayerUid);
        return entity;
    }

    public BuildBrushEntity() : base()
    {
    }

    public override void Initialize(EntityProperties properties, ICoreAPI api, long chunkindex3d)
    {
        base.Initialize(properties, api, chunkindex3d);

        // On client side, look up the brush instance from the mod system
        if (api.Side == EnumAppSide.Client)
        {
            string? ownerUid = WatchedAttributes.GetString(OwnerPlayerUidKey);
            if (!string.IsNullOrEmpty(ownerUid))
            {
                TryLinkToBrushInstance((ICoreClientAPI)api, ownerUid);
            }

            // Also listen for attribute changes in case it arrives later
            WatchedAttributes.RegisterModifiedListener(OwnerPlayerUidKey, () => OnOwnerPlayerUidChanged((ICoreClientAPI)api));
        }
    }

    /// <summary>
    /// Called when the owner player UID attribute changes.
    /// </summary>
    private void OnOwnerPlayerUidChanged(ICoreClientAPI capi)
    {
        string? ownerUid = WatchedAttributes.GetString(OwnerPlayerUidKey);
        if (!string.IsNullOrEmpty(ownerUid))
        {
            TryLinkToBrushInstance(capi, ownerUid);
        }
    }

    /// <summary>
    /// Attempts to link to the brush instance via the mod system.
    /// </summary>
    private void TryLinkToBrushInstance(ICoreClientAPI capi, string ownerUid)
    {
        // Only link if this entity belongs to the local player
        if (capi.World.Player?.PlayerUID != ownerUid)
            return;

        var brushSystem = capi.ModLoader.GetModSystem<BuildBrushSystem_Client>();
        if (brushSystem == null)
            return;

        var brushInstance = brushSystem.GetBrush(capi.World.Player);
        if (brushInstance != null)
        {
            SetBrushInstance(brushInstance);
        }
    }

    /// <summary>
    /// Sets the brush instance and subscribes to its events.
    /// </summary>
    private void SetBrushInstance(BuildBrushInstance instance)
    {
        // Unsubscribe from previous instance
        if (brushInstanceRef?.TryGetTarget(out var oldInstance) == true)
        {
            oldInstance.OnPositionChanged -= OnBrushPositionChanged;
        }

        brushInstanceRef = new WeakReference<BuildBrushInstance>(instance);

        // Subscribe to events
        instance.OnPositionChanged += OnBrushPositionChanged;

        // Also associate the entity with the brush instance
        instance.AssociateEntity(this);
    }

    /// <summary>
    /// Called when the brush position changes.
    /// </summary>
    private void OnBrushPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        // Update entity position
        if (e.CurrentPosition is not null)
        {
            var vec = e.CurrentPosition.ToVec3d();
            Pos.SetPos(vec);
            ServerPos.SetPos(vec);
        }
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        // Unsubscribe from events
        if (brushInstanceRef?.TryGetTarget(out var instance) == true)
        {
            instance.OnPositionChanged -= OnBrushPositionChanged;
        }
        brushInstanceRef = null;

        base.OnEntityDespawn(despawn);
    }
}
