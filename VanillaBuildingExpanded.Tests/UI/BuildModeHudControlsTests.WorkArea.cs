using Moq;
using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Gizmos;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed partial class BuildModeHudControlsTests
{
    [Theory]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("grab")]
    public void ListCaptureDoesNotReserveWorldInputAfterHudContextRetirement(string transition)
    {
        using var f = WorkAreaFixture();
        using var hud = new BuildModeHud(f.Api.Object, () => default, ActiveWorkArea());
        hud.SetVisible(true);
        var selector = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        var press = WorkAreaMouse(f, selector);
        hud.OnMouseDown(press);
        Assert.True(hud.ReservesPointer(-100, -100));
        switch (transition)
        {
            case "hide": hud.SetVisible(false); break;
            case "close": hud.TryClose(); break;
            case "grab":
                f.MouseGrabbed = true;
                hud.OnRenderGUI(0);
                f.MouseGrabbed = false;
                break;
        }
        hud.SetVisible(true);
        Assert.False(hud.ReservesPointer(-100, -100));
        selector = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        press = WorkAreaMouse(f, selector);
        hud.OnMouseDown(press);
        var release = new MouseEvent(press.X, press.Y, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.False(hud.ReservesPointer(-100, -100));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReopenedBuildMenuRestoresGizmoInteractionAfterHiddenPointerRelease(bool grabbed, bool listPress)
    {
        using var f = WorkAreaFixture();
        var world = Mock.Get(f.Api.Object.World);
        var accessor = Mock.Get(f.Api.Object.World.BlockAccessor);
        var workArea = ActiveWorkArea();
        workArea.RestoreEndpoints(new(-30, 65, -200, 0), new(30, 75, -200, 0));
        using var h = new BuildBrush.WorkAreaGizmoSessionTests.Harness(f.Api, world, accessor, workArea);
        h.Render.Setup(r => r.BitmapCreateFromPng(It.IsAny<byte[]>())).Returns(() => new BitmapExternal(1, 1));
        h.Input.Setup(i => i.MouseX).Returns(() => f.MouseX);
        h.Input.Setup(i => i.MouseY).Returns(() => f.MouseY);
        h.Input.Setup(i => i.GetHotKeyByCode(It.IsAny<string>())).Returns((string code) => f.Hotkeys[code]);
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        h.Dialogs.Add(hud);
        hud.SetVisible(true);
        h.Enable();
        for (int repeat = 0; repeat < 3; repeat++)
        {
            if (listPress)
            {
                h.Grabbed = false;
                var selector = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
                var press = WorkAreaMouse(f, selector);
                hud.OnMouseDown(press);
                h.Raw.Left = true;
            }
            workArea.SetContext(false, false);
            hud.SetVisible(false);
            h.Grabbed = true;
            h.Raw.Left = false;
            h.Session.Refresh();
            h.Frame();
            workArea.SetContext(true, true);
            hud.SetVisible(true);
            h.Grabbed = grabbed;
            hud.OnRenderGUI(0);
            for (int frame = 0; frame < 3; frame++) { h.Session.Refresh(); h.Frame(); }
            var start = h.Session.Start!;
            h.Aim(start.Value.Coordinates);
            Assert.False(hud.ReservesPointer(-100, -100));
            Assert.True(h.Down().Handled);
            Assert.Same(start, h.Session.Manager.CapturedGizmo);
            Assert.True(h.Up().Handled);
        }
        Assert.Equal(3, h.Commits);
    }

    [Fact]
    public void DeleteConfirmationCapturesInputUnregistersOnCloseAndEscapeDoesNotDelete()
    {
        using var f = WorkAreaFixture();
        var dialogs = PrepareWorkAreaDialogs(f);
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        ClickWorkAreaCommand(hud, f, "delete");
        var dialog = Assert.Single(dialogs.OfType<GuiDialogConfirm>());
        Assert.True(dialog.UnregisterOnClose);
        Assert.True(dialog.CaptureAllInputs());
        var escape = new KeyEvent { KeyCode = (int)GlKeys.Escape };
        dialog.OnKeyDown(escape);
        Assert.True(escape.Handled);
        Assert.False(dialog.IsOpened());
        Assert.Single(workArea.Areas);
    }

    [Fact]
    public void NamedListUsesLiteralUserTextAndCanceledPressDoesNotSelect()
    {
        using var f = WorkAreaFixture();
        var workArea = ActiveWorkArea();
        workArea.RenameArea(workArea.SelectedId!.Value, "<Garden> & Home");
        var other = workArea.CreateArea("Other");
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var selector = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.Equal("<Garden> & Home", selector.Areas[0].Name);
        var press = WorkAreaListMouse(f, selector, 0);
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        var release = new MouseEvent(-100, -100, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.Equal(other, workArea.SelectedId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void NamedAreaListIsCompactScrollableAndSelectsOnlyOnMatchingRelease(float scale)
    {
        using var f = WorkAreaFixture(scale);
        var workArea = ActiveWorkArea();
        for (int i = 1; i <= 6; i++) workArea.CreateArea("Area " + i);
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var selector = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.Equal(3, selector.VisibleRows);
        Assert.Equal(66, selector.Bounds.fixedHeight);
        Assert.Equal(4, selector.FirstVisible);
        Assert.Equal(workArea.SelectedId, selector.SelectedId);
        WorkAreaListMouse(f, selector, 0);
        for (int i = 0; i < 4; i++)
        {
            var wheel = new MouseWheelEventArgs { delta = 1, deltaPrecise = 1 };
            hud.OnMouseWheel(wheel);
            Assert.True(wheel.IsHandled);
        }
        Assert.Equal(0, selector.FirstVisible);
        var before = workArea.SelectedId;
        var choose = WorkAreaListMouse(f, selector, 1);
        hud.OnMouseDown(choose);
        Assert.True(choose.Handled);
        Assert.Equal(before, workArea.SelectedId);
        var release = new MouseEvent(choose.X, choose.Y, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.Equal(workArea.Areas[1].Id, workArea.SelectedId);
        hud.OnRenderGUI(0);
        Assert.False(hud.ReservesPointer(-100, -100));
        selector = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.Equal(workArea.SelectedId, selector.SelectedId);
        Assert.Equal(0, selector.FirstVisible);
        var outside = new MouseEvent(-100, -100, EnumMouseButton.Left, 0);
        hud.OnMouseDown(outside);
        Assert.False(outside.Handled);
        Assert.False(hud.ReservesPointer(-100, -100));
    }

    [Fact]
    public void NewAndRenameDialogsValidateNamesCaptureKeyboardAndCommitOnlyOnSave()
    {
        using var f = WorkAreaFixture();
        var dialogs = PrepareWorkAreaDialogs(f);
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        ClickWorkAreaCommand(hud, f, "new");
        var dialog = Assert.Single(dialogs.OfType<WorkAreaNameDialog>());
        Assert.True(dialog.IsOpened());
        Assert.True(dialog.Focusable);
        Assert.True(dialog.CaptureAllInputs());
        Assert.True(dialog.DisableMouseGrab);
        Assert.True(dialog.SingleComposer.GetTextInput("name").HasFocus);
        Assert.Equal("Work area 2", dialog.SingleComposer.GetTextInput("name").GetText());
        var typed = new KeyEvent { KeyChar = 'q' };
        dialog.OnKeyPress(typed);
        Assert.True(typed.Handled);
        Assert.Contains("q", dialog.SingleComposer.GetTextInput("name").GetText());
        Assert.Single(workArea.Areas);
        dialog.SingleComposer.GetTextInput("name").SetValue(" ");
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        dialog.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.True(dialog.IsOpened());
        Assert.NotEmpty(dialog.SingleComposer.GetDynamicText("error").GetText());
        dialog.SingleComposer.GetTextInput("name").SetValue("WORK AREA 1");
        dialog.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.True(dialog.IsOpened());
        Assert.Single(workArea.Areas);
        dialog.SingleComposer.GetTextInput("name").SetValue(" Garden ");
        dialog.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.False(dialog.IsOpened());
        Assert.Equal("Garden", workArea.Areas[1].Name);
        Assert.Equal(workArea.Areas[1].Id, workArea.SelectedId);
        Assert.Null(workArea.State.Bounds);
        Assert.True(workArea.State.ShowWorkArea);
        hud.OnRenderGUI(0);
        ClickWorkAreaCommand(hud, f, "rename");
        dialog = dialogs.OfType<WorkAreaNameDialog>().Last();
        dialog.SingleComposer.GetTextInput("name").SetValue("Courtyard");
        dialog.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal("Courtyard", workArea.Areas[1].Name);
        hud.OnRenderGUI(0);
        ClickWorkAreaCommand(hud, f, "new");
        dialog = dialogs.OfType<WorkAreaNameDialog>().Last();
        var escape = new KeyEvent { KeyCode = (int)GlKeys.Escape };
        dialog.OnKeyDown(escape);
        Assert.True(escape.Handled);
        Assert.False(dialog.IsOpened());
        Assert.Equal(2, workArea.Areas.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteRequiresConfirmationAndEmptyCollectionsStillAllowCreatingAnArea(bool confirm)
    {
        using var f = WorkAreaFixture();
        var dialogs = PrepareWorkAreaDialogs(f);
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        ClickWorkAreaCommand(hud, f, "delete");
        Assert.Single(workArea.Areas);
        var dialog = Assert.Single(dialogs.OfType<GuiDialogConfirm>());
        var elements = Assert.IsType<Dictionary<string, GuiElement>>(typeof(GuiComposer)
            .GetField("interactiveElements", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(dialog.SingleComposer));
        var button = elements.Values.OfType<GuiElementTextButton>()
            .Single(button => button.Text == (confirm ? "Confirm" : "Cancel"));
        var mouse = WorkAreaMouse(f, button);
        dialog.OnMouseDown(mouse);
        dialog.OnMouseUp(new MouseEvent(mouse.X, mouse.Y, EnumMouseButton.Left, 0));
        Assert.False(dialog.IsOpened());
        Assert.Equal(confirm ? 0 : 1, workArea.Areas.Count);
        hud.OnRenderGUI(0);
        if (confirm)
        {
            Assert.False(workArea.State.CanPick);
            Assert.False(Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select")).Enabled);
            ClickWorkAreaCommand(hud, f, "new");
            Assert.True(dialogs.OfType<WorkAreaNameDialog>().Last().IsOpened());
        }
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("scale")]
    [InlineData("locale")]
    [InlineData("death")]
    [InlineData("dispose")]
    public void NamingDialogsCloseOnHudLifecycleChangesWithoutCreatingAnArea(string change)
    {
        using var f = WorkAreaFixture();
        var dialogs = PrepareWorkAreaDialogs(f);
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        ClickWorkAreaCommand(hud, f, "new");
        var dialog = Assert.Single(dialogs.OfType<WorkAreaNameDialog>());
        Assert.True(dialog.IsOpened());
        switch (change)
        {
            case "hide": hud.SetVisible(false); break;
            case "scale": f.ScaleWatcher!(1.25f); break;
            case "locale": f.ChangeLocale(); hud.SetVisible(true); break;
            case "death": workArea.SetContext(true, false); hud.OnRenderGUI(0); break;
            case "dispose": hud.Dispose(); break;
        }
        Assert.False(dialog.IsOpened());
        Assert.Single(workArea.Areas);
    }

    private static List<GuiDialog> PrepareWorkAreaDialogs(Fixture f)
    {
        var dialogs = new List<GuiDialog>();
        f.Gui.SetupGet(gui => gui.LoadedGuis).Returns(dialogs);
        f.Gui.SetupGet(gui => gui.OpenedGuis).Returns(() => dialogs.Where(dialog => dialog.IsOpened()).ToList());
        f.Gui.Setup(gui => gui.Icons).Returns(new IconUtil(f.Api.Object));
        f.Gui.Setup(gui => gui.RegisterDialog(It.IsAny<GuiDialog[]>())).Callback<GuiDialog[]>(dialogs.AddRange);
        f.Gui.Setup(gui => gui.RequestFocus(It.IsAny<GuiDialog>())).Callback<GuiDialog>(dialog =>
            typeof(GuiDialog).GetField("focused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(dialog, true));
        return dialogs;
    }

    private static void ClickWorkAreaCommand(BuildModeHud hud, Fixture f, string key)
    {
        var commands = Assert.IsType<GuiElementTable>(WorkAreaTable(hud).GetElement("commands"));
        var mouse = WorkAreaMouse(f, commands.GetElement(key));
        hud.OnMouseDown(mouse);
        Assert.True(mouse.Handled);
        hud.OnMouseUp(new MouseEvent(mouse.X, mouse.Y, EnumMouseButton.Left, 0));
    }

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
    [InlineData("cursor-hud")]
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
        var e = WorkAreaMouse(f, target == "control" ? show : WorkAreaSizeTable(hud).GetElement("label"));
        using var receiver = new ReceivingHud(f.Api.Object);
        var cursorMock = new Mock<HudMouseTools>(f.Api.Object) { CallBase = true };
        cursorMock.Setup(gui => gui.ShouldReceiveMouseEvents()).Returns(true);
        using var cursor = cursorMock.Object;
        if (target == "native-hud")
        {
            var bounds = ElementBounds.Fixed(e.X - 5, e.Y - 5, 10, 10).WithEmptyParent();
            bounds.CalcWorldBounds();
            receiver.SingleComposer = f.Composer(bounds);
            dialogs.Add(receiver);
        }
        if (target == "cursor-hud")
        {
            var bounds = ElementBounds.Fixed(0, 0, 1920, 1080).WithEmptyParent();
            bounds.CalcWorldBounds();
            cursor.Composers["mouseSlot"] = f.Composer(bounds);
            cursor.Composers["itemstackinfo"] = f.Composer(bounds.FlatCopy().WithEmptyParent());
            dialogs.Add(cursor);
            var nativeClick = new MouseEvent(e.X, e.Y, EnumMouseButton.Left, 0);
            cursor.OnMouseDown(nativeClick);
            Assert.False(nativeClick.Handled);
        }
        var context = Gizmos.ComposedPositionGizmoTests.Camera(1920, 1080);
        var pointer = context.Camera!.CreatePointer(context, e.X, e.Y);
        using var manager = new GizmoManager();
        using var renderer = new GizmoRenderAdapter(f.Api.Object, manager, () => context,
            new BuildBrush.WorkAreaRenderingTests.FakeState());
        var gizmo = Gizmos.ComposedPositionGizmoTests.Create(new(pointer.Ray.Origin + pointer.Ray.Direction * 20, 0), _ => { });
        manager.Add(gizmo);
        using var input = new GizmoInputAdapter(f.Api.Object, manager, renderer, () => true, []);
        renderer.SetActive(true);
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        renderer.OnRenderFrame(0, EnumRenderStage.AfterBlit);
        var events = Mock.Get(f.Api.Object.Event);
        events.Raise(ev => ev.MouseMove += null!, new MouseEvent(e.X, e.Y));
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        bool canPick = target is "passive" or "cursor-hud";
        Assert.Equal(canPick, gizmo.HoveredHandle is not null);
        renderer.OnRenderFrame(0, EnumRenderStage.AfterBlit);
        raw.Left = true;
        events.Raise(ev => ev.MouseDown += null!, e);
        Assert.Equal(canPick, e.Handled);
        Assert.Equal(canPick, ReferenceEquals(gizmo, manager.CapturedGizmo));
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
        f.Api.Setup(a => a.Event.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.OIT, It.IsAny<string>()))
            .Callback<IRenderer, EnumRenderStage, string>((value, _, _) =>
            {
                renderer = value;
                registrations++;
            });
        f.Api.Setup(a => a.Event.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.OIT))
            .Callback(() => unregistrations++);
        var shader = new Mock<IShaderProgram>();
        shader.SetupGet(s => s.LoadError).Returns(true);
        f.Api.Setup(a => a.Shader.NewShaderProgram()).Returns(shader.Object);
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
        renderer.OnRenderFrame(0, EnumRenderStage.OIT);
        binding.Refresh();
        Assert.Equal(1, unregistrations);
        hud.OnRenderGUI(0);
        Assert.Equal("Size: 5, 3, 4", WorkAreaSize(hud));
        Assert.Equal(5, WorkAreaTable(hud).Bounds.ChildBounds.Count);
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
    public void WorkAreaSection_HasSelectorCommandsVisibilityAndSharedInclusiveSizes(float scale)
    {
        using var f = WorkAreaFixture(scale);
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var table = WorkAreaTable(hud);
        Assert.Equal(5, table.Bounds.ChildBounds.Count);
        var selector = Assert.IsType<WorkAreaListBox>(table.GetElement("select"));
        Assert.Equal(workArea.SelectedId, selector.SelectedId);
        Assert.Equal(new[] { "Work area 1" }, selector.Areas.Select(area => area.Name));
        var commands = Assert.IsType<GuiElementTable>(table.GetElement("commands"));
        Assert.Equal(3, commands.Bounds.ChildBounds.Count);
        Assert.True(selector.Bounds.renderY + selector.Bounds.OuterHeight <= commands.Bounds.renderY);
        Assert.True(commands.Bounds.renderY + commands.Bounds.OuterHeight <= table.GetElement("visibility").Bounds.renderY);
        var visibility = Assert.IsType<GuiElementTable>(table.GetElement("visibility"));
        var size = WorkAreaSizeTable(hud);
        Assert.Equal(2, visibility.Bounds.ChildBounds.Count);
        Assert.Equal("Show work area", Assert.IsType<GuiElementStaticText>(visibility.GetElement("label")).GetText());
        Assert.Throws<KeyNotFoundException>(() => table.GetElement("endpoints"));
        Assert.Equal("Size: -, -, -", WorkAreaSize(hud));
        foreach (string axis in new[] { "x", "y", "z" })
            Assert.False(Assert.IsAssignableFrom<GuiElementNumberInput>(size.GetElement(axis)).Enabled);
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
    public void NativeWorkAreaSwitchTogglesOnPressAndRetainsVisibilityAcrossCollapse()
    {
        using var f = WorkAreaFixture();
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var checkbox = Assert.IsType<GuiElementSwitch>(WorkAreaElement(hud, "show"));
        var press = WorkAreaMouse(f, checkbox);
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        Assert.True(workArea.State.ShowWorkArea);
        hud.OnMouseUp(new MouseEvent(press.X, press.Y, EnumMouseButton.Left, 0));
        hud.OnRenderGUI(0);
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

    [Fact]
    public void NativeWorkAreaSwitchDragOutsideDoesNotUndoToggleAndPassiveAreasNeverTakeInput()
    {
        using var f = WorkAreaFixture();
        var workArea = ActiveWorkArea();
        using var hud = new BuildModeHud(f.Api.Object, () => default, workArea);
        hud.SetVisible(true);
        var control = WorkAreaElement(hud, "show");
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
        Assert.False(release.Handled);
        Assert.False(WorkAreaTable(hud).HasPointerCapture);
        Assert.True(workArea.State.ShowWorkArea);
        foreach (GuiElement passive in new[]
        {
            WorkAreaSizeTable(hud).GetElement("label"),
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
        ClickWorkArea(hud, f, "show");
        Assert.False(workArea.State.ShowWorkArea);
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
        Assert.Equal(1, changes);
        Assert.True(workArea.State.ShowWorkArea);
        if (change != "dispose")
        {
            f.MouseGrabbed = false;
            hud.SetVisible(true);
            if (change == "collapse") ClickToggle(hud, f, "workArea");
            ClickWorkArea(hud, f, "show");
            Assert.Equal(2, changes);
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
            "vanillabuildingexpanded:build-info-workarea-default-name" => string.Format("Work area {0}", args),
            "vanillabuildingexpanded:build-info-workarea-show" => "Show work area",
            "vanillabuildingexpanded:build-info-workarea-unset" => "-",
            "vanillabuildingexpanded:build-info-workarea-empty" => "No work areas",
            "vanillabuildingexpanded:build-info-workarea-size" => "Size:",
            "vanillabuildingexpanded:build-info-workarea-resize-mode" => string.Format("Resize: {0}", args),
            "vanillabuildingexpanded:build-info-workarea-resize-expansion" => "Expansion",
            "vanillabuildingexpanded:build-info-workarea-resize-centered" => "Centered",
            "vanillabuildingexpanded:build-info-workarea-size-x" => "X",
            "vanillabuildingexpanded:build-info-workarea-size-y" => "Y",
            "vanillabuildingexpanded:build-info-workarea-size-z" => "Z",
            _ => key.Split(':').Last()
        };
        return fixture;
    }

    private static GuiElementTable WorkAreaTable(BuildModeHud hud) =>
        Assert.IsType<GuiElementTable>(hud.SingleComposer.GetElement("workarea-table"));

    private static GuiElementTable WorkAreaSizeTable(BuildModeHud hud) =>
        Assert.IsType<GuiElementTable>(WorkAreaTable(hud).GetElement("size"));

    private static string WorkAreaSize(BuildModeHud hud) =>
        "Size: " + string.Join(", ", new[] { "x", "y", "z" }.Select(axis =>
            Assert.IsAssignableFrom<GuiElementNumberInput>(WorkAreaSizeTable(hud).GetElement(axis)).GetText()));

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
        Assert.False(release.Handled);
        hud.OnRenderGUI(0);
    }

    private static MouseEvent WorkAreaListMouse(Fixture f, WorkAreaListBox list, int row)
    {
        f.MouseX = (int)list.Bounds.renderX + 10;
        f.MouseY = (int)(list.Bounds.renderY + (row + 0.5) * list.RowHeight * RuntimeEnv.GUIScale);
        return new MouseEvent(f.MouseX, f.MouseY, EnumMouseButton.Left, 0);
    }
}
