using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Scans loaded assemblies to discover which <see cref="IRenderer"/> implementations
/// target specific <see cref="BlockEntity"/> or <see cref="BlockEntityBehavior"/> types.
/// This provides a reverse mapping from renderer types to the block entities they render.
/// </summary>
public static class IRendererBlockEntityScanner
{
    private static readonly BindingFlags InstanceFieldFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool _isInitialized;

    /// <summary>
    /// Maps BlockEntity types to the set of IRenderer types that target them.
    /// </summary>
    private static readonly Dictionary<Type, HashSet<Type>> BlockEntityToRenderers = new();

    /// <summary>
    /// Maps BlockEntityBehavior types to the set of IRenderer types that target them.
    /// </summary>
    private static readonly Dictionary<Type, HashSet<Type>> BehaviorToRenderers = new();

    /// <summary>
    /// All discovered IRenderer types that target a BlockEntity or BlockEntityBehavior.
    /// </summary>
    private static readonly HashSet<Type> AllBlockEntityRenderers = new();

    /// <summary>
    /// Gets whether the scanner has been initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Initializes the scanner by scanning all loaded mod assemblies.
    /// Should be called during the AssetsLoaded stage.
    /// </summary>
    /// <param name="api">The core API to access mod loader.</param>
    public static void Initialize(ICoreAPI api)
    {
        if (_isInitialized)
            return;

        var assemblies = new HashSet<Assembly>();

        // Collect assemblies from all loaded mods
        foreach (var mod in api.ModLoader.Mods)
        {
            foreach (var modSystem in mod.Systems)
            {
                var assembly = modSystem.GetType().Assembly;
                assemblies.Add(assembly);
            }
        }

        // Also include the main game assemblies if not already included
        assemblies.Add(typeof(BlockEntity).Assembly);        // vsapi
        assemblies.Add(typeof(IRenderer).Assembly);          // vsapi (client)

        ScanAssemblies(api.Logger, assemblies.ToArray());
        _isInitialized = true;

        api.Logger?.Notification($"[IRendererBlockEntityScanner] Initialized. Found {AllBlockEntityRenderers.Count} renderer(s) targeting {BlockEntityToRenderers.Count} BlockEntity type(s) and {BehaviorToRenderers.Count} behavior type(s).");
    }

    /// <summary>
    /// Scans the specified assemblies for IRenderer implementations that target BlockEntity/BlockEntityBehavior types.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    public static void ScanAssemblies(ILogger? logger, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            try
            {
                ScanAssembly(logger, assembly);
            }
            catch (Exception ex)
            {
                logger?.Warning($"[IRendererBlockEntityScanner] Failed to scan assembly {assembly.GetName().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Scans a single assembly for IRenderer implementations.
    /// </summary>
    private static void ScanAssembly(ILogger? logger, Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types may fail to load, but we can still process the ones that loaded
            types = ex.Types.Where(t => t is not null).ToArray()!;
            logger?.Debug($"[IRendererBlockEntityScanner] Partial type load for {assembly.GetName().Name}, processing {types.Length} types.");
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (!typeof(IRenderer).IsAssignableFrom(type))
                continue;

            AnalyzeRendererType(logger, type);
        }
    }

    /// <summary>
    /// Analyzes a renderer type to find which BlockEntity/BlockEntityBehavior types it targets.
    /// </summary>
    private static void AnalyzeRendererType(ILogger? logger, Type rendererType)
    {
        var targetBlockEntities = new HashSet<Type>();
        var targetBehaviors = new HashSet<Type>();

        // Strategy 1: Check constructor parameters
        foreach (var ctor in rendererType.GetConstructors())
        {
            foreach (var param in ctor.GetParameters())
            {
                ClassifyTargetType(param.ParameterType, targetBlockEntities, targetBehaviors);
            }
        }

        // Strategy 2: Check instance fields
        var currentType = rendererType;
        while (currentType is not null && currentType != typeof(object))
        {
            var fields = currentType.GetFields(InstanceFieldFlags | BindingFlags.DeclaredOnly);

            foreach (var field in fields)
            {
                ClassifyTargetType(field.FieldType, targetBlockEntities, targetBehaviors);
            }

            currentType = currentType.BaseType;
        }

        // Skip renderers that don't reference any BlockEntity or BlockEntityBehavior
        // (these are likely position-only renderers for sub-components)
        if (targetBlockEntities.Count == 0 && targetBehaviors.Count == 0)
            return;

        // Register the mappings
        AllBlockEntityRenderers.Add(rendererType);

        foreach (var beType in targetBlockEntities)
        {
            if (!BlockEntityToRenderers.TryGetValue(beType, out var renderers))
            {
                renderers = new HashSet<Type>();
                BlockEntityToRenderers[beType] = renderers;
            }
            renderers.Add(rendererType);
        }

        foreach (var behaviorType in targetBehaviors)
        {
            if (!BehaviorToRenderers.TryGetValue(behaviorType, out var renderers))
            {
                renderers = new HashSet<Type>();
                BehaviorToRenderers[behaviorType] = renderers;
            }
            renderers.Add(rendererType);
        }

        logger?.Debug($"[IRendererBlockEntityScanner] {rendererType.Name} targets: BE=[{string.Join(", ", targetBlockEntities.Select(t => t.Name))}], Behaviors=[{string.Join(", ", targetBehaviors.Select(t => t.Name))}]");
    }

    /// <summary>
    /// Classifies a type as either a BlockEntity or BlockEntityBehavior target.
    /// </summary>
    private static void ClassifyTargetType(Type type, HashSet<Type> blockEntities, HashSet<Type> behaviors)
    {
        // Check for BlockEntity (but not the base class itself)
        if (typeof(BlockEntity).IsAssignableFrom(type) && type != typeof(BlockEntity))
        {
            blockEntities.Add(type);
        }
        // Check for BlockEntityBehavior (but not the base class itself)
        else if (typeof(BlockEntityBehavior).IsAssignableFrom(type) && type != typeof(BlockEntityBehavior))
        {
            behaviors.Add(type);
        }
    }

    /// <summary>
    /// Gets all IRenderer types that target the specified BlockEntity type.
    /// </summary>
    /// <param name="blockEntityType">The BlockEntity type to look up.</param>
    /// <returns>The set of renderer types, or an empty set if none found.</returns>
    public static IReadOnlySet<Type> GetRenderersForBlockEntity(Type blockEntityType)
    {
        if (BlockEntityToRenderers.TryGetValue(blockEntityType, out var renderers))
            return renderers;

        return new HashSet<Type>();
    }

    /// <summary>
    /// Gets all IRenderer types that target the specified BlockEntityBehavior type.
    /// </summary>
    /// <param name="behaviorType">The BlockEntityBehavior type to look up.</param>
    /// <returns>The set of renderer types, or an empty set if none found.</returns>
    public static IReadOnlySet<Type> GetRenderersForBehavior(Type behaviorType)
    {
        if (BehaviorToRenderers.TryGetValue(behaviorType, out var renderers))
            return renderers;

        return new HashSet<Type>();
    }

    /// <summary>
    /// Checks if the specified BlockEntity type has any known renderers targeting it.
    /// </summary>
    /// <param name="blockEntityType">The BlockEntity type to check.</param>
    /// <returns>True if at least one renderer targets this type.</returns>
    public static bool HasKnownRenderer(Type blockEntityType)
    {
        return BlockEntityToRenderers.ContainsKey(blockEntityType);
    }

    /// <summary>
    /// Checks if the specified BlockEntityBehavior type has any known renderers targeting it.
    /// </summary>
    /// <param name="behaviorType">The BlockEntityBehavior type to check.</param>
    /// <returns>True if at least one renderer targets this type.</returns>
    public static bool HasKnownBehaviorRenderer(Type behaviorType)
    {
        return BehaviorToRenderers.ContainsKey(behaviorType);
    }

    /// <summary>
    /// Checks if the specified BlockEntity instance (including its behaviors) has any known renderers.
    /// </summary>
    /// <param name="blockEntity">The BlockEntity instance to check.</param>
    /// <returns>True if at least one renderer targets the entity type or any of its behaviors.</returns>
    public static bool HasKnownRenderer(BlockEntity blockEntity)
    {
        if (HasKnownRenderer(blockEntity.GetType()))
            return true;

        foreach (var behavior in blockEntity.Behaviors)
        {
            if (HasKnownBehaviorRenderer(behavior.GetType()))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all BlockEntity types that have known renderers.
    /// </summary>
    /// <returns>The set of BlockEntity types with associated renderers.</returns>
    public static IReadOnlyCollection<Type> GetBlockEntitiesWithRenderers()
    {
        return BlockEntityToRenderers.Keys;
    }

    /// <summary>
    /// Gets all BlockEntityBehavior types that have known renderers.
    /// </summary>
    /// <returns>The set of BlockEntityBehavior types with associated renderers.</returns>
    public static IReadOnlyCollection<Type> GetBehaviorsWithRenderers()
    {
        return BehaviorToRenderers.Keys;
    }

    /// <summary>
    /// Gets all discovered IRenderer types that target a BlockEntity or BlockEntityBehavior.
    /// </summary>
    /// <returns>The set of renderer types.</returns>
    public static IReadOnlySet<Type> GetAllBlockEntityRenderers()
    {
        return AllBlockEntityRenderers;
    }

    /// <summary>
    /// Clears all cached data. Useful for reloading mods.
    /// </summary>
    public static void Clear()
    {
        BlockEntityToRenderers.Clear();
        BehaviorToRenderers.Clear();
        AllBlockEntityRenderers.Clear();
        _isInitialized = false;
    }
}
