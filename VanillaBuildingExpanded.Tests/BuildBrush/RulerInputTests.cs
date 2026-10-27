using System.Reflection;
using System.Runtime.CompilerServices;

using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class RulerInputTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FreshPress_CopiesOriginalSelectionAndCommitsOnce(bool start)
    {
        using var f = new Fixture();
        f.Arm(start);
        BlockPos position = f.Game.BlockSelection.Position;
        Assert.True(f.Down(EnumMouseButton.Left).Handled);
        position.Set(99, 99, 99);
        var endpoint = start ? f.Ruler.State.Start : f.Ruler.State.End;
        Assert.Equal(new RulerEndpoint(10, 65, 20, 2), endpoint);
        Assert.Equal(RulerPickMode.Idle, f.Ruler.State.PickMode);
        Assert.Equal(1, f.Commits);
        f.RepeatAndRelease(EnumMouseButton.Left);
        Assert.Equal(1, f.Commits);
        f.AssertNoGameplay();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("item")]
    [InlineData("missing")]
    public void Picking_DoesNotRequireAnEligibleHeldBlock(string held)
    {
        using var f = new Fixture(eligibleBrush: true);
        var missing = new Block { BlockId = 99, IsMissing = true };
        var slot = held switch
        {
            "item" => new DummySlot(new ItemStack(new Item { ItemId = 20 })),
            "missing" => new DummySlot(new ItemStack(missing)),
            _ => new DummySlot()
        };
        f.BrushInputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(slot);
        f.Brush.TryUpdate(null);
        Assert.False(f.Brush.CanUseBrushControls);
        f.Arm();
        Assert.True(f.Down(EnumMouseButton.Left).Handled);
        Assert.Equal(1, f.Commits);
        f.RepeatAndRelease(EnumMouseButton.Left);
        f.AssertNoGameplay();
    }

    [Theory]
    [InlineData("sky")]
    [InlineData("entity")]
    [InlineData("boundary")]
    [InlineData("missing-position")]
    public void InvalidPress_IsOwnedAndKeepsPriorEndpointAndPicker(string target)
    {
        using var f = new Fixture();
        var old = new RulerEndpoint(1, 60, 2, 2);
        f.Ruler.RestoreEndpoints(old, null);
        f.Arm();
        if (target == "sky") f.Game.BlockSelection = null;
        if (target == "entity") f.Game.EntitySelection = new EntitySelection();
        if (target == "boundary") f.Accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(false);
        if (target == "missing-position") f.Game.BlockSelection.Position = null;

        Assert.True(f.Down(EnumMouseButton.Left).Handled);
        f.RepeatAndRelease(EnumMouseButton.Left);
        Assert.Equal(old, f.Ruler.State.Start);
        Assert.Equal(RulerPickMode.PickingStart, f.Ruler.State.PickMode);
        Assert.Contains(RulerNotice.AimAtBlock, f.Notices);
        Assert.Equal(0, f.Commits);
        f.AssertNoGameplay();
    }

    [Fact]
    public void RightCancellation_PrecedesEligibleBrushPlacementForEntireGesture()
    {
        using var f = new Fixture(eligibleBrush: true);
        Assert.True(f.Brush.CanUseBrushControls);
        f.Ruler.RestoreEndpoints(new RulerEndpoint(1, 60, 2, 2), null);
        var before = f.Ruler.State.Start;
        f.Arm();
        Assert.True(f.Down(EnumMouseButton.Right).Handled);
        Assert.Equal(RulerPickMode.Idle, f.Ruler.State.PickMode);
        f.RepeatAndRelease(EnumMouseButton.Right);
        Assert.Equal(before, f.Ruler.State.Start);
        Assert.Equal(0, f.Commits);
        f.AssertNoGameplay();
        f.BrushInputs.Validator.VerifyNoOtherCalls();
        Assert.Empty(f.Network.Invocations);
        Assert.Empty(f.Channel.Invocations);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("dialog")]
    [InlineData("paused")]
    [InlineData("handled")]
    [InlineData("dead")]
    [InlineData("off")]
    public void UnavailableInput_DoesNotCaptureOrMutate(string condition)
    {
        using var f = new Fixture();
        f.Arm();
        if (condition == "free") f.Grabbed = false;
        if (condition == "dialog") f.Dialogs.Add(new TestDialog(f.Api.Object));
        if (condition == "paused") f.Paused = true;
        if (condition == "dead") f.Player.Entity.Alive = false;
        if (condition == "off") f.Ruler.SetContext(false, true);
        var e = f.Down(EnumMouseButton.Left, condition == "handled");
        Assert.Equal(condition == "handled", e.Handled);
        Assert.Null(f.Ruler.State.Start);
        Assert.Equal(0, f.Commits);
        if (condition is "off" or "dead")
            Assert.Equal(RulerPickMode.Idle, f.Ruler.State.PickMode);
    }

    [Fact]
    public void ButtonGesture_ArmsWithFreeCursorButCannotSelectWhenGameplayResumesHeld()
    {
        using var f = new Fixture();
        f.Grabbed = false;
        var down = f.Down(EnumMouseButton.Left);
        Assert.False(down.Handled);
        f.Arm(); // GUI activation happens after the mod mouse event.
        f.Grabbed = true;
        f.WorldAction(EnumEntityAction.InWorldLeftMouseDown, true);
        f.WorldAction(EnumEntityAction.LeftMouseDown, true);
        Assert.Null(f.Ruler.State.Start);
        Assert.Equal(0, f.Commits);
        f.Up(EnumMouseButton.Left);
        f.AssertNoGameplay();
        Assert.True(f.Down(EnumMouseButton.Left).Handled);
        Assert.Equal(1, f.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedGesture_RemainsSuppressedThroughFreeCursorOrDialogAndRelease(bool dialog)
    {
        using var f = new Fixture();
        f.Arm();
        f.Down(EnumMouseButton.Left);
        if (dialog) f.Dialogs.Add(new TestDialog(f.Api.Object));
        else f.Grabbed = false;
        Assert.False(f.Up(EnumMouseButton.Left).Handled);
        f.WorldAction(EnumEntityAction.InWorldLeftMouseDown, false);
        f.WorldAction(EnumEntityAction.InWorldLeftMouseDown, true);
        f.Dialogs.Clear();
        f.Grabbed = true;
        f.WorldAction(EnumEntityAction.InWorldLeftMouseDown, true);
        f.AssertNoGameplay();
        Assert.False(f.Down(EnumMouseButton.Left).Handled);
        f.WorldAction(EnumEntityAction.InWorldLeftMouseDown, true);
        Assert.Equal(1, f.Mining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusLoss_RetiresOwnershipWithoutResurrectingHeldActions(bool cancel)
    {
        using var f = new Fixture();
        f.Arm();
        var button = cancel ? EnumMouseButton.Right : EnumMouseButton.Left;
        var action = cancel ? EnumEntityAction.InWorldRightMouseDown : EnumEntityAction.InWorldLeftMouseDown;
        f.Down(button);
        f.WorldButtons.Left = f.WorldButtons.Right = true;
        f.Focus(false);
        Assert.False(cancel ? f.WorldButtons.Right : f.WorldButtons.Left);
        f.Focus(true);
        f.WorldAction(action, true);
        f.WorldAction(action, false);
        Assert.True(f.Up(button).Handled);
        f.AssertNoGameplay();
        Assert.False(f.Down(button).Handled);
        f.WorldAction(action, true);
        Assert.Equal(cancel ? 1 : 0, f.Placement);
        Assert.Equal(cancel ? 0 : 1, f.Mining);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("death")]
    [InlineData("dimension")]
    public void ContextCancellation_KeepsOwnedGestureSuppressed(string change)
    {
        using var f = new Fixture(eligibleBrush: true);
        f.Arm();
        f.Accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(false);
        f.Down(EnumMouseButton.Left);
        if (change == "mode") f.Brush.SetBuildMode(false, null);
        if (change == "death") Invoke(f.System, "Event_PlayerDeath", f.Player);
        if (change == "dimension") f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        Assert.Equal(RulerPickMode.Idle, f.Ruler.State.PickMode);
        f.RepeatAndRelease(EnumMouseButton.Left);
        f.AssertNoGameplay();
    }

    [Fact]
    public void OtherPlayersDimensionChange_DoesNotCancel()
    {
        using var f = new Fixture();
        f.Arm();
        var other = Assert.IsType<ClientPlayer>(RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer)));
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, other);
        Assert.Equal(RulerPickMode.PickingStart, f.Ruler.State.PickMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdleInput_PreservesNativeActionsAndFreshGestures(bool right)
    {
        using var f = new Fixture();
        var button = right ? EnumMouseButton.Right : EnumMouseButton.Left;
        var action = right ? EnumEntityAction.InWorldRightMouseDown : EnumEntityAction.InWorldLeftMouseDown;
        Assert.False(f.Down(button).Handled);
        f.WorldAction(action, true);
        Assert.False(f.Up(button).Handled);
        f.WorldAction(action, false);
        Assert.Equal(right ? 1 : 0, f.Placement);
        Assert.Equal(right ? 0 : 1, f.Mining);
        Assert.Equal(right ? 1 : 0, f.ItemUse);
        Assert.Equal(right ? 0 : 1, f.Attack);
        Assert.Equal(0, f.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetiredGesture_DoesNotBlockANewUnownedFreeCursorPress(bool right)
    {
        using var f = new Fixture();
        f.Arm();
        var button = right ? EnumMouseButton.Right : EnumMouseButton.Left;
        var action = right ? EnumEntityAction.InWorldRightMouseDown : EnumEntityAction.InWorldLeftMouseDown;
        f.Down(button);
        f.Up(button);
        f.Grabbed = false;
        Assert.False(f.Down(button).Handled);
        f.WorldAction(action, true);
        Assert.Equal(right ? 1 : 0, f.Placement);
        Assert.Equal(right ? 0 : 1, f.Mining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenMouseUp_ReconcilesRawReleaseWithoutResurrectingHeldActions(bool right)
    {
        using var f = new Fixture();
        f.Arm();
        var button = right ? EnumMouseButton.Right : EnumMouseButton.Left;
        var action = right ? EnumEntityAction.InWorldRightMouseDown : EnumEntityAction.InWorldLeftMouseDown;
        f.Down(button);
        f.RawButtons.Left = f.RawButtons.Right = false;
        f.Input.ReconcileButtons();
        f.WorldAction(action, true);
        f.AssertNoGameplay();
        Assert.False(f.Down(button).Handled);
        f.WorldAction(action, true);
        Assert.Equal(right ? 1 : 0, f.Placement);
        Assert.Equal(right ? 0 : 1, f.Mining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeaveOrDispose_CancelsAndDetachesWithoutStaleWorldButtons(bool leave)
    {
        using var f = new Fixture();
        f.Arm();
        f.Down(EnumMouseButton.Left);
        f.Arm(false);
        f.WorldButtons.Left = true;
        if (leave) Invoke(f.System, "Event_LeaveWorld");
        else f.Input.Dispose();
        f.Input.Dispose();
        Assert.Equal(RulerPickMode.Idle, f.Ruler.State.PickMode);
        Assert.False(f.WorldButtons.Left);
        Assert.Empty(f.FocusListeners);
        f.Ruler.TogglePicking(RulerPickMode.PickingStart);
        Assert.False(f.Down(EnumMouseButton.Left).Handled);
        f.Events.Raise(e => e.PlayerDimensionChanged += null!, f.Player);
        Assert.Equal(RulerPickMode.PickingStart, f.Ruler.State.PickMode);
        Assert.False(f.Up(EnumMouseButton.Left).Handled);
    }

    [Fact]
    public void Suppression_PreservesEarlierHandlingAndUnrelatedControls()
    {
        using var f = new Fixture();
        f.Arm();
        f.Down(EnumMouseButton.Left);
        var handling = EnumHandling.PreventDefault;
        f.Input.HandleWorldAction(EnumEntityAction.InWorldLeftMouseDown, true, ref handling);
        Assert.Equal(EnumHandling.PreventDefault, handling);
        handling = EnumHandling.PassThrough;
        f.Input.HandleWorldAction(EnumEntityAction.Jump, true, ref handling);
        Assert.Equal(EnumHandling.PassThrough, handling);
        Assert.False(f.Down(EnumMouseButton.Middle).Handled);
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);

    private static void Invoke(object target, string name, params object[] args) =>
        typeof(BuildBrushSystem_Client).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private sealed class TestDialog(ICoreClientAPI api) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => null!;
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Mock<ICoreClientAPI> Api = TestHelpers.CreateMockCapi();
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly Mock<IInputAPI> NativeInput = new();
        public readonly Mock<IBlockAccessor> Accessor = new(MockBehavior.Strict);
        public readonly Mock<IClientNetworkAPI> Network = new(MockBehavior.Strict);
        public readonly Mock<IClientNetworkChannel> Channel = new(MockBehavior.Strict);
        public readonly List<Action<bool>> FocusListeners = [];
        public readonly List<GuiDialog> Dialogs = [];
        public readonly MouseButtonState WorldButtons = new();
        public readonly MouseButtonState RawButtons = new();
        public readonly ClientMain Game = Assert.IsType<ClientMain>(RuntimeHelpers.GetUninitializedObject(typeof(ClientMain)));
        public readonly ClientPlayer Player;
        public readonly RulerController Ruler = new();
        public readonly RulerInput Input;
        public readonly BuildBrushSystem_Client System = new();
        public readonly BuildBrushControllerClient Brush;
        public readonly BrushTestInputs BrushInputs = new();
        public readonly List<RulerNotice> Notices = [];
        public bool Grabbed = true;
        public bool Paused;
        public int Commits;
        public int Mining;
        public int Attack;
        public int ItemUse;
        public int Placement;
        public int ReleaseCallbacks;

        public Fixture(bool eligibleBrush = false)
        {
            Player = Assert.IsType<ClientPlayer>(RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer)));
            var data = Assert.IsType<ClientWorldPlayerData>(
                RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData)));
            SetField(Player, "game", Game);
            SetField(Player, "worlddata", data);
            data.EntityPlayer = new EntityPlayer();
            SetField(Game, "player", Player);
            Game.BlockSelection = new BlockSelection
            {
                Position = new BlockPos(10, 65, 20, 2),
                Face = BlockFacing.EAST
            };
            var world = new Mock<IClientWorldAccessor>();
            world.Setup(w => w.Player).Returns(Player);
            world.Setup(w => w.BlockAccessor).Returns(Accessor.Object);
            Accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(true);
            Api.Setup(a => a.World).Returns(world.Object);
            Api.Setup(a => a.Input).Returns(NativeInput.Object);
            Api.Setup(a => a.Event).Returns(Events.Object);
            Api.Setup(a => a.Network).Returns(Network.Object);
            Api.Setup(a => a.IsGamePaused).Returns(() => Paused);
            var gui = new Mock<IGuiAPI>();
            gui.Setup(g => g.OpenedGuis).Returns(Dialogs);
            Api.Setup(a => a.Gui).Returns(gui.Object);
            NativeInput.Setup(i => i.MouseGrabbed).Returns(() => Grabbed);
            NativeInput.Setup(i => i.InWorldMouseButton).Returns(WorldButtons);
            NativeInput.Setup(i => i.MouseButton).Returns(RawButtons);
            var brushWorld = TestHelpers.CreateMockWorld();
            var block = TestHelpers.CreateAndRegisterBlock(brushWorld, 10, "game:plain");
            BrushInputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot(new ItemStack(block)));
            Brush = new BuildBrushControllerClient(Api.Object, () => BrushInputs.CreateBrush(brushWorld.Object));
            Brush.SetBuildMode(eligibleBrush, null);
            SetField(System, "api", Api.Object);
            SetField(System, "controller", Brush);
            SetField(System, "clientChannel", Channel.Object);
            SetField(System, "ruler", Ruler);
            Ruler.SetContext(true, true);
            Ruler.EndpointsCommitted += () => Commits++;
            Ruler.NoticeRequested += Notices.Add;
            Input = new RulerInput(Api.Object, Ruler, FocusListeners);
            SetField(System, "rulerInput", Input);
            Brush.ModeChanged += () => Invoke(System, "RefreshRulerContext");
        }

        public void Arm(bool start = true) => Assert.True(Ruler.TogglePicking(
            start ? RulerPickMode.PickingStart : RulerPickMode.PickingEnd));

        public MouseEvent Down(EnumMouseButton button, bool handled = false)
        {
            if (button == EnumMouseButton.Left) RawButtons.Left = true;
            if (button == EnumMouseButton.Right) RawButtons.Right = true;
            var e = new MouseEvent(0, 0, button, 0) { Handled = handled };
            Events.Raise(ev => ev.MouseDown += null!, e);
            return e;
        }

        public MouseEvent Up(EnumMouseButton button)
        {
            if (button == EnumMouseButton.Left) RawButtons.Left = false;
            if (button == EnumMouseButton.Right) RawButtons.Right = false;
            var e = new MouseEvent(0, 0, button, 0);
            Events.Raise(ev => ev.MouseUp += null!, e);
            return e;
        }

        public void Focus(bool value)
        {
            foreach (var callback in FocusListeners.ToArray()) callback(value);
        }

        public void RepeatAndRelease(EnumMouseButton button)
        {
            var world = button == EnumMouseButton.Left
                ? EnumEntityAction.InWorldLeftMouseDown : EnumEntityAction.InWorldRightMouseDown;
            var control = button == EnumMouseButton.Left
                ? EnumEntityAction.LeftMouseDown : EnumEntityAction.RightMouseDown;
            for (int i = 0; i < 3; i++)
            {
                WorldAction(world, true);
                WorldAction(control, true);
                Assert.True(Down(button).Handled);
            }
            Assert.True(Up(button).Handled);
            WorldAction(world, false);
            WorldAction(control, false);
            WorldAction(world, true);
        }

        // Model the verified native dispatch gates, including the production brush router.
        public void WorldAction(EnumEntityAction action, bool on)
        {
            object[] args = [action, on, EnumHandling.PassThrough];
            Invoke(System, "InWorldAction", args);
            if ((EnumHandling)args[2] != EnumHandling.PassThrough) return;
            if (!on) { ReleaseCallbacks++; return; }
            if (action == EnumEntityAction.InWorldLeftMouseDown) { Mining++; Attack++; }
            if (action == EnumEntityAction.InWorldRightMouseDown) { ItemUse++; Placement++; }
        }

        public void AssertNoGameplay()
        {
            Assert.Equal(0, Mining);
            Assert.Equal(0, Attack);
            Assert.Equal(0, ItemUse);
            Assert.Equal(0, Placement);
            Assert.Equal(0, ReleaseCallbacks);
            Assert.Empty(Network.Invocations);
            Assert.Empty(Channel.Invocations);
        }

        public void Dispose() => Input.Dispose();
    }
}
