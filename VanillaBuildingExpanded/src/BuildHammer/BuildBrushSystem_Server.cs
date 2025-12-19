using System;
using System.Collections.Generic;

using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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
    protected readonly Dictionary<int, BuildBrushInstance> Brushes = [];
    #endregion

    #region Accessors
    protected ILogger Logger => api.Logger;
    #endregion

    #region Hooks
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
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
        Brushes.Clear();
        BuildBrushInstance.OrientationVariantCache.Clear();
    }
    #endregion

    #region Public
    public BuildBrushInstance? GetBrush(in IPlayer? player)
    {
        if (player is null || api.World is null)
            return null;

        if (!Brushes.TryGetValue(player.ClientId, out BuildBrushInstance? brush))
        {
            Logger.Warning($"Build brush instance not found for player {player.PlayerName} (ID: {player.ClientId})");
            return null;
        }

        return brush;
    }
    #endregion

    #region Block Placement
    public bool TryPlaceBrushBlock(in IWorldAccessor world, in IPlayer? byPlayer, in ItemStack? itemstack, in BlockSelection blockSel)
    {
        var brush = GetBrush(byPlayer);
        if (brush is null || brush.IsDisabled)
            return false;

        // We should be able to place the block, if we dont have a transformed block then we will still return true to act as though we placed it (to prevent normal placement and unexpected behavior)
        if (brush.BlockTransformed is not null)
        {
            brush.BlockTransformed.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
            
            // Apply rotation for dynamic orientation blocks
            if (brush.OrientationMode == EOrientationMode.Dynamic && brush.RotationY != 0f)
            {
                TryApplyBlockEntityRotation(world, blockSel.Position, brush.RotationY);
            }
            
            world.BlockAccessor.MarkBlockModified(blockSel.Position);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(blockSel.Position);
            brush.OnBlockPlaced();
        }
        return true;
    }

    /// <summary>
    /// Attempts to set the mesh angle/rotation on a placed block entity using IRotatable.OnTransformed.
    /// </summary>
    /// <param name="world">The world accessor.</param>
    /// <param name="position">The position of the block entity.</param>
    /// <param name="rotationRadians">The rotation angle in radians.</param>
    /// <returns>True if the rotation was applied successfully; otherwise, false.</returns>
    private bool TryApplyBlockEntityRotation(IWorldAccessor world, Vintagestory.API.MathTools.BlockPos position, float rotationRadians)
    {
        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(position);
        if (blockEntity is not IRotatable rotatable)
        {
            return false;
        }

        // Convert radians to degrees for OnTransformed (it expects degrees)
        int degreeRotation = -(int)Math.Round(rotationRadians * GameMath.RAD2DEG);

        // Get the current tree attributes from the block entity
        TreeAttribute tree = new();
        blockEntity.ToTreeAttributes(tree);

        // Apply the rotation via OnTransformed - it modifies the tree attributes
        rotatable.OnTransformed(world, tree, degreeRotation, [], [], null);

        // Re-apply the modified attributes back to the block entity
        blockEntity.FromTreeAttributes(tree, world);
        blockEntity.MarkDirty(true);

        return true;
    }
    #endregion

    #region Event Handlers
    /// <summary>
    /// Initializes the player's build brush instance on join.
    /// </summary>
    private void Event_PlayerJoin(IServerPlayer byPlayer)
    {
        BuildBrushInstance brush = new(byPlayer, api.World);
        Brushes.Add(byPlayer.ClientId, brush);

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

        brush.IsActive = byPlayer.IsHoldingBuildHammer();
        brush.TryUpdateBlockId();
    }

    /// <summary>
    /// Erases the player's build brush instance on disconnect.
    /// </summary>
    private void Event_PlayerDisconnect(IServerPlayer byPlayer)
    {
        Brushes.Remove(byPlayer.ClientId);
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

        brush.BlockId = inventory.ActiveHotbarSlot?.Itemstack?.Block?.BlockId ?? 0;
    }

    /// <summary>
    /// Handles updating the brush's block ID after a players active held item changes.
    /// </summary>
    private void Event_AfterActiveSlotChanged(IServerPlayer byPlayer, ActiveSlotChangeEventArgs evt)
    {
        var brush = GetBrush(byPlayer);
        if (brush is not null)
        {
            brush.BlockId = byPlayer!.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.BlockId ?? 0;
        }
    }
    #endregion

    #region Network Handlers
    /// <summary>
    /// Handles the <see cref="Packet_SetBuildBrush"/> packet from clients to update their brush state on the server.
    /// </summary>
    private void HandlePacket_SetBuildBrush(IServerPlayer fromPlayer, Packet_SetBuildBrush packet)
    {
        BuildBrushInstance brush = GetBrush(fromPlayer);
        if (brush is null)
            return;

        brush.IsActive = packet.isActive;
        brush.Snapping = packet.snapping;
        brush.OrientationIndex = packet.orientationIndex;
        brush.RotationY = packet.rotationY;
        brush.Position = packet.position;
    }
    #endregion
}
