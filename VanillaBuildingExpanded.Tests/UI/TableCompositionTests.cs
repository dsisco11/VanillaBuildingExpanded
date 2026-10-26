using System.Reflection;
using Cairo;
using Moq;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

[CollectionDefinition("Table composition", DisableParallelization = true)]
public sealed class TableCompositionCollection;

[Collection("Table composition")]
public sealed class TableCompositionTests
{
    [Fact]
    public void Registration_SettlesLayoutAndKeepsChildrenOutOfBothComposerMaps()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var table = fixture.Table();
        var child = fixture.Cell("child", 12, 8);
        table.AddRowCells([new TableCell(child, key: "child")]);

        Assert.True(composer.TryAddTable(table, "table"));

        Assert.True(table.IsRegistered);
        Assert.False(table.Focusable);
        Assert.Same(table, composer.GetElement("table"));
        Assert.Null(composer.GetElement("child"));
        Assert.Same(child, table.GetElement("child"));
        Assert.Same(composer.Bounds, table.Bounds.ParentBounds);
        Assert.Equal((12d, 8d), (table.Bounds.fixedWidth, table.Bounds.fixedHeight));
        Assert.Throws<InvalidOperationException>(() => table.AddRow(fixture.Cell("extra")));
        Assert.Empty(fixture.Uploads);
        Assert.Equal(0, child.BeforeCalls);

        composer.Compose(false);
        Assert.Equal(1, child.BeforeCalls);
        Assert.Equal(1, child.ComposeCalls);
        Assert.Equal(2, fixture.Uploads.Count); // Table texture and outer composer's texture.
        Assert.Null(composer.GetElement("child"));
        composer.Dispose();
        Assert.Equal(1, child.DisposeCalls);
        Assert.Single(fixture.Deleted, id => id == fixture.Uploads[0].Id);
    }

    [Fact]
    public void ConditionalSkip_RetainsCallerOwnershipAndDoesNotFreezeRows()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        composer.AddIf(false);
        Assert.False(composer.TryAddTable(table, "table"));
        composer.EndIf();
        composer.Dispose();

        Assert.False(table.IsRegistered);
        Assert.Null(table.Bounds.ParentBounds);
        Assert.Null(composer.GetElement("table"));
        Assert.Equal(0, child.DisposeCalls);
        table.AddRow(fixture.Cell("extra"));
        using var nextComposer = fixture.Composer();
        Assert.True(nextComposer.TryAddTable(table, "table"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void InvalidKey_IsRejectedWithoutTransferringOwnership(string key)
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        using var table = fixture.Table();
        Assert.Throws<ArgumentException>(() => composer.TryAddTable(table, key));
        Assert.False(table.IsRegistered);
        Assert.Null(table.Bounds.ParentBounds);
    }

    [Fact]
    public void DuplicateKeysComposedStateAndRepeatedRegistration_AreExplicitFailures()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        using var table = fixture.Table();
        using var collision = fixture.Table();
        Assert.True(composer.TryAddTable(table, "table"));
        Assert.Throws<ArgumentException>(() => composer.TryAddTable(collision, "table"));
        Assert.False(collision.IsRegistered);
        Assert.Throws<InvalidOperationException>(() => composer.TryAddTable(table, "another"));
        composer.Composed = true;
        Assert.Throws<InvalidOperationException>(() => composer.TryAddTable(collision, "another"));
        Assert.Null(collision.Bounds.ParentBounds);
    }

    [Fact]
    public void InvalidLayoutAndParentConflicts_FailBeforeNativeInsertion()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        using var invalid = fixture.Table(new TableOptions(new Dictionary<int, TableColumn>
        {
            [1] = TableColumn.Auto()
        }));
        invalid.AddRow(fixture.Cell("invalid"));
        Assert.Throws<ArgumentException>(() => composer.TryAddTable(invalid, "invalid"));
        Assert.Null(composer.GetElement("invalid"));

        using var conflict = fixture.Table();
        conflict.Bounds.WithParent(ElementBounds.Fixed(0, 0, 200, 200));
        Assert.Throws<InvalidOperationException>(() => composer.TryAddTable(conflict, "conflict"));
        Assert.Null(composer.GetElement("conflict"));
        using var cycle = fixture.Table();
        using var cyclicComposer = fixture.Composer(cycle.Bounds);
        Assert.Throws<InvalidOperationException>(() => cyclicComposer.TryAddTable(cycle, "cycle"));
        Assert.Null(cyclicComposer.GetElement("cycle"));
        Assert.False(invalid.IsRegistered || conflict.IsRegistered || cycle.IsRegistered);
    }

    [Fact]
    public void DisposedTableAndDifferentClientApi_AreRejectedBeforeInsertion()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        using var table = fixture.Table();
        table.Dispose();
        Assert.Throws<ObjectDisposedException>(() => composer.TryAddTable(table, "disposed"));
        using var other = new GuiElementTable(Mock.Of<ICoreClientAPI>(), ElementBounds.Fixed(0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => composer.TryAddTable(other, "other"));
        Assert.Null(composer.GetElement("other"));
    }

    [Fact]
    public void PartialNativeInsertion_FreezesTableAndLeavesCleanupWithFailedComposer()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var unrelated = fixture.Cell("unrelated");
        composer.AddStaticElement(unrelated, "unrelated");
        var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        child.ThrowOnClip = true;

        Assert.Throws<InvalidOperationException>(() => composer.TryAddTable(table, "table"));

        Assert.True(table.IsRegistered);
        Assert.Same(table, composer.GetElement("table"));
        Assert.Null(table.Bounds.ParentBounds);
        Assert.Equal(0, child.DisposeCalls);
        Assert.Equal(0, unrelated.DisposeCalls);
        Assert.Throws<InvalidOperationException>(() => table.AddRow(fixture.Cell("extra")));
        composer.Dispose();
        Assert.Equal(1, child.DisposeCalls);
        Assert.Equal(1, unrelated.DisposeCalls);
    }

    [Fact]
    public void ClipConventionFailureDuringAddition_DoesNotTransferAnyRowOwnership()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        using var first = fixture.Cell("first");
        using var second = fixture.Cell("second");
        second.ThrowOnClip = true;
        Assert.Throws<InvalidOperationException>(() => table.AddRow(first, second));
        Assert.Null(first.Bounds.ParentBounds);
        Assert.Null(second.Bounds.ParentBounds);
        Assert.Empty(table.Bounds.ChildBounds);
        table.Dispose();
        Assert.Equal(0, first.DisposeCalls);
        Assert.Equal(0, second.DisposeCalls);
    }

    [Fact]
    public void Composition_UsesLocalCoordinatesAndStableOrderWithoutTouchingOuterArtwork()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var first = fixture.Cell("first", 5, 4, drawOrder: 1);
        var second = fixture.Cell("second", 6, 4, drawOrder: -1);
        var third = fixture.Cell("third", 7, 4, drawOrder: 1);
        table.AddRow(first, second, third);
        table.DividerRenderer = new RecordingDivider(fixture.Events);
        table.Bounds.fixedX = 40;
        table.Bounds.fixedY = 50;
        fixture.Attach(table);
        table.BeforeCalcBounds();
        using var outer = new ImageSurface(Format.Argb32, 100, 100);
        using var context = new Context(outer);
        table.ComposeElements(context, outer);

        Assert.True(table.Bounds.IsDrawingSurface);
        Assert.Equal((0d, 0d), first.DrawPosition);
        Assert.Equal((5d, 0d), second.DrawPosition);
        Assert.Equal((11d, 0d), third.DrawPosition);
        Assert.Equal((40d, 50d), (first.Bounds.renderX, first.Bounds.renderY));
        Assert.Equal(["before:first", "before:second", "before:third", "divider",
            "compose:second", "compose:first", "compose:third", "upload"], fixture.Events);
        outer.Flush();
        Assert.All(outer.Data, value => Assert.Equal((byte)0, value));
        Assert.False(table.IsArtworkDirty);

        fixture.Events.Clear();
        table.RenderInteractiveElements(0.1f);
        table.PostRenderInteractiveElements(0.1f);
        Assert.Equal(["push", "texture-premul", "render:second", "render:first", "render:third", "pop",
            "push", "post:second", "post:first", "post:third", "pop"], fixture.Events);
        Assert.Single(fixture.Uploads);
    }

    [Fact]
    public void RendererReplacementAndStaticInvalidations_CoalesceWithoutReflowOrOuterRecomposition()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        composer.TryAddTable(table, "table");
        composer.Compose(false);
        int layoutInvalidations = 0;
        table.LayoutInvalidated += () => layoutInvalidations++;
        var originalTexture = fixture.Uploads[0].Texture;
        var renderer = new RecordingDivider(fixture.Events);
        table.DividerRenderer = renderer;
        table.InvalidateArtwork();
        table.InvalidateArtwork();
        Assert.Equal(2, fixture.Uploads.Count);
        Assert.Equal(1, child.ComposeCalls);

        table.RenderInteractiveElements(0.1f);
        table.RenderInteractiveElements(0.1f);

        Assert.Equal(3, fixture.Uploads.Count);
        Assert.Same(originalTexture, fixture.Uploads[2].Texture);
        Assert.Equal(2, child.ComposeCalls);
        Assert.Equal(2, child.BeforeCalls);
        Assert.Equal(1, renderer.Calls);
        Assert.Equal(0, layoutInvalidations);
        Assert.True(composer.Composed);
        Assert.False(table.IsArtworkDirty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void BuiltInStyleReplacement_PreservesShortRowLayoutDynamicValuesAndTextureOwnership(float scale)
    {
        using var fixture = new Fixture(scale);
        using var table = fixture.Table(new TableOptions(columnGap: 4, rowGap: 4));
        var children = new[]
        {
            fixture.Cell("first", 16, 12),
            fixture.Cell("second", 24, 10),
            fixture.Cell("third", 16, 18)
        };
        table.AddRow(children[0], children[1]);
        table.AddRow(children[2]);
        fixture.Attach(table);
        var origins = children.Select(child => (child.Bounds.fixedX, child.Bounds.fixedY)).ToArray();
        int layoutInvalidations = 0;
        table.LayoutInvalidated += () => layoutInvalidations++;
        var options = new TableDividerOptions(new Color(1, 1, 1, 1));
        ITableDividerRenderer[] renderers =
        [
            NoTableDividerRenderer.Instance,
            new SolidTableDividerRenderer(options),
            new DashedTableDividerRenderer(options, [3, 3]),
            NoTableDividerRenderer.Instance
        ];
        int[] coverage = new int[renderers.Length];

        for (int index = 0; index < renderers.Length; index++)
        {
            children[0].Value = index;
            table.DividerRenderer = renderers[index];
            table.RenderInteractiveElements(0.1f);
            table.RenderInteractiveElements(0.1f);

            Assert.Equal(index + 1, fixture.Uploads.Count);
            var upload = fixture.Uploads[^1];
            Assert.Same(fixture.Uploads[0].Texture, upload.Texture);
            coverage[index] = upload.Pixels.Count(value => value != 0);
            Assert.Equal(index, children[0].RenderedValue);
            Assert.Equal(new TableSize(44, 34), table.Measure());
            Assert.Equal(origins, children.Select(child => (child.Bounds.fixedX, child.Bounds.fixedY)));
            Assert.All(children, child => Assert.Same(table.Bounds, child.Bounds.ParentBounds));
            Assert.False(table.IsArtworkDirty);
        }

        Assert.Equal(0, coverage[0]);
        Assert.True(coverage[1] > coverage[2]);
        Assert.True(coverage[2] > 0);
        Assert.Equal(0, coverage[3]);
        Assert.Equal(0, layoutInvalidations);
        Assert.All(children, child => Assert.Equal(4, child.ComposeCalls));
        table.Dispose();
        Assert.All(children, child => Assert.Equal(1, child.DisposeCalls));
        Assert.Single(fixture.Deleted);
    }

    [Fact]
    public void DynamicUpdates_FittingReservedBoundsDoNotRecomposeOrReflow()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        child.Value = 42;
        table.RenderInteractiveElements(0.1f);
        Assert.Equal(42, child.RenderedValue);
        Assert.Equal(1, child.ComposeCalls);
        Assert.Equal(1, child.BeforeCalls);
        Assert.Single(fixture.Uploads);
        Assert.False(table.IsArtworkDirty);
    }

    [Fact]
    public void NativeComposer_RenderReadsHoveredChildCursorWithoutMouseMoveOrRecomposition()
    {
        using var fixture = new Fixture();
        var input = new Mock<IInputAPI>();
        int mouseX = 1;
        input.SetupGet(api => api.MouseX).Returns(() => mouseX);
        input.SetupGet(api => api.MouseY).Returns(1);
        fixture.Api.SetupGet(api => api.Input).Returns(input.Object);
        using var composer = fixture.Composer();
        var table = fixture.Table();
        var child = fixture.Cell("child");
        child.CursorToRender = "linkselect";
        table.AddRowCells([new TableCell(child, input: TableInput.Pointer, cancelInput: () => { })]);
        composer.TryAddTable(table, "table");
        composer.Compose(false);
        composer.Render(0.1f);
        Assert.Equal("linkselect", composer.MouseOverCursor);
        mouseX = 100;
        composer.Render(0.1f);
        Assert.Null(composer.MouseOverCursor);
        Assert.Null(table.MouseOverCursor);
        Assert.Equal(1, child.ComposeCalls);
        Assert.Equal(2, fixture.Uploads.Count);
    }

    [Fact]
    public void NativeStaticAndDynamicText_ComposeAndUpdateThroughTheOwnedLifecycle()
    {
        using var fixture = new Fixture();
        using var font = CairoFont.WhiteSmallText();
        font.SetupContext(CairoFont.FontMeasuringContext);
        using var table = fixture.Table();
        var label = new GuiElementStaticText(fixture.Api.Object, "Static", EnumTextOrientation.Left,
            ElementBounds.Fixed(0, 0, 80, 30), font);
        var value = new GuiElementDynamicText(fixture.Api.Object, "1", font,
            ElementBounds.Fixed(0, 0, 80, 30));
        table.AddRow(label, value);
        fixture.Attach(table);
        int invalidations = 0;
        table.LayoutInvalidated += () => invalidations++;
        table.RenderInteractiveElements(0.1f);

        Assert.Equal(2, fixture.Uploads.Count);
        var dynamicUpload = fixture.Uploads[0];
        var tableUpload = fixture.Uploads[1];
        Assert.Equal((160, 30), (tableUpload.Width, tableUpload.Height));
        Assert.Contains(Enumerable.Range(0, tableUpload.Height).SelectMany(y =>
            Enumerable.Range(0, 80).Select(x => tableUpload.Pixels[y * tableUpload.Stride + x * 4 + 3])),
            alpha => alpha != 0);
        Assert.All(Enumerable.Range(0, tableUpload.Height).SelectMany(y =>
            Enumerable.Range(80, 80).Select(x => tableUpload.Pixels[y * tableUpload.Stride + x * 4 + 3])),
            alpha => Assert.Equal((byte)0, alpha));

        value.SetNewText("2");
        table.RenderInteractiveElements(0.1f);

        Assert.Equal(3, fixture.Uploads.Count);
        Assert.Equal(dynamicUpload.Id, fixture.Uploads[2].Id);
        Assert.Equal(new TableSize(160, 30), table.Measure());
        Assert.Equal(0, invalidations);
        Assert.False(table.IsArtworkDirty);
        Assert.Equal(2, fixture.Render.Invocations.Count(call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha) &&
            Equals(call.Arguments[0], dynamicUpload.Id)));
        Assert.Equal(2, fixture.Render.Invocations.Count(call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha) &&
            Equals(call.Arguments[0], tableUpload.Id)));
        table.Dispose();
        Assert.Equal(2, fixture.Deleted.Count);
    }

    [Fact]
    public void ChildPreparation_RetainsReservedSizesBeforeFitToChildrenComposerAllocation()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer(ElementBounds.Fixed(0, 0, 0, 0)
            .WithSizing(ElementSizing.FitToChildren).WithEmptyParent());
        var table = fixture.Table();
        var child = fixture.Cell("child", 12, 8);
        child.Before = () => child.Bounds.fixedWidth = child.Bounds.fixedHeight = 500;
        table.AddRow(child);
        composer.TryAddTable(table, "table");
        composer.Compose(false);
        Assert.Equal((12d, 8d), (child.Bounds.fixedWidth, child.Bounds.fixedHeight));
        Assert.Equal((12d, 8d), (composer.Bounds.OuterWidth, composer.Bounds.OuterHeight));
        Assert.Equal((12, 8), (fixture.Uploads[1].Width, fixture.Uploads[1].Height));
        Assert.Equal(1, child.BeforeCalls);
        Assert.Equal(1, child.ComposeCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void FractionalExtents_RoundOutwardsWithoutRescalingTexturePixels(double scale)
    {
        using var fixture = new Fixture((float)scale);
        using var table = fixture.Table();
        var child = fixture.Cell("child", 3.5, 2.5);
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        var upload = Assert.Single(fixture.Uploads);
        Assert.Equal((int)Math.Ceiling(3.5 * scale), upload.Width);
        Assert.Equal((int)Math.Ceiling(2.5 * scale), upload.Height);
        Assert.Equal(3.5 * scale, child.Bounds.OuterWidth);
        var render = Assert.Single(fixture.Render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha));
        Assert.Equal((double)upload.Width, render.Arguments[3]);
        Assert.Equal((double)upload.Height, render.Arguments[4]);
    }

    [Fact]
    public void ScaleChange_RepreparesBoundsAndReusesTheTextureReference()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child", 8, 6);
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        RuntimeEnv.GUIScale = 1.5f;
        table.RenderInteractiveElements(0.1f);
        Assert.Equal(2, fixture.Uploads.Count);
        Assert.Same(fixture.Uploads[0].Texture, fixture.Uploads[1].Texture);
        Assert.Equal((12, 9), (fixture.Uploads[1].Width, fixture.Uploads[1].Height));
        Assert.Equal((12d, 9d), (child.Bounds.OuterWidth, child.Bounds.OuterHeight));
    }

    [Theory]
    [InlineData(true, 64)]
    [InlineData(false, 128)]
    public void AlphaConvention_IsPropagatedAndUploadMatchesSelectedBlendMode(bool premultiplied, int expectedRed)
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        composer.PremultipliedAlpha(premultiplied);
        var table = fixture.Table();
        var child = fixture.Cell("child", 4, 4);
        child.Paint = (context, _) =>
        {
            context.SetSourceRGBA(0.5, 0, 0, 0.5);
            context.Paint();
        };
        table.AddRow(child);
        composer.TryAddTable(table, "table");
        table.RenderInteractiveElements(0.1f);
        Assert.Equal(premultiplied, child.RenderAsPremultipliedAlpha);
        Assert.Equal(premultiplied, child.AlphaAtComposition);
        var pixel = fixture.Uploads[0].Pixels;
        Assert.InRange((int)pixel[2], expectedRed - 1, expectedRed + 1);
        Assert.InRange((int)pixel[3], 127, 129);
        Assert.Contains(premultiplied ? "texture-premul" : "texture-straight", fixture.Events);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, 64)]
    [InlineData(0.5, 128)]
    [InlineData(1, 255)]
    public void StraightAlphaConversion_PreservesEveryColorChannelAndTransparency(double alpha, int expectedAlpha)
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        table.RenderAsPremultipliedAlpha = false;
        var child = fixture.Cell("child", 2, 2);
        child.Paint = (context, _) =>
        {
            context.SetSourceRGBA(0.25, 0.5, 1, alpha);
            context.Paint();
        };
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        var pixel = fixture.Uploads[0].Pixels;
        Assert.Equal((byte)expectedAlpha, pixel[3]);
        Assert.InRange((int)pixel[2], alpha == 0 ? 0 : 63, alpha == 0 ? 0 : 65);
        Assert.InRange((int)pixel[1], alpha == 0 ? 0 : 127, alpha == 0 ? 0 : 129);
        Assert.Equal((byte)(alpha == 0 ? 0 : 255), pixel[0]);
    }

    [Fact]
    public void ClipAndAlphaConventions_ApplyOnAdditionAndAlphaChangesRebuildOnce()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var clip = ElementBounds.Fixed(0, 0, 100, 100);
        table.InsideClipBounds = clip;
        table.RenderAsPremultipliedAlpha = false;
        var child = fixture.Cell("child");
        table.AddRow(child);
        Assert.Same(clip, child.InsideClipBounds);
        Assert.False(child.RenderAsPremultipliedAlpha);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        table.RenderAsPremultipliedAlpha = true;
        table.RenderInteractiveElements(0.1f);
        table.RenderInteractiveElements(0.1f);
        Assert.True(child.RenderAsPremultipliedAlpha);
        Assert.Equal(2, fixture.Uploads.Count);
        Assert.Same(fixture.Uploads[0].Texture, fixture.Uploads[1].Texture);
        Assert.False(table.IsArtworkDirty);
    }

    [Fact]
    public void StaticArtwork_IsClippedAndChildrenPaintAfterDividers()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child", 3.5, 3.5);
        table.AddRow(child);
        table.DividerRenderer = new RecordingDivider(fixture.Events, (context, _) =>
        {
            context.SetSourceRGBA(1, 0, 0, 1);
            context.Paint();
        });
        child.Paint = (context, _) =>
        {
            context.SetSourceRGBA(0, 1, 0, 1);
            context.Rectangle(1, 1, 1, 1);
            context.Fill();
        };
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        var upload = fixture.Uploads[0];
        Assert.Equal((byte)255, upload.Pixels[2]);
        int green = upload.Stride + 4;
        Assert.Equal((byte)255, upload.Pixels[green + 1]);
        Assert.Equal((byte)0, upload.Pixels[green + 2]);
        int corner = 3 * upload.Stride + 3 * 4;
        Assert.InRange((int)upload.Pixels[corner + 3], 60, 68);
    }

    [Fact]
    public void ClipUpdates_ReachExistingChildrenAndBothRenderStagesRestoreScissorsOnFailure()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        var clip = ElementBounds.Fixed(0, 0, 100, 100);
        table.InsideClipBounds = clip;
        Assert.Same(clip, child.InsideClipBounds);
        fixture.Events.Clear();
        child.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => table.RenderInteractiveElements(0.1f));
        Assert.Equal(["push", "push", "texture-premul", "render:child", "pop", "pop"], fixture.Events);
        fixture.Events.Clear();
        child.ThrowOnPostRender = true;
        Assert.Throws<InvalidOperationException>(() => table.PostRenderInteractiveElements(0.1f));
        Assert.Equal(["push", "push", "post:child", "pop", "pop"], fixture.Events);
        Assert.Single(fixture.Uploads);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void ZeroExtent_SkipsAllocationCompositionAndDynamicRendering(double width, double height)
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child", width, height);
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        table.PostRenderInteractiveElements(0.1f);
        Assert.Empty(fixture.Uploads);
        Assert.Equal(0, child.ComposeCalls);
        Assert.DoesNotContain("render:child", fixture.Events);
        Assert.DoesNotContain("post:child", fixture.Events);
        Assert.False(table.IsArtworkDirty);
    }

    [Fact]
    public void EmptyTable_SkipsTextureAndZeroResizeReleasesExistingTextureOnce()
    {
        using var fixture = new Fixture();
        using var empty = fixture.Table();
        fixture.Attach(empty);
        empty.RenderInteractiveElements(0.1f);
        Assert.Empty(fixture.Uploads);
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        table.SetPreferredSize(child, new TableSize(0, 10));
        table.Arrange();
        table.RenderInteractiveElements(0.1f);
        table.RenderInteractiveElements(0.1f);
        Assert.Single(fixture.Uploads);
        Assert.Single(fixture.Deleted);
        table.Dispose();
        Assert.Single(fixture.Deleted);
        Assert.Equal(1, child.DisposeCalls);
    }

    [Fact]
    public void PendingLayout_IsNeverSilentlySettledByRenderingOrComposition()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        table.SetPreferredSize(child, new TableSize(20, 10));
        Assert.Throws<InvalidOperationException>(() => table.RenderInteractiveElements(0.1f));
        Assert.Throws<InvalidOperationException>(() => table.PostRenderInteractiveElements(0.1f));
        Assert.Equal(10, table.Bounds.fixedWidth);
        Assert.Single(fixture.Uploads);
        table.Arrange();
        table.RenderInteractiveElements(0.1f);
        Assert.Equal(20, fixture.Uploads[1].Width);
    }

    [Fact]
    public void InvalidationDuringChildComposition_RemainsPendingForNextFrame()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        fixture.Attach(table);
        child.Paint = (_, _) => table.InvalidateArtwork();
        table.RenderInteractiveElements(0.1f);
        Assert.True(table.IsArtworkDirty);
        Assert.Single(fixture.Uploads);
        child.Paint = null;
        table.RenderInteractiveElements(0.1f);
        Assert.Equal(2, fixture.Uploads.Count);
        Assert.False(table.IsArtworkDirty);
    }

    [Fact]
    public void ChildCompositionFailure_DoesNotUploadOrClearDirtyStateAndCanBeRetried()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        fixture.Attach(table);
        child.Paint = (_, _) => throw new InvalidOperationException("composition failed");
        Assert.Throws<InvalidOperationException>(() => table.RenderInteractiveElements(0.1f));
        Assert.Empty(fixture.Uploads);
        Assert.True(table.IsArtworkDirty);
        child.Paint = null;
        table.RenderInteractiveElements(0.1f);
        Assert.Single(fixture.Uploads);
        Assert.False(table.IsArtworkDirty);
    }

    [Fact]
    public void FailedUpload_PreservesOwnershipOfPartiallyAllocatedTexture()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        table.AddRow(fixture.Cell("child"));
        fixture.Attach(table);
        fixture.ThrowAfterUpload = true;
        Assert.Throws<InvalidOperationException>(() => table.RenderInteractiveElements(0.1f));
        Assert.True(table.IsArtworkDirty);
        table.Dispose();
        Assert.Single(fixture.Deleted);
    }

    [Fact]
    public void CleanupFailure_StillReleasesTextureAndAllOtherChildrenOnce()
    {
        using var fixture = new Fixture();
        var table = fixture.Table();
        var first = fixture.Cell("first");
        var second = fixture.Cell("second");
        first.ThrowOnDispose = true;
        table.AddRow(first, second);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        Assert.Throws<AggregateException>(() => table.Dispose());
        table.Dispose();
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
        Assert.Single(fixture.Deleted);
        Assert.Empty(table.Bounds.ChildBounds);
    }

    [Fact]
    public void PostDisposalCallbacks_AreNoOpsAndMouseDownNeverUsesConsumingBaseFallback()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRow(child);
        fixture.Attach(table);
        table.RenderInteractiveElements(0.1f);
        var mouse = new MouseEvent(1, 1, EnumMouseButton.Left, 0);
        table.OnMouseDown(fixture.Api.Object, mouse);
        Assert.False(mouse.Handled);
        table.Dispose();
        fixture.Events.Clear();
        table.BeforeCalcBounds();
        table.ComposeElements(null!, null!);
        table.RenderInteractiveElements(0.1f);
        table.PostRenderInteractiveElements(0.1f);
        table.InsideClipBounds = ElementBounds.Fixed(0, 0, 20, 20);
        table.OnMouseDown(fixture.Api.Object, mouse);
        Assert.Empty(fixture.Events);
        Assert.False(mouse.Handled);
        Assert.Single(fixture.Deleted);
        Assert.Equal(1, child.DisposeCalls);
    }

    private sealed record UploadSnapshot(LoadedTexture Texture, int Id, int Width, int Height, int Stride, byte[] Pixels);

    private delegate void UploadCallback(ImageSurface surface, bool linearMag, ref LoadedTexture texture);

    private sealed class Fixture : IDisposable
    {
        private const string TestLocale = "vbe-table-composition-tests";
        private readonly float previousScale = RuntimeEnv.GUIScale;
        private readonly string previousLocale = Lang.CurrentLocale;
        private int nextTextureId = 40;
        public Mock<ICoreClientAPI> Api { get; } = new();
        public Mock<IGuiAPI> Gui { get; } = new();
        public Mock<IRenderAPI> Render { get; } = new();
        public List<string> Events { get; } = [];
        public List<UploadSnapshot> Uploads { get; } = [];
        public List<int> Deleted { get; } = [];
        public bool ThrowAfterUpload { get; set; }

        public Fixture(float scale = 1)
        {
            RuntimeEnv.GUIScale = scale;
            var translations = new Mock<ITranslationService>();
            translations.SetupGet(service => service.LineBreakBehavior).Returns(EnumLinebreakBehavior.AfterWord);
            Lang.AvailableLanguages.Add(TestLocale, translations.Object);
            Lang.ChangeLanguage(TestLocale);
            Api.SetupGet(api => api.Gui).Returns(Gui.Object);
            Api.SetupGet(api => api.Render).Returns(Render.Object);
            Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), true,
                ref It.Ref<LoadedTexture>.IsAny)).Callback(new UploadCallback(Upload));
            Gui.Setup(gui => gui.DeleteTexture(It.IsAny<int>())).Callback<int>(Deleted.Add);
            Render.Setup(render => render.PushScissor(It.IsAny<ElementBounds>(), true))
                .Callback(() => Events.Add("push"));
            Render.Setup(render => render.PopScissor()).Callback(() => Events.Add("pop"));
            Render.Setup(render => render.Render2DTexturePremultipliedAlpha(It.IsAny<int>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
                .Callback(() => Events.Add("texture-premul"));
            Render.Setup(render => render.Render2DTexture(It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
                .Callback(() => Events.Add("texture-straight"));
        }

        public GuiElementTable Table(TableOptions? options = null) =>
            new(Api.Object, ElementBounds.Fixed(0, 0, 0, 0), options);

        public Probe Cell(string name, double width = 10, double height = 10, double drawOrder = 0) =>
            new(Api.Object, ElementBounds.Fixed(0, 0, width, height), name, Events, drawOrder);

        public void Attach(GuiElementTable table)
        {
            var parent = ElementBounds.Fixed(0, 0, 200, 200).WithEmptyParent();
            parent.WithChild(table.Bounds);
            table.Arrange();
            parent.CalcWorldBounds();
        }

        public GuiComposer Composer(ElementBounds? bounds = null) =>
            Assert.IsType<GuiComposer>(Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                [Api.Object, bounds ?? ElementBounds.Fixed(0, 0, 200, 200).WithEmptyParent(), "table-tests"],
                culture: null));

        private void Upload(ImageSurface surface, bool linearMag, ref LoadedTexture texture)
        {
            surface.Flush();
            if (texture.TextureId == 0) texture.TextureId = ++nextTextureId;
            texture.Width = surface.Width;
            texture.Height = surface.Height;
            Uploads.Add(new UploadSnapshot(texture, texture.TextureId, surface.Width, surface.Height,
                surface.Stride, surface.Data.ToArray()));
            Events.Add("upload");
            if (ThrowAfterUpload) throw new InvalidOperationException("Upload failed after allocation.");
        }

        public void Dispose()
        {
            RuntimeEnv.GUIScale = previousScale;
            Lang.ChangeLanguage(previousLocale);
            Lang.AvailableLanguages.Remove(TestLocale);
        }
    }

    private sealed class Probe(ICoreClientAPI api, ElementBounds bounds, string name,
        List<string> events, double drawOrder) : GuiElement(api, bounds)
    {
        public int BeforeCalls { get; private set; }
        public int ComposeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int Value { get; set; }
        public int RenderedValue { get; private set; }
        public bool AlphaAtComposition { get; private set; }
        public bool ThrowOnClip { get; set; }
        public bool ThrowOnRender { get; set; }
        public bool ThrowOnPostRender { get; set; }
        public bool ThrowOnDispose { get; set; }
        public string? CursorToRender { get; set; }
        public (double X, double Y) DrawPosition { get; private set; }
        public Action? Before { get; set; }
        public Action<Context, ImageSurface>? Paint { get; set; }
        public override double DrawOrder => drawOrder;

        public override ElementBounds? InsideClipBounds
        {
            get => base.InsideClipBounds;
            set
            {
                if (ThrowOnClip) throw new InvalidOperationException("Clip update failed.");
                base.InsideClipBounds = value;
            }
        }

        public override void BeforeCalcBounds()
        {
            BeforeCalls++;
            events.Add($"before:{name}");
            Before?.Invoke();
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            ComposeCalls++;
            DrawPosition = (Bounds.drawX, Bounds.drawY);
            AlphaAtComposition = RenderAsPremultipliedAlpha;
            events.Add($"compose:{name}");
            Paint?.Invoke(ctx, surface);
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            RenderedValue = Value;
            MouseOverCursor = CursorToRender;
            events.Add($"render:{name}");
            if (ThrowOnRender) throw new InvalidOperationException("Render failed.");
        }

        public override void PostRenderInteractiveElements(float deltaTime)
        {
            events.Add($"post:{name}");
            if (ThrowOnPostRender) throw new InvalidOperationException("Post-render failed.");
        }

        public override void Dispose()
        {
            DisposeCalls++;
            if (ThrowOnDispose) throw new InvalidOperationException("Cleanup failed.");
        }
    }

    private sealed class RecordingDivider(List<string> events,
        Action<Context, TableDividerGeometry>? paint = null) : ITableDividerRenderer
    {
        public int Calls { get; private set; }

        public void Draw(Context context, TableDividerGeometry geometry)
        {
            Calls++;
            events.Add("divider");
            paint?.Invoke(context, geometry);
        }
    }
}
