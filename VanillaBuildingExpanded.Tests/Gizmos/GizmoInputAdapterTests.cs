using Moq;
using VanillaBuildingExpanded.Gizmos;
using VanillaBuildingExpanded.Tests.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;
using static VanillaBuildingExpanded.Tests.Gizmos.PositionGizmoTests;

namespace VanillaBuildingExpanded.Tests.Gizmos;

[Collection("Table composition")]
public sealed class GizmoInputAdapterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ActualPointerModes_UpdateAfterCameraAndCompleteWithFinalDrawnPointer(bool grabbed, bool thirdPerson)
    {
        using var f = new Fixture(grabbed, thirdPerson);
        Assert.True(f.Down().Handled);
        Assert.Same(f.Gizmo, f.Manager.CapturedGizmo);
        Assert.Equal(PositionGizmo.Center, f.Manager.CapturedHandle);
        var move = f.Move(600, 450);
        Assert.False(move.Handled);
        Assert.Equal(f.Baseline, f.Gizmo.Value);
        if (grabbed) f.ViewX = 2;
        f.Frame();
        Assert.NotEqual(f.Baseline, f.Gizmo.Value);
        var drawn = f.Renderer.Context!;
        AssertCoordinates(f.Gizmo.Value.Coordinates, f.LastEdit!.Value.Candidate.Coordinates);
        if (grabbed)
        {
            drawn.Camera!.TryProject(f.Gizmo.Value.Coordinates, out var screen);
            Assert.Equal(400, screen[0], 6);
            Assert.Equal(300, screen[1], 6);
        }
        var up = f.Up(650, 400);
        Assert.True(up.Handled);
        Assert.Equal(1, f.Completes);
        Assert.Null(f.Manager.CapturedGizmo);
        AssertCoordinates(f.Gizmo.Value.Coordinates, f.LastEdit!.Value.Candidate.Coordinates);
        if (!grabbed)
        {
            drawn.Camera!.TryProject(f.Gizmo.Value.Coordinates, out var screen);
            Assert.Equal(650, screen[0], 6);
            Assert.Equal(400, screen[1], 6);
        }
        f.SuppressBothFamilies();
        f.AssertNoGameplay();
    }

    [Theory]
    [InlineData("handled")]
    [InlineData("dialog")]
    [InlineData("raw")]
    [InlineData("all")]
    [InlineData("paused")]
    [InlineData("dead")]
    [InlineData("disabled")]
    [InlineData("focus")]
    [InlineData("undrawn")]
    public void PriorityAndEligibility_PreventCaptureWithoutOwningOrdinaryInput(string reason)
    {
        using var f = new Fixture();
        f.Change(reason);
        Assert.Equal(reason == "handled", f.Down(handled: reason == "handled").Handled);
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.Equal(0, f.Starts);
        f.Action(EnumEntityAction.InWorldLeftMouseDown, true);
        Assert.Equal(1, f.Mining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryPressOutsideHandles_CannotBecomeCaptureWhileHeld(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        f.Gizmo.Visible = false;
        f.Frame();
        Assert.False(f.Down(10, 10).Handled);
        f.Gizmo.Visible = true;
        f.Frame();
        Assert.False(f.Down().Handled);
        Assert.Null(f.Manager.CapturedGizmo);
        f.Action(EnumEntityAction.InWorldRightMouseDown, true);
        Assert.Equal(1, f.Placement);
        Assert.False(f.Up().Handled);
        Assert.True(f.Down().Handled);
    }

    [Theory]
    [InlineData("dialog")]
    [InlineData("paused")]
    [InlineData("dead")]
    [InlineData("disabled")]
    [InlineData("focus")]
    [InlineData("dimension")]
    [InlineData("mode")]
    [InlineData("camera")]
    [InlineData("world")]
    [InlineData("player")]
    [InlineData("dimension-silent")]
    [InlineData("viewport")]
    [InlineData("hidden")]
    public void ContextLoss_CancelsPreviewAndRetainsGestureSuppression(string reason)
    {
        using var f = new Fixture();
        f.Down();
        f.Move(550, 350);
        f.Frame();
        Assert.NotEqual(f.Baseline, f.Gizmo.Value);
        f.Change(reason);
        f.Input.Refresh();
        Assert.Equal(f.Baseline, f.Gizmo.Value);
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.Equal(1, f.Cancels);
        f.SuppressBothFamilies();
        f.Up();
        Assert.Equal(0, f.Completes);
        f.AssertNoGameplay();
    }

    [Theory]
    [InlineData("dialog")]
    [InlineData("disabled")]
    [InlineData("dimension-silent")]
    [InlineData("mode")]
    [InlineData("undrawn")]
    public void PendingCreation_ContextRetirementIsNotExplicitUserCancellation(string reason)
    {
        using var f = new Fixture();
        f.Creation.Pending = true;
        f.Change(reason);
        f.Input.Refresh();
        Assert.False(f.Creation.Pending);
        Assert.False(f.Creation.ExplicitlyCanceled);
        Assert.Equal(1, f.Creation.Cancels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenRelease_CancelsAndClearsOnlyOwnedWorldButton(bool dialog)
    {
        using var f = new Fixture();
        f.Down();
        f.Move(500, 300);
        f.Frame();
        f.WorldButtons.Left = f.WorldButtons.Right = true;
        if (dialog) f.Change("dialog");
        f.Raw.Left = false;
        f.Input.Refresh();
        Assert.Equal(f.Baseline, f.Gizmo.Value);
        Assert.Equal(1, f.Cancels);
        Assert.False(f.WorldButtons.Left);
        Assert.True(f.WorldButtons.Right);
        f.SuppressBothFamilies();
        f.AssertNoGameplay();
        f.Dialogs.Clear();
        f.Frame();
        Assert.True(f.Down().Handled);
        Assert.Equal(2, f.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightAndEscapeCancellation_OwnCompleteGesturesWithoutBlockingNextPress(bool escape)
    {
        using var f = new Fixture();
        f.Down();
        f.Move(500, 300);
        f.Frame();
        if (escape)
        {
            Assert.True(f.Key().Handled);
            Assert.True(f.Key().Handled);
            Assert.True(f.Key(up: true).Handled);
            Assert.False(f.Key().Handled);
        }
        else
        {
            Assert.True(f.Down(button: EnumMouseButton.Right).Handled);
            Assert.True(f.Down(button: EnumMouseButton.Right).Handled);
            Assert.True(f.Up(button: EnumMouseButton.Right).Handled);
        }
        Assert.Equal(f.Baseline, f.Gizmo.Value);
        Assert.Equal(1, f.Cancels);
        Assert.Equal(0, f.Completes);
        f.Up();
        f.SuppressBothFamilies(includeRight: !escape);
        f.AssertNoGameplay();
        f.Frame();
        Assert.True(f.Down().Handled);
    }

    [Fact]
    public void DialogPriority_CancelsButDoesNotStealEscape()
    {
        using var f = new Fixture();
        f.Down();
        f.Change("dialog");
        Assert.False(f.Key().Handled);
        Assert.False(f.Key(up: true).Handled);
        Assert.Equal(1, f.Cancels);
    }

    [Fact]
    public void FocusReset_DoesNotRequireAnExtraPressAfterNativeButtonReset()
    {
        using var f = new Fixture();
        f.Down();
        foreach (var callback in f.Focus.ToArray()) callback(false);
        f.Raw.Left = false;
        foreach (var callback in f.Focus.ToArray()) callback(true);
        f.Frame();
        Assert.True(f.Down().Handled);
        Assert.Equal(2, f.Starts);
        Assert.Equal(1, f.Cancels);
    }

    [Fact]
    public void HiddenEscapeRelease_DoesNotStealTheNextOrdinaryEscape()
    {
        using var f = new Fixture();
        f.Down();
        Assert.True(f.Key().Handled);
        f.Keys[(int)GlKeys.Escape] = false;
        f.Input.Refresh();
        Assert.False(f.Key().Handled);
        Assert.Equal(1, f.Cancels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AxisCapture_PreservesGrabOffsetAndOtherComponentsOutsideHandles(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        var start = grabbed ? new GizmoPosition(V(-6, 0, -200), 0) : f.Baseline;
        f.Gizmo.Value = start;
        f.Frame();
        Assert.True(f.Down(grabbed ? 400 : 409, 300).Handled);
        Assert.Equal(PositionGizmo.X, f.Manager.CapturedHandle);
        f.Frame();
        Assert.Equal(start.Coordinates[0], f.Gizmo.Value.Coordinates[0], 8);
        if (grabbed) f.ViewX = 4;
        else f.Move(630, 300);
        f.Frame();
        Assert.NotEqual(start.Coordinates[0], f.Gizmo.Value.Coordinates[0]);
        Assert.Equal(start.Coordinates[1], f.Gizmo.Value.Coordinates[1]);
        Assert.Equal(start.Coordinates[2], f.Gizmo.Value.Coordinates[2]);
        if (grabbed) Assert.Equal(start.Coordinates[0] + 4, f.Gizmo.Value.Coordinates[0], 8);
        f.Up(grabbed ? 400 : 630, 300);
        Assert.Equal(1, f.Completes);
        f.SuppressBothFamilies();
        f.AssertNoGameplay();
    }

    [Fact]
    public void HeldAtConstruction_RequiresReleaseBeforeNewCapture()
    {
        using var f = new Fixture(held: true);
        Assert.False(f.Down().Handled);
        Assert.Equal(0, f.Starts);
        Assert.False(f.Up().Handled);
        Assert.True(f.Down().Handled);
        Assert.Equal(1, f.Starts);
        Assert.True(f.Down().Handled);
        Assert.Equal(1, f.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingCreationWithNoInstances_UsesDrawnPointerAndCanCancel(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        f.Manager.Remove(f.Gizmo);
        f.Creation.Pending = true;
        f.Frame();
        Assert.True(f.Down(600, 400).Handled);
        Assert.Equal(1, f.Creation.Attempts);
        Assert.Same(f.Renderer.Context, f.Creation.LastPointer!.Value.Context);
        var pointer = f.Creation.LastPointer.Value;
        pointer.Context.Camera!.TryProject(pointer.Ray.Origin + pointer.Ray.Direction * 10, out var screen);
        Assert.Equal(grabbed ? 400 : 600, screen[0], 6);
        Assert.Equal(grabbed ? 300 : 400, screen[1], 6);
        Assert.Null(f.Manager.CapturedGizmo);
        Assert.True(f.Up().Handled);
        Assert.True(f.Key().Handled);
        Assert.False(f.Creation.Pending);
        Assert.True(f.Creation.ExplicitlyCanceled);
        Assert.True(f.Key(up: true).Handled);
        f.SuppressBothFamilies();
        f.AssertNoGameplay();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HundredsOfCompleteCancelRetryCycles_HaveNoStaleOwnership(bool grabbed)
    {
        using var f = new Fixture(grabbed);
        for (int i = 0; i < 120; i++)
        {
            f.Gizmo.Value = f.Baseline;
            f.Frame();
            Assert.True(f.Down().Handled);
            if (i % 2 == 0) f.Key();
            f.Move(500, 300);
            f.Frame();
            Assert.True(f.Up().Handled);
            f.Key(up: true);
            f.SuppressBothFamilies();
            f.AssertNoGameplay();
        }
        Assert.Equal(120, f.Starts);
        Assert.Equal(60, f.Completes);
        Assert.Equal(60, f.Cancels);
        Assert.Single(f.Focus);
        f.Events.Verify(e => e.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Opaque,
            It.IsAny<string>()), Times.Once);
        f.Events.Verify(e => e.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.AfterBlit,
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void PendingCreation_RespectsHandlePriorityAndOwnsInvalidAttempts()
    {
        using var f = new Fixture();
        f.Creation.Pending = true;
        f.Down();
        Assert.Equal(1, f.Starts);
        Assert.Equal(0, f.Creation.Attempts);
        f.Up();
        f.Gizmo.Visible = false;
        f.Frame();
        Assert.True(f.Down().Handled);
        Assert.Equal(1, f.Creation.Attempts);
        Assert.True(f.Creation.Pending);
        f.Up();
        f.SuppressBothFamilies();
        f.AssertNoGameplay();
        Assert.True(f.Down(button: EnumMouseButton.Right).Handled);
        Assert.False(f.Creation.Pending);
        Assert.Equal(1, f.Creation.Cancels);
        Assert.True(f.Creation.ExplicitlyCanceled);
        f.Up(button: EnumMouseButton.Right);
        f.SuppressBothFamilies(includeRight: true);
        f.AssertNoGameplay();
    }

    [Fact]
    public void Departure_DisposesOnceCancelsAndDetachesEverySubscription()
    {
        using var f = new Fixture();
        f.Down();
        f.Move(550, 350);
        f.Frame();
        f.Events.Raise(e => e.LeaveWorld += null!);
        f.Input.Dispose();
        Assert.Equal(f.Baseline, f.Gizmo.Value);
        Assert.Equal(1, f.Cancels);
        Assert.Empty(f.Focus);
        Assert.False(f.WorldButtons.Left);
        Assert.False(f.Down().Handled);
        Assert.False(f.Up().Handled);
        foreach (string name in new[] { "MouseDown", "MouseUp", "MouseMove", "KeyDown", "KeyUp",
            "LeaveWorld", "PlayerDimensionChanged" })
        {
            Assert.Single(f.Events.Invocations, i => i.Method.Name == $"add_{name}");
            Assert.Single(f.Events.Invocations, i => i.Method.Name == $"remove_{name}");
        }
    }

    [Fact]
    public void InputFailure_LogsNotifiesRollsBackAndDisablesNewCapture()
    {
        using var f = new Fixture();
        f.Down();
        f.ThrowConstraint = true;
        f.Frame();
        Assert.Equal(1, f.Cancels);
        Assert.Equal(f.Baseline, f.Gizmo.Value);
        f.Api.Verify(a => a.TriggerIngameError(It.IsAny<object>(),
            "vanillabuildingexpanded:gizmo-input-unavailable", It.IsAny<string>()), Times.Once);
        f.Api.Verify(a => a.Logger.Error(It.IsAny<string>(), It.IsAny<object[]>()), Times.Once);
        f.Up();
        f.ThrowConstraint = false;
        f.Frame();
        Assert.False(f.Down().Handled);
    }

    [Fact]
    public void EarlierHandlingAndUnrelatedControls_ArePreserved()
    {
        using var f = new Fixture();
        f.Down();
        var handling = EnumHandling.PreventDefault;
        f.Input.HandleWorldAction(EnumEntityAction.InWorldLeftMouseDown, true, ref handling);
        Assert.Equal(EnumHandling.PreventDefault, handling);
        handling = EnumHandling.PassThrough;
        f.Input.HandleWorldAction(EnumEntityAction.Jump, true, ref handling);
        Assert.Equal(EnumHandling.PassThrough, handling);
    }

    private sealed class Dialog(ICoreClientAPI api, string kind) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => null!;
        public override EnumDialogType DialogType => kind == "dialog" ? EnumDialogType.Dialog : EnumDialogType.HUD;
        public override bool CaptureRawMouse() => kind == "raw";
        public override bool CaptureAllInputs() => kind == "all";
    }

    private sealed class Creation : IGizmoCreationInput
    {
        public bool Pending;
        public bool ExplicitlyCanceled;
        public int Attempts, Cancels;
        public GizmoPointer? LastPointer;
        public bool IsPending => Pending;
        public void Offer() { }
        public void Create(GizmoPointer pointer) { Attempts++; LastPointer = pointer; }
        public void Cancel(bool explicitlyCanceled) { Pending = false; ExplicitlyCanceled = explicitlyCanceled; Cancels++; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string previousLocale = Lang.CurrentLocale;
        private readonly string locale = $"vbe-gizmo-input-{Guid.NewGuid():N}";
        public readonly Mock<ICoreClientAPI> Api = TestHelpers.CreateMockCapi();
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly Mock<IInputAPI> Native = new();
        public readonly Mock<IRenderAPI> Render = new();
        public readonly Mock<IClientWorldAccessor> World = new();
        public readonly Mock<IClientNetworkAPI> Network = new(MockBehavior.Strict);
        public readonly ClientPlayer Player = WorkAreaRenderingTests.CreatePlayer();
        public readonly MouseButtonState Raw = new(), WorldButtons = new();
        public readonly bool[] Keys = new bool[256];
        public readonly List<Action<bool>> Focus = [];
        public readonly List<GuiDialog> Dialogs = [];
        public readonly GizmoManager Manager = new();
        public readonly GizmoRenderAdapter Renderer;
        public readonly GizmoInputAdapter Input;
        public readonly PositionGizmo Gizmo;
        public readonly GizmoPosition Baseline;
        public readonly Creation Creation = new();
        public PositionGizmoEdit? LastEdit;
        public GizmoContext Context = null!;
        public bool Grabbed, Paused, Enabled = true, WorldAvailable = true, ThrowConstraint;
        public int Starts, Completes, Cancels, Mining, Attack, ItemUse, Placement, Packets;
        public int Width = 801, Height = 601;
        public double ViewX;

        public Fixture(bool grabbed = false, bool thirdPerson = false, bool held = false)
        {
            var translations = new Mock<ITranslationService>();
            translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(true);
            translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("input unavailable");
            Lang.AvailableLanguages.Add(locale, translations.Object);
            Lang.ChangeLanguage(locale);
            Grabbed = grabbed;
            Raw.Left = held;
            ViewX = thirdPerson ? 3 : 0;
            Player.OverrideCameraMode = thirdPerson ? EnumCameraMode.ThirdPerson : EnumCameraMode.FirstPerson;
            World.Setup(w => w.Player).Returns(Player);
            Api.Setup(a => a.World).Returns(() => WorldAvailable ? World.Object : null!);
            Api.Setup(a => a.Event).Returns(Events.Object);
            Api.Setup(a => a.Input).Returns(Native.Object);
            Api.Setup(a => a.Render).Returns(Render.Object);
            Api.Setup(a => a.Network).Returns(Network.Object);
            Api.Setup(a => a.IsGamePaused).Returns(() => Paused);
            Api.Setup(a => a.Gui.OpenedGuis).Returns(Dialogs);
            Native.Setup(i => i.MouseGrabbed).Returns(() => Grabbed);
            Native.Setup(i => i.MouseButton).Returns(Raw);
            Native.Setup(i => i.KeyboardKeyStateRaw).Returns(Keys);
            Native.Setup(i => i.InWorldMouseButton).Returns(WorldButtons);
            Render.Setup(r => r.FrameWidth).Returns(() => Width);
            Render.Setup(r => r.FrameHeight).Returns(() => Height);
            Context = Capture();
            var initialPointer = Context.Camera!.CreatePointer(Context, 400, 300);
            Baseline = new(initialPointer.Ray.Origin + initialPointer.Ray.Direction * 200, 0);
            Renderer = new(Api.Object, Manager, () => Context, new WorkAreaRenderingTests.FakeState());
            Gizmo = new(Baseline, _ => { }, edit =>
            {
                if (ThrowConstraint) throw new InvalidOperationException("constraint failure");
                LastEdit = edit;
                return edit.Candidate;
            });
            Gizmo.OnInteractionStarted += _ => Starts++;
            Gizmo.OnInteractionCompleted += _ => Completes++;
            Gizmo.OnInteractionCanceled += _ => Cancels++;
            Manager.Add(Gizmo);
            Input = new(Api.Object, Manager, Renderer, () => Enabled, Focus, Creation);
            Renderer.SetActive(true);
            Frame();
        }

        private GizmoContext Capture()
        {
            var view = Mat4d.Translate(new double[16], Mat4d.Create(), -ViewX, 0, 0);
            return new(Player.Entity.Pos.Dimension, new(view,
                Mat4d.Perspective(new double[16], Math.PI / 2, (double)Width / Height, 0.1, 1000),
                default, Width, Height, mouseGrabbed: Grabbed, cameraMode: Player.CameraMode));
        }

        public void Frame()
        {
            Context = Capture();
            Renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
            Renderer.OnRenderFrame(0, EnumRenderStage.AfterBlit);
        }

        public MouseEvent Down(int x = 400, int y = 300, EnumMouseButton button = EnumMouseButton.Left,
            bool handled = false)
        {
            if (button == EnumMouseButton.Left) Raw.Left = true; else Raw.Right = true;
            var e = new MouseEvent(x, y, button, 0) { Handled = handled };
            Events.Raise(ev => ev.MouseDown += null!, e);
            return e;
        }

        public MouseEvent Up(int x = 400, int y = 300, EnumMouseButton button = EnumMouseButton.Left)
        {
            if (button == EnumMouseButton.Left) Raw.Left = false; else Raw.Right = false;
            var e = new MouseEvent(x, y, button, 0);
            Events.Raise(ev => ev.MouseUp += null!, e);
            return e;
        }

        public MouseEvent Move(int x, int y)
        {
            var e = new MouseEvent(x, y);
            Events.Raise(ev => ev.MouseMove += null!, e);
            return e;
        }

        public KeyEvent Key(bool up = false)
        {
            Keys[(int)GlKeys.Escape] = !up;
            var e = new KeyEvent { KeyCode = (int)GlKeys.Escape };
            if (up) Events.Raise(ev => ev.KeyUp += null!, e);
            else Events.Raise(ev => ev.KeyDown += null!, e);
            return e;
        }

        public void Change(string reason)
        {
            if (reason is "dialog" or "raw" or "all") Dialogs.Add(new Dialog(Api.Object, reason));
            if (reason == "paused") Paused = true;
            if (reason == "dead") Player.Entity.Alive = false;
            if (reason == "disabled") Enabled = false;
            if (reason == "focus") foreach (var callback in Focus.ToArray()) callback(false);
            if (reason == "dimension") Events.Raise(e => e.PlayerDimensionChanged += null!, Player);
            if (reason == "mode") Grabbed = !Grabbed;
            if (reason == "camera") Player.OverrideCameraMode = EnumCameraMode.ThirdPerson;
            if (reason == "world") WorldAvailable = false;
            if (reason == "player") World.Setup(w => w.Player).Returns((IClientPlayer)null!);
            if (reason == "dimension-silent") Player.Entity.Pos.Dimension++;
            if (reason == "viewport") Width++;
            if (reason == "hidden") Gizmo.Visible = false;
            if (reason == "undrawn") Renderer.SetActive(false);
        }

        // Model the qualified native downstream gates, not live engine/network execution.
        public void Action(EnumEntityAction action, bool on)
        {
            var handling = EnumHandling.PassThrough;
            Input.HandleWorldAction(action, on, ref handling);
            if (handling != EnumHandling.PassThrough) return;
            if (action == EnumEntityAction.InWorldLeftMouseDown) { Mining++; Attack++; }
            if (action == EnumEntityAction.InWorldRightMouseDown) Placement++;
            if (action == EnumEntityAction.RightMouseDown) ItemUse++;
            Packets++;
        }

        public void SuppressBothFamilies(bool includeRight = false)
        {
            foreach (var action in new[] { EnumEntityAction.LeftMouseDown, EnumEntityAction.InWorldLeftMouseDown,
                EnumEntityAction.RightMouseDown, EnumEntityAction.InWorldRightMouseDown })
            {
                if (!includeRight && action is EnumEntityAction.RightMouseDown or EnumEntityAction.InWorldRightMouseDown) continue;
                Action(action, true);
                Action(action, false);
            }
        }

        public void AssertNoGameplay()
        {
            Assert.Equal(0, Mining + Attack + ItemUse + Placement + Packets);
            Assert.Empty(Network.Invocations);
        }

        public void Dispose()
        {
            Input.Dispose();
            Renderer.Dispose();
            Manager.Dispose();
            Gizmo.Dispose();
            Lang.ChangeLanguage(previousLocale);
            Lang.AvailableLanguages.Remove(locale);
        }
    }
}
