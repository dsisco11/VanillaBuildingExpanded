using System;
using System.Collections.Generic;
using System.Linq;

using Cairo;

namespace VanillaBuildingExpanded.UI;

/// <summary>Draws decorative dividers from device-space geometry without changing table layout or owning resources.</summary>
internal interface ITableDividerRenderer
{
    /// <summary>Draws the selected treatment, leaving caller drawing state and paths unchanged.</summary>
    void Draw(Context context, TableDividerGeometry geometry);
}

/// <summary>Shared, immutable line styling; boundary selection belongs to renderers rather than the table.</summary>
internal sealed class TableDividerOptions
{
    /// <summary>A value-type RGBA color with each component in the inclusive range zero to one.</summary>
    public Color Color { get; }
    /// <summary>The positive stroke width in unscaled logical GUI units.</summary>
    public double StrokeWidth { get; }
    /// <summary>The boundary kinds this renderer draws.</summary>
    public TableDividerKind Boundaries { get; }

    /// <summary>Validates line settings without allocating Cairo or GPU resources.</summary>
    public TableDividerOptions(Color color, double strokeWidth = 1, TableDividerKind boundaries = TableDividerKind.All)
    {
        foreach (double component in new[] { color.R, color.G, color.B, color.A })
            if (!double.IsFinite(component) || component < 0 || component > 1)
                throw new ArgumentOutOfRangeException(nameof(color), "RGBA components must be finite and between zero and one.");
        RequirePositive(strokeWidth, nameof(strokeWidth));
        if ((boundaries & ~TableDividerKind.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(boundaries));
        Color = color;
        StrokeWidth = strokeWidth;
        Boundaries = boundaries;
    }

    internal static void RequirePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "Line dimensions must be finite and positive.");
    }
}

/// <summary>A shareable no-op strategy preserving divider-free content without branches in the table.</summary>
internal sealed class NoTableDividerRenderer : ITableDividerRenderer
{
    /// <summary>The stateless default renderer.</summary>
    public static NoTableDividerRenderer Instance { get; } = new();

    private NoTableDividerRenderer() { }

    /// <summary>Validates the call and deliberately draws nothing.</summary>
    public void Draw(Context context, TableDividerGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
    }
}

/// <summary>Draws selected row and cell boundaries as solid, pixel-aligned lines.</summary>
internal sealed class SolidTableDividerRenderer : ITableDividerRenderer
{
    private readonly TableDividerOptions options;

    /// <summary>Creates a shareable renderer from immutable line settings.</summary>
    public SolidTableDividerRenderer(TableDividerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    /// <summary>Draws all selected segments with one stroke to avoid accumulating alpha at intersections.</summary>
    public void Draw(Context context, TableDividerGeometry geometry) =>
        TableDividerDrawing.Stroke(context, geometry, options, [], 0);
}

/// <summary>Draws selected boundaries with a snapshotted logical dash pattern.</summary>
internal sealed class DashedTableDividerRenderer : ITableDividerRenderer
{
    private readonly TableDividerOptions options;
    private readonly double[] dashPattern;
    private readonly double dashOffset;

    /// <summary>Creates a resource-free renderer with positive logical dash/gap lengths and a finite logical offset.</summary>
    /// <param name="options">Immutable line settings, including boundary selection.</param>
    /// <param name="dashPattern">A nonempty dash pattern; every dash and gap must be finite and positive.</param>
    /// <param name="dashOffset">Finite logical phase offset, which may be negative.</param>
    public DashedTableDividerRenderer(TableDividerOptions options, IEnumerable<double> dashPattern, double dashOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dashPattern);
        this.dashPattern = dashPattern.ToArray();
        if (this.dashPattern.Length == 0)
            throw new ArgumentException("A dashed renderer requires a nonempty dash pattern.", nameof(dashPattern));
        foreach (double length in this.dashPattern) TableDividerOptions.RequirePositive(length, nameof(dashPattern));
        // Cairo repeats odd-length patterns, so their complete alternating period is twice the sum.
        TableDividerOptions.RequirePositive(this.dashPattern.Sum() * (this.dashPattern.Length % 2 == 0 ? 1 : 2),
            nameof(dashPattern));
        if (!double.IsFinite(dashOffset)) throw new ArgumentOutOfRangeException(nameof(dashOffset));
        this.options = options;
        this.dashOffset = dashOffset;
    }

    /// <summary>Scales dash settings once and strokes the selected device-space segments.</summary>
    public void Draw(Context context, TableDividerGeometry geometry) =>
        TableDividerDrawing.Stroke(context, geometry, options, dashPattern, dashOffset);
}

/// <summary>Shares line mechanics between strategies without introducing style decisions into the table.</summary>
internal static class TableDividerDrawing
{
    internal static void Stroke(Context context, TableDividerGeometry geometry, TableDividerOptions options,
        ReadOnlySpan<double> dashPattern, double dashOffset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        double width = options.StrokeWidth * geometry.GuiScale;
        TableDividerOptions.RequirePositive(width, nameof(options.StrokeWidth));
        var scaledDashes = new double[dashPattern.Length];
        for (int index = 0; index < scaledDashes.Length; index++)
        {
            scaledDashes[index] = dashPattern[index] * geometry.GuiScale;
            TableDividerOptions.RequirePositive(scaledDashes[index], nameof(dashPattern));
        }
        if (scaledDashes.Length != 0)
            TableDividerOptions.RequirePositive(scaledDashes.Sum() * (scaledDashes.Length % 2 == 0 ? 1 : 2),
                nameof(dashPattern));
        double scaledOffset = dashOffset * geometry.GuiScale;
        if (!double.IsFinite(scaledOffset)) throw new ArgumentOutOfRangeException(nameof(dashOffset));

        using var originalPath = context.CopyPath();
        context.Save();
        try
        {
            context.NewPath();
            context.Rectangle(0, 0, geometry.Width, geometry.Height);
            context.Clip();
            context.Operator = Operator.Over;
            context.SetSourceColor(options.Color);
            context.LineWidth = width;
            context.LineCap = LineCap.Butt;
            context.SetDash(scaledDashes, scaledOffset);
            foreach (var segment in geometry.Segments)
            {
                if ((options.Boundaries & segment.Kind) == 0) continue;
                if (segment.Kind == TableDividerKind.Row)
                {
                    double y = AlignStroke(segment.StartY, width);
                    context.MoveTo(segment.StartX, y);
                    context.LineTo(segment.EndX, y);
                }
                else
                {
                    double x = AlignStroke(segment.StartX, width);
                    context.MoveTo(x, segment.StartY);
                    context.LineTo(x, segment.EndY);
                }
            }
            // A single Cairo stroke paints the union, rather than blending each crossing separately.
            context.Stroke();
        }
        finally
        {
            context.Restore();
            context.NewPath();
            context.AppendPath(originalPath);
        }
    }

    private static double AlignStroke(double coordinate, double width) =>
        Math.Floor(coordinate - width / 2 + 0.5) + width / 2;
}
