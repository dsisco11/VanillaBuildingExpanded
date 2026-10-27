using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed partial class BuildModeHudControlsTests
{
    [Fact]
    public void RulerBinding_RemainsLazyAndSharesSessionStateWithSuspendedHud()
    {
        using var f = RulerFixture();
        var ruler = ActiveRuler();
        var world = BuildBrush.TestHelpers.CreateMockWorld();
        var inputs = new BuildBrush.BrushTestInputs();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot());
        var brush = new BuildBrushControllerClient(f.Api.Object, () => inputs.CreateBrush(world.Object));
        var owner = new BuildBrushSystem_Client();
        typeof(BuildBrushSystem_Client).GetField("api", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, f.Api.Object);
        typeof(BuildBrushSystem_Client).GetField("ruler", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, ruler);
        BuildModeHud? hud = null;
        using var binding = new BuildModeIndicatorBinding(brush, () =>
            hud = Assert.IsType<BuildModeHud>(typeof(BuildBrushSystem_Client).GetMethod("CreateModeHud",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(owner, null)));
        Assert.Null(hud);
        Assert.Empty(f.CreatedComposers);
        brush.SetBuildMode(true, null);
        Assert.NotNull(hud);
        Assert.True(hud.IsOpened());
        Assert.False(brush.CanUseBrushControls);
        ClickRuler(hud, f, "show");
        ClickRuler(hud, f, "start");
        Assert.True(ruler.State.ShowRuler);
        Assert.Equal(RulerPickMode.PickingStart, ruler.State.PickMode);
        ruler.RestoreEndpoints(new(10, 65, 20, 0), new(14, 67, 23, 0));
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", RulerSize(hud));
        var previous = hud;
        brush.SetBuildMode(false, null);
        Assert.False(hud.IsOpened());
        brush.SetBuildMode(true, null);
        Assert.Same(previous, hud);
        Assert.True(Assert.IsAssignableFrom<GuiElementSwitch>(RulerElement(hud, "show")).On);
        binding.Dispose();
        Assert.False(hud.IsOpened());
        ruler.SetVisibility(false);
        brush.SetBuildMode(false, null);
        brush.SetBuildMode(true, null);
        Assert.False(hud.IsOpened());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void RulerSection_HasExactlyThreeOrderedRowsAndSharedInclusiveSizes(float scale)
    {
        using var f = RulerFixture(scale);
        var ruler = ActiveRuler();
        using var hud = new BuildModeHud(f.Api.Object, () => default, ruler);
        hud.SetVisible(true);
        var table = RulerTable(hud);
        Assert.Equal(3, table.Bounds.ChildBounds.Count);
        var visibility = Assert.IsType<GuiElementTable>(table.GetElement("visibility"));
        var buttons = Assert.IsType<GuiElementTable>(table.GetElement("endpoints"));
        var size = Assert.IsType<GuiElementStaticText>(table.GetElement("size"));
        Assert.Equal(2, visibility.Bounds.ChildBounds.Count);
        Assert.Equal(2, buttons.Bounds.ChildBounds.Count);
        Assert.Equal("Show ruler", Assert.IsType<GuiElementStaticText>(visibility.GetElement("label")).GetText());
        Assert.Equal("Set Start", Assert.IsAssignableFrom<GuiElementToggleButton>(buttons.GetElement("start")).GetText());
        Assert.Equal("Set end", Assert.IsAssignableFrom<GuiElementToggleButton>(buttons.GetElement("end")).GetText());
        Assert.Equal("Size: -, -, -", size.GetText());
        Assert.True(visibility.Bounds.renderY < buttons.Bounds.renderY);
        Assert.True(buttons.Bounds.renderY + buttons.Bounds.OuterHeight <= size.Bounds.renderY);
        Assert.True(hud.SingleComposer.GetElement("room-toggle").Bounds.renderY < table.Bounds.renderY);
        Assert.True(size.Bounds.renderY < hud.SingleComposer.GetElement("controls-title").Bounds.renderY);
        ruler.RestoreEndpoints(new(10, 65, 20, 0), new(14, 67, 23, 0));
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", RulerSize(hud));
        ruler.RestoreEndpoints(new(14, 67, 23, 0), new(10, 65, 20, 0));
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", RulerSize(hud));
        Assert.False(hud.Focusable);
        Assert.False(hud.ShouldReceiveKeyboardEvents());
        Assert.False(RulerTable(hud).Focusable);
    }

    [Fact]
    public void RulerControls_ActivateOnReleaseSwitchCancelAndNotifyOutsideCollapsedContent()
    {
        using var f = RulerFixture();
        var ruler = ActiveRuler();
        var notices = new List<RulerNotice>();
        ruler.NoticeRequested += notices.Add;
        using var hud = new BuildModeHud(f.Api.Object, () => default, ruler);
        hud.SetVisible(true);
        ClickRuler(hud, f, "show");
        Assert.True(ruler.State.ShowRuler);
        Assert.Contains(RulerNotice.SetBothEndpoints, notices);
        Assert.True(Assert.IsAssignableFrom<GuiElementSwitch>(RulerElement(hud, "show")).On);
        ClickRuler(hud, f, "start");
        Assert.Equal(RulerPickMode.PickingStart, ruler.State.PickMode);
        Assert.True(Assert.IsAssignableFrom<GuiElementToggleButton>(RulerElement(hud, "start")).On);
        Assert.False(Assert.IsAssignableFrom<GuiElementToggleButton>(RulerElement(hud, "end")).On);
        ClickRuler(hud, f, "end");
        Assert.Equal(RulerPickMode.PickingEnd, ruler.State.PickMode);
        Assert.True(Assert.IsAssignableFrom<GuiElementToggleButton>(RulerElement(hud, "end")).On);
        ClickRuler(hud, f, "end");
        Assert.Equal(RulerPickMode.Idle, ruler.State.PickMode);
        ClickRuler(hud, f, "start");
        var state = ruler.State;
        ClickToggle(hud, f, "ruler");
        Assert.Null(hud.SingleComposer.GetElement("ruler-table"));
        Assert.Equal(state, ruler.State);
        Assert.False(ruler.TrySelect(null, false));
        Assert.Equal(RulerNotice.AimAtBlock, notices[^1]);
        Assert.Equal(state, ruler.State);
        ClickToggle(hud, f, "ruler");
        Assert.True(Assert.IsAssignableFrom<GuiElementToggleButton>(RulerElement(hud, "start")).On);
        Assert.True(Assert.IsAssignableFrom<GuiElementSwitch>(RulerElement(hud, "show")).On);
        Assert.Null(ruler.State.Start);
        Assert.Null(ruler.State.End);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void RulerButtons_RejectedCommandsDoNotLeaveFalseArmedArtwork(bool enabled, bool alive)
    {
        using var f = RulerFixture();
        var ruler = ActiveRuler();
        ruler.SetContext(enabled, alive);
        using var hud = new BuildModeHud(f.Api.Object, () => default, ruler);
        hud.SetVisible(true);
        ClickRuler(hud, f, "start");
        Assert.Equal(RulerPickMode.Idle, ruler.State.PickMode);
        Assert.False(Assert.IsAssignableFrom<GuiElementToggleButton>(RulerElement(hud, "start")).On);
        Assert.False(Assert.IsAssignableFrom<GuiElementToggleButton>(RulerElement(hud, "end")).On);
    }

    [Theory]
    [InlineData("show")]
    [InlineData("start")]
    [InlineData("end")]
    public void RulerControls_DragOutsideCancelsAndPassiveAreasNeverTakeInput(string key)
    {
        using var f = RulerFixture();
        var ruler = ActiveRuler();
        using var hud = new BuildModeHud(f.Api.Object, () => default, ruler);
        hud.SetVisible(true);
        var control = RulerElement(hud, key);
        var press = RulerMouse(f, control);
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        Assert.True(RulerTable(hud).HasPointerCapture);
        f.MouseX = f.MouseY = -100;
        Assert.True(hud.ShouldReceiveMouseEvents());
        hud.OnMouseMove(new MouseEvent(-100, -100));
        var release = new MouseEvent(-100, -100, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.False(RulerTable(hud).HasPointerCapture);
        Assert.False(ruler.State.ShowRuler);
        Assert.Equal(RulerPickMode.Idle, ruler.State.PickMode);
        foreach (GuiElement passive in new[]
        {
            RulerTable(hud).GetElement("size"),
            ((GuiElementTable)RulerTable(hud).GetElement("visibility")).GetElement("label"),
            hud.SingleComposer.GetElement("ruler-title")
        })
        {
            var e = RulerMouse(f, passive);
            hud.OnMouseMove(e);
            Assert.False(hud.ShouldReceiveMouseEvents());
            hud.OnMouseDown(e);
            hud.OnMouseUp(new MouseEvent(e.X, e.Y, EnumMouseButton.Left, 0));
            Assert.False(e.Handled);
        }
        ClickRuler(hud, f, key);
        Assert.Equal(key == "show", ruler.State.ShowRuler);
        Assert.Equal(key == "start" ? RulerPickMode.PickingStart
            : key == "end" ? RulerPickMode.PickingEnd : RulerPickMode.Idle, ruler.State.PickMode);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("collapse")]
    [InlineData("scale")]
    [InlineData("locale")]
    [InlineData("dispose")]
    [InlineData("grab")]
    public void RulerCaptures_CancelBeforeLifecycleRebuildWithoutDuplicateCommands(string change)
    {
        using var f = RulerFixture();
        var ruler = ActiveRuler();
        int notices = 0;
        ruler.NoticeRequested += _ => notices++;
        using var hud = new BuildModeHud(f.Api.Object, () => default, ruler);
        hud.SetVisible(true);
        var press = RulerMouse(f, RulerElement(hud, "start"));
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        var table = RulerTable(hud);
        Assert.True(table.HasPointerCapture);
        switch (change)
        {
            case "hide": hud.SetVisible(false); break;
            case "collapse": ClickToggle(hud, f, "ruler"); break;
            case "scale": RuntimeEnv.GUIScale = 1.25f; f.ScaleWatcher!(1.25f); break;
            case "locale":
                f.ChangeLocale();
                hud.SetVisible(true);
                break;
            case "dispose": hud.Dispose(); hud.Dispose(); break;
            case "grab": f.MouseGrabbed = true; hud.OnRenderGUI(0); break;
        }
        Assert.False(table.HasPointerCapture);
        hud.OnMouseUp(new MouseEvent(press.X, press.Y, EnumMouseButton.Left, 0));
        Assert.Equal(0, notices);
        Assert.Equal(RulerPickMode.Idle, ruler.State.PickMode);
        if (change != "dispose")
        {
            f.MouseGrabbed = false;
            hud.SetVisible(true);
            if (change == "collapse") ClickToggle(hud, f, "ruler");
            ClickRuler(hud, f, "start");
            Assert.Equal(1, notices);
        }
        hud.Dispose();
        ruler.SetVisibility(true);
        Assert.All(f.Uploads, upload => Assert.True(upload.Texture.Disposed));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void RulerLayout_LongTranslationsAndWideSizesFitWithoutChangingRoomControls(float scale)
    {
        using var f = RulerFixture(scale);
        var original = f.Translate;
        f.Translate = (key, args) => key.StartsWith("vanillabuildingexpanded:build-info-ruler", StringComparison.Ordinal)
            ? "Long translated ruler text " + original(key, args) + " with more translated words"
            : original(key, args);
        var ruler = ActiveRuler();
        ruler.RestoreEndpoints(new(int.MinValue, 0, int.MinValue, 0), new(int.MaxValue, 32767, int.MaxValue, 0));
        using var hud = new BuildModeHud(f.Api.Object, () => default, ruler);
        hud.SetVisible(true);
        var table = RulerTable(hud);
        Assert.Contains("4294967296", RulerSize(hud));
        Assert.Contains("32768", RulerSize(hud));
        foreach (var row in table.Bounds.ChildBounds)
            Assert.True(row.fixedX + row.fixedWidth <= table.Measure().Width);
        var buttons = (GuiElementTable)table.GetElement("endpoints");
        Assert.True(buttons.GetElement("start").Bounds.renderX + buttons.GetElement("start").Bounds.OuterWidth
            < buttons.GetElement("end").Bounds.renderX);
        Assert.True(table.Bounds.renderY + table.Bounds.OuterHeight
            < hud.SingleComposer.GetElement("controls-title").Bounds.renderY);
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
        ClickToggle(hud, f, "controls");
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        Assert.Contains("4294967296", RulerSize(hud));
    }

    private static RulerController ActiveRuler()
    {
        var ruler = new RulerController();
        ruler.SetContext(true, true);
        return ruler;
    }

    private static Fixture RulerFixture(float scale = 1)
    {
        var fixture = new Fixture(scale);
        fixture.Translate = (key, args) => key switch
        {
            "vanillabuildingexpanded:build-info-ruler" => "Ruler",
            "vanillabuildingexpanded:build-info-ruler-show" => "Show ruler",
            "vanillabuildingexpanded:build-info-ruler-start" => "Set Start",
            "vanillabuildingexpanded:build-info-ruler-end" => "Set end",
            "vanillabuildingexpanded:build-info-ruler-unset" => "-",
            "vanillabuildingexpanded:build-info-ruler-size" => string.Format("Size: {0}, {1}, {2}", args),
            _ => key.Split(':').Last()
        };
        return fixture;
    }

    private static GuiElementTable RulerTable(BuildModeHud hud) =>
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("ruler-table"));

    private static string RulerSize(BuildModeHud hud) =>
        Assert.IsType<GuiElementStaticText>(RulerTable(hud).GetElement("size")).GetText();

    private static GuiElement RulerElement(BuildModeHud hud, string key) =>
        ((GuiElementTable)RulerTable(hud).GetElement(key == "show" ? "visibility" : "endpoints")).GetElement(key);

    private static MouseEvent RulerMouse(Fixture f, GuiElement element)
    {
        f.MouseX = (int)(element.Bounds.renderX + element.Bounds.OuterWidth / 2);
        f.MouseY = (int)(element.Bounds.renderY + element.Bounds.OuterHeight / 2);
        return new MouseEvent(f.MouseX, f.MouseY, EnumMouseButton.Left, 0);
    }

    private static void ClickRuler(BuildModeHud hud, Fixture f, string key)
    {
        var e = RulerMouse(f, RulerElement(hud, key));
        Assert.True(hud.ShouldReceiveMouseEvents());
        hud.OnMouseDown(e);
        Assert.True(e.Handled);
        var release = new MouseEvent(e.X, e.Y, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        hud.OnRenderGUI(0);
    }
}
