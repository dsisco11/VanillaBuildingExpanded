using System;
using System.Collections.Concurrent;
using System.Reflection;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Holds information about which rendering systems a block entity type uses.
/// </summary>
public readonly struct RenderSystemInfo
{
    /// <summary>
    /// True if the block entity (or any of its behaviors) has fields assignable to <see cref="IRenderer"/>,
    /// <see cref="MeshRef"/>, or <see cref="MultiTextureMeshRef"/>, indicating it likely uses custom per-frame rendering.
    /// </summary>
    public bool UsesCustomRenderer { get; init; }

    /// <summary>
    /// True if the block entity type (not <see cref="BlockEntity"/> base) overrides OnTesselation,
    /// or if any of its behavior types override OnTesselation.
    /// </summary>
    public bool OverridesOnTesselation { get; init; }

    /// <summary>
    /// True if the block entity relies only on tessellation (no custom renderer).
    /// These will preview correctly with the current brush tessellation approach.
    /// </summary>
    public bool IsTesselationOnly => OverridesOnTesselation && !UsesCustomRenderer;

    /// <summary>
    /// True if the block entity has a custom renderer that won't be captured by tessellation.
    /// These may not preview correctly (or at all) in the brush.
    /// </summary>
    public bool HasUncapturedRenderer => UsesCustomRenderer;

    public override string ToString()
    {
        return $"RenderSystemInfo {{ UsesCustomRenderer={UsesCustomRenderer}, OverridesOnTesselation={OverridesOnTesselation} }}";
    }
}

/// <summary>
/// Utility class for detecting which rendering system a block entity type uses.
/// Uses reflection to identify custom <see cref="IRenderer"/>-based rendering vs
/// <see cref="BlockEntity.OnTesselation"/>-based tessellation.
/// Results are cached to avoid repeated reflection overhead.
/// </summary>
public static class BlockEntityRenderDetector
{
    private static readonly ConcurrentDictionary<Type, RenderSystemInfo> Cache = new();

    private static readonly BindingFlags InstanceFieldFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type[] OnTesselationParamTypes =
    [
        typeof(ITerrainMeshPool),
        typeof(ITesselatorAPI)
    ];

    /// <summary>
    /// Gets render system information for a block entity type.
    /// Results are cached after first lookup.
    /// </summary>
    /// <param name="blockEntityType">The block entity type to analyze.</param>
    /// <returns>Information about which rendering systems the type uses.</returns>
    public static RenderSystemInfo GetRenderSystemInfo(Type blockEntityType)
    {
        ArgumentNullException.ThrowIfNull(blockEntityType);

        return Cache.GetOrAdd(blockEntityType, AnalyzeType);
    }

    /// <summary>
    /// Gets render system information for a block entity instance.
    /// Analyzes both the entity type and all attached behaviors.
    /// Results are cached per-type (not per-instance).
    /// </summary>
    /// <param name="blockEntity">The block entity instance to analyze.</param>
    /// <returns>Aggregated information from the entity and all its behaviors.</returns>
    public static RenderSystemInfo GetRenderSystemInfo(BlockEntity blockEntity)
    {
        ArgumentNullException.ThrowIfNull(blockEntity);

        // Get info for the block entity type itself
        var entityInfo = GetRenderSystemInfo(blockEntity.GetType());

        bool usesCustomRenderer = entityInfo.UsesCustomRenderer;
        bool overridesOnTesselation = entityInfo.OverridesOnTesselation;

        // Also check all attached behaviors
        foreach (var behavior in blockEntity.Behaviors)
        {
            var behaviorInfo = GetRenderSystemInfo(behavior.GetType());
            usesCustomRenderer |= behaviorInfo.UsesCustomRenderer;
            overridesOnTesselation |= behaviorInfo.OverridesOnTesselation;

            // Early exit if we've found both
            if (usesCustomRenderer && overridesOnTesselation)
                break;
        }

        return new RenderSystemInfo
        {
            UsesCustomRenderer = usesCustomRenderer,
            OverridesOnTesselation = overridesOnTesselation
        };
    }

    /// <summary>
    /// Checks if a block entity type (or behavior type) uses a custom renderer.
    /// </summary>
    public static bool UsesCustomRenderer(Type type)
    {
        return GetRenderSystemInfo(type).UsesCustomRenderer;
    }

    /// <summary>
    /// Checks if a block entity type (or behavior type) overrides OnTesselation.
    /// </summary>
    public static bool OverridesOnTesselation(Type type)
    {
        return GetRenderSystemInfo(type).OverridesOnTesselation;
    }

    /// <summary>
    /// Clears the detection cache. Useful if assemblies are reloaded.
    /// </summary>
    public static void ClearCache()
    {
        Cache.Clear();
    }

    /// <summary>
    /// Analyzes a type for rendering system usage.
    /// </summary>
    private static RenderSystemInfo AnalyzeType(Type type)
    {
        return new RenderSystemInfo
        {
            UsesCustomRenderer = DetectCustomRenderer(type),
            OverridesOnTesselation = DetectOnTesselationOverride(type)
        };
    }

    /// <summary>
    /// Detects if a type has fields that indicate custom renderer usage.
    /// Checks for fields assignable to IRenderer, MeshRef, or MultiTextureMeshRef.
    /// </summary>
    private static bool DetectCustomRenderer(Type type)
    {
        // Walk the type hierarchy (but stop at object/BlockEntity/BlockEntityBehavior base)
        Type? currentType = type;

        while (currentType is not null && !IsBaseType(currentType))
        {
            // Check declared fields only (not inherited) to avoid double-counting
            var fields = currentType.GetFields(InstanceFieldFlags | BindingFlags.DeclaredOnly);

            foreach (var field in fields)
            {
                if (IsRendererRelatedType(field.FieldType))
                    return true;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Checks if a field type indicates custom rendering.
    /// </summary>
    private static bool IsRendererRelatedType(Type fieldType)
    {
        // Check for IRenderer interface
        if (typeof(IRenderer).IsAssignableFrom(fieldType))
            return true;

        // Check for MeshRef (GPU-uploaded mesh reference)
        if (typeof(MeshRef).IsAssignableFrom(fieldType))
            return true;

        // Check for MultiTextureMeshRef
        if (typeof(MultiTextureMeshRef).IsAssignableFrom(fieldType))
            return true;

        // Check for arrays/generics containing renderer types
        if (fieldType.IsArray)
        {
            var elementType = fieldType.GetElementType();
            if (elementType is not null && IsRendererRelatedType(elementType))
                return true;
        }

        if (fieldType.IsGenericType)
        {
            foreach (var genericArg in fieldType.GetGenericArguments())
            {
                if (IsRendererRelatedType(genericArg))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects if a type overrides the OnTesselation method (beyond the base implementation).
    /// </summary>
    private static bool DetectOnTesselationOverride(Type type)
    {
        var method = type.GetMethod(
            "OnTesselation",
            InstanceFieldFlags,
            null,
            OnTesselationParamTypes,
            null);

        if (method is null)
            return false;

        // Check if the declaring type is not one of the base types
        var declaringType = method.DeclaringType;
        return declaringType is not null && !IsBaseType(declaringType);
    }

    /// <summary>
    /// Checks if a type is one of the base types we should stop scanning at.
    /// </summary>
    private static bool IsBaseType(Type type)
    {
        return type == typeof(object)
            || type == typeof(BlockEntity)
            || type == typeof(BlockEntityBehavior);
    }
}
