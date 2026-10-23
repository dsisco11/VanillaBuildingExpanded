using System;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Resolves brush placement candidates and validates them without acquiring player state.</summary>
internal sealed class BrushPlacementValidation
{
    private readonly IBrushPlacementValidator validator;

    #region Public API

    /// <summary>Uses the supplied rule boundary for every placement candidate.</summary>
    public BrushPlacementValidation(IBrushPlacementValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        this.validator = validator;
    }

    /// <summary>Tries the requested snapping mode, then falls back to the unsnapped position.</summary>
    public BlockPos ResolvePosition(Block? block, BlockSelection selection, EBuildBrushSnapping mode,
        BrushSnapping snapping, out bool isValid)
    {
        if (block is null)
        {
            isValid = true;
            return selection.Position;
        }

        // Each attempt runs the rules independently, even when both positions coincide.
        if (TryGetValidSnappedPosition(block, selection, snapping, mode, out BlockPos snapped))
        {
            isValid = true;
            return snapped;
        }

        isValid = TryGetValidSnappedPosition(block, selection, snapping, EBuildBrushSnapping.None, out BlockPos unsnapped);
        return unsnapped;
    }

    /// <summary>Resolves one snapping candidate and checks its placement rules.</summary>
    public bool TryGetValidSnappedPosition(Block? block, BlockSelection selection, BrushSnapping snapping,
        EBuildBrushSnapping mode, out BlockPos position)
    {
        if (block is null)
        {
            position = selection.Position.Copy();
            return true;
        }

        position = snapping.ResolvePosition(mode);
        return CanPlaceAtPosition(block, selection, position);
    }

    /// <summary>Checks a resolved preview position using a cloned selection and a fresh failure code.</summary>
    public bool CanPlaceAtPosition(Block block, BlockSelection selection, BlockPos position)
    {
        // Preserve the raycast data while keeping native rule mutations away from the source selection.
        BlockSelection resolved = BuildResolvedSelection(selection, position);
        string failureCode = string.Empty;
        return validator.CanPlace(block, resolved, ref failureCode);
    }

    /// <summary>Clones selection data and replaces only its resolved position and offset marker.</summary>
    public static BlockSelection BuildResolvedSelection(BlockSelection source, BlockPos? position)
    {
        BlockSelection selection = source.Clone();
        if (position is not null)
        {
            selection.DidOffset = true;
            selection.SetPos(position.X, position.Y, position.Z);
        }
        return selection;
    }

    #endregion
}
