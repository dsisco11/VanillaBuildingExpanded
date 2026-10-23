using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Exercises wheel routing with real controller rotation and native event ownership.</summary>
public sealed class BuildBrushWheelTests
{
    #region Public API
    /// <summary>Both Shift keys rotate in either direction and provide the expected synchronized state.</summary>
    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(false, -1, 0)]
    [InlineData(true, 0, 0.25f)]
    [InlineData(false, 0, -0.25f)]
    public void ShiftWheel_RotatesAndPublishes(bool leftShift, int delta, float precise)
    {
        using var f = new Fixture(leftShift);
        var e = new MouseWheelEventArgs { delta = delta, deltaPrecise = precise };
        f.Scroll(e);
        Assert.True(e.IsHandled);
        bool forward = (precise != 0 ? precise : delta) > 0;
        Assert.Equal(forward ? 11 : 13, f.Controller.GetBrush().CurrentPlacementBlock!.Id);
        int expectedOrientation = forward ? 1 : 2;
        Assert.Equal(new[] { expectedOrientation }, f.PublishedOrientations);
    }

    /// <summary>Normal scroll, menus, earlier handlers and zero deltas never rotate or send brush updates.</summary>
    [Theory]
    [InlineData("no-shift")]
    [InlineData("menu")]
    [InlineData("handled")]
    [InlineData("zero")]
    [InlineData("off")]
    [InlineData("empty")]
    [InlineData("nonrotatable")]
    public void UnownedWheel_PreservesNormalHandling(string condition)
    {
        using var f = new Fixture(true);
        var e = new MouseWheelEventArgs { delta = condition == "zero" ? 0 : 1 };
        if (condition == "no-shift") f.Keys[(int)GlKeys.LShift] = false;
        if (condition == "menu") f.Input.Setup(i => i.MouseGrabbed).Returns(false);
        if (condition == "handled") e.SetHandled();
        if (condition == "off") f.Controller.SetBuildMode(false, null);
        if (condition == "empty") f.Slot = new DummySlot();
        if (condition == "nonrotatable") f.Slot = new DummySlot(new ItemStack(f.PlainBlock));
        f.Scroll(e);
        Assert.Equal(condition == "handled", e.IsHandled);
        Assert.Equal(0, f.Controller.GetBrush().OrientationIndex);
        Assert.Empty(f.PublishedOrientations);
    }

    /// <summary>Disposed input bindings cannot consume scroll events or change brush orientation.</summary>
    [Fact]
    public void Disposal_DetachesWheelInput()
    {
        using var f = new Fixture(true);
        f.Dispose();
        var e = new MouseWheelEventArgs { delta = 1 };
        f.Scroll(e);
        Assert.False(e.IsHandled);
        Assert.Empty(f.PublishedOrientations);
        Assert.Equal(0, f.Controller.GetBrush().OrientationIndex);
    }
    #endregion

    #region Private
    /// <summary>Supplies live input/slot doubles to the game callback without starting a client session.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly BuildBrushWheelInput wheel;
        private readonly Mock<IClientEventAPI> events = new();
        public readonly bool[] Keys = new bool[512];
        public readonly Mock<IInputAPI> Input = new();
        public readonly List<int> PublishedOrientations = [];
        public readonly BuildBrushControllerClient Controller;
        public readonly Block PlainBlock;
        public ItemSlot Slot;

        #region Public API
        /// <summary>Builds distinct forward/backward variants and binds input to real controller operations.</summary>
        public Fixture(bool leftShift)
        {
            var world = TestHelpers.CreateMockWorld();
            var north = TestHelpers.CreateAndRegisterBlock(world, 10, "game:test-north");
            var east = TestHelpers.CreateAndRegisterBlock(world, 11, "game:test-east");
            var west = TestHelpers.CreateAndRegisterBlock(world, 13, "game:test-west");
            PlainBlock = TestHelpers.CreateAndRegisterBlock(world, 12, "game:plain");
            north.VariantStrict["rot"] = "north";
            east.VariantStrict["rot"] = "east";
            west.VariantStrict["rot"] = "west";
            world.Setup(w => w.SearchBlocks(It.IsAny<AssetLocation>())).Returns([north, east, west]);
            Slot = new DummySlot(new ItemStack(north));
            BrushTestInputs inputs = new();
            inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(() => Slot);
            var api = TestHelpers.CreateMockCapi();
            api.Setup(a => a.Input).Returns(Input.Object);
            Input.Setup(i => i.MouseGrabbed).Returns(true);
            Input.Setup(i => i.KeyboardKeyState).Returns(Keys);
            Keys[(int)(leftShift ? GlKeys.LShift : GlKeys.RShift)] = true;
            Controller = new BuildBrushControllerClient(api.Object, () => inputs.CreateBrush(world.Object));
            Controller.SetBuildMode(true, null);
            api.Setup(a => a.Event).Returns(events.Object);
            wheel = new BuildBrushWheelInput(api.Object, direction =>
            {
                Controller.TryUpdate(null);
                bool rotated = Controller.RotateCursor(direction);
                if (rotated)
                {
                    Controller.SyncStateFromBrush();
                    PublishedOrientations.Add(Controller.State.OrientationIndex);
                }
                return rotated;
            });
        }

        /// <summary>Invokes the production callback with a real handled-state event.</summary>
        public void Scroll(MouseWheelEventArgs e)
        {
            events.Raise(e => e.MouseWheelMove += null!, e);
        }
        /// <summary>Retires the input subscription after the test.</summary>
        public void Dispose() => wheel.Dispose();
        #endregion

    }
    #endregion
}
