using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Verifies mode-driven HUD lifetime without requiring native graphics.</summary>
public sealed class BuildModeIndicatorTests
{
    #region Public API
    /// <summary>The actual native HUD stays passive and detaches its scale watcher on disposal.</summary>
    [Fact]
    public void Hud_DoesNotCaptureInputAndRetiresScaleWatcher()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var settings = Mock.Get(api.Object.Settings.Float);
        OnSettingsChanged<float>? changed = null;
        settings.Setup(s => s.AddWatcher("guiScale", It.IsAny<OnSettingsChanged<float>>()))
            .Callback<string, OnSettingsChanged<float>>((_, handler) => changed = handler);
        var hud = new BuildModeHud(api.Object);
        Assert.False(hud.Focusable);
        Assert.False(hud.PrefersUngrabbedMouse);
        Assert.False(hud.ShouldReceiveKeyboardEvents());
        Assert.False(hud.ShouldReceiveMouseEvents());
        Assert.True(hud.UnregisterOnClose);
        Assert.False(hud.IsOpened());
        Assert.NotNull(changed);
        changed(1.5f);
        Assert.Null(hud.SingleComposer);
        hud.Dispose();
        hud.Dispose();
        changed(2f);
        settings.Verify(s => s.RemoveWatcher("guiScale", changed), Times.Once);
        Assert.False(hud.IsOpened());
    }

    /// <summary>Repeated transitions reuse artwork; empty hands and missing targets do not hide active mode.</summary>
    [Fact]
    public void TogglesAndSuspension_UseModeInsteadOfPlacementValidity()
    {
        var world = TestHelpers.CreateMockWorld();
        var block = TestHelpers.CreateAndRegisterBlock(world, 10);
        BrushTestInputs inputs = new();
        ItemSlot slot = new DummySlot(new ItemStack(block));
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(() => slot);
        var client = new BuildBrushControllerClient(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        var view = new Indicator();
        int creations = 0;
        using var binding = new BuildModeIndicatorBinding(client, () => { creations++; return view; });
        Assert.Equal(0, creations);
        client.SetBuildMode(true, null);
        Assert.True(client.GetBrush().IsDisabled);
        Assert.Equal(1, view.Shown);
        slot = new DummySlot();
        client.TryUpdate(null);
        Assert.False(client.CanUseBrushControls);
        Assert.Equal(0, view.Hidden);
        client.SetBuildMode(true, null);
        Assert.Equal(1, view.Shown);
        client.SetBuildMode(false, null);
        client.SetBuildMode(true, null);
        Assert.Equal(1, view.Hidden);
        Assert.Equal(2, view.Shown);
        Assert.Equal(1, creations);
    }

    /// <summary>Session retirement releases artwork once and prevents old subscriptions from reopening it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disposal_DetachesAndReleasesOnlyCreatedResources(bool initiallyActive)
    {
        var world = TestHelpers.CreateMockWorld();
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot());
        var client = new BuildBrushControllerClient(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        var view = new Indicator();
        client.SetBuildMode(initiallyActive, null);
        var binding = new BuildModeIndicatorBinding(client, () => view);
        binding.Dispose();
        binding.Dispose();
        client.SetBuildMode(false, null);
        client.SetBuildMode(true, null);
        Assert.Equal(initiallyActive ? 1 : 0, view.Disposals);
        Assert.Equal(initiallyActive ? 1 : 0, view.Shown);
        Assert.Equal(0, view.Hidden);
    }

    /// <summary>Death/join mode resets hide artwork and a fresh session starts without stale visibility.</summary>
    [Fact]
    public void ModeResetAndNewSession_StartHidden()
    {
        var world = TestHelpers.CreateMockWorld();
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new DummySlot());
        var client = new BuildBrushControllerClient(TestHelpers.CreateMockCapi().Object, () => inputs.CreateBrush(world.Object));
        var previous = new Indicator();
        var binding = new BuildModeIndicatorBinding(client, () => previous);
        client.SetBuildMode(true, null);
        client.SetBuildMode(false, null);
        Assert.Equal(1, previous.Hidden);
        binding.Dispose();
        var next = new Indicator();
        using var nextBinding = new BuildModeIndicatorBinding(client, () => next);
        Assert.Equal(0, next.Shown + next.Hidden + next.Disposals);
        client.SetBuildMode(true, null);
        Assert.Equal(1, next.Shown);
        Assert.Equal(1, previous.Shown);
    }
    #endregion

    #region Private
    /// <summary>Records presentation calls without native GUI services.</summary>
    private sealed class Indicator : IBuildModeIndicator
    {
        public int Shown;
        public int Hidden;
        public int Disposals;

        #region Public API
        /// <summary>Records the requested visibility transition.</summary>
        public void SetVisible(bool visible)
        {
            if (visible) Shown++;
            else Hidden++;
        }
        /// <summary>Records resource retirement.</summary>
        public void Dispose() => Disposals++;
        #endregion
    }
    #endregion
}
