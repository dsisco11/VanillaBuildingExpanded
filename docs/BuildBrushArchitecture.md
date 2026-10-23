# BuildBrush architecture

BuildBrush resolves placement intent locally and applies native block rules on both sides. The server owns authoritative placement and preview resources.

## Responsibilities and dependencies

| Component | Responsibility |
| --- | --- |
| [Mod system](../VanillaBuildingExpanded/VanillaBuildingExpandedModSystem.cs) | Registers brush update/acknowledgment packets, the hammer and preview entity, and the native placement intercept. |
| [Client system](../VanillaBuildingExpanded/src/BuildHammer/BuildBrushSystem_Client.cs) | Handles game callbacks, hotkeys, ticks, outgoing updates and placement prediction. Supplies player inventory, identity, current selection and native validation when lazily constructing the local brush. |
| [Server system](../VanillaBuildingExpanded/src/BuildHammer/BuildBrushSystem_Server.cs) | Owns the per-client controller registry, sequence decisions, acknowledgment transport and authoritative placement. Creates each session brush from the real player's game context. |
| [Brush instance](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs) | Holds resolved placement state, source itemstack/rotation and preview resources. Receives `IPlayerInventoryManager`, owner UID/name, `IBrushPlacementValidator` and a deferred-selection getter. Reads the active slot at use time. |
| [Client controller](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushControllerClient.cs) | Lazily obtains one brush from a supplied factory, handles brush operations and publishes its state. Accepts explicit selections for ordinary updates and post-placement refreshes. |
| [Server controller](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushControllerServer.cs) | Applies accepted state and subscribes to brush activation/dimension changes to manage preview lifecycle and dirty counters. Receives a constructed brush; disposal removes its subscriptions. |
| [State](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushState.cs) | Copies brush values without game side effects. |
| [Placement validation](../VanillaBuildingExpanded/src/BuildBrush/Placement/BrushPlacementValidation.cs) | Resolves requested snapping and, upon rejection, an unsnapped fallback. Clones raycast selection data, changes resolved position/offset, and invokes the rule dependency separately for each candidate, even if positions coincide. |
| [Validation contract](../VanillaBuildingExpanded/src/BuildBrush/Placement/IBrushPlacementValidator.cs) and [native adapter](../VanillaBuildingExpanded/src/BuildHammer/Placement/PlayerBrushPlacementValidator.cs) | The adapter retains the actual world/player and directly calls the selected block's virtual `CanPlaceBlock`, forwarding the failure-code reference. Native claims, collision and block behaviors retain their side effects. |
| [Preview dimension](../VanillaBuildingExpanded/src/BuildBrush/BrushDimension.cs) | Owns preview contents and native chunk synchronization. |
| [Preview entity](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntity.cs) and [renderer](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntityRenderer.cs) | Publish/render watched validity, ownership, bounds and dirty state with mini-dimension contents. Local entity association checks the owner UID; rendering uses the local camera's real player context. |
| [Player helpers](../VanillaBuildingExpanded/src/Extensions/Player.cs) and [slot detection](../VanillaBuildingExpanded/src/Extensions/BuildHammerSlotExtensions.cs) | `IsHoldingBuildHammer` and player-facing hammer lookup resolve `InventoryManager?.OffhandHotbarSlot ?? Entity.LeftHandItemSlot` at the game boundary. Collectible detection accepts the effective slot alone. |

Brush/controller decisions require no player proxy. Real `IPlayer`, `IClientPlayer` and `IServerPlayer` objects remain at game callbacks, equipment extraction, native validation/placement, network replies, entity/camera access and dimension synchronization. `BuildBrushInstance.SyncDimensionToPlayers` and `BrushDimension.SyncToPlayers` retain their native recipient contracts.

## Selection and inventory refresh

Ordinary updates receive the current `BlockSelection?` explicitly. An absent selection clears resolved position and validity. The one-millisecond dirty callback unregisters itself before reading its supplied selection getter, so it observes the selection at callback execution time. Client and server composition bind that getter to the owning session player. Inventory refreshes and placement dirty marking reread the current active slot.

Resolved selections preserve the source face, hit position and other raycast data. Placement validation and placement requests use the same cloning rule. Missing block/position paths retain their own early-return semantics; native rules run only when there is a placement candidate.

## Update and preview flow

1. Client ticks update the brush from the current selection; state changes send a [brush update packet](../VanillaBuildingExpanded/src/Networking/SetBuildBrush.packet.cs) with an increasing sequence.
2. The server handler extracts client ID/name and calls `ApplyBrushUpdate`. An unknown client has no acknowledgment or tracking write. Absent sequence tracking defaults to zero; older/equal packets preserve state and select the existing sequence for acknowledgment.
3. An accepted update writes sequence tracking first, then calls the controller's `ApplyState`: state copy, brush activation, snapping, orientation, position and last-applied sequence. Exceptions retain preceding writes and prevent a reply from that handler invocation.
4. The handler sends the [acknowledgment](../VanillaBuildingExpanded/src/Networking/BuildBrushAck.packet.cs) to the actual sender. Only an accepted update with an initialized preview dimension then synchronizes chunks to that same owner.
5. Preview mutations update the entity dirty counter and bounds. Position changes move the preview; the renderer uses dirty state to rebuild its mesh when required.

`ApplyBrushUpdate` stays on the server system. Its nullable result identifies unknown clients or returns the controller, acknowledgment sequence and whether state was applied. It performs no transport.

## Placement flow

1. Client right-click builds a shared placement request containing the resolved selection, placement block, itemstack and rotation.
2. Client prechecks and native `CanPlaceBlock` determine whether prediction can proceed. Successful prediction calls `DoPlaceBlock`, enforces the brush's block-entity rotation, sends the vanilla block-interaction packet and refreshes inventory/selection.
3. The [Harmony intercept](../VanillaBuildingExpanded/src/Harmony/BlockBuildHammerIntercept.cs) redirects eligible server-side `Block.TryPlaceBlock` calls to `TryPlaceBrushBlock`.
4. The server builds the placement request, invokes native validation with the actual player, performs authoritative placement, reapplies block-entity rotation and notifies block/neighbour updates before refreshing the source slot.

## Diagnostics

Settings are stored in `modconfig/vanillabuildingexpanded.json`:

- `BuildBrushDebugLogging` logs update sequences, acknowledgments and placement state.
- `BuildBrushDebugHud` displays local update/acknowledgment status through in-game notices.
- `BuildBrushGatePlacementOnAck` optionally waits for acknowledgment of the current local sequence before placement. It defaults to `false`.

Headless tests exercise brush computations, controller behavior and server packet decisions using inventory/validation contracts and engine-service doubles. Native claims/collision behavior, appearance, real acknowledgment delivery and owner-only chunk delivery require in-game verification.
