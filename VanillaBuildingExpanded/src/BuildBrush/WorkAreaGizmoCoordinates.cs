using System;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

internal static class WorkAreaGizmoCoordinates
{
    public static GizmoPosition ToCenter(WorkAreaBounds bounds) => new(Vector256.Create(
        (bounds.RenderMinX + bounds.RenderMaxExclusiveX) / 2,
        (bounds.RenderMinY + bounds.RenderMaxExclusiveY) / 2,
        (bounds.RenderMinZ + bounds.RenderMaxExclusiveZ) / 2, 0d), bounds.MinCell.Dimension);

    public static GizmoPosition ToAnchor(WorkAreaEndpoint cell, WorkAreaEndpointRole role)
    {
        double offset = Offset(role);
        return new(Vector256.Create(cell.X + offset, cell.Y + offset, cell.Z + offset, 0d), cell.Dimension);
    }

    public static bool TryDecode(GizmoPosition anchor, WorkAreaEndpointRole role,
        IBlockAccessor accessor, out WorkAreaEndpoint cell)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        double offset = Offset(role);
        var coordinates = anchor.Coordinates;
        double x = coordinates[0] - offset, y = coordinates[1] - offset, z = coordinates[2] - offset;
        cell = default;
        if (!Integer(x) || !Integer(y) || !Integer(z)) return false;
        var decoded = new WorkAreaEndpoint(checked((int)x), checked((int)y), checked((int)z), anchor.Dimension);
        if (!decoded.IsValid(accessor)) return false;
        cell = decoded;
        return true;
    }

    public static GizmoPosition? Constrain(PositionGizmoEdit edit, WorkAreaEndpointRole role, IBlockAccessor accessor)
    {
        if (!TryDecode(edit.Start, role, accessor, out _) || Snap(edit) is not { } candidate)
            return null;
        return TryDecode(candidate, role, accessor, out _) ? candidate : null;
    }

    public static GizmoPosition? ConstrainTranslation(PositionGizmoEdit edit, WorkAreaBounds baseline,
        IBlockAccessor accessor)
    {
        if (edit.Start != ToCenter(baseline) || Snap(edit) is not { } candidate) return null;
        var delta = candidate.Coordinates - edit.Start.Coordinates;
        var start = ToAnchor(baseline.MinCell, WorkAreaEndpointRole.Start);
        var end = ToAnchor(baseline.MaxCell, WorkAreaEndpointRole.End);
        if (!GizmoRay.IsCoordinate(start.Coordinates + delta) || !GizmoRay.IsCoordinate(end.Coordinates + delta))
            return null;
        return TryDecode(new(start.Coordinates + delta, start.Dimension), WorkAreaEndpointRole.Start, accessor, out _)
            && TryDecode(new(end.Coordinates + delta, end.Dimension), WorkAreaEndpointRole.End, accessor, out _)
            ? candidate : null;
    }

    private static GizmoPosition? Snap(PositionGizmoEdit edit)
    {
        if (edit.Start.Dimension != edit.Candidate.Dimension) return null;
        var candidate = edit.Candidate;
        var coordinates = edit.Start.Coordinates;
        for (int i = 0; i < 3; i++)
        {
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

    private static double Offset(WorkAreaEndpointRole role) => role switch
    {
        WorkAreaEndpointRole.Start => 0,
        WorkAreaEndpointRole.End => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
