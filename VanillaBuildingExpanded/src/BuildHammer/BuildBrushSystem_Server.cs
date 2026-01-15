using System;
using System.Collections.Generic;

using VanillaBuildingExpanded.Config;
using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildHammer;

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
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
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
    }

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

        // We should be able to place the block; if we don't have a placement block then we still return true
        // to act as though we placed it (to prevent normal placement and unexpected behavior)
        if (brush.CurrentPlacementBlock is not null)
        {
            // Use brush.ItemStack instead of the hotbar itemstack - brush.ItemStack has the correct
            // meshAngle attribute set for IRotatable blocks and the correct block for variant rotation
            brush.CurrentPlacementBlock.DoPlaceBlock(world, byPlayer, blockSel, brush.ItemStack);

            // Some vanilla placement logic overwrites rotatable mesh angles based on player facing.
            // Enforce the brush's target rotation on the placed block entity.
            if (brush.Rotation is not null && brush.Rotation.HasRotatableEntity)
            {
                var placedBe = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                if (placedBe is not null)
                {
                    brush.Rotation.ApplyToPlacedBlockEntity(placedBe, brush.Rotation.Current, brush.ItemStack);
                }
            }

            world.BlockAccessor.MarkBlockModified(blockSel.Position);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(blockSel.Position);
            brush.OnBlockPlacedServer();
        }
        return true;
    }
    #endregion

    #region Event Handlers
    /// <summary>
    /// Initializes the player's build brush instance on join.
    /// </summary>
    private void Event_PlayerJoin(IServerPlayer byPlayer)
    {
        BuildBrushControllerServer controller = new(api, byPlayer);
        Controllers.Add(byPlayer.ClientId, controller);

        IInventory? hotbarInv = byPlayer.InventoryManager?.GetHotbarInventory();
        if (hotbarInv is not null)
        {
            hotbarInv.SlotModified += (int slot) => Event_HotbarSlotModified(byPlayer, slot);
        }

        IInventory? offhandInventory = byPlayer.InventoryManager?.OffhandHotbarSlot.Inventory;
        if (offhandInventory is not null)
        {
            offhandInventory.SlotModified += (int slot) => Event_OffhandSlotModified(byPlayer, slot);
        }

        controller.Brush.IsActive = byPlayer.IsHoldingBuildHammer();
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
        }
    }

    /// <summary>
    /// Handles offhand slot modifications to update the brush's active state if the build hammer is equipped.
    /// </summary>
    private void Event_OffhandSlotModified(IServerPlayer byPlayer, int slot)
    {
        BuildBrushInstance? brush = GetBrush(byPlayer);
        if (brush is null)
            return;

        bool oldState = brush.IsActive;
        bool newState = byPlayer.IsHoldingBuildHammer();

        brush.IsActive = newState;
        if (oldState && !newState)
        {
            brush.OnUnequipped();
        }
        else if (!oldState && newState)
        {
            brush.OnEquipped();
        }
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

        brush.BlockId = inventory.ActiveHotbarSlot?.Itemstack?.Block?.BlockId;
    }

    /// <summary>
    /// Handles updating the brush's block ID after a players active held item changes.
    /// </summary>
    private void Event_AfterActiveSlotChanged(IServerPlayer byPlayer, ActiveSlotChangeEventArgs evt)
    {
        var brush = GetBrush(byPlayer);
        if (brush is not null)
        {
            brush.BlockId = byPlayer!.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.BlockId;
        }
    }
    #endregion

    #region Network Handlers
    /// <summary>
    /// Handles the <see cref="Packet_SetBuildBrush"/> packet from clients to update their brush state on the server.
    /// </summary>
    private void HandlePacket_SetBuildBrush(IServerPlayer fromPlayer, Packet_SetBuildBrush packet)
    {
        if (!Controllers.TryGetValue(fromPlayer.ClientId, out BuildBrushControllerServer? controller))
            return;

        long lastAppliedSeq = 0;
        lastAppliedSeqByClientId.TryGetValue(fromPlayer.ClientId, out lastAppliedSeq);

        // Ignore out-of-order brush updates.
        if (packet.seq <= lastAppliedSeq)
        {
            if (config?.BuildBrushDebugLogging == true)
            {
                Logger.Debug(
                    "[BuildBrush][Debug][ServerRecvIgnored]: player={0} seq={1} lastAppliedSeq={2}",
                    fromPlayer.PlayerName,
                    packet.seq,
                    lastAppliedSeq
                );
            }

            serverChannel.SendPacket(new Packet_BuildBrushAck { lastAppliedSeq = lastAppliedSeq }, fromPlayer);
            return;
        }

        if (config?.BuildBrushDebugLogging == true)
        {
            Logger.Debug(
                "[BuildBrush][Debug][ServerRecv]: player={0} seq={1} isActive={2} orientationIndex={3} snapping={4} pos={5}",
                fromPlayer.PlayerName,
                packet.seq,
                packet.isActive,
                packet.orientationIndex,
                packet.snapping,
                packet.position
            );
        }

        lastAppliedSeqByClientId[fromPlayer.ClientId] = packet.seq;

        controller.ApplyState(packet);

        serverChannel.SendPacket(new Packet_BuildBrushAck { lastAppliedSeq = packet.seq }, fromPlayer);

        // Sync dimension changes to nearby players
        if (controller.Brush.Dimension?.IsInitialized == true)
        {
            controller.Brush.SyncDimensionToPlayers([fromPlayer]);
        }
    }
    #endregion
}
