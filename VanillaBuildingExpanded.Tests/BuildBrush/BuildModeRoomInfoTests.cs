using Moq;
using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BuildModeRoomInfoTests
{
    [Theory]
    [InlineData(0, 6, 4, false, 11)]
    [InlineData(0, 6, 4, true, 15)]
    [InlineData(0, 5, 5, false, 3)]
    [InlineData(0, 0, 0, false, 3)]
    [InlineData(1, 4, 6, false, 2)]
    [InlineData(1, 5, 5, false, 0)]
    [InlineData(1, 6, 4, false, 0)]
    [InlineData(200, 0, 1, false, 2)]
    public void Qualifications_AreAdditiveAndUseStrictNativeSunlightRules(
        int exits, int sunlight, int shade, bool small, int expected)
    {
        var room = Room();
        room.ExitCount = exits;
        room.SkylightCount = sunlight;
        room.NonSkylightCount = shade;
        room.IsSmallRoom = small;
        var info = BuildModeRoomInfo.FromRoom(room);
        Assert.Equal(RoomInfoStatus.Complete, info.Status);
        Assert.Equal((RoomQualifications)expected, info.Qualifications);
        Assert.Equal(exits == 0, info.HasDetails);
        if (exits != 0)
        {
            Assert.Equal((0, 0, 0), (info.Width, info.Height, info.Length));
            Assert.Null(info.SunlightPercent);
            Assert.Null(info.CoolingPercent);
        }
    }

    [Theory]
    [InlineData(7, 7, 7, true)]
    [InlineData(9, 7, 7, true)]
    [InlineData(7, 9, 7, true)]
    [InlineData(7, 7, 9, true)]
    [InlineData(9, 7, 7, false)]
    [InlineData(10, 7, 7, false)]
    [InlineData(8, 8, 7, false)]
    public void Cellar_UsesRegistryVerdictRatherThanInferringVolumeFromBounds(int x, int y, int z, bool nativeVerdict)
    {
        // Registry membership (including the alternate rule's 150-cell cap) is not reconstructible from bounds.
        var room = Room();
        room.Location = new Cuboidi(10, 20, 30, 10 + x - 1, 20 + y - 1, 30 + z - 1);
        room.IsSmallRoom = nativeVerdict;
        var info = BuildModeRoomInfo.FromRoom(room);
        Assert.Equal((x, y, z), (info.Width, info.Height, info.Length));
        Assert.Equal(nativeVerdict, info.Qualifications.HasFlag(RoomQualifications.Cellar));
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(0, 10, 0)]
    [InlineData(10, 0, 100)]
    [InlineData(2, 1, 67)]
    [InlineData(1, 7, 13)]
    [InlineData(int.MaxValue, int.MaxValue, 50)]
    public void Percentages_UseAggregatesAndHandleZeroAndLargeDenominators(int positive, int negative, int? expected)
    {
        var room = Room();
        room.SkylightCount = room.CoolingWallCount = positive;
        room.NonSkylightCount = room.NonCoolingWallCount = negative;
        var info = BuildModeRoomInfo.FromRoom(room);
        Assert.Equal(expected, info.SunlightPercent);
        Assert.Equal(expected, info.CoolingPercent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void IncompleteRooms_NeverPublishProvisionalSuccessOrAggregates(int unloaded)
    {
        var room = Room();
        room.AnyChunkUnloaded = unloaded;
        room.IsSmallRoom = true;
        room.SkylightCount = 100;
        var info = BuildModeRoomInfo.FromRoom(room);
        Assert.Equal(RoomInfoStatus.Incomplete, info.Status);
        Assert.Equal(RoomQualifications.None, info.Qualifications);
        Assert.False(info.HasDetails);
        Assert.Equal((0, 0, 0), (info.Width, info.Height, info.Length));
        Assert.Null(info.SunlightPercent);
        Assert.Null(info.CoolingPercent);
    }

    [Fact]
    public void MissingRoomOrPlayer_IsExplicitlyUnavailableWithoutQueryingRegistry()
    {
        Assert.Equal(default, BuildModeRoomInfo.FromRoom(null));
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(api => api.World.Player).Returns((IClientPlayer)null!);
        Assert.Equal(default, BuildModeRoomInfo.Read(api.Object));
        api.Verify(api => api.ModLoader.GetModSystem<RoomRegistry>(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Snapshot_DoesNotRetainMutableRegistryData()
    {
        var room = Room();
        var snapshot = BuildModeRoomInfo.FromRoom(room);
        room.Location.X2 += 100;
        room.SkylightCount = 100;
        room.AnyChunkUnloaded = 1;
        Assert.Equal(7, snapshot.Width);
        Assert.Equal(RoomInfoStatus.Complete, snapshot.Status);
        Assert.Null(snapshot.SunlightPercent);
    }

    private static Room Room() => new()
    {
        Location = new Cuboidi(1, 2, 3, 7, 5, 8)
    };
}
