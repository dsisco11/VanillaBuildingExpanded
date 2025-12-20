using System;
using System.Runtime.CompilerServices;

using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Handles build brush logic on the client side.
/// </summary>
public class BuildBrushSystem_Client : ModSystem
{
    #region Fields
    private ICoreClientAPI? api;
    private BuildBrushInstance? _brush;
    protected IClientNetworkChannel? clientChannel;
    private BuildPreviewRenderer? renderer;

    /// <summary>
    /// Tracks the last position sent to the server to avoid sending unnecessary updates.
    /// </summary>
    private BlockPos? lastSentPosition;
    #endregion

    #region Accessors
    protected IClientWorldAccessor World => api.World;
    protected IClientPlayer Player => api.World.Player;
    protected ILogger Logger => api.Logger;
    #endregion

    #region Hooks
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;

        // Register entity renderer
        api.RegisterEntityRendererClass(BuildBrushEntity.RendererClassName, typeof(BuildBrushEntityRenderer));

        // Rendering
        renderer = new BuildPreviewRenderer(api, this);
        api.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "build_brush");

        // Networking
        clientChannel = api.Network.GetChannel(Mod.Info.ModID);
        clientChannel.SetMessageHandler<Packet_SetBuildBrush>(HandlePacket_SetBuildBrush);

        // User input
        RegisterInputHandlers();

        // Game events
        api.Event.RegisterGameTickListener(Thunk_Client, 50);
        api.Event.RegisterGameTickListener(Thunk_Client_Slow, 150);
        api.Event.PlayerJoin += Event_PlayerJoin;
        api.Event.BlockChanged += Event_BlockChanged;
        api.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
    }

    public override void Dispose()
    {
        BuildBrushRotationInfo.ClearCaches();
    }
    #endregion

    #region Public
    public BuildBrushInstance GetBrush(in IPlayer? player)
    {
        if(Player is null || api?.World is null)
        {
            throw new InvalidOperationException("Cannot get build brush instance: Player or World is null.");
        }

        // if brush is null but we have the player and world, initialize it
        _brush ??= new BuildBrushInstance(Player!, api!.World);
        return _brush!;
    }
    #endregion

    #region Event Handlers
    private void Event_BlockChanged(BlockPos pos, Block oldBlock)
    {
        BuildBrushInstance? brush = GetBrush(Player);
        // When a block is changed, we need to re-validate the placement
        if (brush is null || !brush.IsActive)
            return;

        // make sure the block that changes was the one we were just about to place
        if (brush.Position != pos)
            return;

        brush.OnBlockPlaced();
        brush.TryUpdateBlockId();
        brush.TryUpdate();
    }

    private void Event_AfterActiveSlotChanged(ActiveSlotChangeEventArgs e)
    {
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null)
            return;

        IPlayerInventoryManager inv = Player!.InventoryManager;
        ItemSlot? activeSlot = inv.GetHotbarInventory()[e.ToSlot];
        if (activeSlot is not null)
        {
            brush.BlockId = activeSlot.Itemstack?.Block?.BlockId;
        }
    }

    private void Event_PlayerJoin(IClientPlayer byPlayer)
    {
        if (byPlayer != Player)
            return;

        IInventory? hotbarInv = byPlayer.InventoryManager?.GetHotbarInventory();
        // unsure if inventory instance persists across respawns, so re-subscribe each time
        if (hotbarInv is not null)
        {
            hotbarInv.SlotModified += Event_HotbarSlotModified;
        }

        IInventory? offhandInventory = byPlayer.InventoryManager?.OffhandHotbarSlot.Inventory;
        if (offhandInventory is not null)
        {
            offhandInventory.SlotModified += (int slot) => Event_OffhandSlotModified(byPlayer, slot);
        }

        BuildBrushInstance? brush = GetBrush(byPlayer);
        if (brush is null)
            return;

        brush.IsActive = byPlayer.IsHoldingBuildHammer();
        Logger.Audit("[BuildBrush][OnPlayerJoin]: Initializing build brush for player.");
    }

    /// <summary>
    /// Handles offhand slot modifications to update the brush's active state if the build hammer is equipped.
    /// </summary>
    private void Event_OffhandSlotModified(IClientPlayer byPlayer, int slot)
    {
        BuildBrushInstance brush = GetBrush(byPlayer);
        if (brush is null)
            return;

        bool oldState = brush.IsActive;
        bool newState = byPlayer.IsHoldingBuildHammer();

        brush.IsActive = newState;
        //brushManager.SendToServer();
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
    /// Handles hotbar slot modifications to update the brush's block ID if the contents of the active slot changes.
    /// </summary>
    private void Event_HotbarSlotModified(int slotId)
    {
        IPlayerInventoryManager? inventory = Player!.InventoryManager;
        if (inventory is null)
            return;

        if (slotId != inventory.ActiveHotbarSlotNumber)
            return;

        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null)
            return;

        brush.BlockId = inventory.ActiveHotbarSlot?.Itemstack?.Block?.BlockId;
    }

    private void Thunk_Client(float dt)
    {
        if (Player is null)
            return;

        if (api?.World is null)
            return;

        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null)
            return;

        if (!brush.IsActive && !brush.IsDirty)
            return;

        BlockSelection? currentSelection = this.api!.World?.Player?.CurrentBlockSelection;
        bool positionChanged = brush.TryUpdate(currentSelection);

        // Send position updates to server when position changes
        if (positionChanged && brush.Position is not null && !brush.Position.Equals(lastSentPosition))
        {
            lastSentPosition = brush.Position?.Copy();
            SendToServer();
        }

        try
        {
            brush.TryUpdateBlockId();
        }
        catch (Exception ex)
        {
            Logger.Error($"[Build Brush] Error updating BlockId: {ex}");
        }
    }

    private void Thunk_Client_Slow(float dt)
    {
        if (Player is null)
            return;

        if (api?.World is null)
            return;

        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null)
            return;

        if (!brush.IsActive && !brush.IsDirty)
            return;

        BlockSelection? currentSelection = this.api!.World?.Player?.CurrentBlockSelection;
        if (currentSelection is null)
            return;

        brush.TryUpdatePlacementValidity(currentSelection!);
    }
    #endregion

    #region API
    public void RotateCursor(in IPlayer player, EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        BuildBrushInstance? brush = GetBrush(player);
        if (brush is null || !brush.IsActive)
        {
            return;
        }

        brush.OrientationIndex += (int)direction;
        SendToServer();
    }

    public void CycleSnappingMode(in IPlayer player, EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        if (api?.World is null)
        {
            return;
        }

        BuildBrushInstance? brush = GetBrush(player);
        if (brush is null)
        {
            return;
        }

        int d = (int)direction;
        EBuildBrushSnapping ModeSearchFilter = EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical;
        int SnappingModeIndex = BuildBrushInstance.BrushSnappingModes.IndexOf(brush.Snapping & ModeSearchFilter);
        SnappingModeIndex = (SnappingModeIndex + d + BuildBrushInstance.BrushSnappingModes.Length) % BuildBrushInstance.BrushSnappingModes.Length;
        brush.Snapping = BuildBrushInstance.BrushSnappingModes[SnappingModeIndex];
        brush.DisplaySnappingModeNotice();
    }

    /// <summary>
    /// Sends the current build brush state to the server.
    /// </summary>
    public void SendToServer()
    {
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null)
        {
            return;
        }

        clientChannel.SendPacket(
            new Packet_SetBuildBrush()
            {
                isActive = brush.IsActive,
                orientationIndex = brush.OrientationIndex,
                position = brush.Position,
                snapping = brush.Snapping
            });
    }
    #endregion

    #region Network Handlers
    private void HandlePacket_SetBuildBrush(Packet_SetBuildBrush packet)
    {
        Logger.Audit("[BuildBrush][OnSetBuildBrushPacket]: Received brush update from server - this should not happen.");
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null)
            return;

        brush.OrientationIndex = packet.orientationIndex;
        //brush.Position = packet.position;
    }
    #endregion

    #region Input Handling

    private void RegisterInputHandlers()
    {
        api.Input.InWorldAction += this.InWorldAction;
        api.Input.TryRegisterHotKey("vbe.ToggleFaceOffsetPlacement", Lang.Get($"{Mod.Info.ModID}:vbe-hotkey-toggle-face-offset"), GlKeys.LShift, HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler("vbe.ToggleFaceOffsetPlacement", this.Input_ToggleFaceOffsetPlacement);
        api.Input.GetHotKeyByCode("vbe.ToggleFaceOffsetPlacement").TriggerOnUpAlso = true;

        api.Input.TryRegisterHotKey("vbe.RotateBuildCursor_Forward", Lang.Get($"{Mod.Info.ModID}:vbe-hotkey-rotate-build-cursor-forward"), GlKeys.R, HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler("vbe.RotateBuildCursor_Forward", this.Input_RotateBuildCursor_Forward);

        api.Input.TryRegisterHotKey("vbe.RotateBuildCursor_Backward", Lang.Get($"{Mod.Info.ModID}:vbe-hotkey-rotate-build-cursor-backward"), GlKeys.R, HotkeyType.CharacterControls, shiftPressed: true);
        api.Input.SetHotKeyHandler("vbe.RotateBuildCursor_Backward", this.Input_RotateBuildCursor_Backward);

        api.Input.TryRegisterHotKeyFirst("vbe.CycleSnappingMode_Forward", Lang.Get($"{Mod.Info.ModID}:vbe-hotkey-cycle-snapping-mode-forward"), (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle), HotkeyType.CharacterControls, shiftPressed: false);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Forward", this.Input_CycleSnappingMode_Forward);

        api.Input.TryRegisterHotKeyFirst("vbe.CycleSnappingMode_Backward", Lang.Get($"{Mod.Info.ModID}:vbe-hotkey-cycle-snapping-mode-backward"), (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle), HotkeyType.CharacterControls, shiftPressed: true);
        api.Input.SetHotKeyHandler("vbe.CycleSnappingMode_Backward", this.Input_CycleSnappingMode_Backward);
    }

    private bool Input_ToggleFaceOffsetPlacement(KeyCombination keys)
    {
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null)
            return false;

        if (keys.OnKeyUp)
        {
            brush.Snapping &= ~EBuildBrushSnapping.ApplyFaceNormalOffset;
        }
        else
        {
            brush.Snapping |= EBuildBrushSnapping.ApplyFaceNormalOffset;
        }

        return true;
    }

    private bool Input_RotateBuildCursor_Forward(KeyCombination keys)
    {
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
            return false;

        RotateCursor(Player, EModeCycleDirection.Forward);
        return true;
    }

    private bool Input_RotateBuildCursor_Backward(KeyCombination keys)
    {
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
            return false;

        RotateCursor(Player, EModeCycleDirection.Backward);
        return true;
    }

    private bool Input_CycleSnappingMode_Forward(KeyCombination keys)
    {
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
            return false;

        CycleSnappingMode(Player, EModeCycleDirection.Forward);
        return true;
    }

    private bool Input_CycleSnappingMode_Backward(KeyCombination keys)
    {
        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null || !brush.IsActive)
            return false;

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
    /// <summary>
    /// Attempts to place a block at the current brush position.
    /// </summary>
    /// <param name="handling">
    /// The handling enum to modify based on whether the placement was successful.
    /// </param>
    protected void TryPlaceBlock(ref EnumHandling handling)
    {
        if (!Player.IsHoldingBuildHammer())
            return;

        BuildBrushInstance? brush = GetBrush(Player);
        if (brush is null || brush.IsDisabled)
            return;

        BlockPos brushPos = brush.Position;
        BlockSelection blockSelection = brush.Selection;
        if (blockSelection is null)
        {
            Logger.Warning($"[BuildBrush][{nameof(TryPlaceBlock)}]: No valid BlockSelection.");
            return;
        }

        ItemStack? stackToPlace = brush.ItemStack;
        if (stackToPlace is null)
        {
            Logger.Warning($"[BuildBrush][{nameof(TryPlaceBlock)}]: No item selected for placement.");
            return;
        }

        Block block = stackToPlace.Block;
        if (block is null)
        {
            Logger.Warning($"[BuildBrush][{nameof(TryPlaceBlock)}]: Selected item is not a block.");
            return;
        }

        if (!CheckBlockPlacement(brushPos, stackToPlace, out string precheckFailure))
            return;

        string failureCode = string.Empty;
        if (block.CanPlaceBlock(api.World, Player, blockSelection, ref failureCode))
        {
            Block oldBlock = World.BlockAccessor.GetBlock(brushPos);
            block.DoPlaceBlock(World, Player, blockSelection, stackToPlace);
            api.Network.SendPacketClient(ClientPackets.BlockInteraction(blockSelection, 1, 0));
            World.BlockAccessor.MarkBlockModified(brushPos);
            World.BlockAccessor.TriggerNeighbourBlockUpdate(brushPos);
            handling = EnumHandling.PreventSubsequent;
            brush.MarkDirty();
        }
        else
        {
            Player.InventoryManager.ActiveHotbarSlot.MarkDirty();
        }
    }

    /// <summary>
    /// Checks whether a block could be placed at the given position with the given itemstack.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="itemstack"></param>
    /// <param name="failureCode"></param>
    /// <returns></returns>
    protected bool CheckBlockPlacement(in BlockPos position, in ItemStack itemstack, out string failureCode)
    {
        failureCode = string.Empty;
        if (position is null || itemstack is null)
        {
            return false;
        }

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

    #endregion
}
