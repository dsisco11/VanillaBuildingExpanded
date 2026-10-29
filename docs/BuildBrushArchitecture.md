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
described below. Shaders, rendering, resource lifecycle integration, and in-game
appearance/performance qualification remain later phases under the
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
and before replacing `Selection.Position` with the destination. It clears prior
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
target/face/hit/selection box/block, matching mode/revision and resolved destination,
and valid accepted U/V movement. Live hotbar slot, block, stack count and source
attributes are checked against the resolved source without refreshing or validating.
The geometry argument is a gate, not a Phase 1 claim that any shape is qualified.

Mode/source changes, dirty refreshes and target loss invalidate presentation.
Centered/disabled axes contribute no marking; None, ordinary/W-only placement,
invalidity, unsupported geometry and stale contexts yield no presentation.
The existing slow validity check may update the matching snapshot's validity using
its one existing rule call, but cannot attach an old result to a new target or face.
The controller/system still own their existing update cadence; no rendering, new
input, preview synchronization, or network fields are introduced by this contract.

Automated coverage resides in `BrushSnappingTests`, `BrushPlacementResultTests`,
`BrushSnappingPresentationTests`, and the existing placement-validation/restriction
tests. It covers all six faces/modes, independent top/bottom directions, exact and
adjacent float thresholds, explicit W offsets, equal-position outcome distinctions,
single validation calls, source/context invalidation, and snapshot ownership.
These checks do not qualify geometry, shader compatibility, appearance, or cost.

### Snapping target geometry and owned cache

Phase 2 introduces `BrushTargetMeshSource`, `BrushTargetBounds`,
`BrushTargetMeshCache`, and `BrushTargetGeometry`; nothing registers or draws an
overlay yet. Consumers must still obtain a current guarded presentation from the
controller, then ask the cache to qualify that original target. The cache's live
block-ID/entity/decor checks supplement, not replace, the controller's context guard.
An unsupported or rejected target returns no geometry, never a generic cube.

The initial source boundary deliberately accepts only the exact engine base `Block`
class, without block/collectible behaviors, entity geometry, decors, positional
randomization, alternate/tiled textures or shapes, overlays, or LOD substitutions.
Cube/JSON drawing with an ordinary Vintage Story composite shape is considered.
Derived/custom/chiseled blocks, animations/custom rendering, voxelized shapes, wind
and depth-offset geometry remain excluded. Static Opaque, OpaqueNoCull, BlendNoCull,
and Transparent passes are admitted: material alpha and texture holes intentionally
do not clip the indicator or disqualify geometry. No texture bitmap is acquired or
scanned; texture availability, baking, and pixel alpha are not qualification inputs.
The shader must use the mesh and procedural marking mask without material-alpha
sampling. Animated liquid and other special, unqualified render paths remain
excluded for geometry fidelity, not for texture opacity. Upload failures remain
explicit errors.

The borrowed default JSON mesh is retained as actual indexed triangle geometry:
finite positions, valid complete triangle indices, and nondegenerate triangles.
There is no cuboid, vertex-count, axis-alignment, closed-volume, or unit-cell-bounds
requirement. Slopes, multi-element shapes, and recessed surfaces are not excluded
merely by their topology. Custom/wind/depth-offset attributes, extra transform bits
and special unqualified per-face passes remain excluded. The four static material
passes above and native inherited passes (`-1`) are allowed. Cube drawtype alone
still requires the borrowed mesh to match native unit
cube faces, because native cube drawing does not generally consume an arbitrary
JSON shape. This is a world-path fidelity check, not replacement geometry.

Surface suitability is separate from source fidelity. `BrushTargetBounds` records
extents but tests the actual triangles for selected-facing directional opportunity.
A triangle faces the selection when its oriented geometric normal has a positive
dot product with the selected outward
axis; it need not be axis-aligned. Raycast hits can lie on native selection boxes
rather than rendered triangles, so neither triangle membership nor an outer
bounding plane is required of the hit. This does not change raycasting: it uses the
accepted result's directions and supplies the actual mesh for shader clipping.
Holes and absent/back-facing surfaces never become generated indicator geometry.
Selected-facing triangles must have projected surface area reaching each applied
direction's existing cell region (`0.35`/`0.65`); independent U/V opportunities may
occur on different triangles. Bounds never redefine snapping or generate a surface.
The cached/uploaded positions and indices remain the actual engine mesh unchanged.
Phase 3 must use matching selected-facing logic in the shader and assess coverage
of its concrete procedural mask; this opportunity check does not establish final
glyph coverage or readability.

Candidate unit cubes, the base-Block `game:ethoslab` slab, and faithful static
sloped/multi-element/recessed assets should be exercised in Phase 6. Many ordinary
stone-brick cubes may be excluded by texture variants; custom-class cobble slabs
remain excluded. Centered narrow geometry without accepted tangential movement
and surfaces outside the selected-facing directional regions are omission cases.
Recessed geometry with an actual qualifying surface is not an
automatic omission. All actual readable-marking claims remain pending Phase 6.

Acquisition/transform evidence is from the installed **1.22.7.0** engine
(`G:\Vintagestory\VintagestoryLib.dll`) and the local API source, not a live rendered
qualification. `ITesselatorManager.GetDefaultBlockMesh` returns borrowed default
chunk geometry; its manager tessellates composite transforms into those positions.
Native `JsonTesselator` can replace it through block entities, custom hooks,
position-selected alternates, LODs, or random transforms, which is why those paths
are excluded. Native `CubeTesselator` uses unit-cell cube faces. Inventory
`GetDefaultBlockMeshRef` and `MiniDimensionTessellator` preview output are not used
as world-fidelity proof. After qualification, only original-target translation is
applied, including encoded dimension Y and double-precision camera subtraction
before float conversion. Shape rotation/scale/offset must not be applied twice.

The one-entry cache keys on the live `Block` object and borrowed default `MeshData`
reference. No admitted variant depends on position, face, hit, mode, or revision;
those remain in a fresh presentation/transform descriptor, so equivalent instances
reuse geometry. Looking up the engine's default mesh does not request independent
retessellation. Positive and negative qualification are cached. In-place geometry
or resource edits require explicit invalidation even if references survive.
Texture-alpha changes alone do not invalidate or requalify geometry.
`InvalidateGeometry(blockId)` retires only that entry; `Invalidate()` retires all
owned resources for world/resource/shader changes; replacement and `Dispose()`
also retire them. Missing default geometry releases the old entry. Unsupported
targets never expose the previous descriptor, but may retain valid cached geometry.
Phase 4 must wire these hooks to actual lifecycle/reload events; no subscriptions
or client-system lifetime integration are claimed here.

Engine mesh arrays remain borrowed and unchanged. The cache clones qualifying CPU
data, uploads its owned copy lazily once, and disposes only its owned GPU/CPU data.
Replacement/disposal are idempotent and clear ownership before release; failed GPU
initialization disposes that failed reference and remains retryable. Acquisition,
qualification, upload, invalidation, and disposal enforce the creating client-main
thread (and the engine main-thread ID when initialized). Tests use synthetic native
cube/slab data plus wedge, alternate-triangulation, multi-element and recessed
meshes with mocked acquisition/upload boundaries. Atlas mocks assert that no
texture loading occurs, including transparent pixels and unavailable bitmaps.
Tests cover actual-mesh
preservation, surface-facing opportunity, selection-box hits, reuse, translation,
invalidation, failures and disposal. They do not prove live asset support, shader
compatibility, appearance, ordering, or rendering cost.

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
