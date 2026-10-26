using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.UI;

/// <summary>An unscaled logical footprint, including fixed padding when used for a cell.</summary>
/// <param name="Width">Outer logical width; validated when consumed by table APIs.</param>
/// <param name="Height">Outer logical height; validated when consumed by table APIs.</param>
internal readonly record struct TableSize(double Width, double Height)
{
    internal void Validate(string parameterName)
    {
        ValidateDimension(Width, parameterName);
        ValidateDimension(Height, parameterName);
    }

    internal static void ValidateDimension(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "Table dimensions must be finite and nonnegative.");
    }
}

/// <summary>Explicit child input eligibility, independent of composition and dynamic rendering.</summary>
[Flags]
internal enum TableInput
{
    /// <summary>Display-only content that receives no routed input.</summary>
    None = 0,
    /// <summary>Pointer input opt-in requiring a non-activating cancellation callback.</summary>
    Pointer = 1,
    /// <summary>Keyboard input opt-in; nested focus also requires the native element to be focusable.</summary>
    Keyboard = 2
}

/// <summary>Immutable metadata for a row element; creating a descriptor does not transfer ownership.</summary>
internal sealed class TableCell
{
    /// <summary>The element whose ownership transfers only when the table accepts its row.</summary>
    public GuiElement Element { get; }
    /// <summary>An optional ordinal, case-sensitive key local to the owning table.</summary>
    public string? Key { get; }
    /// <summary>An optional outer footprint; otherwise the table snapshots fixed bounds and padding on acceptance.</summary>
    public TableSize? PreferredSize { get; }
    /// <summary>Explicit input permissions; content is passive by default.</summary>
    public TableInput Input { get; }
    /// <summary>Idempotent cleanup that clears interaction without clicking or committing a value.</summary>
    public Action? CancelInput { get; }

    /// <summary>Describes a child without registering it, reparenting it, or taking ownership.</summary>
    /// <param name="element">The child to supply to a row.</param>
    /// <param name="key">An optional nonblank table-local lookup key.</param>
    /// <param name="preferredSize">Optional logical dimensions including fixed padding.</param>
    /// <param name="input">Explicit pointer/keyboard eligibility; passive content receives no routed input.</param>
    /// <param name="cancelInput">Non-activating cancellation, required when pointer input is enabled.</param>
    public TableCell(GuiElement element, string? key = null, TableSize? preferredSize = null,
        TableInput input = TableInput.None, Action? cancelInput = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (key is not null) ArgumentException.ThrowIfNullOrWhiteSpace(key);
        preferredSize?.Validate(nameof(preferredSize));
        if ((input & ~(TableInput.Pointer | TableInput.Keyboard)) != 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        if (input.HasFlag(TableInput.Pointer) && cancelInput is null)
            throw new ArgumentException("Pointer-enabled cells require a non-activating cancellation callback.", nameof(cancelInput));
        Element = element;
        Key = key;
        PreferredSize = preferredSize;
        Input = input;
        CancelInput = cancelInput;
    }
}

/// <summary>An immutable fixed-width or content-sized column policy in logical GUI units.</summary>
internal sealed class TableColumn
{
    /// <summary>Whether the configured width is exact rather than a minimum.</summary>
    public bool IsFixed { get; }
    /// <summary>The exact fixed width or the minimum content-sized width.</summary>
    public double Width { get; }

    private TableColumn(bool isFixed, double width)
    {
        TableSize.ValidateDimension(width, nameof(width));
        IsFixed = isFixed;
        Width = width;
    }

    /// <summary>Sizes a column to its widest preferred cell, subject to the supplied nonnegative minimum.</summary>
    public static TableColumn Auto(double minimumWidth = 0) => new(false, minimumWidth);

    /// <summary>Reserves an exact nonnegative width; wider cell footprints are rejected.</summary>
    public static TableColumn Fixed(double width) => new(true, width);
}

/// <summary>Immutable table layout configuration with a defensive snapshot of column policies.</summary>
internal sealed class TableOptions
{
    /// <summary>Policies by zero-based column index; omitted columns use content sizing with minimum zero.</summary>
    public IReadOnlyDictionary<int, TableColumn> Columns { get; }
    /// <summary>Logical spacing between columns, excluding either outer edge.</summary>
    public double ColumnGap { get; }
    /// <summary>Logical spacing between rows, excluding either outer edge.</summary>
    public double RowGap { get; }
    /// <summary>The minimum logical height of each populated row.</summary>
    public double MinimumRowHeight { get; }
    /// <summary>Positions each preferred cell footprint within its row without stretching it.</summary>
    public EnumVerticalAlign VerticalAlignment { get; }

    /// <summary>Validates spacing and snapshots column policies without retaining a mutable caller dictionary.</summary>
    /// <param name="columns">Optional column policies; the completed table must contain every configured index.</param>
    /// <param name="columnGap">Finite, nonnegative logical spacing between columns.</param>
    /// <param name="rowGap">Finite, nonnegative logical spacing between rows.</param>
    /// <param name="minimumRowHeight">Finite, nonnegative minimum row height.</param>
    /// <param name="verticalAlignment">Top, middle, or bottom alignment within each row.</param>
    public TableOptions(IReadOnlyDictionary<int, TableColumn>? columns = null,
        double columnGap = 0, double rowGap = 0, double minimumRowHeight = 0,
        EnumVerticalAlign verticalAlignment = EnumVerticalAlign.Top)
    {
        TableSize.ValidateDimension(columnGap, nameof(columnGap));
        TableSize.ValidateDimension(rowGap, nameof(rowGap));
        TableSize.ValidateDimension(minimumRowHeight, nameof(minimumRowHeight));
        if (verticalAlignment is not (EnumVerticalAlign.Top or EnumVerticalAlign.Middle or EnumVerticalAlign.Bottom))
            throw new ArgumentOutOfRangeException(nameof(verticalAlignment));
        var snapshot = new Dictionary<int, TableColumn>();
        if (columns is not null)
        {
            foreach (var (index, column) in columns)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(columns));
                ArgumentNullException.ThrowIfNull(column, nameof(columns));
                snapshot.Add(index, column);
            }
        }
        Columns = new ReadOnlyDictionary<int, TableColumn>(snapshot);
        ColumnGap = columnGap;
        RowGap = rowGap;
        MinimumRowHeight = minimumRowHeight;
        VerticalAlignment = verticalAlignment;
    }
}
