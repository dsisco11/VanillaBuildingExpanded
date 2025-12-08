using System;

using VanillaBuildingExtended.Networking;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client;

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
    private readonly BuildPreviewRenderer renderer;
    #endregion

    #region Accessors
    protected IClientWorldAccessor World => api.World;
    protected IClientPlayer Player => api.World.Player;
    #endregion

    #region Lifecycle
    public BuildBrushManager_Client(ICoreClientAPI api) : base(api)
    {
        clientChannel = api.Network.GetChannel(NetworkChannelId);
        renderer = new BuildPreviewRenderer(api);
        api.Event.PlayerEntitySpawn += Event_PlayerEntitySpawn;
        api.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
        api.Event.BlockChanged += Event_BlockChanged;
        api.Event.RegisterGameTickListener(Thunk_Client, 50);

        clientChannel.SetMessageHandler<Packet_SetBuildBrush>(OnSetBuildBrushPacket);
        api.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "build_brush");

        RegisterInputHandlers();
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

    #region Event Handlers
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

    #region Input Handling

    private void RegisterInputHandlers()
    {
        api.Input.InWorldAction += this.InWorldAction;

        api.Input.TryRegisterHotKey("vbe.RotateBuildCursor_Forward", Lang.Get("vbe-hotkey-rotate-build-cursor--forward"), GlKeys.R, HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler("vbe.RotateBuildCursor_Forward", this.Input_RotateBuildCursor_Forward);

        api.Input.TryRegisterHotKey("vbe.RotateBuildCursor_Backward", Lang.Get("vbe-hotkey-rotate-build-cursor--backward"), GlKeys.R, HotkeyType.CharacterControls, shiftPressed: true);
        api.Input.SetHotKeyHandler("vbe.RotateBuildCursor_Backward", this.Input_RotateBuildCursor_Backward);

        api.Input.TryRegisterHotKeyFirst("vbe.CycleSnappingMode_Forward", Lang.Get("vbe-hotkey-cycle-snapping-mode--forward"), (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle), HotkeyType.MouseModifiers, shiftPressed: false);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Forward", this.Input_CycleSnappingMode_Forward);

        api.Input.TryRegisterHotKeyFirst("vbe.CycleSnappingMode_Backward", Lang.Get("vbe-hotkey-cycle-snapping-mode--backward"), (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle), HotkeyType.MouseModifiers, shiftPressed: true);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Backward", this.Input_CycleSnappingMode_Backward);
    }

    private bool Input_RotateBuildCursor_Forward(KeyCombination keys)
    {
        var brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        RotateCursor(Player, EModeCycleDirection.Forward);
        return true;
    }

    private bool Input_RotateBuildCursor_Backward(KeyCombination keys)
    {
        var brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        RotateCursor(Player, EModeCycleDirection.Backward);
        return true;
    }

    private bool Input_CycleSnappingMode_Forward(KeyCombination keys)
    {
        var brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        CycleSnappingMode(Player, EModeCycleDirection.Forward);
        return true;
    }

    private bool Input_CycleSnappingMode_Backward(KeyCombination keys)
    {
        var brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
        {
            return false;
        }
        CycleSnappingMode(Player, EModeCycleDirection.Backward);
        return true;
    }
    
    private void InWorldAction(EnumEntityAction action, bool on, ref EnumHandling handling)
    {
        handling = EnumHandling.PassThrough;
        if (action == EnumEntityAction.InWorldRightMouseDown && on)
        {
            TryPlaceBlock(ref handling);
        }
    }
    #endregion

    #region Block Placement
    protected bool TryPrecheckBlockPlacement(in BlockPos position, in ItemStack itemstack, out string failureCode)
    {
        failureCode = string.Empty;
        if (!World.BlockAccessor.IsValidPos(position))
        {
            failureCode = "outsideworld";
            return false;
        }
        // Block.CanPlaceBlock does its own access checks, so we can skip this.
        //if (!tryAccess(blockSelection, EnumBlockAccessFlags.BuildOrBreak))
        //{
        //    return false;
        //}

        if (itemstack is null || itemstack.Class != EnumItemClass.Block)
        {
            return false;
        }

        Block oldBlock = World.BlockAccessor.GetBlock(position);
        Block liqBlock = World.BlockAccessor.GetBlock(position, 2);
        bool preventPlacementInLava = true;// unsure where to get the actual setting from, so we just hardcode it for now
        if (preventPlacementInLava && liqBlock.LiquidCode == "lava" && Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            failureCode = "toohottoplacehere";
            return false;
        }
        return true;
    }

    protected void TryPlaceBlock(ref EnumHandling handling)
    {
        if (!HasHammer(Player))
        {
            return;
        }

        var brush = GetBrush(Player);
        BlockPos brushPos = brush.Position;
        BlockSelection blockSelection = brush.Selection;
        if (blockSelection is null)
        {
            Logger.Warning("[Build Hammer]: No valid placement position.");
            return;
        }

        ItemStack? stackToPlace = brush.ItemStack;
        if (stackToPlace is null)
        {
            Logger.Warning("[Build Hammer]: No item selected for placement.");
            return;
        }

        Block block = stackToPlace.Block;
        if (block is null)
        {
            Logger.Warning("[Build Hammer]: Selected item is not a block.");
            return;
        }

        handling = EnumHandling.PreventSubsequent;
        if (!TryPrecheckBlockPlacement(brushPos, stackToPlace, out string precheckFailure))
        {
            Logger.Warning($"[Build Hammer]: Precheck for block placement failed: {precheckFailure}");
            api.TriggerIngameError(this, precheckFailure, Lang.Get($"placefailure-{precheckFailure}"));
            return;
        }

        string failureCode = string.Empty;
        if (block.CanPlaceBlock(api.World, Player, blockSelection, ref failureCode))
        {
            Block oldBlock = World.BlockAccessor.GetBlock(brushPos);
            block.DoPlaceBlock(World, Player, blockSelection, stackToPlace);
            api.Network.SendPacketClient(ClientPackets.BlockInteraction(blockSelection, 1, 0));
            World.BlockAccessor.MarkBlockModified(brushPos);
            World.BlockAccessor.TriggerNeighbourBlockUpdate(brushPos);
        }
    }
    #endregion
}
