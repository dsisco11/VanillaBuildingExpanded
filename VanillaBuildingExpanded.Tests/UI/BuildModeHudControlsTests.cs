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
    public void SectionHeadings_RemainVisibleAndPassiveWhenCollapsed()
    {
        using var fixture = new Fixture();
        fixture.Translate = (key, _) => key is "vanillabuildingexpanded:build-info-room" or "vanillabuildingexpanded:build-info-controls"
            ? string.Concat(Enumerable.Repeat("Long localized section heading ", 6))
            : key.Split(':').Last();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        double panelWidth = hud.SingleComposer.Bounds.fixedWidth;
        foreach (string id in new[] { "room", "controls" })
        {
            void CheckHeading()
            {
                var heading = Assert.IsType<GuiElementCustomDraw>(hud.SingleComposer.GetElement($"{id}-title"));
                Assert.InRange(heading.Bounds.fixedX, 0, panelWidth);
                Assert.True(heading.Bounds.fixedX + heading.Bounds.fixedWidth <= panelWidth);
                fixture.MouseX = (int)(heading.Bounds.renderX + heading.Bounds.InnerWidth / 2);
                fixture.MouseY = (int)(heading.Bounds.renderY + heading.Bounds.InnerHeight / 2);
                Assert.False(hud.ShouldReceiveMouseEvents());
            }
            CheckHeading();
            ClickToggle(hud, fixture, id);
            Assert.Equal(panelWidth, hud.SingleComposer.Bounds.fixedWidth);
            CheckHeading();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    public void RoomSection_LocalizedTagsFitOneLineAndRemainPassive(float scale)
    {
        using var fixture = new Fixture(scale);
        fixture.Translate = (key, args) => key.Split(':').Last() + " translated" + string.Join(" ", args);
        var state = new BuildModeRoomInfo(RoomInfoStatus.Complete,
            RoomQualifications.Room | RoomQualifications.Shelter | RoomQualifications.Cellar | RoomQualifications.Greenhouse,
            7, 4, 6, 65, 80);
        using var hud = new BuildModeHud(fixture.Api.Object, () => state);
        hud.SetVisible(true);
        var table = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
        Assert.Equal(8, table.Bounds.ChildBounds.Count);
        Assert.IsType<GuiElementCustomDraw>(table.GetElement("type"));
        Assert.False(table.Focusable);
        Assert.IsType<FadingRowTableDividerRenderer>(table.DividerRenderer);
        Assert.Equal(table.Bounds.ChildBounds[1].fixedX, table.Bounds.ChildBounds[7].fixedX);
        var tagBounds = table.GetElement("type").Bounds;
        Assert.True(tagBounds.OuterWidth <= table.Bounds.OuterWidth);
        Assert.True(table.Bounds.renderY > hud.SingleComposer.GetElement("room-title").Bounds.renderY);
        Assert.True(hud.SingleComposer.GetElement("controls-title").Bounds.renderY > table.Bounds.renderY + table.Bounds.OuterHeight);
        Assert.True(tagBounds.fixedX + tagBounds.fixedWidth <= table.Measure().Width);
        fixture.MouseX = (int)tagBounds.renderX + 1;
        fixture.MouseY = (int)tagBounds.renderY + 1;
        var mouse = new MouseEvent(fixture.MouseX, fixture.MouseY, EnumMouseButton.Left, 0);
        table.OnMouseDown(fixture.Api.Object, mouse);
        table.OnMouseUp(fixture.Api.Object, mouse);
        hud.OnMouseDown(mouse);
        hud.OnMouseUp(mouse);
        Assert.False(mouse.Handled);
        Assert.False(hud.ShouldReceiveMouseEvents());
        Assert.False(hud.ShouldReceiveKeyboardEvents());
    }

    [Fact]
    public void RoomSampling_ThrottlesUpdatesAndSuspendsWhileCollapsedHiddenOrDisposed()
    {
        using var fixture = new Fixture();
        fixture.Translate = (key, args) => key.Split(':').Last() + string.Join(" ", args);
        var state = new BuildModeRoomInfo(RoomInfoStatus.Complete,
            RoomQualifications.Room | RoomQualifications.Shelter, 7, 4, 6, 65, 80);
        int samples = 0;
        var hud = new BuildModeHud(fixture.Api.Object, () => { samples++; return state; });
        using (hud)
        {
            hud.SetVisible(true);
            Assert.Equal(1, samples);
            var composer = hud.SingleComposer;
            var table = Assert.IsType<GuiElementTable>(composer.GetElement("room-table"));
            int uploads = fixture.Uploads.Count;
            hud.OnRenderGUI(0.25f);
            Assert.Equal(1, samples);
            hud.OnRenderGUI(0.25f);
            Assert.Equal(2, samples);
            Assert.Equal(uploads, fixture.Uploads.Count);
            state = state with { Width = 8, SunlightPercent = null, CoolingPercent = 25 };
            hud.OnRenderGUI(0.5f);
            Assert.Same(composer, hud.SingleComposer);
            Assert.Same(table, composer.GetElement("room-table"));
            Assert.Equal(uploads + 3, fixture.Uploads.Count);
            Assert.Equal(UiPalette.UnavailableColor, Assert.IsType<GuiElementDynamicText>(table.GetElement("sunlight")).Font.Color);
            Assert.Contains("8", Assert.IsType<GuiElementDynamicText>(table.GetElement("dimensions")).Text);
            Assert.False(table.IsArtworkDirty);

            ClickToggle(hud, fixture, "room");
            Assert.Null(hud.SingleComposer.GetElement("room-table"));
            int before = samples;
            hud.OnRenderGUI(2);
            Assert.Equal(before, samples);
            state = default;
            ClickToggle(hud, fixture, "room");
            Assert.Equal(before + 1, samples);
            table = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
            Assert.Throws<KeyNotFoundException>(() => table.GetElement("dimensions"));
            Assert.Contains("missing", Assert.IsType<GuiElementDynamicText>(table.GetElement("reason")).Text);

            hud.SetVisible(false);
            before = samples;
            hud.OnRenderGUI(2);
            Assert.Equal(before, samples);
            state = new(RoomInfoStatus.Incomplete, RoomQualifications.None, 0, 0, 0, null, null);
            hud.SetVisible(true);
            Assert.Equal(before + 1, samples);
            table = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
            Assert.Contains("incomplete", Assert.IsType<GuiElementDynamicText>(table.GetElement("reason")).Text);
        }
        int final = samples;
        hud.OnRenderGUI(1);
        hud.SetVisible(true);
        Assert.Equal(final, samples);
        Assert.All(fixture.Uploads, upload => Assert.True(upload.Texture.Disposed));
    }

    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(2, 0, 2)]
    [InlineData(2, 2, 1)]
    [InlineData(2, 3, 4)]
    public void RoomStructureChanges_RemoveStaleTagsAndDetails(int statusValue, int tagValue, int rows)
    {
        var status = (RoomInfoStatus)statusValue;
        var tags = (RoomQualifications)tagValue;
        using var fixture = new Fixture();
        var state = new BuildModeRoomInfo(RoomInfoStatus.Complete,
            RoomQualifications.Room | RoomQualifications.Shelter | RoomQualifications.Greenhouse, 7, 4, 6, 65, 80);
        using var hud = new BuildModeHud(fixture.Api.Object, () => state);
        hud.SetVisible(true);
        var oldTable = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
        state = new(status, tags, 5, 3, 4, null, null);
        hud.OnRenderGUI(0.5f);
        Assert.Empty(oldTable.Bounds.ChildBounds);
        var table = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
        Assert.Equal(rows * 2, table.Bounds.ChildBounds.Count);
        if (tags.HasFlag(RoomQualifications.Room))
            Assert.IsType<GuiElementDynamicText>(table.GetElement("dimensions"));
        else
            Assert.Throws<KeyNotFoundException>(() => table.GetElement("dimensions"));
        Assert.Equal(tags != RoomQualifications.None, table.GetElement("type") is GuiElementCustomDraw);
        int uploads = fixture.Uploads.Count;
        hud.OnRenderGUI(0.5f);
        Assert.Same(table, hud.SingleComposer.GetElement("room-table"));
        Assert.Equal(uploads, fixture.Uploads.Count);
    }

    [Fact]
    public void RoomCompositionFailure_ReleasesRegisteredChildrenAndAllowsRetry()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object, () => new(RoomInfoStatus.Complete,
            RoomQualifications.Room | RoomQualifications.Shelter, 7, 4, 6, 50, 80));
        fixture.FailOnUploadNumber = 5;
        Assert.Throws<InvalidOperationException>(() => hud.SetVisible(true));
        Assert.Null(hud.SingleComposer);
        Assert.Empty(Assert.IsType<GuiElementTable>(fixture.CreatedComposers.Single().GetElement("room-table")).Bounds.ChildBounds);
        Assert.All(fixture.Uploads, upload => Assert.True(upload.Texture.Disposed));
        fixture.FailOnUploadNumber = null;
        hud.SetVisible(true);
        Assert.True(hud.IsOpened());
    }

    [Fact]
    public void RoomLocalization_RebuildsEvenWhenKeybindHintsAndSampleAreUnchanged()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object, () => new(RoomInfoStatus.Complete,
            RoomQualifications.Room | RoomQualifications.Shelter, 7, 4, 6, 50, 80));
        hud.SetVisible(true);
        var composer = hud.SingleComposer;
        var table = Assert.IsType<GuiElementTable>(composer.GetElement("room-table"));
        string previous = Lang.CurrentLocale;
        const string locale = "vbe-room-localization-tests";
        Lang.AvailableLanguages.Add(locale, Lang.AvailableLanguages[previous]);
        try
        {
            Lang.ChangeLanguage(locale);
            hud.OnRenderGUI(0.5f);
            Assert.NotSame(composer, hud.SingleComposer);
            Assert.Empty(table.Bounds.ChildBounds);
            Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
        }
        finally
        {
            Lang.ChangeLanguage(previous);
            Lang.AvailableLanguages.Remove(locale);
        }
    }

    [Fact]
    public void InformationTable_OwnsPassiveDynamicValues()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var composer = hud.SingleComposer;
        var table = Assert.IsType<GuiElementTable>(composer.GetElement("information-table"));
        var snapping = Assert.IsType<GuiElementDynamicText>(table.GetElement("snapping"));
        var rotation = Assert.IsType<GuiElementDynamicText>(table.GetElement("rotation"));
        Assert.False(snapping.autoHeight);
        Assert.False(rotation.autoHeight);
        Assert.Equal(UiPalette.ValueColor, snapping.Font.Color);
        Assert.Equal(UiPalette.UnavailableColor, rotation.Font.Color);
        Assert.Null(composer.GetElement("snapping"));
        Assert.Null(composer.GetElement("rotation"));
        Assert.IsType<GuiElementDynamicText>(composer.GetElement("title"));
        Assert.False(table.Focusable);
        Assert.IsType<FadingRowTableDividerRenderer>(table.DividerRenderer);
        fixture.MouseX = (int)snapping.Bounds.renderX + 1;
        fixture.MouseY = (int)snapping.Bounds.renderY + 1;
        var mouse = new MouseEvent(fixture.MouseX, fixture.MouseY, EnumMouseButton.Left, 0);
        table.OnMouseDown(fixture.Api.Object, mouse);
        table.OnMouseUp(fixture.Api.Object, mouse);
        hud.OnMouseDown(mouse);
        hud.OnMouseUp(mouse);
        Assert.False(mouse.Handled);
        Assert.False(hud.ShouldReceiveMouseEvents());

    }

    [Fact]
    public void InformationUpdates_PreserveNativeValuesColorsAndTexturesWithoutReflow()
    {
        using var fixture = new Fixture();
        fixture.Translate = (key, args) => key.Split(':').Last() + string.Concat(args.Select(arg => $" {arg}"));
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var composer = hud.SingleComposer;
        var table = Assert.IsType<GuiElementTable>(composer.GetElement("information-table"));
        var snapping = Assert.IsType<GuiElementDynamicText>(table.GetElement("snapping"));
        var rotation = Assert.IsType<GuiElementDynamicText>(table.GetElement("rotation"));
        var title = composer.GetDynamicText("title");
        var initialSize = table.Measure();
        var artwork = Assert.Single(fixture.Uploads, upload =>
            upload.Width == table.Bounds.OuterWidthInt && upload.Height == table.Bounds.OuterHeightInt);
        int invalidations = 0;
        table.LayoutInvalidated += () => invalidations++;
        BuildModeInfo[] states =
        [
            new(false, EBuildBrushSnapping.Horizontal, 0, 4, null),
            new(false, EBuildBrushSnapping.Vertical, 1, 4, null),
            new(false, EBuildBrushSnapping.None, 0, 4, 90),
            new(true, EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical, 0, 4, 90),
            new(false, EBuildBrushSnapping.Horizontal, 0, 1, null),
            new(false, EBuildBrushSnapping.Vertical, 2, 4, null)
        ];
        foreach (var state in states)
        {
            int uploadsBefore = fixture.Uploads.Count;
            hud.Update(state);
            Assert.True(fixture.Uploads.Count > uploadsBefore);
            Assert.Equal(Lang.Get($"vanillabuildingexpanded:build-info-snapping-{state.Snapping.GetCode()}"), snapping.Text);
            string expectedRotation = state.IsSuspended
                ? Lang.Get("vanillabuildingexpanded:build-info-unavailable")
                : state.OrientationCount <= 1
                    ? Lang.Get("vanillabuildingexpanded:build-info-no-rotation")
                    : state.RotationAngleDegrees is float angle
                        ? Lang.Get("vanillabuildingexpanded:build-info-angle", angle)
                        : Lang.Get("vanillabuildingexpanded:build-info-orientation", state.OrientationIndex + 1, state.OrientationCount);
            Assert.Equal(expectedRotation, rotation.Text);
            Assert.Equal(state.IsSuspended || state.OrientationCount <= 1
                ? UiPalette.UnavailableColor : UiPalette.ValueColor, rotation.Font.Color);
            Assert.Equal(Lang.Get(state.IsSuspended
                ? "vanillabuildingexpanded:build-info-suspended"
                : "vanillabuildingexpanded:build-info-title"), title.Text);
            int uploadsAfter = fixture.Uploads.Count;
            hud.Update(state);
            Assert.Equal(uploadsAfter, fixture.Uploads.Count);
            table.RenderInteractiveElements(0);
            Assert.Single(fixture.Uploads, upload => ReferenceEquals(upload.Texture, artwork.Texture));
            Assert.Same(composer, hud.SingleComposer);
            Assert.Same(snapping, table.GetElement("snapping"));
            Assert.Same(rotation, table.GetElement("rotation"));
            Assert.Equal(initialSize, table.Measure());
            Assert.False(table.IsArtworkDirty);
        }
        Assert.Equal(0, invalidations);
        Assert.Single(fixture.CreatedComposers);
        hud.SetVisible(false);
        hud.Update(new(false, EBuildBrushSnapping.None, 0, 4, 180));
        hud.SetVisible(true);
        Assert.Same(composer, hud.SingleComposer);
        Assert.Equal(Lang.Get("vanillabuildingexpanded:build-info-angle", 180f), rotation.Text);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void InformationTable_InitialStateAndCollapsedRebuildKeepCurrentValues(bool suspended, int orientations)
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        var state = new BuildModeInfo(suspended, EBuildBrushSnapping.Vertical, 0, orientations, null);
        hud.Update(state);
        hud.SetVisible(true);
        var previous = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("information-table"));
        var previousRotation = Assert.IsType<GuiElementDynamicText>(previous.GetElement("rotation"));
        string expectedText = previousRotation.Text;
        Assert.Equal(suspended || orientations <= 1
            ? UiPalette.UnavailableColor : UiPalette.ValueColor, previousRotation.Font.Color);
        ClickToggle(hud, fixture);
        var collapsed = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("information-table"));
        Assert.NotSame(previous, collapsed);
        Assert.Empty(previous.Bounds.ChildBounds);
        Assert.Null(previousRotation.Bounds.ParentBounds);
        Assert.Null(hud.SingleComposer.GetElement("controls-table"));
        Assert.Equal(expectedText, Assert.IsType<GuiElementDynamicText>(collapsed.GetElement("rotation")).Text);
        hud.Update(new(false, EBuildBrushSnapping.None, 0, 4, 90));
        var current = Assert.IsType<GuiElementDynamicText>(collapsed.GetElement("rotation"));
        Assert.Equal(UiPalette.ValueColor, current.Font.Color);
        Assert.Same(collapsed, hud.SingleComposer.GetElement("information-table"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InformationCompositionFailure_ReleasesBothTablesAndNativeValueTexturesBeforeRetry(int failedUpload)
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        fixture.FailOnUploadNumber = failedUpload;
        Assert.Throws<InvalidOperationException>(() => hud.SetVisible(true));
        var failed = Assert.Single(fixture.CreatedComposers);
        Assert.Null(hud.SingleComposer);
        Assert.Null(PendingTable(hud));
        Assert.False(hud.IsOpened());
        Assert.Empty(Assert.IsType<GuiElementTable>(failed.GetElement("information-table")).Bounds.ChildBounds);
        Assert.Empty(Assert.IsType<GuiElementTable>(failed.GetElement("controls-table")).Bounds.ChildBounds);
        Assert.All(fixture.Uploads, upload => Assert.True(upload.Texture.Disposed));
        Assert.Equal(failedUpload, fixture.Deleted.Count);
        fixture.FailOnUploadNumber = null;
        hud.SetVisible(true);
        hud.Update(new(false, EBuildBrushSnapping.Horizontal, 1, 4, null));
        Assert.True(hud.IsOpened());
        Assert.Equal(2, fixture.CreatedComposers.Count);
        hud.Dispose();
        Assert.All(fixture.Uploads, upload => Assert.Equal(0, upload.Texture.TextureId));
        Assert.Equal(fixture.Uploads.Select(upload => upload.Texture).Distinct().Count(), fixture.Deleted.Count);
        Assert.Equal(fixture.Deleted.Count, fixture.Deleted.Distinct().Count());
    }

    [Fact]
    public void ControlsRows_UsePassiveFadingDividers()
    {
        using var fixture = new Fixture();
        using var table = BuildModeHud.CreateControlsTable(fixture.Api.Object,
            [Hint("A", ["B"]), Hint("C", ["D"])]);
        Assert.IsType<FadingRowTableDividerRenderer>(table.DividerRenderer);
        table.Arrange();
        Assert.Equal(4, table.Bounds.ChildBounds.Count);
        Assert.False(table.Focusable);
        fixture.Attach(table);
        var mouse = new MouseEvent(101, 1, EnumMouseButton.Left, 0);
        table.OnMouseDown(fixture.Api.Object, mouse);
        Assert.False(mouse.Handled);
        Assert.Empty(fixture.Uploads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    public void Controls_LongLabelsAndAlternateBindingsExpandWithoutOverlap(float scale)
    {
        using var fixture = new Fixture(scale);
        using var compact = BuildModeHud.CreateControlsTable(fixture.Api.Object, [Hint("A", ["B"])]);
        BuildModeKeybindHint[] hints =
        [
            Hint("A very long translated controls action label", ["Ctrl", "Shift", "Esc"], ["Alt", "Esc"]),
            Hint("A", ["An unusually long translated mouse binding", "Shift"]),
            Hint("Rotate", ["R"])
        ];
        using var table = BuildModeHud.CreateControlsTable(fixture.Api.Object, hints);
        Assert.True(table.Measure().Width > compact.Measure().Width);
        table.Arrange();
        var bindingStarts = table.Bounds.ChildBounds.Where((_, index) => index % 2 == 1)
            .Select(bounds => bounds.fixedX).ToArray();
        Assert.All(bindingStarts, x => Assert.Equal(bindingStarts[0], x));
        for (int row = 0; row < hints.Length; row++)
        {
            var label = table.Bounds.ChildBounds[row * 2];
            var binding = table.Bounds.ChildBounds[row * 2 + 1];
            Assert.True(label.fixedX + label.fixedWidth <= binding.fixedX);
            Assert.True(binding.fixedX + binding.fixedWidth <= table.Measure().Width);
        }
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
        Assert.Equal(table.Measure().Height, expandedHeight - section.MeasureHeight(width));
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
        double contentHeight = previous.Measure().Height;
        ClickToggle(hud, fixture);
        Assert.Null(hud.SingleComposer.GetElement("controls-table"));
        Assert.Empty(previous.Bounds.ChildBounds);
        Assert.Equal(width, hud.SingleComposer.Bounds.fixedWidth);
        Assert.Equal(contentHeight, expandedHeight - hud.SingleComposer.Bounds.fixedHeight);
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
            double contentHeight = previous.Measure().Height;
            var previousInformation = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("information-table"));
            var informationChildren = previousInformation.Bounds.ChildBounds.ToArray();
            var children = previous.Bounds.ChildBounds.ToArray();
            ClickToggle(hud, fixture);
            Assert.Null(hud.SingleComposer.GetElement("controls-table"));
            Assert.Empty(previous.Bounds.ChildBounds);
            Assert.Empty(previousInformation.Bounds.ChildBounds);
            Assert.All(informationChildren, bounds => Assert.Null(bounds.ParentBounds));
            Assert.All(children, bounds => Assert.Null(bounds.ParentBounds));
            Assert.Equal(width, hud.SingleComposer.Bounds.fixedWidth);
            Assert.Equal(height - contentHeight, hud.SingleComposer.Bounds.fixedHeight);
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
            Assert.Empty(Assert.IsType<GuiElementTable>(composer.GetElement("information-table")).Bounds.ChildBounds);
            Assert.Empty(Assert.IsType<GuiElementTable>(composer.GetElement("room-table")).Bounds.ChildBounds);
        });
    }

    [Fact]
    public void Hud_ScaleChangesRebuildVisibleContentAndDeferHiddenRebuilds()
    {
        using var fixture = new Fixture();
        using var hud = new BuildModeHud(fixture.Api.Object);
        hud.SetVisible(true);
        var previous = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        var previousRoom = Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
        RuntimeEnv.GUIScale = 1.25f;
        fixture.ScaleWatcher!(1.25f);
        Assert.Empty(previous.Bounds.ChildBounds);
        Assert.Empty(previousRoom.Bounds.ChildBounds);
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

    private static GuiElementTable? PendingTable(BuildModeHud hud) =>
        (GuiElementTable?)typeof(BuildModeHud).GetField("controlsTable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(hud);

    private static void ClickToggle(BuildModeHud hud, Fixture fixture, string section = "controls")
    {
        var toggle = hud.SingleComposer.GetElement($"{section}-toggle");
        int x = (int)toggle.Bounds.renderX + 1;
        int y = (int)toggle.Bounds.renderY + 1;
        hud.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        hud.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        hud.OnRenderGUI(0);
    }

    private delegate void UploadCallback(ImageSurface surface, bool linearMag, ref LoadedTexture texture);
    private sealed record UploadSnapshot(LoadedTexture Texture, int Width, int Height);

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
        public int? FailOnUploadNumber { get; set; }
        public int MouseX { get; set; }
        public int MouseY { get; set; }
        public bool MouseGrabbed { get; set; }
        public System.Func<string, object[], string> Translate { get; set; } = (key, _) => key.Split(':').Last();

        public Fixture(float scale = 1)
        {
            RuntimeEnv.GUIScale = scale;
            var translations = new Mock<ITranslationService>();
            translations.Setup(service => service.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(true);
            translations.Setup(service => service.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] args) => Translate(key, args));
            translations.SetupGet(service => service.LineBreakBehavior).Returns(EnumLinebreakBehavior.AfterWord);
            Lang.AvailableLanguages.Add(Locale, translations.Object);
            Lang.ChangeLanguage(Locale);
            Api.SetupGet(api => api.Gui).Returns(Gui.Object);
            Api.SetupGet(api => api.World.Player).Returns((IClientPlayer)null!);
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
            Uploads.Add(new UploadSnapshot(texture, surface.Width, surface.Height));
            if (FailUpload || Uploads.Count == FailOnUploadNumber)
                throw new InvalidOperationException("Test upload failure.");
        }

        public void Dispose()
        {
            RuntimeEnv.GUIScale = previousScale;
            Lang.ChangeLanguage(previousLocale);
            Lang.AvailableLanguages.Remove(Locale);
        }
    }
}
