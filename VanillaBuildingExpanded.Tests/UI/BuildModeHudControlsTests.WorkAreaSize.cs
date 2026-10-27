using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed partial class BuildModeHudControlsTests
{
    [Theory]
    [InlineData("x", -14, 61, -32, -6, 67, -28)]
    [InlineData("y", -12, 60, -32, -8, 68, -28)]
    [InlineData("z", -12, 61, -34, -8, 67, -26)]
    public void DimensionFieldsCaptureTypingAndCommitOnlyTheirAxisOnEnter(string axis,
        int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        int commits = 0;
        area.EndpointsCommitted += () => commits++;
        var input = FocusDimension(hud, f, axis);
        var original = area.State;
        var backspace = new KeyEvent { KeyCode = (int)GlKeys.BackSpace };
        hud.OnKeyDown(backspace);
        var typed = new KeyEvent { KeyChar = '9' };
        hud.OnKeyPress(typed);
        Assert.True(backspace.Handled);
        Assert.True(typed.Handled);
        Assert.Equal("9", input.GetText());
        Assert.Equal(original, area.State);
        Assert.Equal(0, commits);
        Assert.True(hud.Focusable);
        Assert.True(hud.Focused);
        Assert.True(hud.CaptureAllInputs());
        Assert.True(hud.DisableMouseGrab);
        Assert.True(hud.ShouldReceiveKeyboardEvents());
        hud.OnRenderGUI(0.6f);
        Assert.True(input.HasFocus);
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        hud.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.Equal(new WorkAreaEndpoint(minX, minY, minZ, 0), area.State.Start);
        Assert.Equal(new WorkAreaEndpoint(maxX, maxY, maxZ, 0), area.State.End);
        Assert.Equal(1, commits);
        Assert.False(input.HasFocus);
        Assert.False(hud.Focusable);
        Assert.False(hud.Focused);
        Assert.False(hud.CaptureAllInputs());
        Assert.False(hud.ShouldReceiveKeyboardEvents());
        Assert.False(hud.ReservesPointer(-100, -100));
    }

    [Fact]
    public void DimensionBlurCommitsAndConsumesTheOutsideGestureWithoutWorldClickThrough()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var input = FocusDimension(hud, f, "x");
        input.SetValue("6");
        Assert.True(hud.ReservesPointer(-100, -100));
        var press = new MouseEvent(-100, -100, EnumMouseButton.Left, 0);
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        Assert.Equal(6, area.State.Bounds!.Value.SizeX);
        Assert.Equal(-12, area.State.Start!.Value.X);
        Assert.Equal(-7, area.State.End!.Value.X);
        Assert.False(hud.ShouldReceiveKeyboardEvents());
        hud.OnRenderGUI(0);
        Assert.True(hud.ReservesPointer(-100, -100));
        var release = new MouseEvent(-100, -100, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.False(hud.ReservesPointer(-100, -100));
    }

    [Fact]
    public void ClickingAnotherFieldAndTabCommitWithoutLosingTheNewFocusOnTheNextFrame()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        FocusDimension(hud, f, "x").SetValue("7");
        var y = FocusDimension(hud, f, "y");
        Assert.Equal(7, area.State.Bounds!.Value.SizeX);
        hud.OnRenderGUI(0);
        Assert.True(y.HasFocus);
        y.SetValue("9");
        var tab = new KeyEvent { KeyCode = (int)GlKeys.Tab };
        hud.OnKeyDown(tab);
        Assert.True(tab.Handled);
        Assert.Equal(9, area.State.Bounds!.Value.SizeY);
        hud.OnRenderGUI(0);
        var z = DimensionInput(hud, "z");
        Assert.True(z.HasFocus);
        Assert.False(y.HasFocus);
        hud.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = true });
        Assert.True(y.HasFocus);
        Assert.False(z.HasFocus);
        hud.UnFocus();
        Assert.False(hud.ShouldReceiveKeyboardEvents());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("99999999999999999999999999999")]
    public void InvalidDimensionDraftsReportAnErrorAndRestoreTheActualSize(string text)
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        int errors = 0, commits = 0;
        area.NoticeRequested += _ => errors++;
        area.EndpointsCommitted += () => commits++;
        f.Api.Setup(a => a.TriggerIngameError(It.IsAny<object>(), "vanillabuildingexpanded:workarea-invalid-size",
            It.IsAny<string>())).Callback(() => errors++);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var input = FocusDimension(hud, f, "x");
        var original = area.State;
        input.SetValue(text);
        hud.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(original, area.State);
        Assert.Equal("5", input.GetText());
        Assert.Equal(1, errors);
        Assert.Equal(0, commits);
        Assert.False(hud.CaptureAllInputs());
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("scale")]
    [InlineData("locale")]
    [InlineData("grab")]
    [InlineData("death")]
    [InlineData("select")]
    [InlineData("dispose")]
    public void DimensionDraftsCancelWhenTheirHudOrAreaContextIsRetired(string change)
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        var first = area.Areas[0];
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var input = FocusDimension(hud, f, "x");
        input.SetValue("9");
        int commits = 0;
        area.EndpointsCommitted += () => commits++;
        switch (change)
        {
            case "hide": hud.SetVisible(false); break;
            case "close": hud.TryClose(); break;
            case "collapse": ClickToggle(hud, f, "workArea"); break;
            case "scale": f.ScaleWatcher!(1.25f); break;
            case "locale": f.ChangeLocale(); hud.SetVisible(true); break;
            case "grab": f.MouseGrabbed = true; hud.OnRenderGUI(0); break;
            case "death": area.SetContext(true, false); break;
            case "select": area.CreateArea("Other"); break;
            case "dispose": hud.Dispose(); break;
        }
        Assert.False(input.HasFocus);
        Assert.False(hud.CaptureAllInputs());
        Assert.False(hud.ShouldReceiveKeyboardEvents());
        Assert.Equal(first.Start, area.Areas[0].Start);
        Assert.Equal(first.End, area.Areas[0].End);
        Assert.Equal(0, commits);
    }

    [Fact]
    public void EscapeCancelsDraftWhileOrdinaryFocusLossCommitsAndUnchangedFieldsDoNotSave()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        int commits = 0;
        area.EndpointsCommitted += () => commits++;
        var input = FocusDimension(hud, f, "x");
        input.SetValue("9");
        Assert.True(hud.OnEscapePressed());
        Assert.Equal("5", input.GetText());
        Assert.Equal(0, commits);
        FocusDimension(hud, f, "x").SetValue("7");
        hud.UnFocus();
        Assert.Equal(7, area.State.Bounds!.Value.SizeX);
        Assert.Equal(1, commits);
        FocusDimension(hud, f, "x");
        hud.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(1, commits);
    }

    [Fact]
    public void ClickingCollapseCommitsTheFieldBeforeRetiringItsComposer()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        FocusDimension(hud, f, "x").SetValue("9");
        ClickToggle(hud, f, "workArea");
        Assert.Equal(9, area.State.Bounds!.Value.SizeX);
        Assert.False(hud.CaptureAllInputs());
        Assert.Null(hud.SingleComposer.GetElement("workarea-table"));
    }

    [Fact]
    public void DimensionFieldsSupportFullSpansAndIntegerSpinnerModifiersWithoutFloatRounding()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        area.RestoreEndpoints(new(int.MinValue, 0, int.MinValue, 0),
            new(int.MaxValue, 32767, int.MaxValue, 0));
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var input = FocusDimension(hud, f, "x");
        Assert.Equal("4294967296", input.GetText());
        hud.OnKeyUp(new KeyEvent { KeyCode = (int)GlKeys.Down });
        Assert.Equal("4294967295", input.GetText());
        Assert.Equal(4294967295L, area.State.Bounds!.Value.SizeX);
        f.Api.Object.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftLeft] = true;
        hud.OnKeyUp(new KeyEvent { KeyCode = (int)GlKeys.Down });
        Assert.Equal("4294967285", input.GetText());
        Assert.Equal(4294967285L, area.State.Bounds!.Value.SizeX);
        f.Api.Object.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftLeft] = false;
        input.SetValue("4294967294");
        hud.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(4294967294L, area.State.Bounds!.Value.SizeX);
        Assert.Equal("4294967294", input.GetText());
    }

    [Fact]
    public void ArrowsApplyImmediatelyWhileWheelEditsRemainDrafts()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var input = DimensionInput(hud, "x");
        int x = (int)(input.Bounds.renderX + input.Bounds.OuterWidth - 5);
        int y = (int)(input.Bounds.renderY + input.Bounds.OuterHeight / 4);
        hud.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        hud.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.True(input.HasFocus);
        Assert.Equal("6", input.GetText());
        Assert.Equal(6, area.State.Bounds!.Value.SizeX);
        f.MouseX = x;
        f.MouseY = y;
        var wheel = new MouseWheelEventArgs { delta = -1, deltaPrecise = -1 };
        hud.OnMouseWheel(wheel);
        Assert.True(wheel.IsHandled);
        Assert.Equal("5", input.GetText());
        Assert.Equal(6, area.State.Bounds!.Value.SizeX);
        f.Api.Object.Input.KeyboardKeyStateRaw[(int)GlKeys.ControlLeft] = true;
        hud.OnKeyUp(new KeyEvent { KeyCode = (int)GlKeys.Up });
        Assert.Equal("105", input.GetText());
        Assert.Equal(105, area.State.Bounds!.Value.SizeX);
        hud.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(105, area.State.Bounds!.Value.SizeX);
    }

    [Theory]
    [InlineData("x", 1, 1f)]
    [InlineData("y", -1, 1.33f)]
    [InlineData("z", 1, 2f)]
    public void SpinnerPressesResizeImmediatelyAndRetainFocusWithoutDuplicateCommits(
        string axis, int direction, float scale)
    {
        using var f = WorkAreaFixture(scale);
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var input = DimensionInput(hud, axis);
        long original = axis == "y" ? 7 : 5;
        int commits = 0;
        area.EndpointsCommitted += () => commits++;
        int x = (int)(input.Bounds.renderX + input.Bounds.OuterWidth - 5 * scale);
        int y = (int)(input.Bounds.renderY + input.Bounds.OuterHeight * (direction > 0 ? 0.25 : 0.75));
        for (int step = 1; step <= 2; step++)
        {
            hud.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            Assert.Equal((original + direction * step).ToString(), input.GetText());
            var bounds = area.State.Bounds!.Value;
            Assert.Equal(original + direction * step,
                axis == "x" ? bounds.SizeX : axis == "y" ? bounds.SizeY : bounds.SizeZ);
            Assert.Equal(axis == "x" ? original + direction * step : 5, bounds.SizeX);
            Assert.Equal(axis == "y" ? original + direction * step : 7, bounds.SizeY);
            Assert.Equal(axis == "z" ? original + direction * step : 5, bounds.SizeZ);
            Assert.Equal(step, commits);
            hud.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            hud.OnRenderGUI(0);
            Assert.Same(input, DimensionInput(hud, axis));
            Assert.True(input.HasFocus);
            Assert.True(hud.CaptureAllInputs());
        }
        hud.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(2, commits);
        Assert.False(input.HasFocus);
    }

    [Fact]
    public void RejectedImmediateResizeRestoresTheActualDimensionWithoutLosingFocus()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var input = FocusDimension(hud, f, "x");
        var original = area.State;
        int commits = 0, errors = 0;
        area.EndpointsCommitted += () => commits++;
        area.NoticeRequested += _ => errors++;
        f.Api.Setup(a => a.World.BlockAccessor.IsValidPos(It.IsAny<BlockPos>())).Returns(false);
        hud.OnKeyUp(new KeyEvent { KeyCode = (int)GlKeys.Up });
        Assert.Equal(original, area.State);
        Assert.Equal("5", input.GetText());
        Assert.Equal(0, commits);
        Assert.Equal(1, errors);
        Assert.True(input.HasFocus);
        Assert.True(hud.CaptureAllInputs());
    }

    [Fact]
    public void DimensionEditingBlocksWorldGizmosThenUpdatesTheirPositionsAndRestoresInteraction()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        area.RestoreEndpoints(new(90, 65, -200, 0), new(150, 75, -200, 0));
        using var h = new BuildBrush.WorkAreaGizmoSessionTests.Harness(f.Api,
            Mock.Get(f.Api.Object.World), Mock.Get(f.Api.Object.World.BlockAccessor), area);
        h.Input.Setup(i => i.MouseX).Returns(() => f.MouseX);
        h.Input.Setup(i => i.MouseY).Returns(() => f.MouseY);
        h.Input.Setup(i => i.GetHotKeyByCode(It.IsAny<string>())).Returns((string code) => f.Hotkeys[code]);
        h.Render.Setup(r => r.BitmapCreateFromPng(It.IsAny<byte[]>())).Returns(() => new BitmapExternal(1, 1));
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        h.Dialogs.Add(hud);
        h.Enable();
        hud.SetVisible(true);
        h.Frame();
        var input = FocusDimension(hud, f, "x");
        input.SetValue("81");
        h.Aim(h.Session.Start!.Value.Coordinates);
        Assert.False(h.Down().Handled);
        Assert.Null(h.Session.Manager.CapturedGizmo);
        h.Up();
        hud.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(1, h.Commits);
        Assert.Equal((81L, 11L, 1L), h.Size);
        for (int frame = 0; frame < 3; frame++) { h.Session.Refresh(); h.Frame(); }
        Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(area.State.Start!.Value, WorkAreaEndpointRole.Start),
            h.Session.Start!.Value);
        Assert.Equal(WorkAreaGizmoCoordinates.ToAnchor(area.State.End!.Value, WorkAreaEndpointRole.End),
            h.Session.End!.Value);
        h.Aim(h.Session.Start.Value.Coordinates);
        Assert.True(h.Down().Handled);
        Assert.Same(h.Session.Start, h.Session.Manager.CapturedGizmo);
        Assert.True(h.Up().Handled);
        Assert.Equal(2, h.Commits);
    }

    private static WorkAreaController EditableWorkArea(Fixture f)
    {
        f.Api.Setup(a => a.World.BlockAccessor.IsValidPos(It.IsAny<BlockPos>())).Returns(true);
        f.Gui.Setup(g => g.RequestFocus(It.IsAny<GuiDialog>())).Callback<GuiDialog>(dialog => dialog.Focus());
        var area = ActiveWorkArea();
        area.RestoreEndpoints(new(-12, 61, -32, 0), new(-8, 67, -28, 0));
        return area;
    }

    private static GuiElementNumberInput DimensionInput(BuildModeHud hud, string axis) =>
        Assert.IsAssignableFrom<GuiElementNumberInput>(WorkAreaSizeTable(hud).GetElement(axis));

    private static GuiElementNumberInput FocusDimension(BuildModeHud hud, Fixture f, string axis)
    {
        var input = DimensionInput(hud, axis);
        var press = WorkAreaMouse(f, input);
        hud.OnMouseDown(press);
        hud.OnMouseUp(new MouseEvent(press.X, press.Y, EnumMouseButton.Left, 0));
        Assert.True(press.Handled);
        Assert.True(input.HasFocus);
        return input;
    }
}
