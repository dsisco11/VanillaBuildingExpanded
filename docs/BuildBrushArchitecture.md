# BuildBrush architecture

BuildBrush resolves placement intent locally and applies native block rules on both sides. The server owns authoritative placement and brush-preview resources. The build-mode ruler is a separate, entirely client-owned world-session feature.

## Responsibilities and dependencies

| Component | Responsibility |
| --- | --- |
| [Mod system](../VanillaBuildingExpanded/VanillaBuildingExpandedModSystem.cs) | Registers brush update/acknowledgment packets, the saved hammer collectible and preview entity, and the native placement intercept. |
| [Client system](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Client.cs) | Handles game callbacks, hotkeys, ticks, outgoing updates and placement prediction. Supplies player inventory, identity, current selection and native validation when lazily constructing the local brush. Owns the client ruler controller, config, input and render binding for each local world session, and supplies the same controller to the lazy HUD. |
| [Server system](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Server.cs) | Owns the per-client controller registry, sequence decisions, acknowledgment transport and authoritative placement. Creates each session brush from the real player's game context. |
| [Brush instance](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs) | Holds resolved placement state, source itemstack/rotation and preview resources. Receives `IPlayerInventoryManager`, owner UID/name, `IBrushPlacementValidator` and a deferred-selection getter. Reads the active slot at use time. |
| [Client controller](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushControllerClient.cs) | Lazily obtains one brush from a supplied factory, handles brush operations and publishes its state. Accepts explicit selections for ordinary updates and post-placement refreshes. |
| [Server controller](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushControllerServer.cs) | Applies accepted state and subscribes to brush activation/dimension changes to manage preview lifecycle and dirty counters. Receives a constructed brush; disposal removes its subscriptions. |
| [State](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushState.cs) | Copies brush values without game side effects. |
| [Placement validation](../VanillaBuildingExpanded/src/BuildBrush/Placement/BrushPlacementValidation.cs) | Resolves requested snapping and, upon rejection, an unsnapped fallback. Clones raycast selection data, changes resolved position/offset, and invokes the rule dependency separately for each candidate, even if positions coincide. |
| [Validation contract](../VanillaBuildingExpanded/src/BuildBrush/Placement/IBrushPlacementValidator.cs) and [native adapter](../VanillaBuildingExpanded/src/BuildBrush/Placement/PlayerBrushPlacementValidator.cs) | The adapter retains the actual world/player and directly calls the selected block's virtual `CanPlaceBlock`, forwarding the failure-code reference. Native claims, collision and block behaviors retain their side effects. |
| [Preview dimension](../VanillaBuildingExpanded/src/BuildBrush/BrushDimension.cs) | Owns preview contents and native chunk synchronization. |
| [Preview entity](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntity.cs) and [renderer](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntityRenderer.cs) | Publish/render watched validity, ownership, bounds and dirty state with mini-dimension contents. Local entity association checks the owner UID; rendering uses the local camera's real player context. |
| [HUD binding](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeIndicatorBinding.cs) and [HUD](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs) | Observe enabled mode independently of brush eligibility and lazily own the non-focusing information panel. The [Ruler partial](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.Ruler.cs) observes shared ruler state and issues commands without owning coordinates, config or GPU resources. |
| [Ruler state](../VanillaBuildingExpanded/src/BuildBrush/RulerState.cs) and [controller](../VanillaBuildingExpanded/src/BuildBrush/RulerController.cs) | Own immutable copied endpoints with local Y/dimension, inclusive wide-integer bounds, visibility preference and direct endpoint transactions. Coordinate validation is shared with the world-facing adapters. |
| [Ruler persistence](../VanillaBuildingExpanded/src/BuildBrush/RulerPersistence.cs) | Uses native mod-config APIs for versioned per-savegame endpoints, validates without requiring loaded blocks, preserves unrelated entries and retries dirty edits. |
| [Ruler gizmo session](../VanillaBuildingExpanded/src/BuildBrush/RulerGizmoSession.cs) | Owns one manager/render/input composition, per-endpoint positioning controls, controller transaction binding and explicit missing-endpoint creation. [Terrain targeting](../VanillaBuildingExpanded/src/BuildBrush/RulerTerrainTarget.cs) raycasts the finite visual near/far segment without changing native selection or loading terrain. |
| [Ruler render binding](../VanillaBuildingExpanded/src/BuildBrush/RulerRenderBinding.cs) | Observes state and local dimension/session eligibility, balances opaque-stage registration, reports mismatch/render failure, and owns one renderer per session. |
| [Ruler renderer](../VanillaBuildingExpanded/src/BuildBrush/RulerRenderer.cs) and [render state](../VanillaBuildingExpanded/src/BuildBrush/RulerRenderState.cs) | Reuse one 12-edge mesh with the native wireframe shader, compute double-precision camera-relative drawing and restore the active shader and captured depth/line state without mutating blend state. |

Brush/controller decisions require no player proxy. Real `IPlayer`, `IClientPlayer` and `IServerPlayer` objects remain at game callbacks, native validation/placement, network replies, entity/camera access and dimension synchronization. `BuildBrushInstance.SyncDimensionToPlayers` and `BrushDimension.SyncToPlayers` retain their native recipient contracts.

## Mode and input

The local mode toggle uses the rebindable backtick key. Mode transitions refresh or clear local placement before immediately publishing the sequenced brush packet, including when there is no target. Sessions start disabled; death and respawn clear mode. The server refreshes the held source before applying incoming orientation.

Brush controls require enabled mode and an eligible held block. Rotation additionally requires orientation support; snapping and held face offset can be adjusted without a target. Suspended or disabled brushes leave ordinary input handling intact; ruler manipulation/creation is an independent exception while mode is enabled. Exit and suspension clear held face offset while preserving snapping preferences. Supported brush placement owns its native validation failures so a failed brush attempt cannot fall through to an unrelated vanilla placement.

Each successful local rotation, by hotkey or mouse wheel, plays the native GUI button hover sound (`menubutton`) non-positionally through the client world audio API. Rejected rotations and preview refreshes do not play it.

The rebindable rotation hotkeys (R forward, Shift + R backward by default) are registered ahead of ordinary hotkeys. Their handlers consume input only when the active brush successfully rotates; otherwise other actions can handle the key.

The saved hammer collectible retains its class and material variant codes, with no crafting recipe, creative inventory entries, or activation behavior.

Shift + mouse wheel provides an additional orientation control using either Shift key: wheel up cycles forward and wheel down cycles backward. The client refreshes the source before rotation, publishes successful changes through the existing brush update, and consumes only successful rotations during grabbed-mouse gameplay. Previously handled events and menu scrolling pass through.

## Build-mode HUD

[BuildModeIndicatorBinding](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeIndicatorBinding.cs) observes local controller mode transitions independently of preview eligibility. It lazily owns a [BuildModeHud](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs), the top-left information panel with the mod icon, snapping/rotation state and collapsible Room, Ruler and Controls sections. The HUD never takes keyboard focus or grabs the cursor. Only section toggles, actual ruler control hits, outstanding captures and hover exit receive pointer routing while the cursor is free; passive text and panel space pass through.

The Ruler section contains exactly two rows: Show ruler and localized inclusive X/Y/Z size text (dashes while incomplete). Native checkbox artwork is retained with activation deferred to valid captured release. The shared table input layer owns nested hit routing and cancellation; it does not route the entire panel through native dialog defaults. State changes queue HUD rebuilding until outside control dispatch. Collapse, hide, scale/locale changes, structural Room changes and disposal cancel HUD captures before composer retirement without changing world handles, endpoints or visibility preference. Unchecking Show ruler cancels world manipulation and hides both handles and box.

Death and join reset mode and hide the panel. World departure and system disposal detach the binding and release its dialog, scale watcher and composer textures. A new session starts hidden. An empty slot, unsupported item or absent target does not hide an enabled-mode panel or prevent ruler interaction.

## Client-only ruler

Reusable [gizmo instances and typed values](../VanillaBuildingExpanded/src/Gizmos/Gizmo.cs),
[shared context/ray/hit carriers](../VanillaBuildingExpanded/src/Gizmos/GizmoContext.cs)
and a [session manager](../VanillaBuildingExpanded/src/Gizmos/GizmoManager.cs) are available
implement the [in-world gizmo plan](InWorldGizmos.todo).
The ruler session owns their native input/render adapters independently of HUD composition.
The manager owns its remaining instances on disposal; removing an instance cancels and
detaches it without disposing it. Changed `Value` assignments notify synchronously using
default equality; immutable values are expected. Owners cancel an active edit before
assigning a genuinely different authoritative value. Cancellation restores through the
same notifying setter before the cancellation event; completion alone is not a value event.
Only successfully rendered, eligible instances in the matching shared frame can acquire
capture. Context supplies frame identity/dimension and a separate validated visual
ray using built-in `Vector256<double>` SIMD coordinates in `(X, Y, Z, 0)` lanes,
preserving double precision without a custom vector type.

[PositionGizmo](../VanillaBuildingExpanded/src/Gizmos/PositionGizmo.cs) supplies continuous
SIMD double positions, fixed-plane axis dragging, center-target resolution and a nullable
owner constraint hook before publishing values. Rejected motion retains the last valid
value and requests a reasoned notice. [Camera snapshots](../VanillaBuildingExpanded/src/Gizmos/GizmoCamera.cs)
copy perspective matrices, project/unproject the actual cursor or integer reticle center,
and retain local-Y dimension identity. [Shared geometry](../VanillaBuildingExpanded/src/Gizmos/PositionGizmoGeometry.cs)
drives both line meshes and hit shapes, with clipped parts omitted from both paths.
[The render adapter](../VanillaBuildingExpanded/src/Gizmos/GizmoRenderAdapter.cs)
publishes camera context at Opaque 0.49 and draws overlays at AfterBlit 0.49, sharing
arrow/circle/diamond meshes and the ruler's extracted native line-state restoration.
It restores shader/depth/write/line state without changing blending, invalidates hits
immediately on failure/retirement, and defers callback-requested renderer membership and
GPU teardown outside native live-list iteration. The session activates it for checked,
enabled/living contexts, including pending creation with zero instances.

The [native input adapter](../VanillaBuildingExpanded/src/Gizmos/GizmoInputAdapter.cs)
uses the renderer's perspective updates for drag previews and last drawn context for
button hits/final release. Its session eligibility callback covers owner mode/visibility;
native checks additionally guard focus, dialogs, player life, world/dimension, camera/pointer
mode and viewport. Free-cursor events retain their coordinates; reticle motion remains
unhandled for native aiming. Exact BuildModeHud/table control reservation excludes
passive text and stale hover-exit requests. Per-button ownership suppresses both native
action families, including late callbacks; hidden releases cancel and owned world-button
fields are explicitly cleared. Right/Escape cancel and session teardown detaches observers.
The session calls Refresh on eligibility changes even without rendering and the client
routes HandleWorldAction before brush placement. Tests exercise actual session, adapter,
manager and controller routing with native carriers and modeled downstream gates; they
are not live-game qualification.

Direct ruler edits have explicit begin/preview/complete/cancel boundaries. The controller
snapshots both endpoints, previews only the selected Start/End without swapping roles,
and restores the pair on cancellation. Differing authoritative restoration cancels first;
identical echoes leave the edit active. [Role conversion and constraints](../VanillaBuildingExpanded/src/BuildBrush/RulerGizmoCoordinates.cs)
use double SIMD coordinates with offsets Start=(0,0,0), End=(1,1,1); axis deltas round
AwayFromZero before being added to the initial anchor. Decode/validation applies to
the cell with its explicit dimension, including End anchors at local Y32768.
State remains the live presentation; CommittedEndpoints supplies the baseline during
an active edit. Persistence reads only committed endpoints, even on an explicit dirty
retry during a preview. Completion requests the durable save; a clean unchanged
completion causes no write. Cancellation precedes both client departure teardown and
standalone persistence disposal/dirty retries, so canceled previews cannot be saved.

On local join, the client system creates a fresh ruler controller, restores endpoints through persistence, then creates the box render binding. Visibility remains unchecked and mode disabled. The gizmo session is created lazily once enabled and alive, independent of brush placement eligibility. The 50-ms client tick refreshes context before brush early-outs; mode transitions refresh it immediately.

Both free cursor and mouse-look reticle support center and axis drags; reticle motion remains available for native aiming. The terrain resolver uses the captured visual near/far ray through the camera far plane without a player-reach cap. It retains native meta-block/interactable-entity filters and liquid policy, rejects foreground entities, sky, unloaded chunks and outside-clip hits, and copies the selected cell without brush offsets. It never refreshes native selection or loads terrain. Per-button ownership suppresses hold/repeat/release actions in both native action families, including after completion; Right/Escape cancel before placement. The installed 1.22.7 public event API lacks a focus callback, so the adapter uses the native client world's focus-listener list and explicitly detaches it.

Endpoints retain Start/End identity and explicit local Y/dimension. Each existing endpoint, even in a partial ruler, has a positioning control: Start circle at cell+(0,0,0), End diamond at cell+(1,1,1), both with RGB arrows. The binding uses notifying Value assignments and controller transactions, distinguishes owner synchronization from previews, and cancels before authoritative replacement. Axis edits snap whole-block deltas from the initial anchor; center edits follow terrain. Derived sizes use `max - min + 1` in wide integers; rendering spans minimum cell through maximum cell plus one.

Checking Show ruler with no endpoints offers one explicit world click that creates both on that cell and consumes the entire gesture. A partial ruler retains its existing handle; only a handle miss can create the missing endpoint. Invalid targets notify and remain pending. Right/Escape cancel creation until off/on visibility or explicit mode re-enable; contextual losses can reoffer only with eligible camera context and released mouse buttons. Retained other-dimension endpoints hide without automatic overwrite or relocation. The box still needs two valid same-dimension endpoints.

Persistence uses `LoadModConfig`/`StoreModConfig` in `vanillabuildingexpanded-rulers.json`, schema version 1, keyed by the exact `World.SavegameIdentifier`. It saves only endpoint commits, not visibility/context changes. Saves reread and merge the document, preserving unrelated worlds/metadata. Reads reject unsupported or unreadable documents without replacing them with defaults, and isolate invalid current-world records. A successful explicit edit can replace that invalid entry. The native writer truncates its destination, so a native-config staging write is promoted only after success, with targeted staging-file cleanup. Failed writes leave the edit dirty and notify; the next explicit edit or orderly disposal retries while the captured world identity is available. Missing identity is memory-only with feedback. Only endpoints persist; no ruler state is synchronized to the server.

The box render binding requires enabled/alive context, checked visibility, two valid same-dimension endpoints, a matching live player dimension, and the same available client world. It registers at most one renderer in `Opaque` and unregisters at that same stage when eligibility is lost. Endpoint edits and GUI rebuilds do not recreate or duplicate the renderer. Mode off/death hide and cancel while retaining endpoints and preference. Leaving a dimension hides the old ruler, retains size and notifies once per mismatch transition; returning restores eligible visibility.

The renderer uploads one unit cube lazily, using two `LineMeshUtil` loops plus four connectors for exactly 12 unique edges. Frames do not rebuild geometry, sample blocks or cull by endpoint distance/`RenderRange`. Stored bounds stay local-Y; the drawing adapter encodes the dimension only to match native `CameraPos`, subtracting camera coordinates in double precision before float matrix conversion. Drawing uses order 0.51 after opaque terrain, depth testing with depth writes disabled, and a 0.002-block outward expansion. Zero vertex flags avoid wind and extra Z-offset beyond the native shader's built-in 0.0014 clip-W bias. None of these adjustments change measured bounds.

The installed engine's OpenTK binding supplies depth-test/depth-mask/line-width snapshots because the public render API exposes setters only. Nested cleanup restores those values and the prior active shader even if drawing fails. Blend state, including indexed MRT settings, is untouched. Recognized native GL/setup failures are logged and surfaced once as an unavailable wireframe for the session, while sizes and controls remain usable. Unregistration is deferred out of the callback because native render-stage iteration indexes its live renderer list, including post-callback profiling.

World departure/system disposal cancel previews/creation, detach input and render observers, unregister/dispose renderers and shared meshes, retry dirty config before clearing controller ownership, and dispose the HUD binding. Late queued retirement checks disposal before touching the retired session. Repeated hide/show and teardown are idempotent.

Automated registration/matrix/cleanup tests and normal publish checks are distinct from supported-game qualification. The earlier ruler-only live-check waiver does not waive the replacement's required [Phase 7](InWorldGizmos.todo) qualification. Native input, restart/world isolation, terrain/transparent-material depth behavior, bias stability and preview/native-selection overlap remain unverified, not passed. The inspected runtime is 1.22.7; mocked services do not establish those live results.

## Selection and inventory refresh

Ordinary updates receive the current `BlockSelection?` explicitly. An absent selection clears resolved position and validity. The one-millisecond dirty callback unregisters itself before reading its supplied selection getter, so it observes the selection at callback execution time. Client and server composition bind that getter to the owning session player. Inventory refreshes and placement dirty marking reread the current active slot.

Placement stacks retain the selected orientation's block ID and deep-clone all held-stack attributes before applying brush rotation. This preserves attribute-based properties such as lantern material, lining and glass without modifying the inventory stack. Source refreshes detect attribute changes even when the block ID is unchanged, replacing stale attributes while retaining the selected orientation. They also track the selected hotbar slot number: switching slots recreates the preview block and entity even for identical stacks, while retaining brush orientation. Unchanged sources in the same slot do not recreate the preview.

Resolved selections preserve the source face, hit position and other raycast data. Placement validation and placement requests use the same cloning rule. Missing block/position paths retain their own early-return semantics; native rules run only when there is a placement candidate.

## Update and preview flow

Client initialization designates the received brush mini-dimension as the native blocks-preview dimension through `SetBlocksPreviewDimension`, before scanning its contents. This enables native preview-aware tick suppression; it does not suppress placement callbacks or every physics operation.

1. Client ticks update the brush from the current selection; state changes send a [brush update packet](../VanillaBuildingExpanded/src/Networking/SetBuildBrush.packet.cs) with an increasing sequence.
2. The server handler extracts client ID/name and calls `ApplyBrushUpdate`. An unknown client has no acknowledgment or tracking write. Absent sequence tracking defaults to zero; older/equal packets preserve state and select the existing sequence for acknowledgment.
3. An accepted update writes sequence tracking first, then calls the controller's `ApplyState`: state copy, source refresh, brush activation, snapping, orientation, eligible position (or exit cleanup) and last-applied sequence. Exceptions retain preceding writes and prevent a reply from that handler invocation.
4. The handler sends the [acknowledgment](../VanillaBuildingExpanded/src/Networking/BuildBrushAck.packet.cs) to the actual sender. Only an accepted update with an initialized preview dimension then synchronizes chunks to that same owner.
5. Preview mutations update the entity dirty counter and bounds. Position changes move the preview and copy the solid-layer blocks at all six face-adjacent world positions into the mini-dimension (including air to remove stale neighbors). Fluids are not copied. On client entity ticks, the preview entity checks both the dirty counter and the live block ID at the preview bounds' minimum corner, records changes and raises a local `PreviewChanged` event. The renderer subscribes to that event instead of polling during rendering, and unsubscribes on disposal. A received variant change requests rebuilding without waiting for watched-attribute replication, including when the counter arrived before the chunk. Counter changes still rebuild same-ID entity/rotation updates. Copied context stays outside the active preview bounds. Appearance remains subject to client entity ticks, server processing and chunk delivery; this is not client-side visual prediction.

Initial entity loading and preview-change notifications request mesh rebuilding. Requests coalesce into one pending main-thread task, which reads the latest preview bounds and dimension contents before tessellation; requests queued before it runs do not create redundant mesh uploads. Pending tasks do no work after renderer disposal.

The preview entity has no physics behaviors on either side. Brush position updates, not physical simulation, control its position, so water cannot push it or trigger physics-driven splash effects. Its inherited `EntityChunky` behavior also disables gravity and interaction. The existing hitbox remains configured independently of physics; preview rendering uses entity position and mesh data, not that hitbox.

Preview creation passes a cloned prepared placement stack to the mini-dimension's `SetBlock` and, if needed, fallback `SpawnBlockEntity`. Native creation invokes the block/entity placement hooks; the brush does not repeat the entity hook on newly spawned entities. Slot changes and source-attribute edits recreate the preview from native defaults, restore brush-controlled rotation and mark the preview dirty. Orientation notifications expose the updated placement stack before preview creation.

Before native placement hooks run, all six face-adjacent world positions are copied into the preview, including the literal block or air below it. No synthetic support is added, and packed dirt is not required. Without a resolved world position, no adjacent context is populated. New preview blocks and source/variant replacements also refresh adjacent context. Only the preview block is included in the published active bounds.

Neighbor copying uses ordinary `SetBlock(id, position)` calls for native placement and chunk synchronization. It does not use the faulty layer-specific overload or manipulate raw chunk data.

After native initialization, a serialize-modify-deserialize pass supplements properties not initialized by the hooks, including lantern material, lining and glass. It copies only top-level scalar attributes already exposed by the entity's serialized state with the same attribute type. Coordinates, block identity and recognized rotation fields are protected; nested trees, arrays and item stacks are left to native hooks. Each changed source receives a fresh entity, so removed properties naturally use native defaults without tracking or removing earlier overrides.

Both stages run on initial preview creation and changed sources. Unchanged sources and mesh-angle-only brush rotation reuse the preview entity. There is no lantern-specific branch or `DoPlaceBlock` call. Matching serialized names/types remains a compatibility heuristic, not a universal conversion contract for differently named, nested or derived properties.

`ApplyBrushUpdate` stays on the server system. Its nullable result identifies unknown clients or returns the controller, acknowledgment sequence and whether state was applied. It performs no transport.

## Placement flow

1. An enabled brush with a supported held block and target handles client right-click by building a shared placement request containing the resolved selection, placement block, itemstack and rotation.
2. Client prechecks and native `CanPlaceBlock` determine whether prediction can proceed. Successful prediction calls `DoPlaceBlock`, enforces the brush's block-entity rotation, sends the vanilla block-interaction packet and refreshes inventory/selection.
3. The [Harmony intercept](../VanillaBuildingExpanded/src/Harmony/BlockBuildBrushIntercept.cs) redirects eligible server-side `Block.TryPlaceBlock` calls to `TryPlaceBrushBlock`.
4. The server builds the placement request, invokes native validation with the actual player, performs authoritative placement, reapplies block-entity rotation and notifies block/neighbour updates before refreshing the source slot.

## Diagnostics

Settings are stored in `modconfig/vanillabuildingexpanded.json`:

- `BuildBrushDebugLogging` logs update sequences, acknowledgments and placement state.
- `BuildBrushDebugHud` displays local update/acknowledgment status through in-game notices.
- `BuildBrushGatePlacementOnAck` optionally waits for acknowledgment of the current local sequence before placement. It defaults to `false`.

Headless tests exercise brush computations, controller behavior and server packet decisions using inventory/validation contracts and engine-service doubles. Native claims/collision behavior, appearance, real acknowledgment delivery and owner-only chunk delivery require in-game verification.
