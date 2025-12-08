using System;

using VanillaBuildingExtended.Networking;

using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VanillaBuildingExtended.BuildHammer;

/// <summary>
/// Handles build brush logic on the server side.
/// </summary>
public class BuildBrushManager_Server : BuildBrushManager
{
    #region Fields
    private ICoreServerAPI api => (ICoreServerAPI)coreApi;
    protected readonly IServerNetworkChannel serverChannel;
    #endregion

    #region Lifecycle
    public BuildBrushManager_Server(ICoreServerAPI api) : base(api)
    {
        serverChannel = api.Network.GetChannel(NetworkChannelId);
        serverChannel.SetMessageHandler<Packet_SetBuildBrush>(OnSetBuildBrushPacket);
    }
    #endregion

    #region Public
    public override BuildBrushInstance? GetBrush(in IPlayer? player)
    {
        if (player is null || api.World is null)
        {
            return null;
        }

        if (Brushes.TryGetValue(player.ClientId, out BuildBrushInstance? brush))
        {
            return brush;
        }

        // initialize
        brush = new BuildBrushInstance(player, api.World);
        Brushes.Add(player.ClientId, brush);

        return brush;
    }
    #endregion

    #region Handlers
    private void OnSetBuildBrushPacket(IServerPlayer fromPlayer, Packet_SetBuildBrush packet)
    {
        BuildBrushInstance? brush = GetBrush(fromPlayer);
        if (brush is null)
            return;

        brush.Rotation = packet.rotation;
        //brush.Position = packet.position;
    }
    #endregion
}
