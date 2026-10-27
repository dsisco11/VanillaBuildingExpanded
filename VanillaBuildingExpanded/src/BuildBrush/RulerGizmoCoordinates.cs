using System;
using System.Runtime.Intrinsics;

using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Common;

namespace VanillaBuildingExpanded.BuildBrush;

internal static class RulerGizmoCoordinates
{
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
        if (edit.Start.Dimension != edit.Candidate.Dimension || !TryDecode(edit.Start, role, accessor, out _))
            return null;
        var candidate = edit.Candidate;
        if (edit.Handle != PositionGizmo.Center)
        {
            int component = edit.Handle.Value - 1;
            if (component is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(edit));
            for (int i = 0; i < 3; i++)
                if (i != component && edit.Start.Coordinates[i] != candidate.Coordinates[i]) return null;
            double coordinate = edit.Start.Coordinates[component]
                + Math.Round(candidate.Coordinates[component] - edit.Start.Coordinates[component],
                    MidpointRounding.AwayFromZero);
            if (!double.IsFinite(coordinate)) return null;
            candidate = new(edit.Start.Coordinates.WithElement(component, coordinate), edit.Start.Dimension);
        }
        return TryDecode(candidate, role, accessor, out _) ? candidate : null;
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
