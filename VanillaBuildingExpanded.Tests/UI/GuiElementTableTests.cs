using Moq;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed class GuiElementTableTests
{
    private readonly ICoreClientAPI api = Mock.Of<ICoreClientAPI>();

    [Fact]
    public void PlannedFixture_MeasuresAndArrangesExactDimensions()
    {
        using var table = CreateTable(new TableOptions(new Dictionary<int, TableColumn>
        {
            [0] = TableColumn.Auto(88),
            [1] = TableColumn.Auto(200)
        }, columnGap: 12, rowGap: 4, minimumRowHeight: 20));
        var first = CreateCell(40, 12);
        var second = CreateCell(100, 16);
        var third = CreateCell(90, 30);
        table.AddRow(first, second);
        table.AddRow(third);
        table.Bounds.fixedX = 50;
        table.Bounds.fixedY = 60;

        Assert.Equal(new TableSize(302, 54), table.Measure());
        Assert.Equal(0, table.Bounds.fixedWidth);
        Assert.Null(first.Bounds.ParentBounds?.ParentBounds);
        table.Arrange();

        Assert.Equal((302d, 54d), (table.Bounds.fixedWidth, table.Bounds.fixedHeight));
        Assert.Equal((50d, 60d), (table.Bounds.fixedX, table.Bounds.fixedY));
        AssertPosition(first, 0, 0);
        AssertPosition(second, 102, 0);
        AssertPosition(third, 0, 24);
        Assert.Equal((40d, 12d), (first.Bounds.fixedWidth, first.Bounds.fixedHeight));
        Assert.Equal(3, table.Bounds.ChildBounds.Count);
    }

    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(2, 23, 10)]
    [InlineData(3, 36, 10)]
    public void Columns_AreInferredAndGapsHaveNoTrailingSpace(int count, double width, double height)
    {
        using var table = CreateTable(new TableOptions(columnGap: 3, rowGap: 7));
        var cells = Enumerable.Range(0, count).Select(_ => CreateCell(10, 10)).ToArray();
        table.AddRow(cells);
        table.Arrange();

        Assert.Equal(new TableSize(width, height), table.Measure());
        for (int column = 0; column < count; column++) AssertPosition(cells[column], column * 13, 0);
    }

    [Fact]
    public void ShortRows_StayInTheirRowsAndDoNotSpanOrCreatePlaceholders()
    {
        using var table = CreateTable(new TableOptions(columnGap: 2, rowGap: 3));
        var first = CreateCell(10, 5);
        var second = CreateCell(20, 7);
        var third = CreateCell(30, 9);
        var last = CreateCell(40, 4);
        table.AddRow(first, second, third);
        table.AddRow(last);
        table.Arrange();

        Assert.Equal(new TableSize(94, 16), table.Measure());
        AssertPosition(second, 42, 0);
        AssertPosition(third, 64, 0);
        AssertPosition(last, 0, 12);
        Assert.Equal(40, last.Bounds.fixedWidth);
        Assert.Equal(4, table.Bounds.ChildBounds.Count);
    }

    [Fact]
    public void EmptyTable_HasNoPhantomRowsColumnsOrGaps()
    {
        using var table = CreateTable(new TableOptions(columnGap: 10, rowGap: 10, minimumRowHeight: 20));
        Assert.Equal(new TableSize(0, 0), table.Measure());
        table.Arrange();
        Assert.Equal((0d, 0d), (table.Bounds.fixedWidth, table.Bounds.fixedHeight));
    }

    [Fact]
    public void RowAndOptionsCollections_AreSnapshotted()
    {
        var columns = new Dictionary<int, TableColumn> { [0] = TableColumn.Fixed(25) };
        using var table = CreateTable(new TableOptions(columns));
        var first = CreateCell(10, 10);
        var ignored = CreateCell(200, 200);
        var row = new List<GuiElement> { first };
        table.AddRow(row);
        row[0] = ignored;
        columns[0] = TableColumn.Fixed(500);

        Assert.Equal(new TableSize(25, 10), table.Measure());
        Assert.Same(table.Bounds, first.Bounds.ParentBounds);
        Assert.Null(ignored.Bounds.ParentBounds);
        table.Dispose();
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, ignored.DisposeCount);
    }

    [Fact]
    public void EnumerableRows_AreEnumeratedOnce()
    {
        using var table = CreateTable();
        int enumerations = 0;
        table.AddRow(Enumerate());
        table.Measure();
        table.Arrange();
        table.Measure();
        Assert.Equal(1, enumerations);

        IEnumerable<GuiElement> Enumerate()
        {
            enumerations++;
            yield return CreateCell(10, 10);
        }
    }

    [Fact]
    public void ExplicitFootprint_IncludesPaddingExactlyOnce()
    {
        using var table = CreateTable();
        var first = CreateCell(10, 6);
        first.Bounds.fixedPaddingX = 2;
        first.Bounds.fixedPaddingY = 3;
        var second = CreateCell(5, 5);
        table.AddRowCells([
            new TableCell(first, "padded", new TableSize(24, 18)),
            new TableCell(second)
        ]);
        table.Arrange();

        Assert.Equal(new TableSize(29, 18), table.Measure());
        Assert.Equal((20d, 12d), (first.Bounds.fixedWidth, first.Bounds.fixedHeight));
        AssertPosition(second, 24, 0);
        Assert.Same(first, table.GetElement("padded"));
    }

    [Fact]
    public void DefaultFootprint_IncludesNativePadding()
    {
        using var table = CreateTable();
        var cell = CreateCell(10, 6);
        cell.Bounds.fixedPaddingX = 2;
        cell.Bounds.fixedPaddingY = 3;
        table.AddRow(cell);
        Assert.Equal(new TableSize(14, 12), table.Measure());
        table.Arrange();
        Assert.Equal((10d, 6d), (cell.Bounds.fixedWidth, cell.Bounds.fixedHeight));
    }

    [Fact]
    public void RepeatedLayout_PreservesPreferredSizesAndClearsOldPositioning()
    {
        using var table = CreateTable();
        var cell = CreateCell(10, 20);
        cell.Bounds.fixedX = 400;
        cell.Bounds.fixedY = -200;
        cell.Bounds.fixedOffsetX = 50;
        cell.Bounds.fixedMarginY = 60;
        cell.Bounds.Alignment = EnumDialogArea.CenterMiddle;
        cell.Bounds.LeftOfBounds = ElementBounds.Fixed(0, 0, 100, 100);
        table.AddRow(cell);
        table.Arrange();
        cell.Bounds.fixedWidth = 900;
        cell.Bounds.fixedHeight = 800;
        table.Arrange();
        table.Arrange();

        Assert.Equal(new TableSize(10, 20), table.Measure());
        Assert.Equal((10d, 20d), (cell.Bounds.fixedWidth, cell.Bounds.fixedHeight));
        AssertPosition(cell, 0, 0);
        Assert.Equal(0, cell.Bounds.fixedOffsetX);
        Assert.Equal(0, cell.Bounds.fixedMarginY);
        Assert.Equal(EnumDialogArea.FixedTop, cell.Bounds.Alignment);
        Assert.Null(cell.Bounds.LeftOfBounds);
        Assert.False(table.Bounds.Initialized);
        Assert.False(cell.Bounds.Initialized);
    }

    [Fact]
    public void Measure_DoesNotChangeElementBoundsOrInvokeNativeSizing()
    {
        using var table = CreateTable();
        var cell = CreateCell(10, 20);
        cell.Bounds.fixedX = 123;
        cell.Bounds.Initialized = true;
        table.AddRow(cell);
        Assert.Equal(new TableSize(10, 20), table.Measure());
        Assert.Equal(123, cell.Bounds.fixedX);
        Assert.True(cell.Bounds.Initialized);
        Assert.Equal(0, cell.BeforeCalcCount);
    }

    [Fact]
    public void Lookup_IsOrdinalAndRejectsUnknownKeys()
    {
        using var table = CreateTable();
        var upper = CreateCell(1, 1);
        var lower = CreateCell(1, 1);
        table.AddRowCells([new TableCell(upper, "Value"), new TableCell(lower, "value")]);
        Assert.Same(upper, table.GetElement("Value"));
        Assert.Same(lower, table.GetElement("value"));
        Assert.Throws<KeyNotFoundException>(() => table.GetElement("unknown"));
        Assert.Throws<ArgumentException>(() => table.GetElement(" "));
    }

    [Fact]
    public void FailedRow_DoesNotTransferEarlierElementsInThatRow()
    {
        using var table = CreateTable();
        var existing = CreateCell(1, 1);
        table.AddRowCells([new TableCell(existing, "existing")]);
        var rejected = CreateCell(2, 2);
        Assert.Throws<ArgumentException>(() => table.AddRowCells([
            new TableCell(rejected, "new"),
            new TableCell(CreateCell(3, 3), "existing")
        ]));
        Assert.Null(rejected.Bounds.ParentBounds);
        Assert.Single(table.Bounds.ChildBounds);
        Assert.Throws<KeyNotFoundException>(() => table.GetElement("new"));
        table.Dispose();
        Assert.Equal(0, rejected.DisposeCount);
        Assert.Equal(1, existing.DisposeCount);
    }

    [Fact]
    public void ThrowingRowEnumeration_IsAtomic()
    {
        using var table = CreateTable();
        var rejected = CreateCell(1, 1);
        Assert.Throws<InvalidOperationException>(() => table.AddRow(ThrowAfter(rejected)));
        Assert.Empty(table.Bounds.ChildBounds);
        Assert.Null(rejected.Bounds.ParentBounds);
        table.Dispose();
        Assert.Equal(0, rejected.DisposeCount);
    }

    [Fact]
    public void CompleteConstructor_AcceptsAllRowsInOrder()
    {
        var first = CreateCell(10, 20);
        var second = CreateCell(30, 40);
        using var table = new GuiElementTable(api, ElementBounds.Fixed(0, 0, 0, 0), new TableOptions(),
            new[] { new GuiElement[] { first }, new GuiElement[] { second } });
        table.Arrange();
        Assert.Equal(new TableSize(30, 60), table.Measure());
        AssertPosition(second, 0, 20);
    }

    [Fact]
    public void CompleteConstructor_InvalidLaterRowTransfersNothing()
    {
        var first = CreateCell(10, 10);
        var rejectedBounds = ElementBounds.Fixed(0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => new GuiElementTable(api, rejectedBounds, new TableOptions(),
            new[] { new GuiElement[] { first }, Array.Empty<GuiElement>() }));
        Assert.Null(first.Bounds.ParentBounds);
        Assert.Empty(rejectedBounds.ChildBounds);
        Assert.Equal(0, first.DisposeCount);
    }

    [Fact]
    public void CompleteConstructor_LaterEnumerationFailureTransfersNothing()
    {
        var first = CreateCell(10, 10);
        var second = CreateCell(20, 20);
        Assert.Throws<InvalidOperationException>(() => new GuiElementTable(api, ElementBounds.Fixed(0, 0, 0, 0),
            new TableOptions(), new[] { new GuiElement[] { first }, ThrowAfter(second) }));
        Assert.Null(first.Bounds.ParentBounds);
        Assert.Null(second.Bounds.ParentBounds);
        Assert.Equal(0, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
    }

    [Fact]
    public void CompleteConstructor_InvalidColumnConfigurationTransfersNothing()
    {
        var cell = CreateCell(1, 1);
        var options = new TableOptions(new Dictionary<int, TableColumn> { [1] = TableColumn.Auto() });
        Assert.Throws<ArgumentException>(() => new GuiElementTable(api, ElementBounds.Fixed(0, 0, 0, 0),
            options, new[] { new GuiElement[] { cell } }));
        Assert.Null(cell.Bounds.ParentBounds);
        Assert.Equal(0, cell.DisposeCount);
    }

    [Fact]
    public void CompleteConstructor_TotalOverflowTransfersNothing()
    {
        var first = CreateCell(double.MaxValue, 1);
        var second = CreateCell(double.MaxValue, 1);
        var bounds = ElementBounds.Fixed(0, 0, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GuiElementTable(api, bounds, new TableOptions(),
            new[] { new GuiElement[] { first, second } }));
        Assert.Empty(bounds.ChildBounds);
        Assert.Null(first.Bounds.ParentBounds);
        Assert.Null(second.Bounds.ParentBounds);
        Assert.Equal(0, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
    }

    [Fact]
    public void DuplicateElementsOrBounds_AreRejectedWithoutOwnershipTransfer()
    {
        using var table = CreateTable();
        var cell = CreateCell(1, 1);
        Assert.Throws<ArgumentException>(() => table.AddRow(cell, cell));
        var alias = new ProbeElement(api, cell.Bounds);
        Assert.Throws<ArgumentException>(() => table.AddRow(cell, alias));
        Assert.Empty(table.Bounds.ChildBounds);
        Assert.Null(cell.Bounds.ParentBounds);
    }

    [Fact]
    public void DuplicateAlreadyOwnedElement_RemainsWithOriginalOwner()
    {
        using var table = CreateTable();
        var cell = CreateCell(1, 1);
        table.AddRow(cell);
        Assert.Throws<ArgumentException>(() => table.AddRow(cell));
        Assert.Same(table.Bounds, cell.Bounds.ParentBounds);
        table.Dispose();
        Assert.Equal(1, cell.DisposeCount);
    }

    [Fact]
    public void ConflictingParentAndBoundsCycles_AreRejected()
    {
        using var table = CreateTable();
        var attached = CreateCell(1, 1);
        ElementBounds.Fixed(0, 0, 10, 10).WithChild(attached.Bounds);
        Assert.Throws<ArgumentException>(() => table.AddRow(attached));
        Assert.Throws<ArgumentException>(() => table.AddRow(new ProbeElement(api, table.Bounds)));

        var cyclic = CreateCell(1, 1);
        cyclic.Bounds.ChildBounds.Add(cyclic.Bounds);
        Assert.Throws<ArgumentException>(() => table.AddRow(cyclic));
        Assert.Empty(table.Bounds.ChildBounds);
    }

    [Fact]
    public void SharedDescendantBounds_AreRejected()
    {
        using var table = CreateTable();
        var first = CreateCell(1, 1);
        var second = CreateCell(1, 1);
        var descendant = ElementBounds.Fixed(0, 0, 1, 1);
        first.Bounds.WithChild(descendant);
        second.Bounds.ChildBounds.Add(descendant);
        Assert.Throws<ArgumentException>(() => table.AddRow(first, second));
        Assert.Null(first.Bounds.ParentBounds);
        Assert.Empty(table.Bounds.ChildBounds);
    }

    [Fact]
    public void ValidNestedChildBounds_ArePreserved()
    {
        using var table = CreateTable();
        var cell = CreateCell(5, 5);
        var descendant = ElementBounds.Fixed(1, 1, 1, 1);
        cell.Bounds.WithChild(descendant);
        table.AddRow(cell);
        table.Arrange();
        Assert.Same(cell.Bounds, descendant.ParentBounds);
        Assert.Single(cell.Bounds.ChildBounds);
    }

    [Fact]
    public void InvalidRows_AreRejected()
    {
        using var table = CreateTable();
        Assert.Throws<ArgumentException>(() => table.AddRow());
        Assert.Throws<ArgumentNullException>(() => table.AddRow((GuiElement[])null!));
        Assert.Throws<ArgumentNullException>(() => table.AddRow(new GuiElement[] { null! }));
        Assert.Throws<ArgumentNullException>(() => table.AddRowCells(new TableCell[] { null! }));
        Assert.Throws<ArgumentException>(() => table.AddRowCells([]));
    }

    [Fact]
    public void InvalidTableBoundsAndNullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new GuiElementTable(null!, ElementBounds.Fixed(0, 0, 0, 0)));
        Assert.Throws<ArgumentNullException>(() => new GuiElementTable(api, null!));
        var bounds = ElementBounds.Fixed(0, 0, 0, 0);
        bounds.fixedPaddingX = 1;
        Assert.Throws<ArgumentException>(() => new GuiElementTable(api, bounds));
        bounds.fixedPaddingX = 0;
        bounds.horizontalSizing = ElementSizing.Percentual;
        Assert.Throws<ArgumentException>(() => new GuiElementTable(api, bounds));
        bounds.horizontalSizing = ElementSizing.Fixed;
        bounds.WithChild(ElementBounds.Fixed(0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => new GuiElementTable(api, bounds));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void InvalidDimensionsAndGaps_FailExplicitly(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableOptions(columnGap: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableOptions(rowGap: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableOptions(minimumRowHeight: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableColumn.Fixed(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableColumn.Auto(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableCell(CreateCell(1, 1), preferredSize: new(invalid, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableCell(CreateCell(1, 1), preferredSize: new(1, invalid)));
        using var table = CreateTable();
        var cell = CreateCell(invalid, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.AddRow(cell));
        cell.Bounds.fixedWidth = 1;
        cell.Bounds.fixedHeight = invalid;
        Assert.Throws<ArgumentOutOfRangeException>(() => table.AddRow(cell));
        cell.Bounds.fixedHeight = 1;
        cell.Bounds.fixedPaddingX = invalid;
        Assert.Throws<ArgumentOutOfRangeException>(() => table.AddRow(cell));
        Assert.Null(cell.Bounds.ParentBounds);
    }

    [Fact]
    public void NonexistentColumnsAndInvalidColumnDefinitions_FailExplicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TableOptions(new Dictionary<int, TableColumn> { [-1] = TableColumn.Auto() }));
        Assert.Throws<ArgumentNullException>(() =>
            new TableOptions(new Dictionary<int, TableColumn> { [0] = null! }));
        using var table = CreateTable(new TableOptions(new Dictionary<int, TableColumn> { [1] = TableColumn.Auto() }));
        table.AddRow(CreateCell(1, 1));
        Assert.Throws<ArgumentException>(() => table.Measure());
        table.AddRow(CreateCell(1, 1), CreateCell(1, 1));
        Assert.Equal(new TableSize(2, 2), table.Measure());
    }

    [Fact]
    public void FixedColumns_KeepTheirWidthAndRejectOverflowAtomically()
    {
        using var table = CreateTable(new TableOptions(new Dictionary<int, TableColumn> { [0] = TableColumn.Fixed(20) }));
        var cell = CreateCell(20, 5);
        table.AddRow(cell);
        var overflow = CreateCell(21, 5);
        Assert.Throws<ArgumentException>(() => table.AddRow(overflow));
        Assert.Null(overflow.Bounds.ParentBounds);
        Assert.Equal(new TableSize(20, 5), table.Measure());
        table.SetPreferredSize(cell, new TableSize(21, 5));
        Assert.Throws<ArgumentException>(() => table.Arrange());
        Assert.Equal(0, table.Bounds.fixedWidth);
    }

    [Fact]
    public void InvalidPreferredFootprint_DoesNotTransferOrUpdateOwnership()
    {
        using var table = CreateTable();
        var cell = CreateCell(10, 10);
        cell.Bounds.fixedPaddingX = 4;
        Assert.Throws<ArgumentException>(() =>
            table.AddRowCells([new TableCell(cell, preferredSize: new TableSize(7, 10))]));
        Assert.Null(cell.Bounds.ParentBounds);
        table.AddRow(cell);
        Assert.Throws<ArgumentException>(() => table.SetPreferredSize(cell, new TableSize(7, 10)));
        Assert.Equal(new TableSize(18, 10), table.Measure());
    }

    [Theory]
    [InlineData(ElementSizing.Percentual)]
    [InlineData(ElementSizing.PercentualSubstractFixed)]
    [InlineData(ElementSizing.FitToChildren)]
    public void NonFixedChildren_AreRejected(ElementSizing sizing)
    {
        using var table = CreateTable();
        var cell = CreateCell(1, 1);
        cell.Bounds.horizontalSizing = sizing;
        Assert.Throws<ArgumentException>(() => table.AddRow(cell));
        cell.Bounds.horizontalSizing = ElementSizing.Fixed;
        cell.Bounds.verticalSizing = sizing;
        Assert.Throws<ArgumentException>(() => table.AddRow(cell));
        Assert.Null(cell.Bounds.ParentBounds);
    }

    [Fact]
    public void PercentagePaddingAndAutomaticTextHeight_AreRejected()
    {
        using var table = CreateTable();
        var cell = CreateCell(1, 1);
        cell.Bounds.percentPaddingX = 0.1;
        Assert.Throws<ArgumentException>(() => table.AddRow(cell));
        using var font = CairoFont.WhiteSmallText();
        font.SetupContext(CairoFont.FontMeasuringContext);
        using var text = new GuiElementDynamicText(api, "text", font, ElementBounds.Fixed(0, 0, 20, 20)) { autoHeight = true };
        Assert.Throws<ArgumentException>(() => table.AddRow(text));
        Assert.Null(text.Bounds.ParentBounds);
    }

    [Fact]
    public void ExternalGraphMutation_IsDetectedBeforeArrangement()
    {
        using var table = CreateTable();
        var cell = CreateCell(1, 1);
        table.AddRow(cell);
        var original = cell.Bounds;
        cell.Bounds = ElementBounds.Fixed(0, 0, 1, 1);
        Assert.Throws<InvalidOperationException>(() => table.Measure());
        cell.Bounds = original;
        original.fixedPaddingX = 1;
        Assert.Throws<InvalidOperationException>(() => table.Measure());
        original.fixedPaddingX = 0;
        original.ParentBounds = null;
        Assert.Throws<ArgumentException>(() => table.Measure());
        original.ParentBounds = table.Bounds;
        table.Bounds.ChildBounds.Clear();
        Assert.Throws<InvalidOperationException>(() => table.Measure());
    }

    [Fact]
    public void FiniteInputsThatOverflowTotalSize_AreRejectedBeforeBoundsChange()
    {
        using var table = CreateTable();
        table.AddRow(CreateCell(double.MaxValue, 1), CreateCell(double.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Arrange());
        Assert.Equal(0, table.Bounds.fixedWidth);
    }

    [Fact]
    public void PreferredSizeUpdates_CoalesceInvalidationUntilArrangement()
    {
        using var table = CreateTable();
        int notifications = 0;
        table.LayoutInvalidated += () => notifications++;
        var cell = CreateCell(10, 10);
        table.AddRow(cell);
        table.AddRow(CreateCell(5, 5));
        Assert.Equal(1, notifications);
        table.Measure();
        Assert.Equal(1, notifications);
        table.Arrange();
        table.SetPreferredSize(cell, new TableSize(30, 20));
        table.SetPreferredSize(cell, new TableSize(40, 30));
        Assert.Equal(2, notifications);
        Assert.Equal(new TableSize(40, 35), table.Measure());
        table.Arrange();
        table.SetPreferredSize(cell, new TableSize(40, 30));
        Assert.Equal(2, notifications);
        Assert.Throws<ArgumentException>(() => table.SetPreferredSize(CreateCell(1, 1), new TableSize(1, 1)));
    }

    [Fact]
    public void RegistrationMarker_FreezesRowsButAllowsPreferredSizeUpdates()
    {
        using var table = CreateTable();
        var cell = CreateCell(10, 10);
        table.AddRow(cell);
        table.Arrange();
        table.MarkRegistered();
        Assert.True(table.IsRegistered);
        Assert.Throws<InvalidOperationException>(() => table.AddRow(CreateCell(1, 1)));
        Assert.Throws<InvalidOperationException>(() => table.AddRowCells([new TableCell(CreateCell(1, 1))]));
        Assert.Throws<InvalidOperationException>(() => table.MarkRegistered());
        table.SetPreferredSize(cell, new TableSize(20, 20));
        Assert.Equal(new TableSize(20, 20), table.Measure());
    }

    [Fact]
    public void CellMetadata_IsPassiveByDefaultAndRequiresExplicitSafePointerCancellation()
    {
        var cell = CreateCell(1, 1);
        Assert.Equal(TableInput.None, new TableCell(cell).Input);
        Assert.Throws<ArgumentException>(() => new TableCell(cell, input: TableInput.Pointer));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableCell(cell, input: (TableInput)4));
        Assert.Throws<ArgumentException>(() => new TableCell(cell, key: " "));
        Assert.Throws<ArgumentNullException>(() => new TableCell(null!));
        using var table = CreateTable();
        table.AddRowCells([new TableCell(cell, input: TableInput.Pointer | TableInput.Keyboard, cancelInput: () => { })]);
    }

    [Fact]
    public void Disposal_ReleasesAcceptedChildrenOnceAndCancelsWithoutActivation()
    {
        var table = CreateTable();
        var cell = CreateCell(1, 1);
        int cancellations = 0;
        table.AddRowCells([new TableCell(cell, "cell", input: TableInput.Pointer, cancelInput: () => cancellations++)]);
        cell.OnFocusGained();
        table.Dispose();
        table.Dispose();

        Assert.Equal(1, cancellations);
        Assert.Equal(1, cell.FocusLostCount);
        Assert.Equal(1, cell.DisposeCount);
        Assert.Null(cell.Bounds.ParentBounds);
        Assert.Empty(table.Bounds.ChildBounds);
        Assert.Throws<ObjectDisposedException>(() => table.AddRow(CreateCell(1, 1)));
        Assert.Throws<ObjectDisposedException>(() => table.SetPreferredSize(cell, new TableSize(2, 2)));
        Assert.Throws<ObjectDisposedException>(() => table.Measure());
        Assert.Throws<ObjectDisposedException>(() => table.Arrange());
        Assert.Throws<ObjectDisposedException>(() => table.GetElement("cell"));
        Assert.Throws<ObjectDisposedException>(() => table.MarkRegistered());
    }

    [Fact]
    public void CleanupFailure_DoesNotLeakOtherChildrenAndIsSurfaced()
    {
        var table = CreateTable();
        var first = CreateCell(1, 1);
        first.ThrowOnDispose = true;
        var second = CreateCell(1, 1);
        table.AddRowCells([
            new TableCell(first, input: TableInput.Pointer, cancelInput: () => throw new InvalidOperationException("cancel")),
            new TableCell(second)
        ]);
        var exception = Assert.Throws<AggregateException>(() => table.Dispose());
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.Empty(table.Bounds.ChildBounds);
        table.Dispose();
        Assert.Equal(1, first.DisposeCount);
    }

    private GuiElementTable CreateTable(TableOptions? options = null) =>
        new(api, ElementBounds.Fixed(0, 0, 0, 0), options);

    private ProbeElement CreateCell(double width, double height) =>
        new(api, ElementBounds.Fixed(0, 0, width, height));

    private static void AssertPosition(GuiElement element, double x, double y) =>
        Assert.Equal((x, y), (element.Bounds.fixedX, element.Bounds.fixedY));

    private static IEnumerable<GuiElement> ThrowAfter(GuiElement first)
    {
        yield return first;
        throw new InvalidOperationException("enumeration");
    }

    private sealed class ProbeElement(ICoreClientAPI api, ElementBounds bounds) : GuiElement(api, bounds)
    {
        public int DisposeCount { get; private set; }
        public int BeforeCalcCount { get; private set; }
        public int FocusLostCount { get; private set; }
        public bool ThrowOnDispose { get; set; }

        public override void BeforeCalcBounds() => BeforeCalcCount++;

        public override void OnFocusLost()
        {
            FocusLostCount++;
            base.OnFocusLost();
        }

        public override void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose) throw new InvalidOperationException("dispose");
        }
    }
}
