using System;

using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>Independent, additive qualifications reported by the native room registry.</summary>
[Flags]
internal enum RoomQualifications
{
    None = 0,
    Room = 1,
    Shelter = 2,
    Cellar = 4,
    Greenhouse = 8
}

/// <summary>Distinguishes missing data from a complete detection with no qualifications.</summary>
internal enum RoomInfoStatus { Unavailable, Incomplete, Complete }

/// <summary>Copies display data without retaining mutable registry rooms or their membership bitmaps.</summary>
internal readonly record struct BuildModeRoomInfo(
    RoomInfoStatus Status, RoomQualifications Qualifications,
    int Width, int Height, int Length, int? SunlightPercent, int? CoolingPercent)
{
    public bool HasDetails => Status == RoomInfoStatus.Complete && Qualifications.HasFlag(RoomQualifications.Room);

    /// <summary>Queries the real player's position; no brush preview or server protocol is involved.</summary>
    public static BuildModeRoomInfo Read(ICoreClientAPI api)
    {
        var position = api.World?.Player?.Entity?.Pos;
        if (position is null) return default;
        var registry = api.ModLoader.GetModSystem<RoomRegistry>();
        return FromRoom(registry?.GetRoomForPosition(position.AsBlockPos));
    }

    /// <summary>Applies native qualifications and suppresses aggregates for incomplete or open detections.</summary>
    public static BuildModeRoomInfo FromRoom(Room? room)
    {
        if (room is null) return default;
        if (room.AnyChunkUnloaded != 0)
            return new(RoomInfoStatus.Incomplete, RoomQualifications.None, 0, 0, 0, null, null);

        bool enclosed = room.ExitCount == 0;
        var tags = RoomQualifications.None;
        if (enclosed) tags |= RoomQualifications.Room;
        if (enclosed || room.SkylightCount < room.NonSkylightCount) tags |= RoomQualifications.Shelter;
        if (room.IsSmallRoom) tags |= RoomQualifications.Cellar;
        if (enclosed && room.SkylightCount > room.NonSkylightCount) tags |= RoomQualifications.Greenhouse;
        return enclosed
            ? new(RoomInfoStatus.Complete, tags,
                room.Location.X2 - room.Location.X1 + 1,
                room.Location.Y2 - room.Location.Y1 + 1,
                room.Location.Z2 - room.Location.Z1 + 1,
                Percentage(room.SkylightCount, room.NonSkylightCount),
                Percentage(room.CoolingWallCount, room.NonCoolingWallCount))
            : new(RoomInfoStatus.Complete, tags, 0, 0, 0, null, null);
    }

    private static int? Percentage(int positive, int negative)
    {
        long total = (long)positive + negative;
        return total == 0 ? null : (int)Math.Round(100d * positive / total, MidpointRounding.AwayFromZero);
    }
}
