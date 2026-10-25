using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.UI;

/// <summary>Owns GUI elements supplied as rows and arranges them into shared, content-sized or fixed columns.</summary>
/// <remarks>
/// Sizes use unscaled logical GUI units. Accepted children must not be registered, reparented, or disposed independently.
/// This phase implements layout and ownership only; composition, divider rendering, and input routing are pending.
/// </remarks>
internal sealed class GuiElementTable : GuiElement
{
    /// <summary>Retains accepted bounds and preferred footprints independently of later arrangement.</summary>
    private sealed class CellEntry(TableCell cell, TableSize size)
    {
        public TableCell Cell { get; } = cell;
        public ElementBounds Bounds { get; } = cell.Element.Bounds;
        public double PaddingX { get; } = cell.Element.Bounds.fixedPaddingX;
        public double PaddingY { get; } = cell.Element.Bounds.fixedPaddingY;
        public TableSize Size { get; set; } = size;
    }

    private sealed record Layout(double[] ColumnWidths, double[] RowHeights, TableSize Size);

    private readonly ElementBounds tableBounds;
    private readonly TableOptions options;
    private readonly List<CellEntry[]> rows = [];
    private readonly Dictionary<GuiElement, CellEntry> elements = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, GuiElement> keyedElements = new(StringComparer.Ordinal);
    private bool invalidationNotified;
    private bool disposed;

    /// <summary>Whether the registration transition has frozen row structure; it does not itself register the table.</summary>
    public bool IsRegistered { get; private set; }

    /// <summary>Signals a layout change once until <see cref="Arrange"/> consumes the pending invalidation.</summary>
    /// <remarks>The owner must settle measurement and arrangement at a safe GUI composition boundary.</remarks>
    public event Action? LayoutInvalidated;

    /// <summary>Creates an empty table without allocating drawing resources or accepting any children.</summary>
    /// <param name="api">Client services retained by the native GUI base class.</param>
    /// <param name="bounds">Fixed table bounds with zero padding and no existing children.</param>
    /// <param name="options">Immutable layout options, or null to use content sizing without gaps or minimums.</param>
    public GuiElementTable(ICoreClientAPI api, ElementBounds bounds, TableOptions? options = null)
        : base(api, bounds)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(bounds);
        tableBounds = bounds;
        this.options = options ?? new TableOptions();
        ValidateTableBounds();
        if (bounds.ChildBounds.Count != 0)
            throw new ArgumentException("Table bounds must not already contain children.", nameof(bounds));
    }

    /// <summary>Creates a table from passive rows, validating the entire input before transferring child ownership.</summary>
    /// <param name="api">Client services retained by the native GUI base class.</param>
    /// <param name="bounds">Fixed table bounds with zero padding and no existing children.</param>
    /// <param name="options">Layout options applied to the completed row collection.</param>
    /// <param name="rows">Nonempty element collections in row-major order, enumerated and snapshotted once.</param>
    /// <remarks>Enumeration or validation failure leaves every supplied child owned by the caller.</remarks>
    public GuiElementTable(ICoreClientAPI api, ElementBounds bounds, TableOptions options,
        IEnumerable<IEnumerable<GuiElement>> rows) : this(api, bounds, options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rows);
        var knownElements = new HashSet<GuiElement>(ReferenceEqualityComparer.Instance);
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        var knownBounds = CollectOwnedBounds();
        var pending = new List<CellEntry[]>();
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row, nameof(rows));
            pending.Add(PrepareRow(row.Select(element => new TableCell(element)),
                knownElements, knownKeys, knownBounds));
        }
        // Validate aggregate dimensions before committing any of the constructor's rows.
        CalculateLayout(pending);
        foreach (var row in pending) AcceptRow(row);
    }

    /// <summary>Appends a passive row and accepts ownership after its complete validation.</summary>
    /// <param name="elements">Unparented, fixed-size elements in column order; trailing placeholders are unnecessary.</param>
    public void AddRow(params GuiElement[] elements) => AddRow((IEnumerable<GuiElement>)elements);

    /// <summary>Snapshots and appends a passive row without retaining the caller's enumerable.</summary>
    /// <param name="elements">A nonempty collection of unparented, fixed-size elements.</param>
    public void AddRow(IEnumerable<GuiElement> elements)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(elements);
        AddRowCells(elements.Select(element => new TableCell(element)));
    }

    /// <summary>Appends a row with explicit keys, preferred footprints, or input metadata.</summary>
    /// <param name="cells">Descriptors to snapshot and validate before accepting any elements in this addition.</param>
    /// <remarks>Previously accepted rows remain owned by the table if this row fails enumeration or validation.</remarks>
    /// <exception cref="InvalidOperationException">Row structure has been frozen by registration.</exception>
    public void AddRowCells(IEnumerable<TableCell> cells)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(cells);
        var pending = PrepareRow(cells,
            new HashSet<GuiElement>(elements.Keys, ReferenceEqualityComparer.Instance),
            new HashSet<string>(keyedElements.Keys, StringComparer.Ordinal), CollectOwnedBounds());
        AcceptRow(pending);
        NotifyLayoutInvalidated();
    }

    /// <summary>Validates the completed layout and measures its preferred footprint without changing native bounds.</summary>
    /// <returns>The unscaled table size, with gaps only between allocated tracks.</returns>
    /// <remarks>Fails explicitly for nonexistent configured columns, fixed-column overflow, or invalid bounds graphs.</remarks>
    public TableSize Measure()
    {
        ThrowIfDisposed();
        return CalculateLayout().Size;
    }

    /// <summary>Applies measured dimensions and top-left cell positions while preserving the table's own origin.</summary>
    /// <remarks>
    /// Clears child offsets, margins, and sibling-relative positioning. Preserves each preferred footprint,
    /// marks native bounds dirty, and resets coalesced layout notifications without calculating world bounds.
    /// </remarks>
    public void Arrange()
    {
        ThrowIfDisposed();
        var layout = CalculateLayout();
        tableBounds.fixedWidth = layout.Size.Width;
        tableBounds.fixedHeight = layout.Size.Height;
        double y = 0;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            double x = 0;
            for (int columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                var entry = rows[rowIndex][columnIndex];
                var bounds = entry.Bounds;
                bounds.fixedX = x;
                bounds.fixedY = y;
                // Native fixed dimensions exclude padding; preferred footprints already include it.
                bounds.fixedWidth = entry.Size.Width - 2 * entry.PaddingX;
                bounds.fixedHeight = entry.Size.Height - 2 * entry.PaddingY;
                bounds.fixedOffsetX = bounds.fixedOffsetY = 0;
                bounds.fixedMarginX = bounds.fixedMarginY = 0;
                bounds.absMarginX = bounds.absMarginY = 0;
                bounds.LeftOfBounds = null;
                bounds.Alignment = EnumDialogArea.FixedTop;
                x += layout.ColumnWidths[columnIndex] + options.ColumnGap;
            }
            y += layout.RowHeights[rowIndex] + options.RowGap;
        }
        tableBounds.MarkDirtyRecursive();
        invalidationNotified = false;
    }

    /// <summary>Finds an owned child by its case-sensitive, table-local key.</summary>
    /// <param name="key">The nonblank key supplied in its cell descriptor.</param>
    /// <returns>A non-owning reference to the child.</returns>
    /// <exception cref="KeyNotFoundException">No child has the specified key.</exception>
    public GuiElement GetElement(string key)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return keyedElements[key];
    }

    /// <summary>Changes an owned child's reserved footprint and coalesces notification of layout invalidation.</summary>
    /// <param name="element">A child already accepted by this table.</param>
    /// <param name="size">Finite, nonnegative logical dimensions including all fixed child padding.</param>
    /// <remarks>Does not immediately change bounds. Completed-column constraints are checked by measurement.</remarks>
    public void SetPreferredSize(GuiElement element, TableSize size)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(element);
        size.Validate(nameof(size));
        if (!elements.TryGetValue(element, out var entry))
            throw new ArgumentException("The element is not owned by this table.", nameof(element));
        ValidateFootprint(size, entry.PaddingX, entry.PaddingY);
        if (entry.Size == size) return;
        entry.Size = size;
        NotifyLayoutInvalidated();
    }

    /// <summary>Freezes row structure after the future composer helper establishes registration ownership.</summary>
    /// <remarks>This internal state transition performs no native registration and allocates no resources.</remarks>
    internal void MarkRegistered()
    {
        ThrowIfDisposed();
        if (IsRegistered) throw new InvalidOperationException("The table is already registered.");
        IsRegistered = true;
    }

    /// <summary>Cancels configured child interaction, releases every owned child, and detaches their root bounds once.</summary>
    /// <remarks>Repeated calls do nothing, including after the initial cleanup reports failures.</remarks>
    /// <exception cref="AggregateException">One or more cleanup operations failed; all children were still processed.</exception>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? errors = null;
        foreach (var entry in rows.SelectMany(row => row))
        {
            if (entry.Cell.CancelInput is { } cancel) Cleanup(cancel);
            if (entry.Cell.Element.HasFocus) Cleanup(entry.Cell.Element.OnFocusLost);
            Cleanup(entry.Cell.Element.Dispose);
            if (ReferenceEquals(entry.Bounds.ParentBounds, tableBounds)) entry.Bounds.ParentBounds = null;
            tableBounds.ChildBounds.Remove(entry.Bounds);
        }
        rows.Clear();
        elements.Clear();
        keyedElements.Clear();
        LayoutInvalidated = null;
        base.Dispose();
        if (errors is not null) throw new AggregateException("Table child cleanup failed.", errors);

        // Release every child even when one cleanup operation fails, then surface all failures.
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
    }

    /// <summary>Stages a row using temporary identity sets without changing parentage or accepting ownership.</summary>
    private CellEntry[] PrepareRow(IEnumerable<TableCell> cells, HashSet<GuiElement> knownElements,
        HashSet<string> knownKeys, HashSet<ElementBounds> knownBounds)
    {
        var pending = new List<CellEntry>();
        foreach (var cell in cells)
        {
            ArgumentNullException.ThrowIfNull(cell, nameof(cells));
            if (!knownElements.Add(cell.Element))
                throw new ArgumentException("An element may only occur once in a table.", nameof(cells));
            if (cell.Key is not null && !knownKeys.Add(cell.Key))
                throw new ArgumentException($"Duplicate table child key '{cell.Key}'.", nameof(cells));
            var bounds = cell.Element.Bounds;
            ArgumentNullException.ThrowIfNull(bounds, nameof(cells));
            if (bounds.ParentBounds is not null)
                throw new ArgumentException("A table child must not already have a parent.", nameof(cells));
            ValidateCellSizing(cell.Element);
            CollectBounds(bounds, null, knownBounds);
            var size = cell.PreferredSize ?? new TableSize(
                bounds.fixedWidth + 2 * bounds.fixedPaddingX,
                bounds.fixedHeight + 2 * bounds.fixedPaddingY);
            size.Validate(nameof(cells));
            ValidateFootprint(size, bounds.fixedPaddingX, bounds.fixedPaddingY);
            if (options.Columns.TryGetValue(pending.Count, out var column) && column.IsFixed && size.Width > column.Width)
                throw new ArgumentException("Cell content exceeds its fixed column width.", nameof(cells));
            pending.Add(new CellEntry(cell, size));
        }
        if (pending.Count == 0) throw new ArgumentException("Table rows must not be empty.", nameof(cells));
        return pending.ToArray();
    }

    private void AcceptRow(CellEntry[] row)
    {
        foreach (var entry in row)
        {
            elements.Add(entry.Cell.Element, entry);
            if (entry.Cell.Key is not null) keyedElements.Add(entry.Cell.Key, entry.Cell.Element);
            tableBounds.WithChild(entry.Bounds);
        }
        rows.Add(row);
    }

    private Layout CalculateLayout()
    {
        ValidateTableBounds();
        CollectOwnedBounds();
        return CalculateLayout(rows);
    }

    /// <summary>Measures either accepted rows or a fully staged constructor input before ownership commits.</summary>
    private Layout CalculateLayout(IReadOnlyList<CellEntry[]> layoutRows)
    {
        int columnCount = layoutRows.Count == 0 ? 0 : layoutRows.Max(row => row.Length);
        ValidateColumnIndices(columnCount);
        var columnWidths = new double[columnCount];
        foreach (var (index, column) in options.Columns) columnWidths[index] = column.Width;
        var rowHeights = new double[layoutRows.Count];
        for (int rowIndex = 0; rowIndex < layoutRows.Count; rowIndex++)
        {
            rowHeights[rowIndex] = options.MinimumRowHeight;
            for (int columnIndex = 0; columnIndex < layoutRows[rowIndex].Length; columnIndex++)
            {
                var entry = layoutRows[rowIndex][columnIndex];
                ValidateCellSizing(entry.Cell.Element);
                if (entry.Bounds.fixedPaddingX != entry.PaddingX || entry.Bounds.fixedPaddingY != entry.PaddingY)
                    throw new InvalidOperationException("Owned cell padding must not be changed.");
                ValidateFootprint(entry.Size, entry.PaddingX, entry.PaddingY);
                if (options.Columns.TryGetValue(columnIndex, out var column) && column.IsFixed)
                {
                    if (entry.Size.Width > column.Width)
                        throw new ArgumentException("Cell content exceeds its fixed column width.");
                }
                else columnWidths[columnIndex] = Math.Max(columnWidths[columnIndex], entry.Size.Width);
                rowHeights[rowIndex] = Math.Max(rowHeights[rowIndex], entry.Size.Height);
            }
        }
        var size = new TableSize(
            columnWidths.Sum() + options.ColumnGap * Math.Max(0, columnCount - 1),
            rowHeights.Sum() + options.RowGap * Math.Max(0, layoutRows.Count - 1));
        size.Validate(nameof(size));
        return new Layout(columnWidths, rowHeights, size);
    }

    private void ValidateColumnIndices(int columnCount)
    {
        if (options.Columns.Keys.Any(index => index >= columnCount))
            throw new ArgumentException("Column configuration refers to a nonexistent column.", nameof(options));
    }

    /// <summary>Checks the accepted bounds graph and collects identities that new children must not reuse.</summary>
    private HashSet<ElementBounds> CollectOwnedBounds()
    {
        ValidateTableBounds();
        var seen = new HashSet<ElementBounds>(ReferenceEqualityComparer.Instance);
        // Ancestors also belong to the exclusion set: accepting one as a descendant would create a cycle.
        for (var ancestor = tableBounds; ancestor is not null; ancestor = ancestor.ParentBounds)
            if (!seen.Add(ancestor)) throw new ArgumentException("Table bounds contain a parent cycle.");
        if (tableBounds.ChildBounds.Count != elements.Count)
            throw new InvalidOperationException("The table's child bounds were modified externally.");
        foreach (var entry in elements.Values)
        {
            if (!ReferenceEquals(entry.Cell.Element.Bounds, entry.Bounds) || !tableBounds.ChildBounds.Contains(entry.Bounds))
                throw new InvalidOperationException("Owned element bounds were replaced or detached.");
            CollectBounds(entry.Bounds, tableBounds, seen);
        }
        return seen;
    }

    private static void CollectBounds(ElementBounds root, ElementBounds? parent, HashSet<ElementBounds> seen)
    {
        var pending = new Stack<(ElementBounds Bounds, ElementBounds? Parent)>();
        pending.Push((root, parent));
        while (pending.TryPop(out var item))
        {
            if (!seen.Add(item.Bounds))
                throw new ArgumentException("Child bounds contain a cycle or a shared bounds instance.");
            if (!ReferenceEquals(item.Bounds.ParentBounds, item.Parent))
                throw new ArgumentException("Child bounds have conflicting parentage.");
            foreach (var child in item.Bounds.ChildBounds)
            {
                ArgumentNullException.ThrowIfNull(child);
                pending.Push((child, item.Bounds));
            }
        }
    }

    private void ValidateTableBounds()
    {
        if (!ReferenceEquals(Bounds, tableBounds))
            throw new InvalidOperationException("Table bounds must not be replaced.");
        ValidateFixedBounds(tableBounds);
        if (tableBounds.fixedPaddingX != 0 || tableBounds.fixedPaddingY != 0)
            throw new ArgumentException("Use cell padding or an enclosing layout instead of table padding.");
    }

    private static void ValidateCellSizing(GuiElement element)
    {
        ValidateFixedBounds(element.Bounds);
        if (element is GuiElementDynamicText { autoHeight: true })
            throw new ArgumentException("Automatically sized text requires explicit fixed sizing before it can be table content.");
    }

    private static void ValidateFixedBounds(ElementBounds bounds)
    {
        if (bounds.horizontalSizing != ElementSizing.Fixed || bounds.verticalSizing != ElementSizing.Fixed
            || bounds.percentPaddingX != 0 || bounds.percentPaddingY != 0)
            throw new ArgumentException("Table layout requires fixed sizing and fixed padding.");
        TableSize.ValidateDimension(bounds.fixedWidth, nameof(bounds.fixedWidth));
        TableSize.ValidateDimension(bounds.fixedHeight, nameof(bounds.fixedHeight));
        TableSize.ValidateDimension(bounds.fixedPaddingX, nameof(bounds.fixedPaddingX));
        TableSize.ValidateDimension(bounds.fixedPaddingY, nameof(bounds.fixedPaddingY));
    }

    private static void ValidateFootprint(TableSize size, double paddingX, double paddingY)
    {
        if (size.Width < 2 * paddingX || size.Height < 2 * paddingY)
            throw new ArgumentException("A preferred footprint must include all fixed cell padding.", nameof(size));
    }

    private void NotifyLayoutInvalidated()
    {
        if (invalidationNotified) return;
        invalidationNotified = true;
        LayoutInvalidated?.Invoke();
    }

    private void EnsureMutable()
    {
        ThrowIfDisposed();
        if (IsRegistered) throw new InvalidOperationException("Registered table rows cannot be changed.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
