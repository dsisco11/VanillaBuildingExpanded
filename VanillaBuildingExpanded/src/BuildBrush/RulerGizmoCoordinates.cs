using System;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

internal static class RulerGizmoCoordinates
{
    public static GizmoPosition ToCenter(RulerBounds bounds) => new(Vector256.Create(
        (bounds.RenderMinX + bounds.RenderMaxExclusiveX) / 2,
        (bounds.RenderMinY + bounds.RenderMaxExclusiveY) / 2,
        (bounds.RenderMinZ + bounds.RenderMaxExclusiveZ) / 2, 0d), bounds.MinCell.Dimension);

    public static GizmoPosition ToAnchor(RulerEndpoint cell, RulerEndpointRole role)
    {
        double offset = Offset(role);
        return new(Vector256.Create(cell.X + offset, cell.Y + offset, cell.Z + offset, 0d), cell.Dimension);
    }

    public static bool TryDecode(GizmoPosition anchor, RulerEndpointRole role,
        IBlockAccessor accessor, out RulerEndpoint cell)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        double offset = Offset(role);
        var coordinates = anchor.Coordinates;
        double x = coordinates[0] - offset, y = coordinates[1] - offset, z = coordinates[2] - offset;
        cell = default;
        if (!Integer(x) || !Integer(y) || !Integer(z)) return false;
        var decoded = new RulerEndpoint(checked((int)x), checked((int)y), checked((int)z), anchor.Dimension);
        if (!decoded.IsValid(accessor)) return false;
        cell = decoded;
        return true;
    }

    public static GizmoPosition? Constrain(PositionGizmoEdit edit, RulerEndpointRole role, IBlockAccessor accessor)
    {
        if (!TryDecode(edit.Start, role, accessor, out _) || Snap(edit) is not { } candidate)
            return null;
        return TryDecode(candidate, role, accessor, out _) ? candidate : null;
    }

    public static GizmoPosition? ConstrainTranslation(PositionGizmoEdit edit, RulerBounds baseline,
        IBlockAccessor accessor)
    {
        if (edit.Start != ToCenter(baseline) || Snap(edit) is not { } candidate) return null;
        var delta = candidate.Coordinates - edit.Start.Coordinates;
        var start = ToAnchor(baseline.MinCell, RulerEndpointRole.Start);
        var end = ToAnchor(baseline.MaxCell, RulerEndpointRole.End);
        if (!GizmoRay.IsCoordinate(start.Coordinates + delta) || !GizmoRay.IsCoordinate(end.Coordinates + delta))
            return null;
        return TryDecode(new(start.Coordinates + delta, start.Dimension), RulerEndpointRole.Start, accessor, out _)
            && TryDecode(new(end.Coordinates + delta, end.Dimension), RulerEndpointRole.End, accessor, out _)
            ? candidate : null;
    }

    private static GizmoPosition? Snap(PositionGizmoEdit edit)
    {
        if (edit.Start.Dimension != edit.Candidate.Dimension) return null;
        var candidate = edit.Candidate;
        bool center = edit.Handle == PositionGizmo.Center;
        int component = edit.Handle.Value - 1;
        if (!center)
        {
            if (component is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(edit));
            for (int i = 0; i < 3; i++)
                if (i != component && edit.Start.Coordinates[i] != candidate.Coordinates[i]) return null;
        }
        var coordinates = edit.Start.Coordinates;
        for (int i = 0; i < 3; i++)
        {
            if (!center && i != component) continue;
            double coordinate = edit.Start.Coordinates[i]
                + Math.Round(candidate.Coordinates[i] - edit.Start.Coordinates[i],
                    MidpointRounding.AwayFromZero);
            if (!double.IsFinite(coordinate)) return null;
            coordinates = coordinates.WithElement(i, coordinate);
        }
        return new(coordinates, edit.Start.Dimension);
    }

    private static bool Integer(double value) => double.IsFinite(value)
        && value >= int.MinValue && value <= int.MaxValue && value == Math.Truncate(value);

    private static double Offset(RulerEndpointRole role) => role switch
    {
        RulerEndpointRole.Start => 0,
        RulerEndpointRole.End => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
