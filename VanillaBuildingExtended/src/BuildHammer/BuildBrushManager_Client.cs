using System;

using VanillaBuildingExtended.Networking;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VanillaBuildingExtended.BuildHammer;

/// <summary>
/// Handles build brush logic on the client side.
/// </summary>
public class BuildBrushManager_Client : BuildBrushManager
{
    #region Fields
    private ICoreClientAPI api => (ICoreClientAPI)coreApi;
    private BuildBrushInstance? _brush;
    protected readonly IClientNetworkChannel clientChannel;
    #endregion

    #region Accessors
    protected IClientPlayer? Player => api.World.Player;
    #endregion

    #region Lifecycle
    public BuildBrushManager_Client(ICoreClientAPI api) : base(api)
    {
        clientChannel = api.Network.GetChannel(NetworkChannelId);
        api.Event.PlayerEntitySpawn += Event_PlayerEntitySpawn;
        api.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
        api.Event.BlockChanged += Event_BlockChanged;
        api.Event.RegisterGameTickListener(Thunk_Client, 50);

        clientChannel.SetMessageHandler<Packet_SetBuildBrush>(OnSetBuildBrushPacket);
    }
    #endregion

    #region Public
    public override BuildBrushInstance? GetBrush(in IPlayer? player)
    {
        // if brush is null but we have the player and world, initialize it
        if (_brush is null && Player is not null && api?.World is not null)
        {
            _brush = new BuildBrushInstance(Player!, api!.World);
        }
        return _brush;
    }
    #endregion

    #region Handlers
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

    private void Thunk_Client(float dt)
    {
        if (Player is null)
            return;

        if (api?.World is null)
            return;

        var brush = GetBrush(Player);
        if (brush is null)
            return;

        if (!brush.IsActive)
            return;

        BlockSelection? currentSelection = this.api!.World?.Player?.CurrentBlockSelection;
        brush.TryUpdateBrush(currentSelection);
    }
    #endregion

    #region Private Methods
    private bool GetIsHoldingBuildHammer(in IClientPlayer? byPlayer)
    {
        return byPlayer?.Entity.LeftHandItemSlot?.Itemstack?.Item is ItemBuildHammer;
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
        clientChannel.SendPacket(
            new Packet_SetBuildBrush()
            {
                rotation = brush.Rotation,
                position = brush.Position,
            });
    }

    public void CycleSnappingMode(in IPlayer player, EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        if (api?.World is null)
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
        api.TriggerIngameError(this, "vbe-snapping-mode-changed", text);
    }
    #endregion

    #region Network Handlers
    private void OnSetBuildBrushPacket(Packet_SetBuildBrush packet)
    {
        var brush = GetBrush(Player);
        if (brush is null)
            return;

        brush.Rotation = packet.rotation;
        //brush.Position = packet.position;
    }
    #endregion
}
