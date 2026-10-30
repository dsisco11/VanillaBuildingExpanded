# BuildBrush architecture

BuildBrush resolves placement intent locally and applies native block rules on both sides. The server owns authoritative placement and brush-preview resources. The build-mode work area is a separate, entirely client-owned world-session feature.

## Responsibilities and dependencies

| Component | Responsibility |
| --- | --- |
| [Mod system](../VanillaBuildingExpanded/VanillaBuildingExpandedModSystem.cs) | Registers brush update/acknowledgment packets, the saved hammer collectible and preview entity, and the native placement intercept. |
| [Client system](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Client.cs) | Handles game callbacks, hotkeys, ticks, outgoing updates and placement prediction. Supplies player inventory, identity, current selection and native validation when lazily constructing the local brush. Owns the client work area controller, config, input and render binding for each local world session, and supplies the same controller to the lazy HUD. |
| [Server system](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Server.cs) | Owns the per-client controller registry, sequence decisions, acknowledgment transport and authoritative placement. Creates each session brush from the real player's game context. |
| [Brush instance](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs) | Holds resolved placement state, source itemstack/rotation and preview resources. Receives `IPlayerInventoryManager`, owner UID/name, `IBrushPlacementValidator` and a deferred-selection getter. Reads the active slot at use time. |
| [Client controller](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushControllerClient.cs) | Lazily obtains one brush from a supplied factory, handles brush operations and publishes its state. Accepts explicit selections for ordinary updates and post-placement refreshes. |
| [Server controller](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushControllerServer.cs) | Applies accepted state and subscribes to brush activation/dimension changes to manage preview lifecycle and dirty counters. Receives a constructed brush; disposal removes its subscriptions. |
| [State](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushState.cs) | Copies brush values without game side effects. |
| [Placement validation](../VanillaBuildingExpanded/src/BuildBrush/Placement/BrushPlacementValidation.cs) | Resolves one requested candidate and validates it once, retaining rejection without an unrestricted fallback. Reports requested, resolved and accepted directions separately from candidate position. Clones raycast selection data before invoking the rule dependency. |
| [Placement result](../VanillaBuildingExpanded/src/BuildBrush/Placement/BrushPlacementResult.cs) and [snapping presentation](../VanillaBuildingExpanded/src/BuildBrush/BrushSnappingPresentation.cs) | Retain local outcome meaning and a copied original-target snapshot. The brush instance publishes the snapshot after resolution; the client controller exposes a guarded read without validation, drawing, or transport changes. |
| [Validation contract](../VanillaBuildingExpanded/src/BuildBrush/Placement/IBrushPlacementValidator.cs) and [native adapter](../VanillaBuildingExpanded/src/BuildBrush/Placement/PlayerBrushPlacementValidator.cs) | The adapter retains the actual world/player and directly calls the selected block's virtual `CanPlaceBlock`, forwarding the failure-code reference. Native claims, collision and block behaviors retain their side effects. |
| [Preview dimension](../VanillaBuildingExpanded/src/BuildBrush/BrushDimension.cs) | Owns preview contents and native chunk synchronization. |
| [Preview entity](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntity.cs) and [renderer](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushEntityRenderer.cs) | Publish/render watched validity, ownership, bounds and dirty state with mini-dimension contents. Local entity association checks the owner UID; rendering uses the local camera's real player context. |
| [HUD binding](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeIndicatorBinding.cs) and [HUD](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs) | Observe enabled mode independently of brush eligibility and lazily own the information panel, with keyboard focus only during dimension editing. The [Work area partial](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.WorkArea.cs) observes shared work area state and issues commands without owning coordinates, config or GPU resources. |
| [Work area state](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaState.cs) and [controller](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaController.cs) | Own the named-area collection, stable IDs, selected-area projection, per-area session visibility, copied local-Y/dimension endpoints, inclusive bounds and direct endpoint transactions. Coordinate validation is shared with the world-facing adapters. |
| [Work area persistence](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaPersistence.cs) | Uses native mod-config APIs for schema-2 per-savegame named-area collections, validates without requiring loaded blocks, preserves unrelated entries and retries dirty edits. |
| [Work area gizmo session](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaGizmoSession.cs) | Owns one manager/render/input composition, per-endpoint positioning controls and a whole-selection midpoint control, controller transaction binding and explicit missing-endpoint creation. Creation-only [terrain targeting](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaTerrainTarget.cs) raycasts the finite visual near/far segment without changing native selection or loading terrain; handle movement never invokes it. |
| [Work area render binding](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaRenderBinding.cs) | Observes state and local dimension/session eligibility, balances opaque-stage registration, reports mismatch/render failure, and owns one renderer per session. |
| [Work area renderer](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaRenderer.cs) and [render state](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaRenderState.cs) | Draw all eligible visible boxes using one shared 12-edge mesh and the native wireframe shader, compute double-precision camera-relative drawing and restore the active shader and captured depth/line state without mutating blend state. |

Brush/controller decisions require no player proxy. Real `IPlayer`, `IClientPlayer` and `IServerPlayer` objects remain at game callbacks, native validation/placement, network replies, entity/camera access and dimension synchronization. `BuildBrushInstance.SyncDimensionToPlayers` and `BrushDimension.SyncToPlayers` retain their native recipient contracts.

## Mode and input

The local mode toggle uses the rebindable backtick key. Mode transitions refresh or clear local placement before immediately publishing the sequenced brush packet, including when there is no target. Sessions start disabled; death and respawn clear mode. The server refreshes the held source before applying incoming orientation.

Brush controls require enabled mode and an eligible held block. Rotation additionally requires orientation support; snapping and held face offset can be adjusted without a target. Suspended or disabled brushes leave ordinary input handling intact; work area manipulation/creation is an independent exception while mode is enabled. Exit and suspension clear held face offset while preserving snapping preferences. Supported brush placement owns its native validation failures so a failed brush attempt cannot fall through to an unrelated vanilla placement.

Each successful local rotation, by hotkey or mouse wheel, plays the native GUI button hover sound (`menubutton`) non-positionally through the client world audio API. Rejected rotations and preview refreshes do not play it.

The rebindable rotation hotkeys (R forward, Shift + R backward by default) are registered ahead of ordinary hotkeys. Their handlers consume input only when the active brush successfully rotates; otherwise other actions can handle the key.

The saved hammer collectible retains its class and material variant codes, with no crafting recipe, creative inventory entries, or activation behavior.

Shift + mouse wheel provides an additional orientation control using either Shift key: wheel up cycles forward and wheel down cycles backward. The client refreshes the source before rotation, publishes successful changes through the existing brush update, and consumes only successful rotations during grabbed-mouse gameplay. Previously handled events and menu scrolling pass through.

## Build-mode HUD

[BuildModeIndicatorBinding](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeIndicatorBinding.cs) observes local controller mode transitions independently of preview eligibility. It lazily owns a [BuildModeHud](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs), the top-left information panel with the mod icon, snapping/rotation state and collapsible Room, Work area and Controls sections. The HUD takes native focus and captures keyboard/gameplay input only while a work area size field is being edited; otherwise it remains non-focusing. Only section toggles, actual work area control hits, outstanding captures and hover exit receive pointer routing while the cursor is free; passive text and panel space pass through unless a field edit is ending.

The Work area section contains five rows: a compact area list, management buttons,
visibility, a shared resize-mode toggle, and localized inclusive X/Y/Z numeric inputs
(disabled dashes while incomplete). Visibility uses a native `GuiElementSwitch`, toggling immediately on mouse-down;
it has no pending press state to cancel. The shared table input layer owns nested hit routing and cancellation; it does not route the entire panel through native dialog defaults. State changes queue HUD rebuilding until outside control dispatch. Collapse, hide, scale/locale changes, structural Room changes and disposal cancel HUD captures before composer retirement without changing world handles, endpoints or visibility preference. Unchecking Show work area cancels world manipulation and hides both handles and box.

Work-area command and resize-mode buttons use native `GuiElementTextButton`
instances with pointer-only table registration. Table cancellation sends an
outside release to clear native pending presses without invoking their actions.

[Dimension editing](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.WorkAreaSize.cs)
owns native text focus independently of the table's per-release pointer cleanup.
Enter/blur commits one axis; Tab commits and traverses, and Escape cancels the draft.
On-screen spinner arrows and keyboard Up/Down apply each step immediately without
releasing focus; mouse-wheel steps remain draft edits until Enter/blur. An
outside blur click consumes its complete gesture rather than reaching world
interaction. Context retirement discards drafts; clicking another HUD control
first commits on blur. Successful size edits refresh the numeric values in place
so clicking or tabbing between fields does not lose the incoming focus to a HUD
rebuild. External state changes cancel the draft before rebuilding.

The controller resizes committed bounds atomically using inclusive Int64 sizes.
The shared policy defaults to Expansion, anchoring the minimum corner and moving
the maximum corner. Centered preserves the center, with half-block parity ties
rounding toward the positive axis, including at negative coordinates. The HUD
toggle changes policy without moving endpoints; it applies to numeric edits only,
not gizmo dragging. Persistence stores the shared `resizeMode` at the collection
document root, across worlds and named areas. Older schema-2 documents without
the setting default to Expansion without being rewritten on load. Both candidate corners must pass coordinate,
dimension and world validation before any state mutation or the single endpoint
commit notification. No-op edits do not save; invalid sizes produce localized
feedback. Metadata, other areas, selection and visibility are unchanged.

Death and join reset mode and hide the panel. World departure and system disposal detach the binding and release its dialog, scale watcher and composer textures. A new session starts hidden. An empty slot, unsupported item or absent target does not hide an enabled-mode panel or prevent work area interaction.

## Client-only work area

The full terminology rename includes component/file names, localization and
registration identifiers. Persistence starts fresh in
`vanillabuildingexpanded-workareas.json`; the former
`vanillabuildingexpanded-rulers.json` is intentionally left untouched, with no
fallback reads or migration.

Each world owns a collection of named areas. Names are trimmed, control-free,
at most 64 UTF-16 code units, and unique using ordinal case-insensitive comparison.
GUIDs, rather than names or list indices, identify areas across renames and saves.
A fresh controller offers one empty, hidden area with a localized default name.
Deleting the last area leaves no active selection and disables world creation
until an area is created through New. Selection and independent visibility remain
session-only; restoration selects the first saved area and hides every area.

The reusable [GuiElementListBox<T>](../VanillaBuildingExpanded/src/UI/GuiElementListBox.cs)
owns list rendering, clipping, selection gestures, native scrollbar integration,
scroll position and texture lifetime. It snapshots typed items and accepts a label
projection, item-selection callback, caller-provided bounds, visible-row limit,
row height and empty-state text. It has no work-area, coordinate, persistence or
feature-localization dependencies. Selection is an index into that snapshot.

The HUD exposes a [compact named-area listbox](../VanillaBuildingExpanded/src/BuildBrush/Hud/WorkAreaListBox.cs),
which derives from that component and only maps area names, stable GUID selection,
the localized empty-state message and the existing 280-unit width.
The HUD also exposes
New/Rename/Delete controls, the selected area's visibility checkbox and editable
size fields. The list shows at most three 22-unit rows, highlights the selection,
and uses wheel scrolling or a native compact scrollbar for additional entries.
Names are drawn as plain text and clipped inside the fixed-width viewport.
Selection requires press and release on the same row; scrolling during a row press
cancels its pending selection. Only the list footprint and outstanding captures
reserve pointer input, with no popup or outside-dismissal gesture. Recomposition
preserves the scroll position, while selection changes reveal the selected row.
Hiding/closing the HUD or grabbing the mouse cancels captures so a lost release
cannot block later gizmo input. Dimension-blur gestures alone retain their release
reservation across recomposition.
[Naming dialogs](../VanillaBuildingExpanded/src/BuildBrush/Hud/WorkAreaNameDialog.cs)
take native focus and capture keyboard input; validation errors keep the dialog
open. Escape/Cancel do not mutate the collection. Deletion requires confirmation.
Hiding/disposal, death, locale changes and scale changes close management dialogs.

Only the selected area's existing endpoints and complete-bounds midpoint own
gizmos. Switching selection cancels its transaction before projecting the next
area and clears pending creation/cancellation state. Other visible complete areas
keep their boxes, filtered individually by dimension and coordinate validity,
even when the selected area's visibility is unchecked.

Schema 2 stores each world as an `areas` array of `id`, `name`, nullable `start`
and nullable `end`. Validation rejects duplicate IDs/names and invalid records
atomically for that world; unrelated worlds and world-level metadata remain
untouched by repairs. The prior schema-1 single-area config is intentionally not
migrated: it is ignored and replaced only by a subsequent explicit save.
Unsupported other versions and unreadable files remain protected from overwrite.
Metadata commits use `CommittedAreas`, substituting the original pair for the
active preview, so a rename cannot accidentally save a drag preview. Dirty
collection saves retain the existing staged-file promotion and retry behavior.

Analytic movement uses a camera-facing plane captured at drag start, analytic arrows
and whole-block work area snapping. Terrain targeting serves only explicit
first-use/missing-endpoint creation.

Reusable [gizmo instances and typed values](../VanillaBuildingExpanded/src/Gizmos/Gizmo.cs),
[shared context/ray/hit carriers](../VanillaBuildingExpanded/src/Gizmos/GizmoContext.cs)
and a [session manager](../VanillaBuildingExpanded/src/Gizmos/GizmoManager.cs)
provide the shared ownership and lifecycle infrastructure.
The work area session owns their native input/render adapters independently of HUD composition.
The manager owns its remaining instances on disposal; removing an instance cancels and
detaches it without disposing it. Changed `Value` assignments notify synchronously using
default equality; immutable values are expected. Owners cancel an active edit before
assigning a genuinely different authoritative value. Cancellation restores through the
same notifying setter before the cancellation event; completion alone is not a value event.
Only successfully rendered, eligible instances in the matching shared frame can acquire
capture. Context supplies frame identity/dimension and a separate validated visual
ray using built-in `Vector256<double>` SIMD coordinates in `(X, Y, Z, 0)` lanes,
preserving double precision without a custom vector type.

[ComposedPositionGizmo](../VanillaBuildingExpanded/src/Gizmos/ComposedPositionGizmo.cs) supplies continuous
SIMD double positions, fixed-plane axis and camera-plane center dragging, and a nullable
owner constraint hook before publishing values. Rejected motion retains the last valid
value and requests one reasoned notice per uninterrupted invalid interval until recovery.
Center capture fixes the original anchor, camera view normal and initial ray/plane
intersection. Updates add the difference between current and initial intersections to
that anchor, preserving off-center grab offsets even during camera motion and owner
echoes. This moves within the initial view plane; arrows provide independent depth
adjustment. No movement path queries terrain, entities, native selection or destination
callbacks. Starts without a usable camera/forward intersection are rejected; parallel
or behind-ray updates retain the last valid value. Both plane calculations reject
absolute ray/normal dot products <=0.01, intersection distances beyond
10000*max(1,initial eye-anchor distance), and nonfinite results. Accepted center
steps are limited to max(64,8*initial distance), measured from the last accepted
unsnapped intersection; axes retain the same scalar step limit.
[Camera snapshots](../VanillaBuildingExpanded/src/Gizmos/GizmoCamera.cs)
copy perspective matrices, project/unproject the actual cursor or integer reticle center,
derive the camera view direction from the inverse view (not an off-center cursor ray),
and retain local-Y dimension identity. [Shared handle geometry](../VanillaBuildingExpanded/src/Gizmos/GizmoHandleGeometry.cs)
drives both line meshes and hit shapes from persistent controls, with clipped parts omitted from both paths.
[The render adapter](../VanillaBuildingExpanded/src/Gizmos/GizmoRenderAdapter.cs)
publishes camera context at Opaque 0.49 and draws overlays at AfterBlit 0.49, sharing
arrow/circle/diamond meshes. A session-owned unlit memory shader keeps handle colors
independent of scene lighting: every center/axis handle uses 80% opacity normally,
100% while hovered or captured, and thicker lines for hover/capture. It is compiled
lazily, recreated after native shader reload and disposed with the session's GPU resources.
The overlay state guard enables standard alpha blending only on color attachment zero,
restores its indexed enable/factors/equations, and leaves other MRT attachments unchanged.
It also restores shader/depth/write/line state using the work area's extracted line-state guard, invalidates hits
immediately on failure/retirement, and defers callback-requested renderer membership and
GPU teardown outside native live-list iteration. The session activates it for checked,
enabled/living contexts, including pending creation with zero instances.

[Per-handle labels](../VanillaBuildingExpanded/src/Gizmos/GizmoLabel.cs) attach through
`GizmoHandle.Label` and detach by assigning null. A label contains
literal single-line text and an `(OffsetX, OffsetY)` in framebuffer pixels, positive
right/down, locating its top-left corner relative to the projected arrow tip or
center marker. Offsets do not scale with GUI scale or world distance; text size
follows GUI scale. `Always` draws with the eligible handle, `Hover` requires that
exact handle to be hovered, `Selected` requires that exact handle to be captured,
and `Predicate` evaluates a caller-supplied function each drawn frame with the
gizmo, handle, camera context and hover/selection state. Hover and selection are
distinct: capturing clears hover. Predicates should be side-effect-free, and do
not override hidden, disabled, disposed, cross-dimension or clipped handles.

The [label overlay](../VanillaBuildingExpanded/src/Gizmos/GizmoRenderAdapter.Labels.cs)
uses the same double-precision projection snapshot and AfterBlit state guard as
the handle lines. A lazily created textured quad shader maps pixels directly to
clip space without relying on ambient GUI matrices. Label text is rasterized
with grayscale font antialiasing, retaining Cairo's premultiplied alpha and using
`One, OneMinusSrcAlpha` blending without pixel conversion. This preserves
translucent backgrounds without RGB fringes at glyph edges. It is rasterized
only on first visibility or text/GUI-scale/resolved-style changes; changing visibility,
moving, or replacing offsets reuses textures. Caches use owner and actual handle
identity, not numeric IDs. Removing/replacing a label, detaching/disposing its handle
or removing its gizmo retires stale textures on the next active overlay frame. Session disposal
releases all remaining textures, the shared quad and shader. Labels do not
participate in hit testing or input capture.

Work-area handles use hover labels for X/Y/Z and endpoint Center markers.
Axis arrows use a nominal size of 32 (half their previous length), with the same
geometry used for rendering and picking; center-marker sizes are unchanged.
The whole-area midpoint's center label is always visible with its eligible handle
and uses the selected work area's name, including renames; all names are rendered
as literal text rather than markup.

### Reusable interaction contracts

[Interaction contracts](../VanillaBuildingExpanded/src/Gizmos/GizmoInteractionContracts.cs)
define separate APIs for gesture routing, hit targets, logical frames, measurement
sessions, typed parameters, edited values and edit-state coordination. They do not
register native handlers or own global capture/render resources.

`GizmoValueSource<TValue>` borrows a gizmo and forwards access to its notifying
`Value` setter without subscriptions or copied storage. `IGizmoHandle` exposes
identity/ownership. [GizmoHandleHit](../VanillaBuildingExpanded/src/Gizmos/GizmoHandleHit.cs)
validates attached handle references and positive finite distances; local ordering
is priority, distance, then ID. [GizmoFrame](../VanillaBuildingExpanded/src/Gizmos/GizmoFrame.cs)
validates finite coordinates, normalized directions, orthogonal paired tangents and
context dimensions. Frame normalization uses a `1e-12` floor and orthogonality a
`1e-8` dot tolerance, independently of positioning solver guards.

`Gizmo` exposes a read-only `Handles` map with controlled attachment/detachment.
[GizmoHandle](../VanillaBuildingExpanded/src/Gizmos/GizmoHandle.cs) owns identity,
local eligibility, labels and borrowed interaction configuration; removal cancels
capture and detaches without disposal, while owner disposal releases remaining handles.
Actual hover/capture references accompany diagnostic IDs. Picking and interaction
require an actual eligible member even when the owner has no handles; there is no
ID-only interaction path.
[Appearance properties](../VanillaBuildingExpanded/src/Gizmos/AppearenceProperties.cs)
resolve each nullable state override independently against a validated Default baseline.
Selected takes precedence over Hover but inherits missing fields directly from Default,
never through Hover. Default cannot be removed, and every configured state must resolve
all shape-required fields and, when a label is attached, all label-style fields before
a configuration mutation is published.
Colors are nullable `System.Numerics.Vector4` RGBA values with whole-color fallback.
Resolved snapshots contain complete handle values and optional complete label styling.
Invalidation distinguishes spatial geometry, visual changes and label raster/placement.
Production positioning uses these collections for drawing, picking, hover and capture.

[Frame sources](../VanillaBuildingExpanded/src/Gizmos/GizmoFrameSources.cs) adapt
live position/axis readers, camera-facing planes or immutable captured frames.
They validate dimension and basis without deriving movement axes from meshes.
Measurement uses `TryGetFrame` to reject temporarily unavailable context, such as a
missing camera, with the normal drag notice rather than a fabricated frame. Invalid
configuration still throws through the existing logged failure path.
[Handle geometry](../VanillaBuildingExpanded/src/Gizmos/GizmoHandleGeometry.cs)
publishes immutable draw snapshots containing complete appearance and shared
double-precision spatial data. Its single-handle cache keys owner, context, logical
frame, shape and size; color/style-only updates reuse spatial data. Clipped shapes
publish no draw/pick snapshot. Disabled but visible handles retain drawing geometry.
[Hit targets](../VanillaBuildingExpanded/src/Gizmos/GizmoHitTargets.cs) provide
geometry-backed picking from the last published drawing context, explicit custom
padding or delegate picking, and immutable owner-local aggregates. Custom pickers may
deliberately differ from visible geometry without changing the configured measurement
target. Shared validation
rejects malformed or foreign identities, filters eligibility, deduplicates hits and
orders them by priority, distance and ID. Hover feedback uses the existing owner
reference; capture feedback acknowledges lifecycle state without acquiring capture.

[Measurement targets](../VanillaBuildingExpanded/src/Gizmos/GizmoDragTargets.cs)
freeze logical frames and anchor-relative grab offsets into capture-local axis or
plane sessions. Sessions initialize typed parameter bookkeeping before the edit
scope, retain the last accepted unsnapped measurement, end parameters before
committing, and cancel on forced termination or cleanup failure. Parameter factories
receive the exact frozen frame, so planar interpretation cannot reread a moved camera.
[Position parameter sources](../VanillaBuildingExpanded/src/Gizmos/GizmoPositionParameterSources.cs)
map scalar or planar deltas through existing constraint delegates and notifying value
sources; ending parameter modification does not commit or restore a transaction.
Axis bindings enforce movement along their frozen frame, including keeping forbidden
components unchanged; the work-area constraint rounds world-coordinate deltas and
validates cells without interpreting handle IDs. The conventional Center/X/Y/Z IDs in
the positioning composition are optional identity conveniences, not operation selectors.
[Edit-state targets](../VanillaBuildingExpanded/src/Gizmos/GizmoEditStateTarget.cs)
bind existing owner begin/complete/cancel callbacks to the gizmo's single baseline.
Owner lifecycle and composed sessions share idempotent restoration through the normal
setter, before owner cancellation and public canceled notifications. Transaction
callbacks are not also bound to lifecycle events. Work-area endpoint and midpoint
compositions use these adapters with the existing controller's edit scope.

[HandleGizmo](../VanillaBuildingExpanded/src/Gizmos/HandleGizmo.cs) composes those
contracts through the same manager and native input adapter. Its
[drag input behavior](../VanillaBuildingExpanded/src/Gizmos/GizmoDragInputBehavior.cs)
owns only the capture-local session, handling preparation, rejection, termination
and disposal without solver math or input subscriptions. Handle mutations cancel
through the owner's lifecycle, including during initialization. External authoritative
values cancel before assignment; parameter writes retain the notifying setter without
being mistaken for external replacements.
Initialization publishes no successful start until measurement, parameter modification
and the owner edit scope are ready. Failed initialization unwinds partial scopes and
disposes capture-local resources without completion. Termination clears active
references before callbacks, ends parameter bookkeeping, restores the baseline on
cancellation and then notifies the owner and public lifecycle observers. Reentrant
begins are rejected while initialization or termination is active. Shared sources
remain borrowed; capture-local resources are released exactly once, including failures.
[ComposedPositionGizmo](../VanillaBuildingExpanded/src/Gizmos/ComposedPositionGizmo.cs)
explicitly configures a camera-plane center and three world-axis controls with
replaceable frames, pickers and typed parameter/edit bindings. IDs identify controls
only. Work-area gameplay uses this composition for both endpoint markers and the
whole-area midpoint. Normalized role anchors synchronize through the same notifying
value path during previews without canceling or rebasing the captured measurement.

### Native input and work-area transactions

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

Direct work area edits have explicit begin/preview/complete/cancel boundaries. The controller
snapshots both endpoints, calculates previews against the original opposite endpoint,
then normalizes Start/End to the per-axis minimum/maximum cells even during crossing,
and restores the pair on cancellation. Differing authoritative restoration cancels first;
identical echoes leave the edit active. [Role conversion and constraints](../VanillaBuildingExpanded/src/BuildBrush/WorkAreaGizmoCoordinates.cs)
use double SIMD coordinates with offsets Start=(0,0,0), End=(1,1,1). Center deltas
round all three world components AwayFromZero from the initial anchor; axes round
only the selected component and preserve the other two. Neither snapped previews nor
normalized role anchors replace the unsnapped interaction baseline. World-grid rounding
can put the published center slightly off the continuous plane. Decode/validation applies to
the cell with its explicit dimension, including End anchors at local Y32768.
State remains the selected area's live presentation; CommittedEndpoints supplies the baseline during
an active edit. Persistence reads the collection's committed endpoints, even on an explicit dirty
retry during a preview. Completion requests the durable save; a clean unchanged
completion causes no write. Cancellation precedes both client departure teardown and
standalone persistence disposal/dirty retries, so canceled previews cannot be saved.

A complete valid selection also owns a positioning gizmo at the geometric midpoint
of its rendered bounds, `(minCell + maxCell + 1) / 2`, using double SIMD coordinates
and explicit dimension. Center/axis motion shares the endpoint gizmo's analytic math,
snapping and visual feedback. Whole-selection transactions snapshot both corners and
apply the snapped integer displacement to that original pair on every preview;
neither a previous preview nor the moving midpoint rebases the drag. Both translated
corners must validate before one atomic state update. Long displacement checks guard
integer overflow, including moves between opposite signed-coordinate extremes.
Size is unchanged; the box and all three controls update live. Only completion saves,
and cancellation restores both corners. Endpoint edits resynchronize the midpoint.
Partial work areas do not have this control; visibility, dimension and lifecycle gates
are shared with the endpoint controls.

On local join, the client system creates a fresh work area controller, restores the named-area collection through persistence, then creates the shared box render binding. Visibility remains unchecked and mode disabled. The gizmo session is created lazily once enabled and alive, independent of brush placement eligibility. The 50-ms client tick refreshes context before brush early-outs; mode transitions refresh it immediately.

Both free cursor and mouse-look reticle support center and axis drags; reticle motion remains available for native aiming. Existing handles move over sky, foreground entities and absent terrain subject to coordinate/dimension validity. Only an explicit fresh empty/partial creation click calls the terrain resolver, using the captured visual near/far ray through the camera far plane without a player-reach cap. Creation retains native meta-block/interactable-entity filters and liquid policy, rejects foreground entities, sky, unloaded chunks and outside-clip hits, and copies the selected cell without brush offsets. It never refreshes native selection or loads terrain. Per-button ownership suppresses hold/repeat/release actions in both native action families, including after completion; Right/Escape cancel before placement. The installed 1.22.7 public event API lacks a focus callback, so the adapter uses the native client world's focus-listener list and explicitly detaches it.

Endpoints use Start for the per-axis minimum cell and End for the per-axis maximum cell, with explicit local Y/dimension. Crossing is allowed and renormalizes both corners on each preview; it does not clamp motion. Existing mixed/reversed saved pairs normalize on load without a schema migration. Each existing endpoint, even in a partial work area, has a positioning control: Start circle at cell+(0,0,0), End diamond at cell+(1,1,1), both with RGB arrows. The binding uses notifying Value assignments and controller transactions, distinguishes owner synchronization from previews, and cancels before authoritative replacement. Axis edits snap selected-component deltas; camera-plane center edits snap all world-coordinate deltas from the original anchor. Derived sizes use `max - min + 1` in wide integers; rendering spans minimum cell through maximum cell plus one.

Checking Show work area with no endpoints offers one explicit world click that creates both on that cell and consumes the entire gesture. A partial work area retains its existing handle; only a handle miss can create the missing endpoint. Invalid targets notify and remain pending. Right/Escape cancel creation until off/on visibility or explicit mode re-enable; contextual losses can reoffer only with eligible camera context and released mouse buttons. Retained other-dimension endpoints hide without automatic overwrite or relocation. The box still needs two valid same-dimension endpoints.

Persistence uses `LoadModConfig`/`StoreModConfig` in `vanillabuildingexpanded-workareas.json`, schema version 1, keyed by the exact `World.SavegameIdentifier`. It saves only endpoint commits, not visibility/context changes. Saves reread and merge the document, preserving unrelated worlds/metadata. Reads reject unsupported or unreadable documents without replacing them with defaults, and isolate invalid current-world records. A successful explicit edit can replace that invalid entry. The native writer truncates its destination, so a native-config staging write is promoted only after success, with targeted staging-file cleanup. Failed writes leave the edit dirty and notify; the next explicit edit or orderly disposal retries while the captured world identity is available. Missing identity is memory-only with feedback. Only endpoints persist; no work area state is synchronized to the server.

The box render binding requires enabled/alive context, checked visibility, two valid same-dimension endpoints, a matching live player dimension, and the same available client world. It registers at most one renderer in `Opaque` and unregisters at that same stage when eligibility is lost. Endpoint edits and GUI rebuilds do not recreate or duplicate the renderer. Mode off/death hide and cancel while retaining endpoints and preference. Leaving a dimension hides the old work area, retains size and notifies once per mismatch transition; returning restores eligible visibility.

The renderer uploads one unit cube lazily, using two `LineMeshUtil` loops plus four connectors for exactly 12 unique edges. Frames do not rebuild geometry, sample blocks or cull by endpoint distance/`RenderRange`. Stored bounds stay local-Y; the drawing adapter encodes the dimension only to match native `CameraPos`, subtracting camera coordinates in double precision before float matrix conversion. Drawing uses order 0.51 after opaque terrain, depth testing with depth writes disabled, and a 0.002-block outward expansion. Zero vertex flags avoid wind and extra Z-offset beyond the native shader's built-in 0.0014 clip-W bias. None of these adjustments change measured bounds.

The installed engine's OpenTK binding supplies depth-test/depth-mask/line-width snapshots because the public render API exposes setters only. Nested cleanup restores those values and the prior active shader even if drawing fails. Blend state, including indexed MRT settings, is untouched. Recognized native GL/setup failures are logged and surfaced once as an unavailable wireframe for the session, while sizes and controls remain usable. Unregistration is deferred out of the callback because native render-stage iteration indexes its live renderer list, including post-callback profiling.

World departure/system disposal cancel previews/creation, detach input and render observers, unregister/dispose renderers and shared meshes, retry dirty config before clearing controller ownership, and dispose the HUD binding. Late queued retirement checks disposal before touching the retired session. Repeated hide/show and teardown are idempotent.

Automated registration/matrix/cleanup tests and normal Build/JSON checks are distinct
from supported-game qualification. Required gizmo visual/input observations are tracked
in [delivery qualification](GizmoHandles.todo#phase-7-remove-superseded-code-document-and-qualify-delivery).
Native visuals/input, restart/world isolation, terrain/transparent-material depth
behavior, bias stability and preview/native-selection overlap require actual live
evidence; mocked services do not establish those results.

## Selection and inventory refresh

Ordinary updates receive the current `BlockSelection?` explicitly. An absent selection clears resolved position and validity. The one-millisecond dirty callback unregisters itself before reading its supplied selection getter, so it observes the selection at callback execution time. Client and server composition bind that getter to the owning session player. Inventory refreshes and placement dirty marking reread the current active slot.

Placement stacks retain the selected orientation's block ID and deep-clone all held-stack attributes before applying brush rotation. This preserves attribute-based properties such as lantern material, lining and glass without modifying the inventory stack. Source refreshes detect attribute changes even when the block ID is unchanged, replacing stale attributes while retaining the selected orientation. They also track the selected hotbar slot number: switching slots recreates the preview block and entity even for identical stacks, while retaining brush orientation. Unchanged sources in the same slot do not recreate the preview.

Resolved selections preserve the source face, hit position and other raycast data. Placement validation and placement requests use the same cloning rule. Missing block/position paths retain their own early-return semantics; native rules run only when there is a placement candidate.

### Local snapping outcome and presentation contract

Phase 1 of the [snapping indicator plan](BuildBrushSnappingIndicator.todo) implements
local result reporting. Phase 2 adds the isolated target-geometry/cache substrate
described below. Phase 3 adds shader drawing; Phase 4 wires the client session and
resource lifecycle. The user confirmed and approved Phase 4 installed-game smoke
verification on 2026-10-29. Phase 5 distributable verification is complete;
the user accepted the appearance and explicitly waived outstanding Phase 6
qualification for closure, without establishing measured cost or broad shape fidelity.
The implementation remains governed by the
[approved proposal](BuildBrushSnappingIndicator.proposal.md).

`BrushSnapping.ResolvePosition` has an overload that reports the resolved U/V/W
directions using the same calculation as candidate placement. Its original overload
remains available. Thresholds retain the existing float projection and strict
comparisons against `0.15f`; exact ordinary `0.35f` and `0.65f` boundaries are centered.
U is horizontal on every face; V is horizontal on top/bottom faces and vertical
on side faces. Opposite faces do not reverse world-axis U/V signs. W follows the
outward normal and is applied at most once, including explicit face offset.

`BrushPlacementValidation.Resolve` owns the detailed result and invokes native rules
once for a non-null block. `BrushPlacementResult` distinguishes no block, invalid
placement, ordinary placement (including W-only or no permitted movement), and
accepted tangential snapping. Requested directions retain the hit's U/V/W and mode;
resolved directions describe the candidate; applied directions are zero when no
block is present or validation rejected it. Equal positions do not imply equal
outcomes. Existing position-only wrappers retain their null-block and validation
contracts, and rejected candidates are never retried unrestricted.

`BuildBrushInstance.TryUpdate` captures the original target before native validation
and before replacing `Selection.Position` with the destination. Native selection cloning
omits `Block`, so the original-target copy explicitly retains that reference for
capturing target identity. It clears prior
presentation during resolution and publishes a `BrushSnappingPresentation` only if
its input revision remains current. The snapshot owns copies of target coordinates,
face, hit and selection-box context, captures the target block ID, and retains an
immutable result with independent U/V directions and copied position accessors.
The existing update shortcut additionally requires matching original target context,
so aiming at the previous destination cannot incorrectly reuse the previous outcome.

Consumers read `BuildBrushControllerClient.GetSnappingPresentation` with the current
world selection and an explicit supported-geometry decision. They must use this
guarded read rather than caching a previously returned snapshot across context
changes. Reads require active mode, a supported held source, a current matching
target/face/block, matching mode/revision and resolved destination,
whether placement is valid or invalid.
Live hotbar slot, block, stack count and source
attributes are checked against the resolved source without refreshing or validating.
The geometry argument is a gate, not a Phase 1 claim that any shape is qualified.

`MatchesTarget` checks the shared target/mode/revision identity and finite live
hit coordinates. Presentation reads and validity refresh use that context check:
when the hit crosses a region boundary on the same face, the indicator continues
showing the current resolved preview until the existing 50 ms placement update
publishes its replacement. Neither fill nor inactive guides disappear during
that interval. A slow validity refresh likewise retains that preview's directions
and destination while updating its validity with the existing single rule call.

The stricter `Matches` builds on the same target check and compares permitted
U/V directions through `BrushSnapping.GetTangentialDirections`. The placement
update shortcut still requires it, so a region change cannot accidentally reuse
the old outcome instead of resolving the new candidate. This separates preview
display lifetime from placement-update eligibility without extra rendering-time
validation, a new timer, changed thresholds, or hysteresis.
Nonfinite hits, changed targets, faces, modes, revisions, and source contexts
still suppress immediately. Selection-box index alone does not invalidate the
same target context. Movement on disabled axes also remains stable.

Mode/source changes, dirty refreshes and target loss invalidate presentation.
`ShowsSnappingRegions` permits centered and ordinary/W-only results whenever a
horizontal or vertical snapping mode is enabled. `None`, unsupported geometry,
and stale contexts yield no presentation. Centered/disabled axes select the center
slot of the face-local 3x3 partition. Invalid placement retains its resolved directions
and turns the active chevron red; valid placement uses orange. The active patch
remains faint white in either state. Applied directions remain zero for
invalid placement, so rendering
uses the resolved directions without changing placement semantics.
The existing slow validity check may update the matching snapshot's validity using
its one existing rule call, but cannot attach an old result to a new target or face.
The controller/system still own their existing update cadence; no rendering, new
input, preview synchronization, or network fields are introduced by this contract.

Automated coverage resides in `BrushSnappingTests`, `BrushPlacementResultTests`,
`BrushSnappingPresentationTests`, and the existing placement-validation/restriction
tests. It covers all six faces/modes, independent top/bottom directions, exact and
adjacent float thresholds, explicit W offsets, equal-position outcome distinctions,
single validation calls, source/context invalidation, and snapshot ownership.
Decaying 120-frame camera/hit-motion regressions cover all six faces and the
client drawing path, requiring uninterrupted drawing with no extra validation or
upload while directions stay unchanged. Region-transition regressions traverse all
nine regions on all six faces, requiring both mesh passes on all 270 simulated
frames before and after placement updates, including intervening slow validity
refreshes. They assert unchanged resolved directions until the placement update,
correct new directions afterward, no rendering-time validation, and no mesh
reupload or disposal. All 2,177 focused Release snapping, target, placement, and
overlay-state tests passed after this correction. The 12 new transition cases
first reproduced the missing-presentation/draw gap before the production fix.
These checks do not qualify geometry, shader compatibility, appearance, or cost.

### Snapping target geometry and owned cache

Phase 2 introduces `BrushTargetMeshSource`, `BrushTargetBounds`,
`BrushTargetMeshCache`, and `BrushTargetGeometry`; these types do not register or draw
an overlay. Consumers must still obtain a current guarded presentation from the
controller, then ask the cache to qualify that original target. The cache's live
block-ID check supplements, not replaces, the controller's context guard.
An unsupported or unavailable target returns no geometry, never a generic cube.

Phase 4 replaces default-mesh qualification with `BrushTargetWorldMeshSource`.
There is no block-class, behavior, shape, snow, render-pass, or texture whitelist.
An original-target request schedules native chunk tessellation once, not every
frame. Harmony observers copy only the target's newly emitted triangle indices
and referenced positions from the actual native terrain pools. The engine itself
chooses world-position variants, invokes block-entity/custom tessellation and
snow hooks, applies transforms, and tessellates attached decors and native draw
types. Close-range pools 0, 1, and 2 are all retained; only the far replacement
pool 3 is excluded. Decor ownership follows the native decor-emission scope,
including the alternate-Z-offset branch that does not set `isDecorTesselate`.
Edge-only chunk rebuilds do not clear interior captures.
The acquisition request's own synchronous `BlockChanged` notification from
`MarkBlockDirty` is ignored only for that in-flight request and position, preventing
an invalidate/re-request loop; real edits and subsequent notifications still invalidate.

The observer never substitutes geometry, changes native output, or calls placement
validation. Copied positions are converted from chunk-local to original-block-local
coordinates; native transforms are not applied twice. Main-thread publication is
guarded by request identity, so old-target, invalidated, and departed-session results
are discarded. Missing/pending or empty native output produces no overlay, with
diagnostics for requests, empty output, and capture failures. A renderer that emits
no terrain triangles has no captured mesh; this is not inferred from its class.
GPU-only material deformation is not reproduced by copying CPU positions and is
not claimed as verified animated-surface fidelity.

Material alpha and texture holes intentionally do not clip the indicator. No
texture bitmap is acquired or scanned, and the shader has no material-alpha sampling.
Native material/custom attributes are not eligibility tests. Positions, indices,
packed face normals, and native Z offsets are copied. The flags' low byte carries
the native render-pass ID instead of material glow, allowing the vertex shader to
match opaque, transparent/meta, liquid, and topsoil depth conventions without
sampling materials. Capture equality includes these flags. Upload failures remain explicit.
The native uploader assigns consecutive attribute slots, not the fixed material
layout: this position/flags-only mesh uses position slot 0 and integer flags slot 1.
The snapping vertex shader must use those slots; there are no UV/color/normal
buffers between them.

Captured geometry is checked as indexed triangles:
finite positions, valid complete triangle indices, and nondegenerate triangles.
There is no cuboid, vertex-count, axis-alignment, closed-volume, or unit-cell-bounds
requirement. Slopes, multi-element shapes, and recessed surfaces are not excluded
merely by their topology. Declared draw type does not impose a separate cube test:
the native tessellator has already generated the actual geometry.

`BrushTargetBounds` records extents and validates indexed geometry; it does not
perform a second CPU directional-coverage or triangle-facing rejection.
Separate chevron geometry is clipped onto the captured triangles; shader-facing
filtering applies to both draw passes. The U/V fill mask is an
intersection: only the single resolved cell is filled, not a union of axis bands.
Native packed normals determine facing when available, with geometric derivatives
as the fallback for meshes without packed normals. Raycast hits can lie on native
selection boxes rather than rendered triangles, so neither triangle membership nor
an outer bounding plane is required of the hit. This does not change raycasting or
snapping. Holes and absent surfaces never become generated indicator geometry.
Bounds never redefine snapping or generate a surface. Cached/uploaded positions
and indices remain the actual engine mesh unchanged. Shader clipping alone does
not establish readable opacity or final visual quality.

Candidate unit cubes, the base-Block `game:ethoslab` slab, and faithful static
sloped/multi-element/recessed assets were proposed for Phase 6, along with
native vertical cobble/stone/wood slabs, including their native snow geometry.
Texture-only variants do not exclude stone-brick cubes or otherwise qualified slabs.
Centered narrow geometry without resolved tangential movement
and surfaces outside the selected-facing directional regions are omission cases.
Recessed geometry with an actual qualifying surface is not an
automatic omission. The user waived the additional Phase 6 geometry checks;
these examples are not a newly qualified support set.

Acquisition/transform evidence is from the installed **1.22.7.0** engine
(`G:\Vintagestory\VintagestoryLib.dll`) and the local API source, not a live rendered
qualification. The capture observes `ChunkTesselator.NowProcessChunk` and its
per-block emission into terrain pools, rather than asking for a default or
inventory mesh. `MiniDimensionTessellator` preview output is not world-fidelity
proof. After capture, only original-target translation is
applied, including encoded dimension Y and double-precision camera subtraction
before float conversion. Shape rotation/scale/offset must not be applied twice.

The one-entry render cache keys on the live `Block` object and captured `MeshData`
reference. Acquisition is position/dimension-specific; moving to another instance
requires its native output, not an assumption that its default mesh is equivalent.
Face, hit, and mode changes reuse the existing capture and upload. Repeated native
captures with identical positions/indices/render flags keep the existing reference. Target and
neighbor edits invalidate acquisition; subsequent native rebuilds also refresh the
current capture. Resource edits explicitly invalidate requests and render resources.
Texture-alpha changes alone do not invalidate or requalify geometry.
`InvalidateGeometry(blockId)` retires only that entry; `Invalidate()` retires all
owned resources for world/resource/shader changes; replacement and `Dispose()`
also retire them. Missing captured geometry releases the old entry. Suppressed
targets never expose the previous descriptor, but may retain valid cached geometry.
`BrushSnappingRenderBinding` wires these hooks to actual target-edit, shape,
texture/atlas, shader, and session lifecycle events as described below.

Engine mesh arrays remain borrowed and unchanged. The observer copies them during
native tessellation and never retains or disposes native pool data. The cache clones captured CPU
data, uploads its owned copy lazily once, and disposes only its owned GPU/CPU data.
Replacement/disposal are idempotent and clear ownership before release; failed GPU
initialization disposes that failed reference and remains retryable. Acquisition,
qualification, upload, invalidation, and disposal enforce the creating client-main
thread (and the engine main-thread ID when initialized). The capture observer runs
on the native tessellation thread, with stale-safe publication to the main thread.
Tests install the Harmony observers against the installed engine, exercise pool
deltas, baked transforms, LOD choice, request identity/dimensions, and late disposal.
Other tests use synthetic native
cube/slab data plus wedge, alternate-triangulation, multi-element and recessed
meshes with mocked acquisition/upload boundaries. Atlas mocks assert that no
texture loading occurs, including transparent pixels and unavailable bitmaps.
Tests cover actual-mesh
preservation, shader-owned directional clipping, selection-box hits, reuse, translation,
invalidation, failures and disposal. They do not prove live asset support, shader
compatibility, appearance, ordering, or rendering cost.

### Isolated snapping shader drawing

`BrushSnappingRenderer` draws the fill with `BrushSnappingShader` and the
`vbe-snapping` vertex/fragment assets, then switches to `BrushSnappingChevronShader`
and the independent `vbe-snapping-chevron` assets for glyph geometry.
Both dedicated classes derive from the game's `ShaderProgram` through
`BrushSnappingSurfaceShader`, matching native file-shader registration and
compilation. The shared typed base exposes the seven common uniform setters.
The fill adds color/center-threshold setters (nine uniforms total); the chevron
class adds its active-color setter (eight total). No pass-selection uniform or
fill/glyph branch exists in either program. Only shader classes map property
setters to GLSL names; the renderer never makes raw uniform or texture-binding calls.
Each program has its own typed `BrushSnappingShaderOwner<TShader>`, retaining lazy
creation, registration, compile failure cleanup, replacement/reload, and disposal.
Default factories create the actual native subclasses; typed factory seams
keep lifecycle and renderer tests independent of an OpenGL context.
Both draws share a single `BrushOverlayRenderState` capture/restore scope, but
each program is used/stopped around its own draw. Failure in either program
disables the overlay until explicit reload. Reload replaces both programs;
renderer disposal attempts to retire both even if one disposal fails.
The session binding registers this renderer and supplies current guarded geometry
on each callback. The user confirmed and approved the Phase 4 installed-game smoke
on 2026-10-29, covering shader loading, visibility/stability, validity colors,
depth quality, and reload/lifecycle behavior. The user later accepted the appearance
and waived additional Phase 6 appearance/ordering/performance qualification;
no comprehensive qualification or measured performance is claimed.
The renderer never retains a target between callbacks or owns the cache's mesh.

The user-directed chevron revision replaces the earlier bands and region-grid lines.
Face-local thresholds remain `0.35` and `0.65`, but no grid lines are drawn.
Only the intersection of the resolved U/V slots carries the faint white rounded
patch. The active chevron is orange for valid placement and red for invalid
placement; inactive guides remain faint orange. Zero
directions select the center slot, including centered/W-only placements; a disabled
axis also resolves to that slot. `None` suppresses the entire visualization.
There is no separate W glyph and placement semantics remain unchanged.
Independent positive/negative directions use Z/Y on X faces, X/Z on Y faces, and
X/Y on Z faces; opposite faces never reverse the axes. Positions remain block-local
through interpolation. Native packed normals, or triangle derivatives corrected
for back faces when those normals are absent, supply the selected-face dot test.
No texture UV, atlas, bitmap, material alpha, shape bounds, or triangle-edge artwork
is involved. Mesh triangles clip the chevrons and fill, including over texture holes.
The renderer passes the actual `BrushSnapping.CenterThreshold` (`0.15`) through
the typed shader property, avoiding a separate shader threshold. Outer slots use
strict comparisons; exact thresholds belong to the center slot. The fill is an
inset rounded square centered within the resolved cell, sized to its smaller
dimension so rectangular cells do not stretch it. A signed-distance mask supplies
`0.045`-block corner radii and an inward `0.05`-block feather, widened to at least
one pixel, with `0.015`-block inset. Fill opacity reaches `0.08` in the interior
and falls to zero at its rounded boundary; the original region mask prevents
leakage into neighboring CPU regions. White fill is independent of validity.
Chevrons use stroke width `0.01` block units with a geometry-based feathered edge.
Their opacity fades smoothly to zero
over the outer `0.15` block units of the face-local `[0, 1]` perimeter. This is a
block-cell edge fade, not arbitrary native-mesh contour detection; the captured
triangles still clip the overlay. `BrushSnappingChevronMesh` builds the eight
directional glyphs on the CPU using the shared snapping thresholds.
Every noncenter region draws exactly one
chevron, oriented by its normalized two-component direction; corner regions use
one diagonally rotated V, not separate U/V glyphs. The center has no chevron.
A canonical tip and two arms are rotated, centered by their bounds, and uniformly
scaled so their centerline bounds span the rounded fill's full width and height.
Stroke coverage extends slightly beyond those bounds but remains within its region.
This sizing applies to all eight orientations, not just cardinal arrows.

Each V consists of miter-joined quad strips with a feathered perimeter: vertex
coverage is `1` in the core and `0` at the outside, interpolated across the edge.
The strips are triangulated and intersected with the captured native triangles
in the selected face's projection. Barycentric reconstruction puts generated
vertices on the actual source surface, including slopes and recessed geometry;
missing surfaces do not produce a replacement plane. The target mesh lazily
owns one combined glyph mesh per face (at most six); region, mode, validity, and
camera changes reuse these meshes. Replacement/invalidation/disposal of the
target releases all derived CPU/GPU meshes on the client main thread.

Native position/flags remain at attribute locations `0`/`1`. Glyph-only custom
float attributes occupy `2` through `8`: region/coverage, interpolated native
normal, the three original triangle positions, barycentric weights, and original
Z offsets. The installed native uploader was inspected to confirm that custom
float groups follow flags consecutively. Stride is `84` bytes, seven `vec3`
groups, without UV/color/normal arrays inserted before flags.

`nativePosition` preserves the existing pass-specific offset and depth bias.
Glyph vertices interpolate the three original *clip-space* positions after that
calculation; recomputing nonlinear native offsets at subdivided vertices could
otherwise reintroduce surface fighting. Normal interpolation retains the source
values without repacking/quantization. The renderer draws the faint fill on the
original mesh, then the combined chevrons on their own mesh, within one owned
render-state scope, switching between separate fill and chevron programs.
Both vertex shaders include `vbesnappingposition.ash` from the mod's
`shaderincludes` category, sharing the native offset/bias calculation without
duplicating it. The fill vertex layout has only native position/flags; the
chevron program alone consumes the additional glyph metadata. Its fragment
shader only colors/fades rasterized glyph triangles; it contains no procedural
chevron or line-segment-distance function, and no rounded-fill calculation.

The former combined program's `GlyphPass` uniform/property has been removed.
Vintage Story 1.22.7's `collectUniformNames` scanner does not recognize GLSL
`bool` uniforms, even though OpenGL accepts them. The uniform-contract regression
test expands the shared depth include and invokes the installed game's actual
scanner for every declared uniform in each program, independently.

The split-program revision passed all 414 focused Release renderer, render-binding,
and native mesh-source tests. Coverage includes each program's uniform contract,
registration, compile/replacement failure cleanup and reload recovery, disposed
program replacement, shared transforms, and render-state restoration across both
draws. An isolated OpenGL 4.6/NVIDIA 610.88 check compiled all four actual shader
stages and linked both programs, expanding the actual native/shared includes and
stubbing only OIT output. It verified all 17 active uniform bindings and 11
attribute slots across the two programs, with no `glyphPass` binding and no GL
errors. This compile/link check is not a new live-game visual or depth qualification.

The resolved directions select which region is active. Its chevron uses the
configured validity color; every other directional region uses orange at `0.2`
opacity. Vertex coverage and the existing face-edge fade multiply both. Only the
active region receives faint white fill; inactive regions have no fill.
Disabled axes still resolve to their center slot, but inactive guides remain
visible. Centered/W-only placement shows its patch and the eight faint surrounding
chevrons. `None` suppresses the entire visualization. No grid lines are restored.
Narrow coverage may still be unreadable. Earlier Phase 6 appearance acceptance
applies to the prior bands, not a new in-game acceptance of this chevron revision.

The separate-geometry revision passed all 2,157 focused Release snapping,
target-mesh, placement, and overlay-state tests, with no failures or skips.
An isolated RTX 4090/OpenGL 4.6 run exercised actual builder output with the
21-float layout and both shader assets, expanding the native include and replacing
only the OIT output with straight RGBA. All 4,512 draws and 13,942,730 checks passed,
including 672 depth cases covering native passes `0` through `7` and offsets
`0`, `1`, `2`, and `7`. Checks covered single-V/full-span geometry, surface clipping,
white fill independent of validity, active orange/red and inactive orange opacity,
foreground occlusion, and a bit-identical depth buffer after overlay rendering.
No GL errors occurred; scratch files and GPU resources were released. Geometry
inputs were synthetic native-format fixtures, not fresh live terrain captures.
The native uploader's attribute layout was checked against its implementation,
not executed in that harness. This is not new installed-game visual or performance
qualification.

The earlier fragment-rendered enlarged-chevron revision passed all 345 focused Release renderer
tests, with no failures or skips. An isolated RTX 4090/OpenGL 4.6 check compiled
the actual fragment asset with only the OIT include replaced by straight-RGBA
output: 55,890 renders and 702,834 checks passed across six faces and five
viewports. All 96 measured glyph bounds at 640x640 met the full-fill-span target:
cardinal width/height `0.28125` versus target `0.27` and measured fill `0.265625`;
diagonal width/height `0.33125` versus target `0.32` and measured fill `0.315625`.
Checks also covered single-V connectivity/orientation, the inactive `0.2` opacity
ratio, no center arrow, active-only feathered fill, threshold ties, clipping,
face fade, source-over composition, and back-facing rejection. No GL errors
occurred. This is fragment verification, not new installed-game or performance
qualification.

The earlier small-chevron revision passed all 385 focused Release renderer/session-binding
tests, with no failures or skips. An isolated RTX 4090/OpenGL 4.6 check compiled
the actual fragment asset with only the OIT include replaced by straight-RGBA
output. All 432 face/cell/color/viewport combinations passed: 75,168 renders and
2,390,568 checks covered signed zero/one/two-chevron output, removal of grid lines,
cell isolation, softened fill, face fade, source-over composition, and back-facing
rejection. No GL errors occurred. At very small viewports, antialiased chevrons can
overlap the patch center and increase composited opacity; isolated fill remains
`0.35`. This is not new installed-game or performance qualification.

The earlier softened grid style passed all 345 focused Release renderer tests, with no failures
or skips. An isolated RTX 4090/OpenGL 4.6 check compiled the actual fragment asset
with only the OIT include replaced by straight-RGBA output. All 432 combinations
of face, active cell, validity color, and viewport passed: 37,152 renders and
354,816 checks covered rounded-square symmetry, feathering, no cell leakage,
opaque divider cores, monotonic fade at all four block-cell edges, intersections,
source-over composition, and back-facing rejection. No GL errors occurred.
This is fragment verification, not new installed-game or performance qualification.

The initial hard-cell region-grid verification passed 2,148 focused Release snapping/placement/target/
render-state tests, with no failures or skips. An isolated RTX 4090/OpenGL 4.6
check compiled the actual fragment asset, replacing only the OIT include with
straight-RGBA output: 2,916 renders and 151,632 pixel checks passed across six
faces, nine cells, both validity colors, and three viewport sizes. Checks covered
single-cell fill, orange dividers/intersections, antialiasing and bounded widths,
back-facing rejection, source-over composition, and strict threshold boundaries.
No GL errors occurred. This validates the fragment visualization, not a new
installed-game scene, native vertex/depth regression, or performance measurement.

The render stage is OIT, order `0.52`, using the native OIT shader include and
preserving indexed blending. The owned mesh is drawn once with the original-target
camera-relative/dimension-aware transform. The vertex shader first matches the
captured pass's native depth offset. A clip-depth bias of
`0.00001 * w` and polygon offset factor/units `-1/-1` then move depth toward the
camera without moving local/world geometry; the polygon factor handles slope.
Depth testing uses LEQUAL, depth writes are disabled, and culling is disabled to
support no-cull surfaces. Counterclockwise front winding makes derivative
normal fallback independent of the
previous renderer's winding state. Capture/apply/restore isolates depth enable,
depth mask, depth function, culling, front winding, and polygon-offset enable/factor/units.
Shader stop, state restoration, and the
previous active shader's reactivation run through nested cleanup even on failure.
The Phase 4 runtime smoke is user-approved. Comprehensive bias, stage ordering
versus selection/preview/transparency, and occlusion qualification was explicitly
waived at Phase 6 closure rather than newly established.

The earlier band-rendering depth correction passed 1,361 rebuilt managed tests and Release publish.
An isolated native OpenGL 4.6 context on the RTX 4090 compiled/linked the actual
shader assets and exercised the production render-state implementation in 1,728
cases: orthographic/perspective/grazing views, two surface heights, all nine native
passes, axis/diagonal directions, and native/fallback normals. Visible cases
required nonzero rasterized coverage; foreground-occluded cases required zero.
Depth buffers remained bit-identical, and all captured GL state was restored,
including both prior polygon-offset enable states and nondefault factor/units.
This is not installed-game scene, asset, ordering, lifecycle, or performance evidence.
That earlier harness manually assigned the flags slot and missed an uploader/shader
layout mismatch. The subsequent live test still showed the block texture cutting
through the band. Native `ClientPlatformWindows.UploadMesh` inspection established
that flags are uploaded at slot 1 for the stripped capture, while the shader read
slot 3. Correcting that binding restores the captured normals, pass IDs, and native
Z offsets without increasing the bias or disabling depth testing.
The corrected check invokes the installed engine's actual `UploadMesh` and
`RenderMesh` on both full material meshes and production-captured stripped meshes,
using the native opaque vertex shader for terrain depth. On RTX 4090/OpenGL 4.6,
384 coverage/occlusion/depth-preservation cases passed across orthographic,
perspective, and grazing views, two heights, offsets 0/1/2/7, and all eight U/V
direction combinations. Reverting only the shader slot reproduced 48 missing-band
failures; the corrected slot passed those same cases with the original bias.
Rebuilt managed verification again passed all 1,361 tests and Release publish.
The user subsequently confirmed the z-fighting correction and, on 2026-10-29,
approved all remaining Phase 4 runtime checks.

Shader creation/registration/compilation failures dispose failed candidates and
retire replaced owned programs. Drawing failures restore state and disable only
the overlay, with a `[BuildBrush]` logger error, until an explicit successful
`ReloadShader`. Native-disposed programs are recreated before use. Disposal is
idempotent and releases only the renderer's shader, never the geometry cache or
borrowed engine meshes. The session binding owns subscriptions and cache invalidation.
`BrushSnappingRendererTests` cover all face/mode/direction inputs, actual mesh and
original-target transform, source masking intent, state-cleanup failure paths,
shader replacement/recovery, and ownership. C# compilation and these tests are
not GLSL compilation, live state/ordering, visual, or performance proof.

### Snapping overlay client-session lifecycle

`BuildBrushSystem_Client` creates one `BrushSnappingRenderBinding` on the local
player's join and disposes/clears it on world departure and system disposal.
The binding owns one geometry cache and one renderer, registered in OIT at order
`0.52` for that session. Disabled, suspended, targetless, or failed overlays leave an
idle callback, not a separately cached presentation or repeated registration churn.
The server preview renderer, network packets, input handlers, and placement
validation cadence are unchanged.

Each frame reads the live local selection through the controller's presentation
guard before qualifying geometry. World identity, player life, and dimension are
also checked. New faces/hits/targets/items/modes without matching resolved outcomes
are omitted immediately, including between placement ticks. Drawing never invokes
placement validation. Death disables mode through the existing system event and
is independently gated by the player's live state. Target loss, mode disable,
unsupported inventory, or a stale/suppressed presentation clears drawing without
releasing valid reusable mesh data; subsequent matching outcomes can reuse it.
Placement-validity changes update the active chevron's orange/red color without
rebuilding or reuploading geometry; the fill stays faint white.

The binding subscribes to target-position `BlockChanged`, `ReloadShapes`,
`ReloadTextures`, `BlockTexturesLoaded`, `ReloadShader`, and `LeaveWorld`.
World edits elsewhere do not invalidate the cache. An edit at the original target,
shape reload, or resource reload invalidates owned geometry; shader reload also
compiles/replaces both owned programs and returns its success to the event caller.
The installed 1.22.7.0 `ShaderAPI.ReloadShaders` retires native programs before
triggering mod reload handlers. Failed compilation/replacement/drawing/registration
is explicitly logged and disables only the overlay; a successful explicit shader
reload restores drawing and can retry a failed renderer registration.
Successful compilation logs
`[BuildBrush] Snapping indicator shader vbe-snapping compiled.` and
`[BuildBrush] Snapping indicator shader vbe-snapping-chevron compiled.`

Resources and render-list membership are not retired within the overlay callback:
invalidation/disposal during drawing is queued on the client main thread.
Pending invalidation/disposal suppresses new target reads immediately. A deferred
shader-reload event returns false rather than claiming compilation already succeeded.
Departure/disposal detaches all six subscriptions synchronously, then unregisters
the renderer and disposes owned shader and CPU/GPU meshes (after the callback, if
necessary). Disposal is idempotent and never disposes borrowed engine geometry.

`BrushSnappingRenderBindingTests` use the real brush/controller with installed
native player/selection access and mocked rendering. They cover rapid unmatched
contexts, source suspension/resumption, modes, invalidity, death/world/dimension
guards, geometry replacement, targeted and global invalidation, shader failures
and recovery, deferred retirement, subscriptions, and system session ownership.
The native player fixture avoids an installed-API default-interface method that
Castle cannot proxy. These tests are not a live shader or visual qualification.
The user confirmed and approved the installed-game Phase 4 checks on 2026-10-29,
closing that runtime evidence gap. This approval is user-reported, not an
agent-captured runtime diagnostic trace. Phase 5 records the distributable
verification below; Phase 6 records current appearance acceptance and the explicit
waiver of outstanding qualification, not measured cost.

### Snapping distributable verification

The Phase 5 evaluation candidate is built from revision
`7cb7cd87f10644da43f612dc3f3ee25cee3d3ff2` with .NET SDK 10.0.401 on Windows
win-x64 against Vintage Story 1.22.7. The existing Cake `Package` dependency chain
performs JSON validation, clean/publish, full tests, and archive creation without
installing the mod. Focused snapping/placement/target/render-state tests passed
2,142 cases; the full package test dependency passed 3,565 cases, with no failures.
The [implementation plan](BuildBrushSnappingIndicator.todo#phase-5-regression-and-distributable-verification)
records commands, results, candidate identity, and the completion review.

The resulting `Releases\vanillabuildingexpanded_999.99.9.zip` has 34 entries and
SHA256 `7E792614A8342BD5DCCDEC6580546B40487729E78D44C163A1D633CF5B7766F8`.
Both snapping shaders are present at their domain asset paths and exactly match
the current sources and the previously user-approved installed archive.
The mod metadata and all other entries also match that installed archive except
the rebuilt mod DLL and PDB. The DLL has unchanged types, method signatures/IL,
and assembly references; its informational revision and MVID differ after the
Phase 4 commit. This is reuse of the applicable user-reported packaged-runtime
approval for unchanged implementation and shader bytes, not a new game/GLSL run
or a claim that the rebuilt DLL is byte-identical.

The package contains the pre-existing protobuf/service-model dependencies, not
test frameworks, Moq/Castle, engine DLLs, or game-provided OpenTK libraries.
No new server-side requirement or migration is introduced. Existing Cake-build
NuGet vulnerability warnings remain separate from package contents and are not
claimed fixed. Comprehensive geometry, overlay interaction, and measured rendering
cost were not established by Phase 5 and are not inferred from Phase 6 acceptance.
The required second direct review and fresh independent completion audit found
Phase 5 fully satisfied. The audit independently inspected the documentation,
source/diff, TRX files, ZIPs, and PE metadata, including all 2,197 methods/constructors
with unchanged implementation.

### Snapping user acceptance and qualification waiver

On 2026-10-29 the user accepted appearance/readability/performance and then
explicitly directed closure of Phase 6 while skipping outstanding qualification.
Additional structured visual/interaction checks, named geometry qualification,
comparable overlay-on/off timing measurements, and live cache observations are
waived, not successful tests. Existing Phase 4 runtime acceptance and Phase 5
automated/package evidence remain distinct from that decision. No additional
geometry categories are claimed qualified, and no timing numbers are invented.
Native terrain capture, missing-output omission, and GPU-only/material-deformation
limitations above remain unchanged.

The current release and installed ZIP on disk match SHA256
`A359C738D543C6EC5EC9FF4C7EC22FC05DE463901E359DD41EFAB87923F8F51F`
(529,080 bytes, rebuilt 2026-10-29 16:26:39). Shader and modinfo bytes still match
the Phase 5 candidate; the DLL bytes differ. This is archive identity, not proof
of a fresh runtime or performance test. Closure changes only documentation;
implementation, shader, and packaging inputs are unchanged. The
[implementation plan](BuildBrushSnappingIndicator.todo#phase-6-user-run-geometry-appearance-and-performance-qualification)
tracks the closure review and final acceptance under this user-authorized contract.
The direct second review and fresh independent completion audit found the revised
contract fully satisfied, with no unresolved findings. Phase 6, final acceptance,
and the existing project.todo indicator are complete under the explicit waiver.

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
