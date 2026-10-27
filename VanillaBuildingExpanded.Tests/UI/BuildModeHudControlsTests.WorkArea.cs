using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Gizmos;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed partial class BuildModeHudControlsTests
{
    [Fact]
    public void WorkAreaSession_HudPriorityAndRecompositionLeaveWorldCaptureAndSessionIntact()
    {
        using var f = WorkAreaFixture();
        var world = Mock.Get(f.Api.Object.World);
        var accessor = Mock.Get(f.Api.Object.World.BlockAccessor);
        var workArea = ActiveWorkArea();
        workArea.RestoreEndpoints(new(-30, 65, -200, 0), new(30, 65, -200, 0));
        using var h = new BuildBrush.WorkAreaGizmoSessionTests.Harness(f.Api, world, accessor, workArea);
        h.Input.Setup(i => i.MouseX).Returns(() => f.MouseX);
        h.Input.Setup(i => i.MouseY).Returns(() => f.MouseY);
        h.Input.Setup(i => i.GetHotKeyByCode(It.IsAny<string>())).Returns((string code) => f.Hotkeys[code]);
        h.Render.Setup(r => r.BitmapCreateFromPng(It.IsAny<byte[]>())).Returns(() => new BitmapExternal(1, 1));
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        h.Dialogs.Add(hud);
        hud.SetVisible(true);
        ClickWorkArea(hud, f, "show");
        h.Frame();
        var check = WorkAreaMouse(f, WorkAreaElement(hud, "show"));
        Assert.False(h.Down(check.X, check.Y).Handled);
        hud.OnMouseDown(check);
        var release = h.Up(check.X, check.Y);
        hud.OnMouseUp(release);
        Assert.False(workArea.State.ShowWorkArea);
        hud.OnRenderGUI(0);
        ClickWorkArea(hud, f, "show");
        h.Frame();
        var gizmo = h.Session.Start!;
        h.Aim(gizmo.Value.Coordinates);
        Assert.True(h.Down().Handled);
        Assert.Same(gizmo, h.Session.Manager.CapturedGizmo);
        ClickToggle(hud, f, "workArea");
        f.ScaleWatcher!(1.25f);
        f.ChangeLocale();
        hud.OnRenderGUI(0);
        h.Session.Refresh();
        Assert.Same(gizmo, h.Session.Manager.CapturedGizmo);
        Assert.Equal(3, h.Session.Manager.Count);
        h.MoveCenterTo(new(-50, 70, -200, 0));
        h.Frame();
        h.Up();
        Assert.Equal(new WorkAreaEndpoint(-50, 65, -200, 0), workArea.State.Start);
        Assert.Equal(new WorkAreaEndpoint(30, 70, -200, 0), workArea.State.End);
        Assert.Equal(1, h.Commits);
    }

    [Theory]
    [InlineData("control")]
    [InlineData("passive")]
    [InlineData("native-hud")]
    public void GizmoInput_ReservesActualHudHitsButNotPassiveTextOrStaleHover(string target)
    {
        using var f = WorkAreaFixture();
        using var hud = new BuildModeHud(f.Api.Object, () => default, ActiveWorkArea());
        hud.SetVisible(true);
        var player = BuildBrush.WorkAreaRenderingTests.CreatePlayer();
        player.OverrideCameraMode = EnumCameraMode.FirstPerson;
        f.Api.SetupGet(a => a.World.Player).Returns(player);
        var dialogs = new List<GuiDialog> { hud };
        f.Gui.Setup(g => g.OpenedGuis).Returns(dialogs);
        var raw = new MouseButtonState();
        f.Api.Setup(a => a.Input.MouseButton).Returns(raw);
        f.Api.Setup(a => a.Input.InWorldMouseButton).Returns(new MouseButtonState());
        var show = WorkAreaElement(hud, "show");
        hud.OnMouseMove(WorkAreaMouse(f, show));
        var e = WorkAreaMouse(f, target == "control" ? show : WorkAreaTable(hud).GetElement("size"));
        using var receiver = new ReceivingHud(f.Api.Object);
        if (target == "native-hud")
        {
            var bounds = ElementBounds.Fixed(e.X - 5, e.Y - 5, 10, 10).WithEmptyParent();
            bounds.CalcWorldBounds();
            receiver.SingleComposer = f.Composer(bounds);
            dialogs.Add(receiver);
        }
        var context = Gizmos.PositionGizmoTests.Camera(1920, 1080);
        var pointer = context.Camera!.CreatePointer(context, e.X, e.Y);
        using var manager = new GizmoManager();
        using var renderer = new GizmoRenderAdapter(f.Api.Object, manager, () => context,
            new BuildBrush.WorkAreaRenderingTests.FakeState());
        var gizmo = new PositionGizmo(new(pointer.Ray.Origin + pointer.Ray.Direction * 20, 0), _ => { });
        manager.Add(gizmo);
        using var input = new GizmoInputAdapter(f.Api.Object, manager, renderer, () => true, []);
        renderer.SetActive(true);
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        renderer.OnRenderFrame(0, EnumRenderStage.AfterBlit);
        raw.Left = true;
        var events = Mock.Get(f.Api.Object.Event);
        events.Raise(ev => ev.MouseDown += null!, e);
        Assert.Equal(target == "passive", e.Handled);
        Assert.Equal(target == "passive", ReferenceEquals(gizmo, manager.CapturedGizmo));
        if (target == "control")
        {
            hud.OnMouseDown(e);
            Assert.True(e.Handled);
            Assert.True(hud.ReservesPointer(-100, -100));
        }
        else if (target == "passive")
        {
            f.ScaleWatcher!(1.25f);
            f.ChangeLocale();
            hud.OnRenderGUI(0);
            input.Refresh();
            Assert.Same(gizmo, manager.CapturedGizmo);
        }
        raw.Left = false;
        var up = new MouseEvent(e.X, e.Y, EnumMouseButton.Left, 0);
        events.Raise(ev => ev.MouseUp += null!, up);
        if (target == "control") hud.OnMouseUp(up);
    }

    private sealed class ReceivingHud(ICoreClientAPI api) : HudElement(api)
    {
        public override bool ShouldReceiveMouseEvents() => true;
    }

    [Fact]
    public void WorkAreaDrawingFailure_LeavesSizesAndControlsUsableAcrossHudRebuilds()
    {
        using var f = WorkAreaFixture();
        var player = BuildBrush.WorkAreaRenderingTests.CreatePlayer();
        f.Api.SetupGet(a => a.World.Player).Returns(player);
        f.Api.Setup(a => a.World.BlockAccessor.IsValidPos(It.IsAny<Vintagestory.API.MathTools.BlockPos>()))
            .Returns(true);
        var workArea = ActiveWorkArea();
        workArea.RestoreEndpoints(new(10, 65, 20, 0), new(14, 67, 23, 0));
        IRenderer? renderer = null;
        int registrations = 0, unregistrations = 0;
        f.Api.Setup(a => a.Event.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Opaque, It.IsAny<string>()))
            .Callback<IRenderer, EnumRenderStage, string>((value, _, _) =>
            {
                renderer = value;
                registrations++;
            });
        f.Api.Setup(a => a.Event.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Opaque))
            .Callback(() => unregistrations++);
        var shader = new Mock<IShaderProgram>();
        shader.SetupGet(s => s.LoadError).Returns(true);
        f.Api.Setup(a => a.Shader.GetProgram((int)EnumShaderProgram.Wireframe)).Returns(shader.Object);
        using var binding = new WorkAreaRenderBinding(f.Api.Object, workArea, new BuildBrush.WorkAreaRenderingTests.FakeState());
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        ClickWorkArea(hud, f, "show");
        Assert.NotNull(renderer);
        var originalRenderer = renderer;
        f.ScaleWatcher!(1.25f);
        f.ChangeLocale();
        hud.OnRenderGUI(0);
        Assert.Equal(1, registrations);
        Assert.Equal(0, unregistrations);
        Assert.Same(originalRenderer, renderer);
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        binding.Refresh();
        Assert.Equal(1, unregistrations);
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", WorkAreaSize(hud));
        Assert.Equal(2, WorkAreaTable(hud).Bounds.ChildBounds.Count);
        workArea.RestoreEndpoints(new(10, 65, 20, 0), new(20, 68, 24, 0));
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 11, 4, 5", WorkAreaSize(hud));
        Assert.Equal(1, registrations);
        f.Api.Verify(a => a.TriggerIngameError(It.IsAny<object>(),
            "vanillabuildingexpanded:workarea-render-unavailable", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void WorkAreaBinding_RemainsLazyAndSharesSessionStateWithSuspendedHud()
    {
        using var f = WorkAreaFixture();
        var workArea = ActiveWorkArea();
        var world = BuildBrush.TestHelpers.CreateMockWorld();
        var inputs = new BuildBrush.BrushTestInputs();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot());
        var brush = new BuildBrushControllerClient(f.Api.Object, () => inputs.CreateBrush(world.Object));
        var owner = new BuildBrushSystem_Client();
        typeof(BuildBrushSystem_Client).GetField("api", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, f.Api.Object);
        typeof(BuildBrushSystem_Client).GetField("workArea", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, workArea);
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
        ClickWorkArea(hud, f, "show");
        Assert.True(workArea.State.ShowWorkArea);
        workArea.RestoreEndpoints(new(10, 65, 20, 0), new(14, 67, 23, 0));
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", WorkAreaSize(hud));
        var previous = hud;
        brush.SetBuildMode(false, null);
        Assert.False(hud.IsOpened());
        brush.SetBuildMode(true, null);
        Assert.Same(previous, hud);
        Assert.True(Assert.IsAssignableFrom<GuiElementSwitch>(WorkAreaElement(hud, "show")).On);
        binding.Dispose();
        Assert.False(hud.IsOpened());
        workArea.SetVisibility(false);
        brush.SetBuildMode(false, null);
        brush.SetBuildMode(true, null);
        Assert.False(hud.IsOpened());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void WorkAreaSection_HasExactlyTwoOrderedRowsAndSharedInclusiveSizes(float scale)
    {
        using var f = WorkAreaFixture(scale);
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var table = WorkAreaTable(hud);
        Assert.Equal(2, table.Bounds.ChildBounds.Count);
        var visibility = Assert.IsType<GuiElementTable>(table.GetElement("visibility"));
        var size = Assert.IsType<GuiElementStaticText>(table.GetElement("size"));
        Assert.Equal(2, visibility.Bounds.ChildBounds.Count);
        Assert.Equal("Show work area", Assert.IsType<GuiElementStaticText>(visibility.GetElement("label")).GetText());
        Assert.Throws<KeyNotFoundException>(() => table.GetElement("endpoints"));
        Assert.Equal("Size: -, -, -", size.GetText());
        Assert.True(visibility.Bounds.renderY + visibility.Bounds.OuterHeight <= size.Bounds.renderY);
        Assert.True(hud.SingleComposer.GetElement("room-toggle").Bounds.renderY < table.Bounds.renderY);
        Assert.True(size.Bounds.renderY < hud.SingleComposer.GetElement("controls-title").Bounds.renderY);
        workArea.RestoreEndpoints(new(10, 65, 20, 0), new(14, 67, 23, 0));
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", WorkAreaSize(hud));
        workArea.RestoreEndpoints(new(14, 67, 23, 0), new(10, 65, 20, 0));
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", WorkAreaSize(hud));
        Assert.False(hud.Focusable);
        Assert.False(hud.ShouldReceiveKeyboardEvents());
        Assert.False(WorkAreaTable(hud).Focusable);
    }

    [Fact]
    public void WorkAreaControls_ActivateOnReleaseSwitchCancelAndNotifyOutsideCollapsedContent()
    {
        using var f = WorkAreaFixture();
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        ClickWorkArea(hud, f, "show");
        Assert.True(workArea.State.ShowWorkArea);
        Assert.True(Assert.IsAssignableFrom<GuiElementSwitch>(WorkAreaElement(hud, "show")).On);
        var state = workArea.State;
        ClickToggle(hud, f, "workArea");
        Assert.Null(hud.SingleComposer.GetElement("workarea-table"));
        Assert.Equal(state, workArea.State);
        ClickToggle(hud, f, "workArea");
        Assert.True(Assert.IsAssignableFrom<GuiElementSwitch>(WorkAreaElement(hud, "show")).On);
        Assert.Null(workArea.State.Start);
        Assert.Null(workArea.State.End);
    }

    [Theory]
    [InlineData("show")]
    public void WorkAreaControls_DragOutsideCancelsAndPassiveAreasNeverTakeInput(string key)
    {
        using var f = WorkAreaFixture();
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var control = WorkAreaElement(hud, key);
        var press = WorkAreaMouse(f, control);
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        Assert.True(WorkAreaTable(hud).HasPointerCapture);
        f.MouseX = f.MouseY = -100;
        Assert.True(hud.ReservesPointer(-100, -100));
        Assert.True(hud.ShouldReceiveMouseEvents());
        hud.OnMouseMove(new MouseEvent(-100, -100));
        var release = new MouseEvent(-100, -100, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.False(WorkAreaTable(hud).HasPointerCapture);
        Assert.False(workArea.State.ShowWorkArea);
        foreach (GuiElement passive in new[]
        {
            WorkAreaTable(hud).GetElement("size"),
            ((GuiElementTable)WorkAreaTable(hud).GetElement("visibility")).GetElement("label"),
            hud.SingleComposer.GetElement("workArea-title")
        })
        {
            var e = WorkAreaMouse(f, passive);
            Assert.False(hud.ReservesPointer(e.X, e.Y));
            hud.OnMouseMove(e);
            Assert.False(hud.ShouldReceiveMouseEvents());
            hud.OnMouseDown(e);
            hud.OnMouseUp(new MouseEvent(e.X, e.Y, EnumMouseButton.Left, 0));
            Assert.False(e.Handled);
        }
        ClickWorkArea(hud, f, key);
        Assert.Equal(key == "show", workArea.State.ShowWorkArea);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("collapse")]
    [InlineData("scale")]
    [InlineData("locale")]
    [InlineData("dispose")]
    [InlineData("grab")]
    public void WorkAreaCaptures_CancelBeforeLifecycleRebuildWithoutDuplicateCommands(string change)
    {
        using var f = WorkAreaFixture();
        var workArea = ActiveWorkArea();
        int changes = 0;
        workArea.Changed += _ => changes++;
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var press = WorkAreaMouse(f, WorkAreaElement(hud, "show"));
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        var table = WorkAreaTable(hud);
        Assert.True(table.HasPointerCapture);
        switch (change)
        {
            case "hide": hud.SetVisible(false); break;
            case "collapse": ClickToggle(hud, f, "workArea"); break;
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
        Assert.Equal(0, changes);
        Assert.False(workArea.State.ShowWorkArea);
        if (change != "dispose")
        {
            f.MouseGrabbed = false;
            hud.SetVisible(true);
            if (change == "collapse") ClickToggle(hud, f, "workArea");
            ClickWorkArea(hud, f, "show");
            Assert.Equal(1, changes);
        }
        hud.Dispose();
        workArea.SetVisibility(true);
        Assert.All(f.Uploads, upload => Assert.True(upload.Texture.Disposed));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void WorkAreaLayout_LongTranslationsAndWideSizesFitWithoutChangingRoomControls(float scale)
    {
        using var f = WorkAreaFixture(scale);
        var original = f.Translate;
        f.Translate = (key, args) => key.StartsWith("vanillabuildingexpanded:build-info-workarea", StringComparison.Ordinal)
            ? "Long translated work area text " + original(key, args) + " with more translated words"
            : original(key, args);
        var workArea = ActiveWorkArea();
        workArea.RestoreEndpoints(new(int.MinValue, 0, int.MinValue, 0), new(int.MaxValue, 32767, int.MaxValue, 0));
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var table = WorkAreaTable(hud);
        Assert.Contains("4294967296", WorkAreaSize(hud));
        Assert.Contains("32768", WorkAreaSize(hud));
        foreach (var row in table.Bounds.ChildBounds)
            Assert.True(row.fixedX + row.fixedWidth <= table.Measure().Width);
        Assert.True(table.Bounds.renderY + table.Bounds.OuterHeight
            < hud.SingleComposer.GetElement("controls-title").Bounds.renderY);
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("room-table"));
        ClickToggle(hud, f, "controls");
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("controls-table"));
        Assert.Contains("4294967296", WorkAreaSize(hud));
    }

    private static WorkAreaController ActiveWorkArea()
    {
        var workArea = new WorkAreaController();
        workArea.SetContext(true, true);
        return workArea;
    }

    private static Fixture WorkAreaFixture(float scale = 1)
    {
        var fixture = new Fixture(scale);
        fixture.Translate = (key, args) => key switch
        {
            "vanillabuildingexpanded:build-info-workarea" => "Work area",
            "vanillabuildingexpanded:build-info-workarea-show" => "Show work area",
            "vanillabuildingexpanded:build-info-workarea-unset" => "-",
            "vanillabuildingexpanded:build-info-workarea-size" => string.Format("Size: {0}, {1}, {2}", args),
            _ => key.Split(':').Last()
        };
        return fixture;
    }

    private static GuiElementTable WorkAreaTable(BuildModeHud hud) =>
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("workarea-table"));

    private static string WorkAreaSize(BuildModeHud hud) =>
        Assert.IsType<GuiElementStaticText>(WorkAreaTable(hud).GetElement("size")).GetText();

    private static GuiElement WorkAreaElement(BuildModeHud hud, string key) =>
        ((GuiElementTable)WorkAreaTable(hud).GetElement("visibility")).GetElement(key);

    private static MouseEvent WorkAreaMouse(Fixture f, GuiElement element)
    {
        f.MouseX = (int)(element.Bounds.renderX + element.Bounds.OuterWidth / 2);
        f.MouseY = (int)(element.Bounds.renderY + element.Bounds.OuterHeight / 2);
        return new MouseEvent(f.MouseX, f.MouseY, EnumMouseButton.Left, 0);
    }

    private static void ClickWorkArea(BuildModeHud hud, Fixture f, string key)
    {
        var e = WorkAreaMouse(f, WorkAreaElement(hud, key));
        Assert.True(hud.ShouldReceiveMouseEvents());
        hud.OnMouseDown(e);
        Assert.True(e.Handled);
        var release = new MouseEvent(e.X, e.Y, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        hud.OnRenderGUI(0);
    }
}
