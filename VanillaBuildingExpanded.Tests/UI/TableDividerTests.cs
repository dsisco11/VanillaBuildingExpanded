using Cairo;
using Moq;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed class TableDividerTests
{
    private readonly ICoreClientAPI api = Mock.Of<ICoreClientAPI>();

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PlannedFixture_ProducesExactScaledBoundaries(double scale)
    {
        using var table = CreateTable(new TableOptions(new Dictionary<int, TableColumn>
        {
            [0] = TableColumn.Auto(88),
            [1] = TableColumn.Auto(200)
        }, columnGap: 12, rowGap: 4, minimumRowHeight: 20));
        table.AddRow(Cell(40, 12), Cell(100, 16));
        table.AddRow(Cell(90, 30));
        table.Bounds.fixedX = 800;
        table.Bounds.fixedY = 900;
        table.Arrange();

        var geometry = table.GetDividerGeometry(scale);

        Assert.Equal(302 * scale, geometry.Width);
        Assert.Equal(54 * scale, geometry.Height);
        Assert.Equal(scale, geometry.GuiScale);
        Assert.Equal(new[]
        {
            new TableDividerSegment(TableDividerKind.Cell, 96 * scale, 0, 96 * scale, 20 * scale),
            new TableDividerSegment(TableDividerKind.Row, 0, 22 * scale, 302 * scale, 22 * scale)
        }, geometry.Segments);
    }

    [Fact]
    public void ZeroGaps_UseSharedEdgesWithoutTrailingBoundaries()
    {
        using var table = CreateTable();
        table.AddRow(Cell(10, 10), Cell(10, 10));
        table.AddRow(Cell(10, 10), Cell(10, 10));
        table.Arrange();
        Assert.Equal(new[]
        {
            new TableDividerSegment(TableDividerKind.Cell, 10, 0, 10, 10),
            new TableDividerSegment(TableDividerKind.Row, 0, 10, 20, 10),
            new TableDividerSegment(TableDividerKind.Cell, 10, 10, 10, 20)
        }, table.GetDividerGeometry(1).Segments);
    }

    [Fact]
    public void SingleCellAndEmptyTables_HaveNoDividers()
    {
        using var empty = CreateTable();
        empty.Arrange();
        var emptyGeometry = empty.GetDividerGeometry(1.25);
        Assert.Equal(0, emptyGeometry.Width);
        Assert.Equal(0, emptyGeometry.Height);
        Assert.Empty(emptyGeometry.Segments);

        using var single = CreateTable();
        single.AddRow(Cell(10, 10));
        single.Arrange();
        Assert.Empty(single.GetDividerGeometry(1).Segments);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(0, 0)]
    public void ZeroDrawableExtent_ProducesNoSegments(double width, double height)
    {
        using var table = CreateTable();
        table.AddRow(Cell(width, height), Cell(width, height));
        table.AddRow(Cell(width, height));
        table.Arrange();
        Assert.Empty(table.GetDividerGeometry(1).Segments);
    }

    [Fact]
    public void SingleColumn_HasOnlyRowBoundaries()
    {
        using var table = CreateTable(new TableOptions(rowGap: 4));
        table.AddRow(Cell(10, 6));
        table.AddRow(Cell(30, 12));
        table.Arrange();
        Assert.Equal(new[] { new TableDividerSegment(TableDividerKind.Row, 0, 8, 30, 8) },
            table.GetDividerGeometry(1).Segments);
    }

    [Fact]
    public void FractionalTracks_KeepFinalCellBoundariesWithinTheMeasuredExtent()
    {
        using var table = CreateTable(new TableOptions(columnGap: 0.2, rowGap: 0.2));
        for (int row = 0; row < 100; row++) table.AddRow(Cell(0.1, 0.1), Cell(0.1, 0.1));
        table.Arrange();
        var geometry = table.GetDividerGeometry(1.25);
        Assert.Equal(geometry.Height, geometry.Segments.Last().EndY);
        Assert.All(geometry.Segments, segment =>
        {
            Assert.InRange(segment.EndX, 0, geometry.Width);
            Assert.InRange(segment.EndY, 0, geometry.Height);
        });
    }

    [Fact]
    public void GeometryRequiresSettledLayoutAndRemainsAnIndependentSnapshot()
    {
        using var table = CreateTable();
        var first = Cell(10, 10);
        table.AddRow(first, Cell(20, 20));
        Assert.Throws<InvalidOperationException>(() => table.GetDividerGeometry(1));
        table.Arrange();
        var snapshot = table.GetDividerGeometry(1);
        table.SetPreferredSize(first, new TableSize(30, 30));
        Assert.Throws<InvalidOperationException>(() => table.GetDividerGeometry(1));
        table.Measure();
        Assert.Throws<InvalidOperationException>(() => table.GetDividerGeometry(1));
        table.Arrange();
        Assert.Equal(50, table.GetDividerGeometry(1).Width);
        Assert.Equal(30, snapshot.Width);
        Assert.Equal(new TableDividerSegment(TableDividerKind.Cell, 10, 0, 10, 20), Assert.Single(snapshot.Segments));
    }

    [Fact]
    public void Geometry_DefensivelyCopiesItsSegmentCollection()
    {
        var source = new List<TableDividerSegment> { new(TableDividerKind.Row, 0, 8, 32, 8) };
        var geometry = new TableDividerGeometry(32, 16, 1, source);
        source.Clear();
        Assert.Single(geometry.Segments);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<TableDividerSegment>)geometry.Segments).Add(new(TableDividerKind.Row, 0, 4, 32, 4)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidGuiScales_FailExplicitly(double scale)
    {
        using var table = CreateTable();
        table.Arrange();
        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetDividerGeometry(scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableDividerGeometry(10, 10, scale, []));
    }

    [Fact]
    public void DeviceSpaceOverflow_FailsBeforeRendering()
    {
        using var table = CreateTable();
        table.AddRow(Cell(double.MaxValue, 1));
        table.Arrange();
        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetDividerGeometry(2));
    }

    [Theory]
    [InlineData((int)TableDividerKind.None, 0, 1, 10, 1)]
    [InlineData((int)TableDividerKind.All, 0, 1, 10, 1)]
    [InlineData((int)TableDividerKind.Row, 0, 1, 10, 2)]
    [InlineData((int)TableDividerKind.Cell, 1, 0, 2, 10)]
    [InlineData((int)TableDividerKind.Row, 0, 1, 11, 1)]
    [InlineData((int)TableDividerKind.Row, 0, 1, 0, 1)]
    [InlineData((int)TableDividerKind.Cell, 1, 5, 1, 3)]
    [InlineData((int)TableDividerKind.Row, -1, 1, 10, 1)]
    public void MalformedSegments_AreRejected(int kind, double x1, double y1, double x2, double y2)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TableDividerGeometry(10, 10, 1, [new((TableDividerKind)kind, x1, y1, x2, y2)]));
    }

    [Fact]
    public void StrategyInjection_UsesOneInterfaceAndDoesNotChangeLayoutOrOwnership()
    {
        using var table = CreateTable();
        var first = Cell(10, 10);
        table.AddRow(first, Cell(20, 20));
        table.Arrange();
        int invalidations = 0;
        table.LayoutInvalidated += () => invalidations++;
        var before = table.GetDividerGeometry(1.5);
        Assert.Same(NoTableDividerRenderer.Instance, table.DividerRenderer);
        var renderer = new RecordingRenderer();
        table.DividerRenderer = renderer;
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var context = new Context(surface);
        table.DrawDividers(context, 1.5);

        Assert.Equal(1, renderer.Calls);
        Assert.Equal(before.Segments, renderer.LastGeometry!.Segments);
        Assert.Equal(before.Width, renderer.LastGeometry.Width);
        Assert.Equal(0, invalidations);
        Assert.Same(table.Bounds, first.Bounds.ParentBounds);
        Assert.Equal(new TableSize(30, 20), table.Measure());
        Assert.Equal(0, first.Bounds.fixedX);
        Assert.True(table.IsArtworkDirty);
        Assert.Throws<ArgumentNullException>(() => table.DividerRenderer = null!);
    }

    [Fact]
    public void RendererCanBeSharedWithoutItsLifetimeBeingOwnedByTables()
    {
        var renderer = new RecordingRenderer();
        var first = CreateTable();
        using var second = CreateTable();
        first.Arrange();
        second.Arrange();
        first.DividerRenderer = second.DividerRenderer = renderer;
        first.Dispose();
        using var surface = new ImageSurface(Format.Argb32, 32, 32);
        using var context = new Context(surface);
        first.DrawDividers(context, 1);
        second.DrawDividers(context, 1);
        Assert.Equal(1, renderer.Calls);
        Assert.Throws<ObjectDisposedException>(() => first.DividerRenderer = NoTableDividerRenderer.Instance);
        Assert.Throws<ObjectDisposedException>(() => first.GetDividerGeometry(1));
        Assert.Throws<ObjectDisposedException>(() => first.InvalidateArtwork());
    }

    [Fact]
    public void NoOpRenderer_LeavesEveryPixelTransparent()
    {
        var pixels = Render(NoTableDividerRenderer.Instance, CrossGeometry());
        Assert.All(pixels, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData((int)TableDividerKind.None, false, false)]
    [InlineData((int)TableDividerKind.Row, true, false)]
    [InlineData((int)TableDividerKind.Cell, false, true)]
    [InlineData((int)TableDividerKind.All, true, true)]
    public void SolidRenderer_SelectsBoundaryKinds(int kinds, bool horizontal, bool vertical)
    {
        var renderer = new SolidTableDividerRenderer(new TableDividerOptions(new Color(1, 1, 1, 1), 2, (TableDividerKind)kinds));
        var pixels = Render(renderer, CrossGeometry());
        Assert.Equal(horizontal, Alpha(pixels, 4, 16) > 0);
        Assert.Equal(vertical, Alpha(pixels, 16, 4) > 0);
    }

    [Fact]
    public void Intersections_ArePaintedOnceInsteadOfAccumulatingAlpha()
    {
        var renderer = new SolidTableDividerRenderer(new TableDividerOptions(new Color(1, 1, 1, 0.5), 2));
        var pixels = Render(renderer, CrossGeometry());
        Assert.InRange(Alpha(pixels, 4, 16), 127, 128);
        Assert.Equal(Alpha(pixels, 4, 16), Alpha(pixels, 16, 16));
        Assert.Equal(Alpha(pixels, 16, 4), Alpha(pixels, 16, 16));
    }

    [Fact]
    public void DashedRenderer_SnapshotsItsPatternAndDrawsRealGaps()
    {
        var pattern = new[] { 4d, 4d };
        var renderer = new DashedTableDividerRenderer(new TableDividerOptions(new Color(1, 1, 1, 1)), pattern);
        pattern[0] = 100;
        pattern[1] = 1;
        var pixels = Render(renderer, HorizontalGeometry(1));
        Assert.Equal(255, Alpha(pixels, 1, 10));
        Assert.Equal(0, Alpha(pixels, 5, 10));
        Assert.Equal(255, Alpha(pixels, 9, 10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void FadingRows_HaveSymmetricTransparentEndsAndAFaintPlateau(double scale)
    {
        var renderer = new FadingRowTableDividerRenderer(
            new TableDividerOptions(new Color(1, 1, 1, 0.25)), fadeLength: 8);
        var pixels = Render(renderer, HorizontalGeometry(scale));
        int width = (int)(32 * scale);
        int y = (int)Math.Floor(10 * scale - scale / 2 + 0.5);
        Assert.InRange(Alpha(pixels, 0, y), 0, 4);
        Assert.True(Alpha(pixels, (int)(4 * scale), y) > Alpha(pixels, 0, y));
        Assert.True(Alpha(pixels, (int)(8 * scale), y) > Alpha(pixels, (int)(4 * scale), y));
        Assert.InRange(Alpha(pixels, width / 2, y), 63, 64);
        for (int x = 0; x < width; x++)
            Assert.InRange(Math.Abs(Alpha(pixels, x, y) - Alpha(pixels, width - x - 1, y)), 0, 1);
        Assert.Equal(0, Alpha(pixels, width / 2, y - 1));
        if (width < 64) Assert.Equal(0, Alpha(pixels, width, y));
    }

    [Fact]
    public void FadingRows_DrawEveryRowButNoCellOrOuterBorder()
    {
        var geometry = new TableDividerGeometry(32, 32, 1, [
            new(TableDividerKind.Row, 0, 10, 32, 10),
            new(TableDividerKind.Row, 0, 20, 32, 20),
            new(TableDividerKind.Cell, 16, 0, 16, 32)
        ]);
        var pixels = Render(new FadingRowTableDividerRenderer(), geometry);
        Assert.InRange(Alpha(pixels, 15, 10), 35, 39);
        Assert.Equal(Alpha(pixels, 15, 10), Alpha(pixels, 15, 20));
        Assert.Equal(0, Alpha(pixels, 16, 5));
        Assert.Equal(0, Alpha(pixels, 15, 0));
        Assert.Equal(0, Alpha(pixels, 15, 31));
    }

    [Fact]
    public void FadingRows_NarrowTablesMeetAtCenterAndEmptyOrUnselectedTablesDrawNothing()
    {
        var geometry = new TableDividerGeometry(8, 16, 1, [new(TableDividerKind.Row, 0, 10, 8, 10)]);
        var pixels = Render(new FadingRowTableDividerRenderer(), geometry);
        Assert.InRange(Alpha(pixels, 3, 10), 32, 35);
        Assert.Equal(Alpha(pixels, 3, 10), Alpha(pixels, 4, 10));
        Assert.True(Alpha(pixels, 0, 10) < Alpha(pixels, 3, 10));
        Assert.All(Render(new FadingRowTableDividerRenderer(), new TableDividerGeometry(0, 0, 1, [])),
            value => Assert.Equal(0, value));
        Assert.All(Render(new FadingRowTableDividerRenderer(
            new TableDividerOptions(new Color(1, 1, 1, 1), boundaries: TableDividerKind.Cell)), geometry),
            value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-4)]
    public void DashOffset_UsesTheConfiguredPhase(double offset)
    {
        var renderer = new DashedTableDividerRenderer(
            new TableDividerOptions(new Color(1, 1, 1, 1)), [4, 4], offset);
        var pixels = Render(renderer, HorizontalGeometry(1));
        Assert.Equal(0, Alpha(pixels, 1, 10));
        Assert.Equal(255, Alpha(pixels, 5, 10));
    }

    [Fact]
    public void FractionalScale_ScalesCoordinatesStrokeAndDashLengthsExactlyOnce()
    {
        var options = new TableDividerOptions(new Color(1, 1, 1, 1), 2);
        var solid = Render(new SolidTableDividerRenderer(options), HorizontalGeometry(1.5));
        Assert.Equal(0, Alpha(solid, 4, 13));
        Assert.Equal(255, Alpha(solid, 4, 14));
        Assert.Equal(255, Alpha(solid, 4, 15));
        Assert.Equal(255, Alpha(solid, 4, 16));
        Assert.Equal(0, Alpha(solid, 4, 17));

        var dashed = Render(new DashedTableDividerRenderer(options, [4, 4]), HorizontalGeometry(1.5));
        Assert.Equal(255, Alpha(dashed, 4, 15));
        Assert.Equal(0, Alpha(dashed, 8, 15));
        Assert.Equal(255, Alpha(dashed, 13, 15));
    }

    [Fact]
    public void SolidRenderer_OverridesAndRestoresTheCallersDashPattern()
    {
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var context = new Context(surface);
        context.SetDash([1, 5], 0);
        var renderer = new SolidTableDividerRenderer(new TableDividerOptions(new Color(1, 1, 1, 1), 2));
        renderer.Draw(context, HorizontalGeometry(1));
        surface.Flush();
        Assert.Equal(255, Alpha(surface.Data, 4, 10));
        context.NewPath();
        context.SetSourceRGBA(1, 1, 1, 1);
        context.LineWidth = 2;
        context.MoveTo(0, 24);
        context.LineTo(32, 24);
        context.Stroke();
        surface.Flush();
        Assert.Equal(0, Alpha(surface.Data, 4, 24));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Strategies_PreserveSourceOperatorLineWidthTransformClipAndPath(bool fading)
    {
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var context = new Context(surface);
        context.Translate(2, 3);
        context.LineWidth = 7;
        context.Operator = Operator.Source;
        context.SetSourceRGBA(0, 0, 1, 1);
        context.Rectangle(1, 1, 30, 30);
        context.Clip();
        context.Rectangle(20, 20, 4, 4);
        ITableDividerRenderer renderer = fading
            ? new FadingRowTableDividerRenderer(new TableDividerOptions(new Color(1, 0, 0, 0.5)))
            : new DashedTableDividerRenderer(new TableDividerOptions(new Color(1, 0, 0, 0.5)), [4, 4]);
        renderer.Draw(context, HorizontalGeometry(1));

        Assert.Equal(7, context.LineWidth);
        Assert.Equal(Operator.Source, context.Operator);
        Assert.Equal(2, context.Matrix.X0);
        Assert.Equal(3, context.Matrix.Y0);
        Assert.False(context.InClip(0, 0));
        Assert.True(context.InClip(2, 2));
        Assert.True(context.InFill(22, 22));
        Assert.False(context.InFill(5, 5));
        context.Fill();
        surface.Flush();
        int offset = 25 * surface.Stride + 24 * 4;
        Assert.Equal(255, surface.Data[offset]);
        Assert.Equal(0, surface.Data[offset + 2]);
        Assert.Equal(Status.Success, context.Status);
    }

    [Fact]
    public void Table_ClipsCustomStrategiesToItsExtentAndRestoresPaths()
    {
        using var table = CreateTable();
        table.AddRow(Cell(10, 10));
        table.Arrange();
        table.DividerRenderer = new RecordingRenderer((context, _) =>
        {
            context.SetSourceRGBA(1, 1, 1, 1);
            context.Paint();
            context.Translate(50, 50);
            context.LineWidth = 99;
        });
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var context = new Context(surface);
        context.LineWidth = 3;
        context.Rectangle(20, 20, 4, 4);
        table.DrawDividers(context, 1);
        surface.Flush();

        Assert.Equal(255, Alpha(surface.Data, 5, 5));
        Assert.Equal(0, Alpha(surface.Data, 15, 5));
        Assert.Equal(3, context.LineWidth);
        Assert.Equal(0, context.Matrix.X0);
        Assert.True(context.InFill(22, 22));
        Assert.True(context.InClip(15, 5));
    }

    [Fact]
    public void RendererErrors_PropagateAfterRestoringTheTableDrawingState()
    {
        using var table = CreateTable();
        table.Arrange();
        table.DividerRenderer = new RecordingRenderer((context, _) =>
        {
            context.LineWidth = 99;
            context.Translate(50, 50);
            context.NewPath();
            throw new InvalidOperationException("renderer");
        });
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var context = new Context(surface);
        context.LineWidth = 3;
        context.Rectangle(20, 20, 4, 4);
        Assert.Throws<InvalidOperationException>(() => table.DrawDividers(context, 1));
        Assert.Equal(3, context.LineWidth);
        Assert.Equal(0, context.Matrix.X0);
        Assert.True(context.InFill(22, 22));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidLineAndDashDimensions_FailExplicitly(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableDividerOptions(new Color(1, 1, 1, 1), value));
        var options = new TableDividerOptions(new Color(1, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DashedTableDividerRenderer(options, [value, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FadingRowTableDividerRenderer(options, value));
    }

    [Fact]
    public void InvalidColorsSelectionsPatternsAndOffsets_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableDividerOptions(new Color(-1, 0, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableDividerOptions(new Color(0, 2, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableDividerOptions(new Color(0, 0, double.NaN, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableDividerOptions(new Color(0, 0, 0, double.PositiveInfinity)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableDividerOptions(new Color(1, 1, 1, 1), boundaries: (TableDividerKind)4));
        var options = new TableDividerOptions(new Color(1, 1, 1, 1));
        Assert.Throws<ArgumentNullException>(() => new SolidTableDividerRenderer(null!));
        Assert.Throws<ArgumentNullException>(() => new FadingRowTableDividerRenderer(null!));
        Assert.Throws<ArgumentNullException>(() => new DashedTableDividerRenderer(options, null!));
        Assert.Throws<ArgumentException>(() => new DashedTableDividerRenderer(options, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DashedTableDividerRenderer(options, [double.MaxValue, double.MaxValue]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DashedTableDividerRenderer(options, [double.MaxValue]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DashedTableDividerRenderer(options, [1, 1], double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DashedTableDividerRenderer(options, [1, 1], double.PositiveInfinity));
    }

    [Fact]
    public void ScaledStrokeAndDashOverflow_FailWithoutChangingContextState()
    {
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var context = new Context(surface);
        context.LineWidth = 3;
        var geometry = HorizontalGeometry(2);
        var hugeStroke = new SolidTableDividerRenderer(new TableDividerOptions(new Color(1, 1, 1, 1), double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => hugeStroke.Draw(context, geometry));
        var hugeDash = new DashedTableDividerRenderer(new TableDividerOptions(new Color(1, 1, 1, 1)),
            [double.MaxValue / 2, double.MaxValue / 2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => hugeDash.Draw(context, geometry));
        var hugeFade = new FadingRowTableDividerRenderer(new TableDividerOptions(new Color(1, 1, 1, 1)), double.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => hugeFade.Draw(context, geometry));
        Assert.Equal(3, context.LineWidth);
        Assert.Equal(Status.Success, context.Status);
    }

    private GuiElementTable CreateTable(TableOptions? options = null) =>
        new(api, ElementBounds.Fixed(0, 0, 0, 0), options);

    private GuiElement Cell(double width, double height) =>
        new TestElement(api, ElementBounds.Fixed(0, 0, width, height));

    private static TableDividerGeometry CrossGeometry() => new(32, 32, 1, [
        new(TableDividerKind.Row, 0, 16, 32, 16),
        new(TableDividerKind.Cell, 16, 0, 16, 32)
    ]);

    private static TableDividerGeometry HorizontalGeometry(double scale) =>
        new(32 * scale, 16 * scale, scale, [new(TableDividerKind.Row, 0, 10 * scale, 32 * scale, 10 * scale)]);

    private static byte[] Render(ITableDividerRenderer renderer, TableDividerGeometry geometry)
    {
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var context = new Context(surface);
        renderer.Draw(context, geometry);
        surface.Flush();
        return surface.Data;
    }

    private static byte Alpha(byte[] pixels, int x, int y) => pixels[(y * 64 + x) * 4 + 3];

    private sealed class TestElement(ICoreClientAPI api, ElementBounds bounds) : GuiElement(api, bounds);

    private sealed class RecordingRenderer(Action<Context, TableDividerGeometry>? draw = null) : ITableDividerRenderer
    {
        public int Calls { get; private set; }
        public TableDividerGeometry? LastGeometry { get; private set; }

        public void Draw(Context context, TableDividerGeometry geometry)
        {
            Calls++;
            LastGeometry = geometry;
            draw?.Invoke(context, geometry);
        }
    }
}
