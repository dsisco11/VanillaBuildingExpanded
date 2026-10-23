using System;
using System.Collections.Generic;

using VanillaBuildingExpanded.Config;
using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>
/// Handles build brush logic on the server side.
/// </summary>
public class BuildBrushSystem_Server : ModSystem
{
    #region Fields
    protected ICoreServerAPI api;
    protected IServerNetworkChannel serverChannel;
    protected readonly Dictionary<int, BuildBrushControllerServer> Controllers = [];

    private VbeConfig? config;

    private readonly Dictionary<int, long> lastAppliedSeqByClientId = [];
    #endregion

    #region Accessors
    protected ILogger Logger => api.Logger;
    #endregion

    #region Hooks
    /// <summary>Loads this system only on its owning game side.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
    /// <summary>Registers server networking and player lifecycle callbacks.</summary>
    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;

        config = VbeConfig.Get(api);

        // Networking
        serverChannel = api.Network.GetChannel(Mod.Info.ModID);
        serverChannel.SetMessageHandler<Packet_SetBuildBrush>(HandlePacket_SetBuildBrush);

        // Game Events
        api.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
        api.Event.PlayerJoin += Event_PlayerJoin;
        api.Event.PlayerDisconnect += Event_PlayerDisconnect;
        api.Event.PlayerDeath += Event_PlayerDeath;
        api.Event.PlayerRespawn += Event_PlayerRespawn;
    }

    /// <summary>Releases resources owned by this system.</summary>
    public override void Dispose()
    {
        // Destroy all brush dimensions before clearing
        foreach (var controller in Controllers.Values)
        {
            controller.Destroy();
            controller.Dispose();
        }
        Controllers.Clear();
    }
    #endregion

    #region Public
    /// <summary>Gets the brush owned by the current player session.</summary>
    public BuildBrushInstance? GetBrush(in IPlayer? player)
    {
        if (player is null || api.World is null)
            return null;

        if (!Controllers.TryGetValue(player.ClientId, out BuildBrushControllerServer? controller))
        {
            Logger.Warning($"Build brush instance not found for player {player.PlayerName} (ID: {player.ClientId})");
            return null;
        }

        return controller.Brush;
    }
    #endregion

    #region Block Placement
    /// <summary>Applies the current brush placement through native game block rules.</summary>
    public bool TryPlaceBrushBlock(in IWorldAccessor world, in IPlayer? byPlayer, in ItemStack? itemstack, in BlockSelection blockSel)
    {
        var brush = GetBrush(byPlayer);
        if (brush is null || brush.IsDisabled)
            return false;

        if (config?.BuildBrushDebugLogging == true)
        {
            Logger.Debug(
                "[BuildBrush][Debug][ServerPlace]: player={0} seq={1} brushPos={2} brushOri={3} blockSelPos={4}",
                byPlayer?.PlayerName,
                brush.LastAppliedSeq,
                brush.Position,
                brush.OrientationIndex,
                blockSel.Position
            );
        }

        if (!brush.TryBuildPlacementRequest(blockSel, out BuildBrushPlacementRequest? request) || request is null)
        {
            return true;
        }

        string failureCode = string.Empty;
        if (!request.PlacementBlock.CanPlaceBlock(world, byPlayer, request.Selection, ref failureCode))
        {
            return true;
        }

        // We should be able to place the block; if we don't have a placement block then we still return true
        // to act as though we placed it (to prevent normal placement and unexpected behavior)
        if (request.PlacementBlock is not null)
        {
            // Use brush.ItemStack instead of the hotbar itemstack - brush.ItemStack has the correct
            // meshAngle attribute set for IRotatable blocks and the correct block for variant rotation
            request.PlacementBlock.DoPlaceBlock(world, byPlayer, request.Selection, request.ItemStack);

            // Some vanilla placement logic overwrites rotatable mesh angles based on player facing.
            // Enforce the brush's target rotation on the placed block entity.
            if (request.Rotation is not null && request.Rotation.HasRotatableEntity)
            {
                var placedBe = world.BlockAccessor.GetBlockEntity(request.Selection.Position);
                if (placedBe is not null)
                {
                    request.Rotation.ApplyToPlacedBlockEntity(placedBe, request.Rotation.Current, request.ItemStack);
                }
            }

            world.BlockAccessor.MarkBlockModified(request.Selection.Position);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(request.Selection.Position);
            brush.OnBlockPlacedServer();
        }
        return true;
    }
    #endregion

    #region Internal packet decisions
    /// <summary>
    /// Applies a client's ordered brush update without performing player transport.
    /// Returns null for an unknown client; otherwise identifies the acknowledgment and whether state was applied.
    /// </summary>
    internal (BuildBrushControllerServer Controller, long AckSequence, bool Applied)? ApplyBrushUpdate(
        int clientId, Packet_SetBuildBrush packet, string playerName)
    {
        if (!Controllers.TryGetValue(clientId, out BuildBrushControllerServer? controller))
            return null;

        lastAppliedSeqByClientId.TryGetValue(clientId, out long lastAppliedSeq);

        // Rejected updates acknowledge the current sequence without changing state or requesting synchronization.
        if (packet.seq <= lastAppliedSeq)
        {
            if (config?.BuildBrushDebugLogging == true)
            {
                Logger.Debug(
                    "[BuildBrush][Debug][ServerRecvIgnored]: player={0} seq={1} lastAppliedSeq={2}",
                    playerName, packet.seq, lastAppliedSeq);
            }
            return (controller, lastAppliedSeq, false);
        }

        if (config?.BuildBrushDebugLogging == true)
        {
            Logger.Debug(
                "[BuildBrush][Debug][ServerRecv]: player={0} seq={1} isActive={2} orientationIndex={3} snapping={4} pos={5}",
                playerName, packet.seq, packet.isActive, packet.orientationIndex, packet.snapping, packet.position);
        }

        // Keep the existing write-before-application behavior, including when application throws.
        lastAppliedSeqByClientId[clientId] = packet.seq;
        controller.ApplyState(packet);
        return (controller, packet.seq, true);
    }
    #endregion

    #region Event Handlers
    /// <summary>
    /// Initializes the player's build brush instance on join.
    /// </summary>
    private void Event_PlayerJoin(IServerPlayer byPlayer)
    {
        BuildBrushInstance brush = new(api.World, byPlayer.InventoryManager, byPlayer.PlayerUID, byPlayer.PlayerName,
            new PlayerBrushPlacementValidator(api.World, byPlayer), () => byPlayer.CurrentBlockSelection);
        BuildBrushControllerServer controller = new(brush);
        Controllers.Add(byPlayer.ClientId, controller);

        IInventory? hotbarInv = byPlayer.InventoryManager?.GetHotbarInventory();
        if (hotbarInv is not null)
        {
            hotbarInv.SlotModified += (int slot) => Event_HotbarSlotModified(byPlayer, slot);
        }

        controller.Brush.IsActive = false;
        controller.Brush.TryUpdateBlockId();
    }

    /// <summary>
    /// Erases the player's build brush instance on disconnect.
    /// </summary>
    private void Event_PlayerDisconnect(IServerPlayer byPlayer)
    {
        if (Controllers.TryGetValue(byPlayer.ClientId, out var controller))
        {
            controller.Destroy();
            controller.Dispose();
            Controllers.Remove(byPlayer.ClientId);
            lastAppliedSeqByClientId.Remove(byPlayer.ClientId);
        }
    }

    /// <summary>Clears server placement immediately on death without resetting packet ordering.</summary>
    private void Event_PlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
    {
        if (Controllers.TryGetValue(byPlayer.ClientId, out var controller))
            controller.ResetBuildMode();
    }

    /// <summary>Ensures respawning never reactivates a previous brush session.</summary>
    private void Event_PlayerRespawn(IServerPlayer byPlayer)
    {
        if (Controllers.TryGetValue(byPlayer.ClientId, out var controller))
            controller.ResetBuildMode();
    }
    /// <summary>
    /// Handles hotbar slot modifications to update the brush's block ID incase the contents of the active slot changes.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="slotId"></param>
    private void Event_HotbarSlotModified(in IServerPlayer player, int slotId)
    {
        IPlayerInventoryManager? inventory = player.InventoryManager;
        if (inventory is null)
            return;

        if (slotId != inventory.ActiveHotbarSlotNumber)
            return;

        BuildBrushInstance? brush = GetBrush(player);
        if (brush is null)
            return;

        brush.TryUpdateBlockId();
        if (!brush.IsValidPlacementBlock(brush.BlockUntransformed))
            brush.TryUpdate(null);
    }

    /// <summary>
    /// Handles updating the brush's block ID after a players active held item changes.
    /// </summary>
    private void Event_AfterActiveSlotChanged(IServerPlayer byPlayer, ActiveSlotChangeEventArgs evt)
    {
        var brush = GetBrush(byPlayer);
        if (brush is not null)
        {
            brush.TryUpdateBlockId();
            if (!brush.IsValidPlacementBlock(brush.BlockUntransformed))
                brush.TryUpdate(null);
        }
    }
    #endregion

    #region Network Handlers
    /// <summary>
    /// Handles the <see cref="Packet_SetBuildBrush"/> packet from clients to update their brush state on the server.
    /// </summary>
    private void HandlePacket_SetBuildBrush(IServerPlayer fromPlayer, Packet_SetBuildBrush packet)
    {
        // A packet already in flight must not reactivate a dead player's brush.
        if (fromPlayer.Entity?.Alive != true)
            packet.isActive = false;
        var result = ApplyBrushUpdate(fromPlayer.ClientId, packet, fromPlayer.PlayerName);
        if (result is null)
            return;
        var decision = result.Value;
        serverChannel.SendPacket(new Packet_BuildBrushAck { lastAppliedSeq = decision.AckSequence }, fromPlayer);

        // Only accepted updates synchronize the initialized preview, after acknowledging its owner.
        if (decision.Applied && decision.Controller.Brush.Dimension?.IsInitialized == true)
        {
            decision.Controller.Brush.SyncDimensionToPlayers([fromPlayer]);
        }
    }
    #endregion
}
