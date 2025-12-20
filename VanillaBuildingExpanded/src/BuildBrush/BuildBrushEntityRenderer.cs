using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Custom renderer for the build brush entity.
/// The actual block rendering is handled by the terrain mesh pool system for the mini-dimension.
/// This renderer exists to allow for future custom effects (glow, tint, etc.) if needed.
/// </summary>
public class BuildBrushEntityRenderer : EntityRenderer
{
    public BuildBrushEntityRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
    }

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        // The mini-dimension blocks are rendered by the terrain mesh pool system.
        // This method is intentionally empty - we could add custom effects here in the future.
    }

    public override void Dispose()
    {
    }
}
