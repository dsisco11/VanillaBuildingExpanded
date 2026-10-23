using VanillaBuildingExpanded.BuildHammer;
using VanillaBuildingExpanded.Networking;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Checks server sequence decisions without player proxies or handler reflection.</summary>
public sealed class BrushSyncTests
{
    #region Public API

    /// <summary>Unknown clients produce no acknowledgment or sequence tracking.</summary>
    [Fact]
    public void UnknownClient_DoesNotAcknowledgeOrTrack()
    {
        using TestServer system = new();
        Assert.Null(system.ApplyBrushUpdate(1, Packet(20), "Tester"));
        using var controller = CreateController();
        system.Sessions.Add(1, controller);
        var result = system.ApplyBrushUpdate(1, Packet(1), "Tester");
        Assert.True(result!.Value.Applied);
        Assert.Equal(1, result.Value.AckSequence);
    }

    /// <summary>Absent tracking starts at zero and rejects nonpositive initial sequences.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InitialNonpositiveSequence_AcknowledgesZero(long sequence)
    {
        using TestServer system = new();
        using var controller = CreateController();
        system.Sessions.Add(1, controller);
        var result = system.ApplyBrushUpdate(1, Packet(sequence), "Tester");
        Assert.False(result!.Value.Applied);
        Assert.Equal(0, result.Value.AckSequence);
        Assert.Equal(0, controller.State.LastAppliedSeq);
        Assert.Null(controller.Brush.Position);
    }

    /// <summary>Older and equal packets preserve accepted state and acknowledge its sequence.</summary>
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void OlderOrEqualSequence_PreservesState(long sequence)
    {
        using TestServer system = new();
        using var controller = CreateController();
        system.Sessions.Add(1, controller);
        Packet_SetBuildBrush accepted = Packet(5);
        system.ApplyBrushUpdate(1, accepted, "Tester");
        BlockPos? position = controller.Brush.Position;
        Packet_SetBuildBrush stale = Packet(sequence);
        stale.position = new BlockPos(90, 80, 70);
        stale.isActive = true;
        stale.snapping = EBuildBrushSnapping.None;
        stale.orientationIndex = 99;

        var result = system.ApplyBrushUpdate(1, stale, "Tester");

        Assert.Same(controller, result!.Value.Controller);
        Assert.False(result.Value.Applied);
        Assert.Equal(5, result.Value.AckSequence);
        Assert.Equal(5, controller.State.LastAppliedSeq);
        Assert.Equal(5, controller.Brush.LastAppliedSeq);
        Assert.False(controller.State.IsActive);
        Assert.False(controller.Brush.IsActive);
        Assert.Equal(accepted.orientationIndex, controller.State.OrientationIndex);
        Assert.Equal(accepted.snapping, controller.State.Snapping);
        Assert.Equal(accepted.snapping, controller.Brush.Snapping);
        Assert.Equal(accepted.position, controller.State.Position);
        Assert.Same(position, controller.Brush.Position);
    }

    /// <summary>Increasing sequences apply state and track each client independently.</summary>
    [Fact]
    public void AcceptedSequence_AppliesStateAndTracksPerClient()
    {
        using TestServer system = new();
        using var first = CreateController();
        using var second = CreateController();
        system.Sessions.Add(1, first);
        system.Sessions.Add(2, second);
        system.ApplyBrushUpdate(1, Packet(5), "First");
        Packet_SetBuildBrush packet = Packet(6);
        packet.isActive = true;
        // Detach preview lifecycle to inspect state without native dimension services.
        first.Dispose();
        var result = system.ApplyBrushUpdate(1, packet, "First");

        Assert.Same(first, result!.Value.Controller);
        Assert.True(result.Value.Applied);
        Assert.Equal(6, result.Value.AckSequence);
        Assert.True(first.State.IsActive);
        Assert.True(first.Brush.IsActive);
        Assert.Equal(packet.orientationIndex, first.State.OrientationIndex);
        Assert.Equal(packet.snapping, first.State.Snapping);
        Assert.Equal(packet.snapping, first.Brush.Snapping);
        Assert.Equal(packet.position, first.State.Position);
        Assert.Equal(packet.position, first.Brush.Position);
        Assert.NotSame(packet.position, first.State.Position);
        Assert.Equal(6, first.State.LastAppliedSeq);
        Assert.Equal(6, first.Brush.LastAppliedSeq);
        Assert.True(system.ApplyBrushUpdate(2, Packet(1), "Second")!.Value.Applied);
        Assert.Equal(1, second.State.LastAppliedSeq);
        Assert.Equal(6, system.ApplyBrushUpdate(1, Packet(5), "First")!.Value.AckSequence);
    }

    /// <summary>Application exceptions retain the sequence write and yield no acknowledgment decision.</summary>
    [Fact]
    public void ApplicationThrows_SequenceWriteRemains()
    {
        using TestServer system = new();
        using var controller = CreateController();
        controller.Dispose();
        system.Sessions.Add(1, controller);
        controller.Brush.OnActivationChanged += (_, _) => throw new InvalidOperationException("application failed");
        Packet_SetBuildBrush packet = Packet(7);
        packet.isActive = true;

        Assert.Throws<InvalidOperationException>(() => system.ApplyBrushUpdate(1, packet, "Tester"));

        var result = system.ApplyBrushUpdate(1, Packet(7), "Tester");
        Assert.False(result!.Value.Applied);
        Assert.Equal(7, result.Value.AckSequence);
        Assert.Equal(7, controller.State.LastAppliedSeq);
        Assert.Equal(0, controller.Brush.LastAppliedSeq);
    }

    #endregion

    #region Private

    /// <summary>Creates an inactive controller from narrow test inputs.</summary>
    private static BuildBrushControllerServer CreateController()
    {
        var world = TestHelpers.CreateMockWorld();
        var block = TestHelpers.CreateAndRegisterBlock(world, 10);
        BrushTestInputs inputs = new();
        inputs.Inventory.Setup(i => i.ActiveHotbarSlot).Returns(new Vintagestory.API.Common.DummySlot(new Vintagestory.API.Common.ItemStack(block)));
        return new BuildBrushControllerServer(inputs.CreateBrush(world.Object));
    }

    /// <summary>Creates a packet with distinguishable state fields.</summary>
    private static Packet_SetBuildBrush Packet(long sequence)
    {
        return new Packet_SetBuildBrush
        {
            seq = sequence, isActive = false, orientationIndex = 2,
            position = new BlockPos(1, 2, 3), snapping = EBuildBrushSnapping.Horizontal
        };
    }

    /// <summary>Exposes the protected session registry without replacing packet policy.</summary>
    private sealed class TestServer : BuildBrushSystem_Server, IDisposable
    {
        public Dictionary<int, BuildBrushControllerServer> Sessions => Controllers;
    }

    #endregion
}
