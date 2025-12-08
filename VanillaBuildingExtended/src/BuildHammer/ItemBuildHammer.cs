using System;
using System.Collections.Generic;

using VanillaBuildingExtended.BuildHammer;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VanillaBuildingExtended;

public class ItemBuildHammer : Item
{
    #region Fields
    private BuildBrushInstance? _brush;
    #endregion

    #region Properties
    public readonly Dictionary<int, BuildBrushInstance> Brushes = [];
    #endregion

    #region Accessors
    protected ILogger Logger => api.Logger;
    protected ICoreServerAPI? server => api as ICoreServerAPI;
    protected ICoreClientAPI? client => api as ICoreClientAPI;
    protected IPlayer? Player => client?.World.Player;

    public BuildBrushInstance? GetBrush(in IPlayer? player)
    {
        if (api.Side == EnumAppSide.Client)
        {
            // if brush is null but we have the player and world, initialize it
            if (_brush is null && Player is not null && client?.World is not null)
            {
                _brush = new BuildBrushInstance(Player!, client!.World);
            }
            return _brush;
        }

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
    public override void OnLoaded(ICoreAPI api)
    {
        this.api = api;
        if (api.Side == EnumAppSide.Client)
        {
            client!.Event.PlayerEntitySpawn += Event_PlayerEntitySpawn;
            client!.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
            client!.Event.BlockChanged += Event_BlockChanged;
            client!.Event.RegisterGameTickListener(Thunk_Client, 50);
        }
    }

    private void Event_BlockChanged(BlockPos pos, Block oldBlock)
    {
        var brush = GetBrush(Player);
        // When a block is changed, we need to re-validate the placement
        if (brush is null || !brush.IsActive)
            return;

        // make sure the block that changes was the one we were just about to place
        if (brush.Position != pos)
            return;

        brush.TryUpdateBrush();
    }

    private void Event_AfterActiveSlotChanged(ActiveSlotChangeEventArgs obj)
    {
        int slotId = obj.ToSlot;
        var brush = GetBrush(Player);
        if (brush is not null)
        {
            brush.BlockId = Player!.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.BlockId ?? 0;
        }
    }

    private void Event_PlayerEntitySpawn(IClientPlayer byPlayer)
    {
        var brush = GetBrush(byPlayer);
        if (brush is null)
            return;
        bool isHoldingHammer = GetIsHoldingBuildHammer(byPlayer);
        brush.IsActive = isHoldingHammer;
    }

    public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null)
    {
        if (world is IClientWorldAccessor clientWorld)
        {
            IClientPlayer byPlayer = clientWorld.Player;
            var brush = GetBrush(byPlayer);
            if (brush is null)
                return;

            if (extractedStack is not null)
            {
                brush.IsActive = false;
                return;
            }
            bool isHoldingHammer = GetIsHoldingBuildHammer(byPlayer);
            brush.IsActive = isHoldingHammer;
        }
    }
    #endregion

    #region Private Methods
    private bool GetIsHoldingBuildHammer(in IClientPlayer? byPlayer)
    {
        return byPlayer?.Entity.LeftHandItemSlot?.Itemstack?.Item is ItemBuildHammer;
    }

    private void Thunk_Client(float dt)
    {
        if (Player is null)
            return;

        if (client?.World is null)
            return;

        var brush = GetBrush(Player);
        if (brush is null)
            return;

        if (!brush.IsActive)
            return;

        BlockSelection? currentSelection = this.client!.World?.Player?.CurrentBlockSelection;
        brush.TryUpdateBrush(currentSelection);
    }
    #endregion

    #region API
    public void RotateCursor(in IPlayer player, EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        var brush = GetBrush(player);
        if (brush is null || !brush.IsActive)
            return;

        var angle = direction == EModeCycleDirection.Forward ? 90 : -90;
        brush.Rotation += angle;
    }

    public void CycleSnappingMode(in IPlayer player, EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        if (client?.World is null)
            return;

        var brush = GetBrush(player);
        if (brush is null)
        {
            return;
        }

        int d = direction == EModeCycleDirection.Forward ? 1 : -1;
        int SnappingModeIndex = BuildBrushInstance.BrushSnappingModes.IndexOf(brush.Snapping);
        SnappingModeIndex = (SnappingModeIndex + d) % BuildBrushInstance.BrushSnappingModes.Length;
        brush.Snapping = BuildBrushInstance.BrushSnappingModes[SnappingModeIndex];

        string text = Lang.Get($"vbe-snapping-mode-changed--{brush.Snapping.GetCode()}");
        client.TriggerIngameError(this, "vbe-snapping-mode-changed", text);
    }
    #endregion
}
