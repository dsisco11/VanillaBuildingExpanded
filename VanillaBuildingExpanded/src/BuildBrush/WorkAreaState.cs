using System;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal enum WorkAreaEndpointRole { Start, End }

internal enum WorkAreaNotice
{
    InvalidEndpoint,
    InvalidSize
}

internal enum WorkAreaNameError { None, Invalid, Duplicate }

internal sealed record NamedWorkArea(Guid Id, string Name, WorkAreaEndpoint? Start, WorkAreaEndpoint? End,
    bool IsVisible = false)
{
    public WorkAreaBounds? Bounds => Start is { } start && End is { } end
        && start.Dimension == end.Dimension ? new WorkAreaBounds(start, end) : null;
}

internal readonly record struct WorkAreaEndpoint(int X, int Y, int Z, int Dimension)
{
    public bool IsValid(IBlockAccessor accessor) => Dimension >= 0
        && Y >= 0 && Y < BlockPos.DimensionBoundary
        && (long)Dimension * BlockPos.DimensionBoundary + Y <= int.MaxValue
        && accessor.IsValidPos(new BlockPos(X, Y, Z, Dimension));

    public bool TryTranslate(long x, long y, long z, IBlockAccessor accessor, out WorkAreaEndpoint translated)
    {
        translated = default;
        if (x < int.MinValue - (long)X || x > int.MaxValue - (long)X
            || y < -(long)Y || y >= BlockPos.DimensionBoundary - (long)Y
            || z < int.MinValue - (long)Z || z > int.MaxValue - (long)Z) return false;
        var candidate = new WorkAreaEndpoint((int)(X + x), (int)(Y + y), (int)(Z + z), Dimension);
        if (!candidate.IsValid(accessor)) return false;
        translated = candidate;
        return true;
    }
}

internal readonly record struct WorkAreaBounds
{
    public WorkAreaEndpoint MinCell { get; }
    public WorkAreaEndpoint MaxCell { get; }
    public long SizeX => (long)MaxCell.X - MinCell.X + 1;
    public long SizeY => (long)MaxCell.Y - MinCell.Y + 1;
    public long SizeZ => (long)MaxCell.Z - MinCell.Z + 1;
    public double RenderMinX => MinCell.X;
    public double RenderMinY => MinCell.Y;
    public double RenderMinZ => MinCell.Z;
    public double RenderMaxExclusiveX => (double)MaxCell.X + 1;
    public double RenderMaxExclusiveY => (double)MaxCell.Y + 1;
    public double RenderMaxExclusiveZ => (double)MaxCell.Z + 1;

    public WorkAreaBounds(WorkAreaEndpoint start, WorkAreaEndpoint end)
    {
        if (start.Dimension != end.Dimension)
            throw new ArgumentException("Work area endpoints must belong to the same dimension.", nameof(end));

        MinCell = new WorkAreaEndpoint(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Min(start.Z, end.Z), start.Dimension);
        MaxCell = new WorkAreaEndpoint(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y),
            Math.Max(start.Z, end.Z), start.Dimension);
    }
}

internal readonly record struct WorkAreaState(
    WorkAreaEndpoint? Start,
    WorkAreaEndpoint? End,
    bool ShowWorkArea,
    bool IsBuildModeEnabled,
    bool IsPlayerAlive,
    bool HasActiveArea = true)
{
    public bool CanPick => HasActiveArea && IsBuildModeEnabled && IsPlayerAlive;

    public WorkAreaBounds? Bounds => Start is { } start && End is { } end
        && start.Dimension == end.Dimension ? new WorkAreaBounds(start, end) : null;
}
