using System;
using System.Collections.Generic;

namespace VanillaBuildingExpanded.UI;

/// <summary>Divider boundary kinds, also usable as a renderer's selection mask.</summary>
[Flags]
internal enum TableDividerKind
{
    /// <summary>No boundaries.</summary>
    None = 0,
    /// <summary>A horizontal boundary between rows.</summary>
    Row = 1,
    /// <summary>A vertical boundary between populated cells in a row.</summary>
    Cell = 2,
    /// <summary>Both row and cell boundaries.</summary>
    All = Row | Cell
}

/// <summary>One table-local, axis-aligned boundary in device units, before style-specific pixel alignment.</summary>
internal readonly record struct TableDividerSegment(
    TableDividerKind Kind, double StartX, double StartY, double EndX, double EndY);

/// <summary>An immutable snapshot of arranged divider boundaries, independent of mutable elements and bounds.</summary>
internal sealed class TableDividerGeometry
{
    /// <summary>The table's drawing width, already converted to device units.</summary>
    public double Width { get; }
    /// <summary>The table's drawing height, already converted to device units.</summary>
    public double Height { get; }
    /// <summary>The positive scale used only to convert a renderer's logical stroke and dash settings.</summary>
    public double GuiScale { get; }
    /// <summary>Snapshotted boundary segments; renderers must not scale their coordinates again.</summary>
    public IReadOnlyList<TableDividerSegment> Segments { get; }

    /// <summary>Validates and snapshots device-space boundaries without retaining the source collection.</summary>
    public TableDividerGeometry(double width, double height, double guiScale, IEnumerable<TableDividerSegment> segments)
    {
        TableSize.ValidateDimension(width, nameof(width));
        TableSize.ValidateDimension(height, nameof(height));
        ValidateScale(guiScale);
        ArgumentNullException.ThrowIfNull(segments);
        var snapshot = new List<TableDividerSegment>();
        foreach (var segment in segments)
        {
            TableSize.ValidateDimension(segment.StartX, nameof(segments));
            TableSize.ValidateDimension(segment.StartY, nameof(segments));
            TableSize.ValidateDimension(segment.EndX, nameof(segments));
            TableSize.ValidateDimension(segment.EndY, nameof(segments));
            bool validDirection = segment.Kind switch
            {
                TableDividerKind.Row => segment.StartY == segment.EndY && segment.StartX < segment.EndX,
                TableDividerKind.Cell => segment.StartX == segment.EndX && segment.StartY < segment.EndY,
                _ => false
            };
            if (!validDirection || segment.EndX > width || segment.EndY > height
                || segment.StartX > width || segment.StartY > height)
                throw new ArgumentException("Divider segments must be positive-length boundaries within the table extent.", nameof(segments));
            snapshot.Add(segment);
        }
        Width = width;
        Height = height;
        GuiScale = guiScale;
        Segments = snapshot.AsReadOnly();
    }

    internal static void ValidateScale(double guiScale)
    {
        if (!double.IsFinite(guiScale) || guiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(guiScale), guiScale, "GUI scale must be finite and positive.");
    }
}
