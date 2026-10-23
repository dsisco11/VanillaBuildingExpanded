using System;
using System.Runtime.CompilerServices;

using VanillaBuildingExpanded.Config;
using VanillaBuildingExpanded.Networking;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client;

namespace VanillaBuildingExpanded.BuildBrush;

/// <summary>
/// Handles build brush logic on the client side.
/// </summary>
public class BuildBrushSystem_Client : ModSystem
{
    #region Fields
    private ICoreClientAPI? api;
    private BuildBrushControllerClient? controller;
    private BuildModeIndicatorBinding? modeIndicator;
    private IInventory? subscribedHotbar;
    private long fastTickId;
    private long slowTickId;
    protected IClientNetworkChannel? clientChannel;

    private VbeConfig? config;

    private long debugLocalSeq;
    private long debugLastAckSeq = 0;

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
    /// <summary>Loads this system only on its owning game side.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
    /// <summary>Registers client input, networking and brush update callbacks.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;

        controller = new BuildBrushControllerClient(api, CreateBrush);
        controller.ModeChanged += SendToServer;
        modeIndicator = new BuildModeIndicatorBinding(controller, () => new BuildModeHud(api));

        config = VbeConfig.Get(api);

        // Register entity renderer
        api.RegisterEntityRendererClass(BuildBrushEntity.RendererClassName, typeof(BuildBrushEntityRenderer));

        // Networking
        clientChannel = api.Network.GetChannel(Mod.Info.ModID);
        clientChannel.SetMessageHandler<Packet_SetBuildBrush>(HandlePacket_SetBuildBrush);
        clientChannel.SetMessageHandler<Packet_BuildBrushAck>(HandlePacket_BuildBrushAck);

        // User input
        RegisterInputHandlers();

        // Game events
        fastTickId = api.Event.RegisterGameTickListener(Thunk_Client, 50);
        slowTickId = api.Event.RegisterGameTickListener(Thunk_Client_Slow, 150);
        api.Event.PlayerJoin += Event_PlayerJoin;
        api.Event.PlayerDeath += Event_PlayerDeath;
        api.Event.BlockChanged += Event_BlockChanged;
        api.Event.AfterActiveSlotChanged += Event_AfterActiveSlotChanged;
        api.Event.LeaveWorld += Event_LeaveWorld;
    }

    /// <summary>Releases resources owned by this system.</summary>
    public override void Dispose()
    {
        if (api is null) return;
        Event_LeaveWorld();
        controller!.ModeChanged -= SendToServer;
        api.Event.UnregisterGameTickListener(fastTickId);
        api.Event.UnregisterGameTickListener(slowTickId);
        api.Event.PlayerJoin -= Event_PlayerJoin;
        api.Event.PlayerDeath -= Event_PlayerDeath;
        api.Event.BlockChanged -= Event_BlockChanged;
        api.Event.AfterActiveSlotChanged -= Event_AfterActiveSlotChanged;
        api.Event.LeaveWorld -= Event_LeaveWorld;
        api.Input.InWorldAction -= InWorldAction;
        base.Dispose();
    }
    #endregion

    #region Public
    /// <summary>Gets the brush owned by the current player session.</summary>
    public BuildBrushInstance GetBrush()
    {
        if(Player is null || api?.World is null)
        {
            throw new InvalidOperationException("Cannot get build brush instance: Player or World is null.");
        }

        return controller!.GetBrush();
    }
    #endregion

    #region Private brush construction
    /// <summary>Creates the local session brush from live game inputs at its first use.</summary>
    private BuildBrushInstance CreateBrush()
    {
        IClientPlayer player = Player;
        // Bind the same session owner to native validation and deferred selection reads.
        return new BuildBrushInstance(api!.World, player.InventoryManager, player.PlayerUID, player.PlayerName,
            new PlayerBrushPlacementValidator(api.World, player), () => player.CurrentBlockSelection);
    }
    #endregion

    #region Event Handlers
    /// <summary>Refreshes placement when the block at the preview position changes.</summary>
    private void Event_BlockChanged(BlockPos pos, Block oldBlock)
    {
        BuildBrushInstance? brush = GetBrush();
        // When a block is changed, we need to re-validate the placement
        if (brush is null || !brush.IsActive)
            return;

        // make sure the block that changes was the one we were just about to place
        if (brush.Position != pos)
            return;

        controller!.OnBlockPlacedClient(Player.CurrentBlockSelection);
    }

    /// <summary>Refreshes the source block from the newly active inventory slot.</summary>
    private void Event_AfterActiveSlotChanged(ActiveSlotChangeEventArgs e)
    {
        BuildBrushInstance? brush = GetBrush();
        if (brush is null)
            return;

        IPlayerInventoryManager inv = Player!.InventoryManager;
        ItemSlot? activeSlot = inv.GetHotbarInventory()[e.ToSlot];
        if (activeSlot is not null)
        {
            controller!.TryUpdate(Player.CurrentBlockSelection);
            SendToServer();
        }
    }

    /// <summary>Subscribes local inventory refresh and initializes mode off.</summary>
    private void Event_PlayerJoin(IClientPlayer byPlayer)
    {
        if (byPlayer != Player)
            return;

        if (subscribedHotbar is not null)
            subscribedHotbar.SlotModified -= Event_HotbarSlotModified;
        subscribedHotbar = byPlayer.InventoryManager?.GetHotbarInventory();
        if (subscribedHotbar is not null)
        {
            subscribedHotbar.SlotModified += Event_HotbarSlotModified;
        }

        // New sessions start with mode disabled, regardless of equipment.
        controller!.SetBuildMode(false, null);
        modeIndicator ??= new BuildModeIndicatorBinding(controller, () => new BuildModeHud(api!));
        Logger.Audit("[BuildBrush][OnPlayerJoin]: Initializing build brush for player.");
    }

    /// <summary>Disables the local mode on death and publishes the transition before respawning.</summary>
    private void Event_PlayerDeath(IClientPlayer byPlayer)
    {
        if (byPlayer == Player)
            controller!.SetBuildMode(false, null);
    }

    /// <summary>Retires session artwork before clearing mode, without sending to a departing server.</summary>
    private void Event_LeaveWorld()
    {
        modeIndicator?.Dispose();
        modeIndicator = null;
        if (subscribedHotbar is not null)
            subscribedHotbar.SlotModified -= Event_HotbarSlotModified;
        subscribedHotbar = null;
        if (controller is not null && controller.State.IsActive)
        {
            controller.ModeChanged -= SendToServer;
            controller.SetBuildMode(false, null);
            controller.ModeChanged += SendToServer;
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

        BuildBrushInstance? brush = GetBrush();
        if (brush is null)
            return;

        controller!.TryUpdate(Player.CurrentBlockSelection);
        SendToServer();
    }

    /// <summary>Updates local placement from the live selection and sends changed positions.</summary>
    private void Thunk_Client(float dt)
    {
        if (Player is null)
            return;

        if (api?.World is null)
            return;

        BuildBrushInstance? brush = GetBrush();
        if (brush is null)
            return;

        if (!brush.IsActive && !brush.IsDirty)
            return;

        BlockSelection? currentSelection = this.api!.World?.Player?.CurrentBlockSelection;
        controller!.TryUpdate(currentSelection);

        // Send position updates to server when position changes
        if (!Equals(brush.Position, lastSentPosition))
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

    /// <summary>Rechecks active preview validity using the current selection.</summary>
    private void Thunk_Client_Slow(float dt)
    {
        if (Player is null)
            return;

        if (api?.World is null)
            return;

        BuildBrushInstance? brush = GetBrush();
        if (brush is null)
            return;

        if (!brush.IsActive && !brush.IsDirty)
            return;

        BlockSelection? currentSelection = this.api!.World?.Player?.CurrentBlockSelection;
        if (currentSelection is null)
            return;

        controller!.TryUpdatePlacementValidity(currentSelection!);
    }
    #endregion

    #region API
    /// <summary>Cycles the local brush orientation and publishes successful changes.</summary>
    public bool RotateCursor(EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        BuildBrushInstance? brush = GetBrush();
        if (brush is null || !controller!.CanUseBrushControls)
        {
            return false;
        }

        if (config?.BuildBrushDebugLogging == true)
        {
            Logger.Debug(
                "[BuildBrush][Debug][Rotate]: direction={0} pre(orientationIndex={1}, pos={2})",
                direction,
                brush.OrientationIndex,
                brush.Position
            );
        }

        if (controller!.RotateCursor(direction))
        {
            controller.TryUpdate(Player.CurrentBlockSelection);
            SendToServer();

            if (config?.BuildBrushDebugHud == true && this.api is not null)
            {
                this.api.TriggerIngameError(
                    this,
                    $"{Mod.Info.ModID}:buildbrush-debug-status",
                    $"BuildBrush seq={debugLocalSeq} ack={debugLastAckSeq} ori={brush.OrientationIndex} pos={brush.Position}"
                );
            }
            return true;
        }
        return false;
    }

    /// <summary>Cycles snapping on the local controller when a client world exists.</summary>
    public bool CycleSnappingMode(EModeCycleDirection direction = EModeCycleDirection.Forward)
    {
        if (api?.World is null || !controller!.CycleSnappingMode(direction))
            return false;

        controller.TryUpdate(Player.CurrentBlockSelection);
        SendToServer();
        return true;
    }
    /// <summary>
    /// Sends the current build brush state to the server.
    /// </summary>
    public void SendToServer()
    {
        BuildBrushInstance? brush = GetBrush();
        if (brush is null)
        {
            return;
        }

        controller!.SyncStateFromBrush();
        debugLocalSeq++;

        if (config?.BuildBrushDebugLogging == true)
        {
            Logger.Debug(
                "[BuildBrush][Debug][SendToServer]: seq={0} isActive={1} orientationIndex={2} snapping={3} pos={4}",
                debugLocalSeq,
                brush.IsActive,
                brush.OrientationIndex,
                brush.Snapping,
                brush.Position
            );
        }

        clientChannel.SendPacket(
            new Packet_SetBuildBrush()
            {
                seq = debugLocalSeq,
                isActive = controller.State.IsActive,
                orientationIndex = controller.State.OrientationIndex,
                position = controller.State.Position,
                snapping = controller.State.Snapping
            });

        if (config?.BuildBrushDebugHud == true && this.api is not null)
        {
            this.api.TriggerIngameError(
                this,
                $"{Mod.Info.ModID}:buildbrush-debug-status",
                $"BuildBrush seq={debugLocalSeq} ack={debugLastAckSeq} ori={brush.OrientationIndex} pos={brush.Position}"
            );
        }
    }
    #endregion

    #region Network Handlers
    /// <summary>Handles a received brush-state packet at the game network boundary.</summary>
    private void HandlePacket_SetBuildBrush(Packet_SetBuildBrush packet)
    {
        Logger.Audit("[BuildBrush][OnSetBuildBrushPacket]: Received brush update from server - this should not happen.");
        BuildBrushInstance? brush = GetBrush();
        if (brush is null)
            return;

        brush.OrientationIndex = packet.orientationIndex;
        //brush.Position = packet.position;
    }

    /// <summary>Records the latest server acknowledgment for client placement gating.</summary>
    private void HandlePacket_BuildBrushAck(Packet_BuildBrushAck packet)
    {
        debugLastAckSeq = Math.Max(debugLastAckSeq, packet.lastAppliedSeq);

        if (config?.BuildBrushDebugLogging == true)
        {
            Logger.Debug("[BuildBrush][Debug][Ack]: lastAppliedSeq={0}", packet.lastAppliedSeq);
        }

        if (config?.BuildBrushDebugHud == true && this.api is not null)
        {
            var brush = GetBrush();
            this.api.TriggerIngameError(
                this,
                $"{Mod.Info.ModID}:buildbrush-debug-status",
                $"BuildBrush seq={debugLocalSeq} ack={debugLastAckSeq} ori={brush.OrientationIndex} pos={brush.Position}"
            );
        }
    }
    #endregion

    #region Input Handling

    /// <summary>Registers brush hotkeys and right-click handling with client input.</summary>
    private void RegisterInputHandlers()
    {
        api.Input.InWorldAction += this.InWorldAction;
        api.Input.TryRegisterHotKey("vbe.ToggleBuildMode", Lang.Get($"{Mod.Info.ModID}:vbe-hotkey-toggle-build-mode"),
            GlKeys.Tilde, HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler("vbe.ToggleBuildMode", Input_ToggleBuildMode);
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

    /// <summary>Toggles build mode for a living player and reports the resulting state.</summary>
    private bool Input_ToggleBuildMode(KeyCombination keys)
    {
        if (keys.OnKeyUp || Player?.Entity?.Alive != true)
            return false;

        bool enabled = !GetBrush().IsActive;
        controller!.SetBuildMode(enabled, Player.CurrentBlockSelection);
        api!.TriggerIngameError(this, $"{Mod.Info.ModID}:build-mode-status",
            Lang.Get($"{Mod.Info.ModID}:build-mode-{(enabled ? "enabled" : "disabled")}"));
        return true;
    }

    /// <summary>Updates face-offset snapping while the configured key is pressed.</summary>
    private bool Input_ToggleFaceOffsetPlacement(KeyCombination keys)
    {
        bool handled = controller!.SetFaceOffset(!keys.OnKeyUp);
        if (handled)
        {
            controller.TryUpdate(Player.CurrentBlockSelection);
            SendToServer();
        }
        return handled;
    }

    /// <summary>Consumes a forward rotation key only when an orientation actually changes.</summary>
    private bool Input_RotateBuildCursor_Forward(KeyCombination keys)
    {
        return !keys.OnKeyUp && RotateCursor(EModeCycleDirection.Forward);
    }

    /// <summary>Consumes a backward rotation key only when an orientation actually changes.</summary>
    private bool Input_RotateBuildCursor_Backward(KeyCombination keys)
    {
        return !keys.OnKeyUp && RotateCursor(EModeCycleDirection.Backward);
    }

    /// <summary>Consumes forward snapping input only for a usable brush.</summary>
    private bool Input_CycleSnappingMode_Forward(KeyCombination keys)
    {
        return !keys.OnKeyUp && CycleSnappingMode(EModeCycleDirection.Forward);
    }

    /// <summary>Consumes backward snapping input only for a usable brush.</summary>
    private bool Input_CycleSnappingMode_Backward(KeyCombination keys)
    {
        return !keys.OnKeyUp && CycleSnappingMode(EModeCycleDirection.Backward);
    }
    /// <summary>Routes right-click input through brush placement while preserving default handling otherwise.</summary>
    private void InWorldAction(EnumEntityAction action, bool on, ref EnumHandling handling)
    {
        if (handling != EnumHandling.PassThrough)
            return;
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
        BuildBrushInstance? brush = GetBrush();
        if (brush is null || !brush.IsActive)
            return;

        // Input may arrive between ticks: publish any refreshed source or target before native placement.
        controller!.TryUpdate(Player.CurrentBlockSelection);
        BuildBrushState sent = controller.State;
        if (!Equals(brush.Position, sent.Position) || brush.BlockId != sent.BlockId
            || brush.OrientationIndex != sent.OrientationIndex || brush.Snapping != sent.Snapping)
            SendToServer();

        if (brush is null || brush.IsDisabled)
            return;

        // An active brush owns this placement attempt, including native validation failures.
        handling = EnumHandling.PreventSubsequent;

        if (!brush.TryBuildPlacementRequest(brush.Selection, out BuildBrushPlacementRequest? request) || request is null)
        {
            Logger.Warning($"[BuildBrush][{nameof(TryPlaceBlock)}]: Unable to build placement request.");
            return;
        }

        if (!CheckBlockPlacement(request.Position, request.ItemStack, out string precheckFailure))
            return;

        // Optional correctness gate. Disabled by default to avoid behavior changes.
        // When enabled, prevents placement until the server has acknowledged the latest brush state.
        if (config?.BuildBrushGatePlacementOnAck == true && debugLastAckSeq < debugLocalSeq)
        {
            if (config?.BuildBrushDebugLogging == true)
            {
                Logger.Debug(
                    "[BuildBrush][Debug][ClientPlaceBlocked]: waiting for ack (seq={0}, ack={1})",
                    debugLocalSeq,
                    debugLastAckSeq
                );
            }
            handling = EnumHandling.PreventSubsequent;
            return;
        }

        if (config?.BuildBrushDebugLogging == true)
        {
            Logger.Debug(
                "[BuildBrush][Debug][ClientPlace]: seq={0} ack={1} pos={2} ori={3} blockSelPos={4}",
                debugLocalSeq,
                debugLastAckSeq,
                request.Position,
                brush.OrientationIndex,
                request.Selection.Position
            );
        }

        string failureCode = string.Empty;
        if (request.PlacementBlock.CanPlaceBlock(api.World, Player, request.Selection, ref failureCode))
        {
            Block oldBlock = World.BlockAccessor.GetBlock(request.Position);
            bool placed = request.PlacementBlock.DoPlaceBlock(World, Player, request.Selection, request.ItemStack);
            if (placed)
            {
                // Match vanilla's interaction sound lookup, including block-specific overrides and decor.
                BlockSounds? sounds = request.PlacementBlock.GetSounds(World.BlockAccessor, request.Selection, null);
                if (sounds is not null)
                {
                    api.World.PlaySound(sounds.Place);
                }
            }

            // Keep client-side prediction consistent with brush rotation.
            // Vanilla placement may set rotation from player facing; enforce brush target rotation.
            if (request.Rotation is not null && request.Rotation.HasRotatableEntity)
            {
                var placedBe = World.BlockAccessor.GetBlockEntity(request.Position);
                if (placedBe is not null)
                {
                    request.Rotation.ApplyToPlacedBlockEntity(placedBe, request.Rotation.Current, request.ItemStack);
                }
            }

            api.Network.SendPacketClient(ClientPackets.BlockInteraction(request.Selection, 1, 0));

            if (config?.BuildBrushDebugLogging == true)
            {
                Logger.Debug(
                    "[BuildBrush][Debug][ClientPlace->BlockInteractionSent]: seq={0} pos={1}",
                    debugLocalSeq,
                    request.Selection.Position
                );
            }

            World.BlockAccessor.MarkBlockModified(request.Position);
            World.BlockAccessor.TriggerNeighbourBlockUpdate(request.Position);
            handling = EnumHandling.PreventSubsequent;
            brush.MarkDirty();
            controller!.OnBlockPlacedClient(Player.CurrentBlockSelection);
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
