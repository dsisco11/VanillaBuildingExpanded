using System.Collections.Immutable;
using System.Reflection;
using Cairo;
using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

[Collection("Table composition")]
public sealed class BuildModeHudControlsTests
{
    [Fact]
    public void ControlsRows_UseTableMinimumsSpacingAndPassiveChildren()
    {
        using var fixture = new Fixture();
        using var table = BuildModeHud.CreateControlsTable(fixture.Api.Object,
            [Hint("A", ["B"]), Hint("C", ["D"])]);
        Assert.Equal(new TableSize(300, 40), table.Measure());
        Assert.Same(NoTableDividerRenderer.Instance, table.DividerRenderer);
        table.Arrange();
        Assert.Equal(4, table.Bounds.ChildBounds.Count);
        Assert.Equal([(0d, 0d), (100d, 0d), (0d, 20d), (100d, 20d)],
            table.Bounds.ChildBounds.Select(bounds => (bounds.fixedX, bounds.fixedY)));
        Assert.False(table.Focusable);
        fixture.Attach(table);
        var mouse = new MouseEvent(101, 1, EnumMouseButton.Left, 0);
        table.OnMouseDown(fixture.Api.Object, mouse);
        Assert.False(mouse.Handled);
        Assert.Empty(fixture.Uploads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.33)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MeasuredDimensions_MatchPreviousColumnMaximaForLongLabelsAndAlternateBindings(float scale)
    {
        using var fixture = new Fixture(scale);
        BuildModeKeybindHint[] hints =
        [
            Hint("A very long translated controls action label", ["Ctrl", "Shift", "Esc"], ["Alt", "Esc"]),
            Hint("A", ["An unusually long translated mouse binding", "Shift"]),
            Hint("Rotate", ["R"])
        ];
        using var table = BuildModeHud.CreateControlsTable(fixture.Api.Object, hints);
        var expected = PreviousSize(hints);
        Assert.Equal(expected, table.Measure());
        table.Arrange();
        var bindingStarts = table.Bounds.ChildBounds.Where((_, index) => index % 2 == 1)
            .Select(bounds => bounds.fixedX).ToArray();
        Assert.All(bindingStarts, x => Assert.Equal(bindingStarts[0], x));
        Assert.True(bindingStarts[0] > 100);
        Assert.Equal(60, table.Measure().Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.33)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void LabelPixels_MatchNativeStaticTextWithoutNewPerCellWrapping(float scale)
    {
        using var fixture = new Fixture(scale);
        BuildModeKeybindHint[] hints =
        [
            Hint("Toggle build mode", ["B"]),
            Hint("Change snapping mode", ["Shift", "R"]),
            Hint("Rotate", ["R"])
        ];
        using var table = BuildModeHud.CreateControlsTable(fixture.Api.Object, hints);
        fixture.Attach(table);
        using var dummy = new ImageSurface(Format.Argb32, 1, 1);
        using var dummyContext = new Context(dummy);
        table.ComposeElements(dummyContext, dummy);
        var uploaded = Assert.Single(fixture.Uploads);
        using var baseline = new ImageSurface(Format.Argb32, uploaded.Width, uploaded.Height);
        using var context = new Context(baseline);
        double columnWidth = table.Bounds.ChildBounds[1].fixedX - 12;
        for (int i = 0; i < hints.Length; i++)
        {
            using var font = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize)
                .WithColor([.. UiPalette.LabelColor]);
            var bounds = ElementBounds.Fixed(0, i * 20, columnWidth, 20).WithEmptyParent();
            bounds.CalcWorldBounds();
            using var text = new GuiElementStaticText(fixture.Api.Object, hints[i].Action,
                EnumTextOrientation.Left, bounds, font);
            text.ComposeElements(context, baseline);
        }
        baseline.Flush();
        var expected = baseline.Data;
        int labelPixels = (int)Math.Ceiling(columnWidth * scale);
        for (int y = 0; y < uploaded.Height; y++)
            for (int x = 0; x < labelPixels; x++)
                for (int channel = 0; channel < 4; channel++)
                    Assert.Equal(expected[y * baseline.Stride + x * 4 + channel],
                        uploaded.Pixels[y * uploaded.Stride + x * 4 + channel]);
        Assert.Contains(uploaded.Pixels, value => value != 0);
    }

    [Fact]
    public void SectionCallbacks_KeepExpandedWidthWhileCollapsedAndRegisterOnlyTheTable()
    {
        using var fixture = new Fixture();
        using var table = BuildModeHud.CreateControlsTable(fixture.Api.Object, [Hint("Action", ["B"])]);
        var section = new CollapsibleSection("controls", "controls-title",
            () => table.Measure().Width, _ => table.Measure().Height, (composer, _) =>
                Assert.True(composer.TryAddTable(table, "controls-table")));
        double width = section.MeasureWidth();
        double expandedHeight = section.MeasureHeight(width);
        section.RequestToggle();
        section.ApplyPendingToggle();
        Assert.Equal(width, section.MeasureWidth());
        Assert.Equal(20, expandedHeight - section.MeasureHeight(width));
        using (var collapsed = fixture.Composer())
        {
            section.Compose(fixture.Api.Object, collapsed, 0, width);
            Assert.Null(collapsed.GetElement("controls-table"));
            Assert.False(table.IsRegistered);
        }
        section.RequestToggle();
        section.ApplyPendingToggle();
        using var expanded = fixture.Composer();
        section.Compose(fixture.Api.Object, expanded, 0, width);
        Assert.Same(table, expanded.GetElement("controls-table"));
        Assert.True(table.IsRegistered);
        Assert.False(table.Focusable);
        Assert.Equal(2, table.Bounds.ChildBounds.Count);
    }

    [Fact]
    public void Hud_CloseReopenAndUnchangedHintsReuseTheLiveTable()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var composer = hud.SingleComposer;
        var table = Assert.IsType<GuiElementTable>(composer.GetElement("controls-table"));
        var texture = Assert.Single(fixture.Uploads, upload => upload.Width == table.Bounds.OuterWidthInt &&
            upload.Height == table.Bounds.OuterHeightInt).Texture;
        Assert.True(hud.IsOpened());
        hud.SetVisible(false);
        Assert.False(texture.Disposed);
        hud.SetVisible(true);
        Assert.Same(composer, hud.SingleComposer);
        Assert.Same(table, hud.SingleComposer.GetElement("controls-table"));
        Assert.Single(fixture.CreatedComposers);
        hud.Dispose();
        Assert.True(texture.Disposed);
        Assert.False(table.Focusable);
        Assert.Empty(table.Bounds.ChildBounds);
        hud.Dispose();
        Assert.All(fixture.Uploads, upload => Assert.Equal(0, upload.Texture.TextureId));
    }

    [Fact]
    public void Hud_RebindingRebuildsAndReleasesTheOldTable()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var previous = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        var childBounds = previous.Bounds.ChildBounds.ToArray();
        fixture.Hotkeys["vbe.ToggleBuildMode"].CurrentMapping.KeyCode = (int)GlKeys.Escape;
        hud.SetVisible(true);
        Assert.Equal(2, fixture.CreatedComposers.Count);
        Assert.NotSame(previous, hud.SingleComposer.GetElement("controls-table"));
        Assert.Empty(previous.Bounds.ChildBounds);
        Assert.All(childBounds, bounds => Assert.Null(bounds.ParentBounds));
    }

    [Fact]
    public void Hud_CollapseExpandMeasuresWithoutKeepingAnUnregisteredTable()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var previous = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        double width = hud.SingleComposer.Bounds.fixedWidth;
        double expandedHeight = hud.SingleComposer.Bounds.fixedHeight;
        ClickToggle(hud, fixture);
        Assert.Null(hud.SingleComposer.GetElement("controls-table"));
        Assert.Empty(previous.Bounds.ChildBounds);
        Assert.Equal(width, hud.SingleComposer.Bounds.fixedWidth);
        Assert.Equal(100, expandedHeight - hud.SingleComposer.Bounds.fixedHeight);
        Assert.Null(PendingTable(hud));
        ClickToggle(hud, fixture);
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        Assert.Equal(width, hud.SingleComposer.Bounds.fixedWidth);
        Assert.Equal(expandedHeight, hud.SingleComposer.Bounds.fixedHeight);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void Hud_RepeatedCollapseReopenAndRebindingReleaseEveryOwnedTextureOnce(float scale)
    {
        using var fixture = new Fixture(scale);
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        double width = hud.SingleComposer.Bounds.fixedWidth;
        double height = hud.SingleComposer.Bounds.fixedHeight;

        for (int cycle = 0; cycle < 8; cycle++)
        {
            var previous = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
            var children = previous.Bounds.ChildBounds.ToArray();
            ClickToggle(hud, fixture);
            Assert.Null(hud.SingleComposer.GetElement("controls-table"));
            Assert.Empty(previous.Bounds.ChildBounds);
            Assert.All(children, bounds => Assert.Null(bounds.ParentBounds));
            Assert.Equal(width, hud.SingleComposer.Bounds.fixedWidth);
            Assert.Equal(height - 100, hud.SingleComposer.Bounds.fixedHeight);
            Assert.Null(PendingTable(hud));

            ClickToggle(hud, fixture);
            var composer = hud.SingleComposer;
            var expanded = Assert.IsType<GuiElementTable>(composer.GetElement("controls-table"));
            Assert.NotSame(previous, expanded);
            Assert.Equal(width, composer.Bounds.fixedWidth);
            Assert.Equal(height, composer.Bounds.fixedHeight);
            int builds = fixture.CreatedComposers.Count;
            hud.SetVisible(false);
            hud.SetVisible(true);
            Assert.Same(composer, hud.SingleComposer);
            Assert.Equal(builds, fixture.CreatedComposers.Count);

            fixture.Hotkeys["vbe.ToggleBuildMode"].CurrentMapping.KeyCode =
                (int)(cycle % 2 == 0 ? GlKeys.Escape : GlKeys.LShift);
            hud.SetVisible(true);
            Assert.NotSame(composer, hud.SingleComposer);
            Assert.Empty(expanded.Bounds.ChildBounds);
            Assert.Null(PendingTable(hud));
        }

        hud.Dispose();
        hud.Dispose();
        var textures = fixture.Uploads.Select(upload => upload.Texture).Distinct().ToArray();
        Assert.All(textures, texture => Assert.True(texture.Disposed));
        Assert.All(textures, texture => Assert.Equal(0, texture.TextureId));
        Assert.Equal(textures.Length, fixture.Deleted.Count);
        Assert.Equal(fixture.Deleted.Count, fixture.Deleted.Distinct().Count());
        Assert.All(fixture.CreatedComposers, composer =>
        {
            if (composer.GetElement("controls-table") is GuiElementTable table)
                Assert.Empty(table.Bounds.ChildBounds);
        });
    }

    [Fact]
    public void Hud_ScaleChangesRebuildVisibleContentAndDeferHiddenRebuilds()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var previous = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        RuntimeEnv.GUIScale = 1.25f;
        fixture.ScaleWatcher!(1.25f);
        Assert.Empty(previous.Bounds.ChildBounds);
        Assert.Equal(2, fixture.CreatedComposers.Count);
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        hud.SetVisible(false);
        RuntimeEnv.GUIScale = 1.5f;
        fixture.ScaleWatcher(1.5f);
        Assert.Null(hud.SingleComposer);
        Assert.Equal(2, fixture.CreatedComposers.Count);
        hud.SetVisible(true);
        Assert.Equal(3, fixture.CreatedComposers.Count);
        Assert.Null(PendingTable(hud));
    }

    [Fact]
    public void Hud_CompositionFailureReleasesRegisteredTableAndDoesNotPublishFailedComposer()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        fixture.FailUpload = true;
        Assert.Throws<InvalidOperationException>(() => hud.SetVisible(true));
        var failed = Assert.Single(fixture.CreatedComposers);
        var table = Assert.IsType<GuiElementTable>(failed.GetElement("controls-table"));
        Assert.Empty(table.Bounds.ChildBounds);
        Assert.Null(hud.SingleComposer);
        Assert.Null(PendingTable(hud));
        Assert.False(hud.IsOpened());
        fixture.FailUpload = false;
        hud.SetVisible(true);
        Assert.Equal(2, fixture.CreatedComposers.Count);
        Assert.True(hud.IsOpened());
    }

    [Fact]
    public void Hud_CollapsedCompositionFailureAlsoDisposesItsMeasurementOnlyTable()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        ClickToggle(hud, fixture);
        hud.ClearComposers();
        GuiElementTable? pending = null;
        fixture.OnCreate = () => pending = PendingTable(hud);
        fixture.FailUpload = true;
        Assert.Throws<InvalidOperationException>(() => hud.SetVisible(true));
        Assert.NotNull(pending);
        Assert.False(pending.IsRegistered);
        Assert.Empty(pending.Bounds.ChildBounds);
        Assert.Null(PendingTable(hud));
    }

    [Fact]
    public void Hud_ControlsRemainInputTransparentAndOnlyToggleButtonsReceiveFreeCursorClicks()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var table = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        fixture.MouseX = (int)table.Bounds.renderX + 1;
        fixture.MouseY = (int)table.Bounds.renderY + 1;
        Assert.False(hud.Focusable);
        Assert.False(hud.ShouldReceiveKeyboardEvents());
        Assert.False(hud.ShouldReceiveMouseEvents());
        var mouse = new MouseEvent(fixture.MouseX, fixture.MouseY, EnumMouseButton.Left, 0);
        hud.OnMouseDown(mouse);
        hud.OnMouseMove(mouse);
        hud.OnMouseUp(mouse);
        Assert.False(mouse.Handled);
        var wheel = new MouseWheelEventArgs();
        hud.OnMouseWheel(wheel);
        Assert.False(wheel.IsHandled);
        var toggle = hud.SingleComposer.GetElement("controls-toggle");
        fixture.MouseX = (int)toggle.Bounds.renderX + 1;
        fixture.MouseY = (int)toggle.Bounds.renderY + 1;
        Assert.True(hud.ShouldReceiveMouseEvents());
        fixture.MouseGrabbed = true;
        Assert.False(hud.ShouldReceiveMouseEvents());
        var grabbed = new MouseEvent(fixture.MouseX, fixture.MouseY, EnumMouseButton.Left, 0);
        hud.OnMouseDown(grabbed);
        Assert.False(grabbed.Handled);
    }

    [Fact]
    public void ControlsFactory_RejectsMissingHintsAndInvalidScaleBeforeAllocatingArtwork()
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => BuildModeHud.CreateControlsTable(fixture.Api.Object, null!));
        Assert.Throws<ArgumentException>(() => BuildModeHud.CreateControlsTable(fixture.Api.Object, []));
        RuntimeEnv.GUIScale = 0;
        Assert.Throws<InvalidOperationException>(() =>
            BuildModeHud.CreateControlsTable(fixture.Api.Object, [Hint("A", ["B"])]));
        Assert.Empty(fixture.Uploads);
    }

    private static BuildModeKeybindHint Hint(string label, ImmutableArray<string> keys,
        ImmutableArray<string> alternate = default) => new(label, keys, alternate.IsDefault ? [] : alternate);

    private static TableSize PreviousSize(IReadOnlyList<BuildModeKeybindHint> hints)
    {
        using var labelFont = CairoFont.WhiteSmallText().WithFontSize(UiPalette.TextFontSize);
        using var keyFont = CairoFont.WhiteSmallText().WithFontSize(UiPalette.KeybindFontSize);
        double labelWidth = Math.Ceiling(Math.Max(88,
            hints.Max(hint => labelFont.GetTextExtents(hint.Action).Width / RuntimeEnv.GUIScale)));
        double KeyWidth(ImmutableArray<string> keys) =>
            keys.Sum(key => (int)(keyFont.GetTextExtents(key).Width + GuiElement.scaled(8))) +
            GuiElement.scaled(3) * Math.Max(0, keys.Length - 1);
        double bindingWidth = Math.Ceiling(Math.Max(200, hints.Max(hint =>
            (KeyWidth(hint.Keys) + (hint.AlternateKeys.IsEmpty ? 0 :
                GuiElement.scaled(13) + KeyWidth(hint.AlternateKeys)) + 3) / RuntimeEnv.GUIScale)));
        return new TableSize(labelWidth + 12 + bindingWidth, hints.Count * 20);
    }

    private static GuiElementTable? PendingTable(BuildModeHud hud) =>
        (GuiElementTable?)typeof(BuildModeHud).GetField("controlsTable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(hud);

    private static void ClickToggle(BuildModeHud hud, Fixture fixture)
    {
        var toggle = hud.SingleComposer.GetElement("controls-toggle");
        int x = (int)toggle.Bounds.renderX + 1;
        int y = (int)toggle.Bounds.renderY + 1;
        hud.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        hud.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        hud.OnRenderGUI(0);
    }

    private delegate void UploadCallback(ImageSurface surface, bool linearMag, ref LoadedTexture texture);
    private sealed record UploadSnapshot(LoadedTexture Texture, int Width, int Height, int Stride, byte[] Pixels);

    private sealed class Fixture : IDisposable
    {
        private const string Locale = "vbe-controls-tests";
        private readonly string previousLocale = Lang.CurrentLocale;
        private readonly float previousScale = RuntimeEnv.GUIScale;
        private int nextTexture = 100;
        public Mock<ICoreClientAPI> Api { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<IGuiAPI> Gui { get; } = new();
        public Mock<IRenderAPI> Render { get; } = new();
        public Dictionary<string, HotKey> Hotkeys { get; } = new();
        public List<GuiComposer> CreatedComposers { get; } = [];
        public List<UploadSnapshot> Uploads { get; } = [];
        public List<int> Deleted { get; } = [];
        public OnSettingsChanged<float>? ScaleWatcher { get; private set; }
        public Action? OnCreate { get; set; }
        public bool FailUpload { get; set; }
        public int MouseX { get; set; }
        public int MouseY { get; set; }
        public bool MouseGrabbed { get; set; }

        public Fixture(float scale = 1)
        {
            RuntimeEnv.GUIScale = scale;
            var translations = new Mock<ITranslationService>();
            translations.Setup(service => service.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(true);
            translations.Setup(service => service.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key.Split(':').Last());
            translations.SetupGet(service => service.LineBreakBehavior).Returns(EnumLinebreakBehavior.AfterWord);
            Lang.AvailableLanguages.Add(Locale, translations.Object);
            Lang.ChangeLanguage(Locale);
            Api.SetupGet(api => api.Gui).Returns(Gui.Object);
            Api.SetupGet(api => api.Render).Returns(Render.Object);
            Gui.SetupGet(gui => gui.Text).Returns(new TextDrawUtil());
            Gui.SetupGet(gui => gui.LoadedGuis).Returns([]);
            Gui.Setup(gui => gui.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
                .Returns((string _, ElementBounds bounds) =>
                {
                    OnCreate?.Invoke();
                    if (bounds.ParentBounds is null) bounds.WithEmptyParent();
                    var composer = Composer(bounds);
                    CreatedComposers.Add(composer);
                    return composer;
                });
            Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), true,
                ref It.Ref<LoadedTexture>.IsAny)).Callback(new UploadCallback(Upload));
            Gui.Setup(gui => gui.DeleteTexture(It.IsAny<int>())).Callback<int>(Deleted.Add);
            Render.SetupGet(render => render.FrameWidth).Returns(1920);
            Render.SetupGet(render => render.FrameHeight).Returns(1080);
            Render.Setup(render => render.BitmapCreateFromPng(It.IsAny<byte[]>()))
                .Returns(() => new BitmapExternal(1, 1));
            Api.Setup(api => api.Assets.Get(It.IsAny<AssetLocation>()).Data).Returns([0]);
            Api.Setup(api => api.Settings.Float.AddWatcher("guiScale", It.IsAny<OnSettingsChanged<float>>()))
                .Callback<string, OnSettingsChanged<float>>((_, callback) => ScaleWatcher = callback);
            Api.SetupGet(api => api.Input.MouseX).Returns(() => MouseX);
            Api.SetupGet(api => api.Input.MouseY).Returns(() => MouseY);
            Api.SetupGet(api => api.Input.MouseGrabbed).Returns(() => MouseGrabbed);
            foreach (string code in new[] { "vbe.ToggleBuildMode", "vbe.RotateBuildCursor_Forward",
                "vbe.RotateBuildCursor_Backward", "vbe.CycleSnappingMode_Forward",
                "vbe.CycleSnappingMode_Backward", "vbe.ToggleFaceOffsetPlacement" })
                Hotkeys.Add(code, new HotKey { CurrentMapping = new KeyCombination { KeyCode = (int)GlKeys.LShift } });
            Api.Setup(api => api.Input.GetHotKeyByCode(It.IsAny<string>()))
                .Returns((string code) => Hotkeys[code]);
        }

        public GuiComposer Composer(ElementBounds? bounds = null) =>
            Assert.IsType<GuiComposer>(Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                [Api.Object, bounds ?? ElementBounds.Fixed(0, 0, 800, 600).WithEmptyParent(), "controls-tests"],
                culture: null));

        public void Attach(GuiElementTable table)
        {
            var parent = ElementBounds.Fixed(0, 0, 1000, 1000).WithEmptyParent();
            parent.WithChild(table.Bounds);
            table.Arrange();
            parent.CalcWorldBounds();
        }

        private void Upload(ImageSurface surface, bool linearMag, ref LoadedTexture texture)
        {
            if (texture.TextureId == 0) texture.TextureId = ++nextTexture;
            texture.Width = surface.Width;
            texture.Height = surface.Height;
            surface.Flush();
            Uploads.Add(new UploadSnapshot(texture, surface.Width, surface.Height, surface.Stride, surface.Data));
            if (FailUpload) throw new InvalidOperationException("Test upload failure.");
        }

        public void Dispose()
        {
            RuntimeEnv.GUIScale = previousScale;
            Lang.ChangeLanguage(previousLocale);
            Lang.AvailableLanguages.Remove(Locale);
        }
    }
}
