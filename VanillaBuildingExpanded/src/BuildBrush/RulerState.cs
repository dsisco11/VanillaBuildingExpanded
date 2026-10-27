using System;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal enum RulerPickMode
{
    Idle,
    PickingStart,
    PickingEnd
}

internal enum RulerNotice
{
    SelectStart,
    SelectEnd,
    AimAtBlock,
    SetOtherEndpoint,
    SetBothEndpoints
}

internal readonly record struct RulerEndpoint(int X, int Y, int Z, int Dimension)
{
    public bool IsValid(IBlockAccessor accessor) => Dimension >= 0
        && Y >= 0 && Y < BlockPos.DimensionBoundary
        && (long)Dimension * BlockPos.DimensionBoundary + Y <= int.MaxValue
        && accessor.IsValidPos(new BlockPos(X, Y, Z, Dimension));
}

internal readonly record struct RulerBounds
{
    public RulerEndpoint MinCell { get; }
    public RulerEndpoint MaxCell { get; }
    public long SizeX => (long)MaxCell.X - MinCell.X + 1;
    public long SizeY => (long)MaxCell.Y - MinCell.Y + 1;
    public long SizeZ => (long)MaxCell.Z - MinCell.Z + 1;
    public double RenderMinX => MinCell.X;
    public double RenderMinY => MinCell.Y;
    public double RenderMinZ => MinCell.Z;
    public double RenderMaxExclusiveX => (double)MaxCell.X + 1;
    public double RenderMaxExclusiveY => (double)MaxCell.Y + 1;
    public double RenderMaxExclusiveZ => (double)MaxCell.Z + 1;

    public RulerBounds(RulerEndpoint start, RulerEndpoint end)
    {
        if (start.Dimension != end.Dimension)
            throw new ArgumentException("Ruler endpoints must belong to the same dimension.", nameof(end));

        MinCell = new RulerEndpoint(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Min(start.Z, end.Z), start.Dimension);
        MaxCell = new RulerEndpoint(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y),
            Math.Max(start.Z, end.Z), start.Dimension);
    }
}

internal readonly record struct RulerState(
    RulerEndpoint? Start,
    RulerEndpoint? End,
    RulerPickMode PickMode,
    bool ShowRuler,
    bool IsBuildModeEnabled,
    bool IsPlayerAlive)
{
    public bool CanPick => IsBuildModeEnabled && IsPlayerAlive;

    public RulerBounds? Bounds => Start is { } start && End is { } end
        && start.Dimension == end.Dimension ? new RulerBounds(start, end) : null;
}
