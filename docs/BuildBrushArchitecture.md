# BuildBrush architecture notes (Phase 0)

This document captures the current BuildBrush system shape and the key entry points.

## Module map (key files)

- VanillaBuildingExpandedModSystem

  - [VanillaBuildingExpanded/VanillaBuildingExpandedModSystem.cs](../VanillaBuildingExpanded/VanillaBuildingExpandedModSystem.cs)
  - Registers network channel message type(s) and the preview entity type.

- Client orchestration

  - [VanillaBuildingExpanded/src/BuildHammer/BuildBrushSystem_Client.cs](../VanillaBuildingExpanded/src/BuildHammer/BuildBrushSystem_Client.cs)
  - Owns the local brush instance; handles hotkeys/ticks; sends brush-state updates; does client-side placement prediction.

- Server orchestration

  - [VanillaBuildingExpanded/src/BuildHammer/BuildBrushSystem_Server.cs](../VanillaBuildingExpanded/src/BuildHammer/BuildBrushSystem_Server.cs)
  - Owns per-player brush instances; receives brush-state updates; performs authoritative placement.

- Brush model + preview lifecycle

  - [VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs)
  - Holds brush state (block id, orientation, snapping, resolved position) and also owns server-side preview lifecycle (mini-dimension + preview entity).

- Preview mini-dimension

  - [VanillaBuildingExpanded/src/BuildBrush/BrushDimension.cs](../VanillaBuildingExpanded/src/BuildBrush/BrushDimension.cs)
  - Manages the preview dimension contents and syncing.

- Preview entity and renderer

  - Entity: [VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntity.cs](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntity.cs)
  - Renderer: [VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntityRenderer.cs](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntityRenderer.cs)

- Networking

  - Packet: [VanillaBuildingExpanded/src/Networking/SetBuildBrush.packet.cs](../VanillaBuildingExpanded/src/Networking/SetBuildBrush.packet.cs)
  - Channel registration: [VanillaBuildingExpanded/VanillaBuildingExpandedModSystem.cs](../VanillaBuildingExpanded/VanillaBuildingExpandedModSystem.cs)

- Placement intercept
  - [VanillaBuildingExpanded/src/Harmony/BlockBuildHammerIntercept.cs](../VanillaBuildingExpanded/src/Harmony/BlockBuildHammerIntercept.cs)
  - Harmony prefix intercepts `Block.TryPlaceBlock` and redirects placement to `BuildBrushSystem_Server.TryPlaceBrushBlock`.

## Current flow

### Tick/update → packet → preview update → render

1. Client tick updates brush resolved position/orientation based on player `CurrentBlockSelection`.
2. Client sends `Packet_SetBuildBrush` when brush position/orientation changes.
3. Server applies packet state to the per-player `BuildBrushInstance`.
4. Server syncs preview mini-dimension updates (currently owner-only sync).
5. Client renders the preview entity via `BuildBrushEntityRenderer`.

### Click → client prediction → server intercept → place

1. Client right-click triggers `BuildBrushSystem_Client.TryPlaceBlock`.
2. Client does local placement prediction (`DoPlaceBlock`) and sends the vanilla `BlockInteraction` packet.
3. Server intercepts `Block.TryPlaceBlock` and calls `BuildBrushSystem_Server.TryPlaceBrushBlock`.
4. Server performs the authoritative `DoPlaceBlock` with the brush-selected block/rotation.

## Phase 0 debug instrumentation

Debug config (modconfig/vanillabuildingexpanded.json):

- `BuildBrushDebugLogging`: Enables debug logs for rotate → send update → server receive → place.
- `BuildBrushDebugHud`: Shows a small status line using `TriggerIngameError` on rotate and send.

Notes:

- A real server ack/seq system is planned in Phase 2. For Phase 0, the client logs a local send counter only.
