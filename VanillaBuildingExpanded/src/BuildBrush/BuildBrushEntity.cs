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
    /// Watched attribute key for the preview mesh dirty counter.
    /// Increment whenever the preview mini-dimension content changes.
    /// </summary>
    public const string BrushDirtyCounterKey = "brushDirtyCounter";

    /// <summary>
    /// Watched attribute key for placement validity (used for tinting the preview).
    /// </summary>
    public const string BrushIsValidKey = "isValid";

    /// <summary>
    /// Watched attribute keys for preview bounds (min/max in mini-dimension space).
    /// </summary>
    public const string BrushBoundsMinKey = "brushBoundsMin";
    public const string BrushBoundsMaxKey = "brushBoundsMax";
    public const string BrushHasBoundsKey = "brushHasBounds";
    public const string BrushBoundsMinXKey = "brushBoundsMinX";
    public const string BrushBoundsMinYKey = "brushBoundsMinY";
    public const string BrushBoundsMinZKey = "brushBoundsMinZ";
    public const string BrushBoundsMaxXKey = "brushBoundsMaxX";
    public const string BrushBoundsMaxYKey = "brushBoundsMaxY";
    public const string BrushBoundsMaxZKey = "brushBoundsMaxZ";

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
        entity.WatchedAttributes.SetInt(BrushDirtyCounterKey, 0);
        entity.WatchedAttributes.MarkPathDirty(BrushDirtyCounterKey);
        return entity;
    }

    /// <summary>
    /// Gets the current preview dirty counter.
    /// </summary>
    public int GetBrushDirtyCounter() => WatchedAttributes.GetInt(BrushDirtyCounterKey);

    /// <summary>
    /// Increments the preview dirty counter and marks it dirty for replication.
    /// Server-side only.
    /// </summary>
    public int IncrementBrushDirtyCounter()
    {
        int next = GetBrushDirtyCounter() + 1;
        WatchedAttributes.SetInt(BrushDirtyCounterKey, next);
        WatchedAttributes.MarkPathDirty(BrushDirtyCounterKey);
        return next;
    }

    /// <summary>
    /// Sets preview bounds for client rendering.
    /// </summary>
    public void SetPreviewBounds(BlockPos min, BlockPos max)
    {
        WatchedAttributes.SetBool(BrushHasBoundsKey, true);
        WatchedAttributes.SetInt(BrushBoundsMinXKey, min.X);
        WatchedAttributes.SetInt(BrushBoundsMinYKey, min.Y);
        WatchedAttributes.SetInt(BrushBoundsMinZKey, min.Z);
        WatchedAttributes.SetInt(BrushBoundsMaxXKey, max.X);
        WatchedAttributes.SetInt(BrushBoundsMaxYKey, max.Y);
        WatchedAttributes.SetInt(BrushBoundsMaxZKey, max.Z);
        WatchedAttributes.MarkPathDirty(BrushHasBoundsKey);
        WatchedAttributes.MarkPathDirty(BrushBoundsMinXKey);
        WatchedAttributes.MarkPathDirty(BrushBoundsMinYKey);
        WatchedAttributes.MarkPathDirty(BrushBoundsMinZKey);
        WatchedAttributes.MarkPathDirty(BrushBoundsMaxXKey);
        WatchedAttributes.MarkPathDirty(BrushBoundsMaxYKey);
        WatchedAttributes.MarkPathDirty(BrushBoundsMaxZKey);
    }

    /// <summary>
    /// Clears preview bounds.
    /// </summary>
    public void ClearPreviewBounds()
    {
        WatchedAttributes.SetBool(BrushHasBoundsKey, false);
        WatchedAttributes.MarkPathDirty(BrushHasBoundsKey);
    }

    /// <summary>
    /// Attempts to read preview bounds from watched attributes.
    /// </summary>
    public bool TryGetPreviewBounds(out BlockPos min, out BlockPos max)
    {
        min = new BlockPos(0, 0, 0);
        max = new BlockPos(0, 0, 0);

        if (!WatchedAttributes.GetBool(BrushHasBoundsKey))
        {
            return false;
        }

        int minX = WatchedAttributes.GetInt(BrushBoundsMinXKey);
        int minY = WatchedAttributes.GetInt(BrushBoundsMinYKey);
        int minZ = WatchedAttributes.GetInt(BrushBoundsMinZKey);
        int maxX = WatchedAttributes.GetInt(BrushBoundsMaxXKey);
        int maxY = WatchedAttributes.GetInt(BrushBoundsMaxYKey);
        int maxZ = WatchedAttributes.GetInt(BrushBoundsMaxZKey);

        min = new BlockPos(minX, minY, minZ);
        max = new BlockPos(maxX, maxY, maxZ);
        return true;
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
