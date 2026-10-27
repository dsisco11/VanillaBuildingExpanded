# Reusable in-world gizmos and ruler manipulation

Status: Proposal with interaction decisions confirmed below. This document does not implement the system. Unconfirmed API/implementation details remain recommendations.

## Purpose and confirmed requirements

Replace the ruler's button-driven endpoint picking with direct interaction with controls rendered in the world. Build the interaction as reusable infrastructure, not ruler-specific rendering or mouse handlers.

The requested design includes:

- A reusable `Gizmo` base class/interface representing an interactive in-world control.
- A generic `Gizmo<TValue>` variant whose type describes its input/current value, with an `OnValueChanged` event for downstream owners.
- A 3D positioning gizmo that marks a position and provides draggable X/Y/Z axis arrows.
- A central icon/handle for movement without selecting an axis.
- Separate positioning-gizmo instances associated with the ruler's Start and End positions.

The ruler remains client-only. Gizmos are controls, not blocks, inventory items or synchronized world entities.

### Confirmed interaction decisions

- Support both the free mouse cursor and mouse-look reticle.
- Dragging the center follows the world block under the active pointer/reticle.
- Center dragging and first-use/missing-endpoint creation can target rendered terrain up to the camera far plane, without a player-reach cap.
- Ruler endpoints snap to whole blocks throughout dragging.
- One initial world click creates both endpoints on the selected block.
- Gizmo handles are overlay controls; the ruler box remains depth-tested.
- Remove Set Start/Set end from the panel, leaving Show ruler and Size.
- Anchor Start at offset `(0, 0, 0)` within its block and End at offset `(1, 1, 1)` within its block. Do not introduce the previously suggested endpoint-selection/cycling controls.

## Current integration points

- [Ruler state](../VanillaBuildingExpanded/src/BuildBrush/RulerState.cs) stores copied block coordinates with explicit local Y and dimension. Start always represents the per-axis minimum cell and End the per-axis maximum cell of the inclusive bounds.
- [Ruler controller](../VanillaBuildingExpanded/src/BuildBrush/RulerController.cs) currently exposes one-shot picking commands. It needs direct endpoint editing and preview/commit/cancel operations instead of forcing a drag through that state machine.
- [Ruler session](../VanillaBuildingExpanded/src/BuildBrush/RulerGizmoSession.cs) replaces the former one-shot ruler input owner. Retain one path for the ruler; do not layer a second independently capturing mouse adapter over it.
- [Ruler HUD](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.Ruler.cs) currently owns Set Start/Set end buttons. Their removal is an explicitly approved UI change for this replacement.
- [Client system](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Client.cs) owns the world session and routes ruler input before brush placement. Retain one explicit composition/ownership path.
- [Ruler rendering](../VanillaBuildingExpanded/src/BuildBrush/RulerRenderBinding.cs) already separates wireframe eligibility from HUD composition. Keep the measurement box independent of gizmo handle rendering.
- [Persistence](../VanillaBuildingExpanded/src/BuildBrush/RulerPersistence.cs) saves endpoint commits through native mod-config APIs. Drag previews must not produce config writes every frame.
- [Architecture documentation](BuildBrushArchitecture.md) describes the existing implementation, not approval of this replacement.

The reported inability to replace an endpoint after initial selection is unresolved. This proposal changes the interaction model; it does not claim that the existing bug has been diagnosed or fixed.

## Proposed component model

### Non-generic Gizmo

Use an abstract `Gizmo : IDisposable` as the common instance contract. Add a separate interface only if a real integration requires it.

The base encapsulates:

- Instance identity, visibility and interaction eligibility.
- Hovered/captured handle identity and interaction lifecycle.
- Rendering and hit-testing entry points using shared world/camera input.
- Begin, update, complete and cancel interaction hooks.
- Idempotent resource/subscription cleanup.

The base does not know ruler endpoints, block-grid rules, mod-config files or brush placement.

A small client-owned gizmo manager coordinates instances, selects a hit, and grants exclusive capture to at most one gizmo/handle. Its input and renderer adapters use the existing client event/render pipelines. An individual gizmo must not independently compete for global mouse events or register a renderer every frame.

### Generic Gizmo<TValue>

Recommended public contract, with names provisional:

```csharp
public abstract class Gizmo<TValue> : Gizmo
{
    public TValue Value { get; set; }
    public event Action<TValue>? OnValueChanged;
}
```

This is an API sketch, not an implementation.

- Construction accepts an initial value.
- The `Value` setter updates the stored value and raises `OnValueChanged` when it actually changes, whether assigned by manipulation or by the owner. No separate `SetValue` method or silent synchronization path is needed.
- Compare values before updating or notifying. Identical owner echoes and drag updates that snap to the same value are no-ops, avoiding duplicate events and feedback loops.
- Base interaction completion/cancellation notifications let owners distinguish a preview from a durable edit without turning every value event into a save.
- Cancellation restores the drag-start value and emits a value change if restoration changes the preview. It never signals a successful commit.
- Before assigning a genuinely different external owner value during a drag, the owner binding cancels that interaction, then assigns the replacement through `Value`. Cancellation must not overwrite the incoming authoritative value. Ordinary drag updates use the same setter without canceling their own interaction.
- Use immutable value types where practical. The generic contract does not promise to detect arbitrary mutation inside reference-type values.

The owner owns application state and persistence. The generic class owns the interactive value, not application-specific side effects.

### PositionGizmo

Introduce a reusable positioning gizmo, provisionally `PositionGizmo : Gizmo<GizmoPosition>`.

Recommend an immutable `GizmoPosition` containing double-precision X/Y/Z and an explicit dimension. Generic positioning supports continuous coordinates; the ruler supplies its grid/validation policy. Do not make every positioning gizmo inherently block-snapped.

Each instance has:

- A center icon/handle at its logical position.
- X, Y and Z arrow shafts/heads in conventional red, green and blue.
- Hover/active feedback that does not depend on color alone.
- Stable screen-sized visual and pick targets, within sensible world-size limits.

The ruler uses distinguishable Start/End center icons, for example a circle and a diamond. Start marks the normalized minimum corner and End the normalized maximum corner.

## Proposed input and dragging

### Activation and capture

Support both the existing free cursor and mouse-look reticle while build mode is enabled. Do not turn the HUD into a focusable editor or force cursor grabbing.

1. Respect earlier handled input, open dialogs and HUD control hits before testing world gizmos.
2. Derive a world-space ray from the actual cursor when it is free, or from the camera reticle in mouse-look. Free-cursor targeting must not substitute the player's center-screen block selection.
3. Hit-test center and arrow shapes, with pixel-aware tolerances matching the visible controls.
4. Choose one deterministic target using the agreed visibility policy, handle priority and hit distance. Give the center handle priority over its own arrow roots.
5. On a fresh left press over an eligible handle, capture that handle and the drag-start value. A press elsewhere retains normal behavior, except an explicitly active first-use creation gesture.
6. During capture, continue tracking outside the handle silhouette. Do not retarget another axis or endpoint halfway through a drag.
7. Release commits once. Right-click or Escape cancels and restores the baseline.

Own the entire captured gesture through release, including late native action callbacks, so dragging cannot mine, attack, place or use an item. Right-click cancellation must also own its gesture. Repeated drags must begin from fresh physical presses; no stale capture/suppression latch may prevent the next edit.

Free-cursor dragging does not engage camera look. Reticle dragging retains camera look so the player can aim at a different block or move the aiming ray along an axis; suppress captured gameplay actions, not the mouse motion needed for aiming. A dialog opening, focus loss, cursor-mode change, build-mode exit, death, dimension change or world departure cancels the interaction. GUI recomposition must not otherwise reset a world gizmo.

### Axis handles

- Keep the other two position components fixed at their drag-start values.
- Compute displacement from the initial pointer/axis relationship, preserving the grab offset rather than jumping the arrow tip onto the cursor.
- Use a stable ray/axis calculation or a suitable axis-containing drag plane fixed at drag start.
- Near-parallel/degenerate camera configurations must not produce huge jumps, NaN or infinity. Reject an unusable drag start with feedback, or retain the last valid value during a temporary degeneracy.
- Apply the instance's agreed snapping and validation before publishing values.

### Center handle

For the ruler, the center handle follows the targeted world block under the active cursor or reticle, up to the camera far plane without a player-reach cap. Apply the same range to first-use and missing-endpoint creation. Target existing loaded scene terrain; do not load or generate terrain for picking. Copy the actual targeted block cell, not a face-adjacent brush placement destination. Update the endpoint and its role-specific corner anchor together.

This can change X/Y/Z together without an axis lock. It is not arbitrary positioning in empty space: sky/entity targets and missing valid terrain retain the last valid value until a block is targeted again. Do not silently switch to a camera-facing plane on a miss.

Keep the reusable positioning type independent of the ruler's block coordinates. Its center-drag calculation may accept an owner-supplied target resolver; a general-purpose plane resolver is a possible future use, not a required extra mode for this implementation.

## Ruler integration and edit transactions

Preserve inclusive block-cell measurements, native per-world endpoint persistence and dimension isolation.

Use role-specific corner anchors in local coordinates:

```text
Start anchor = startCell + (0, 0, 0)
End anchor   = endCell   + (1, 1, 1)
```

The positioning gizmo's value is its anchor position. The ruler binding adds the corresponding offset when synchronizing from an endpoint and subtracts it when converting an edited anchor back to the endpoint cell. For center dragging, a target cell maps directly to that endpoint, then its role-specific anchor is recomputed. Axis dragging moves the anchor in whole-block steps from the drag-start value, preserving integer alignment and correct negative-coordinate behavior.

These are marker offsets, not changes to stored endpoints or inclusive measurements. For the same selected cell, Start marks its minimum corner and End marks its exclusive maximum corner, so the two handles are separated without offsetting the measurement itself. Preserve explicit dimension identity even when an End anchor lies at a local-Y boundary; validate the decoded cell rather than interpreting the anchor as a dimension-encoded block position.

Updated decision (2026-10-26): Start and End always contain the per-axis minimum and maximum cell coordinates, respectively, including previews and restored legacy values. Crossing is allowed: normalize both endpoints, rather than clamping or retaining mixed min/max coordinates. Offsets and marker shapes remain associated with the minimum/maximum roles. This supersedes the earlier selected-endpoint identity preservation decision.

Create one positioning-gizmo instance for each existing endpoint, even for an incomplete ruler. The box still requires both endpoints.

During a drag:

- Capture the original endpoint pair at begin.
- Publish valid snapped preview endpoints to the controller so size and wireframe update live.
- Calculate each preview against the original opposite endpoint, then normalize both corners per axis. The fixed baseline permits crossing and returning without accumulating earlier preview extents; both displayed handles follow their normalized role anchors without losing capture.
- On completion, keep the final preview and issue one endpoint commit when changed. An explicit no-op completion may retry an already dirty save without producing redundant writes.
- On cancellation, restore the baseline, refresh presentation, and do not save the canceled preview.
- Reject invalid coordinates and dimension changes before mutating preview state; retain the last valid position and surface meaningful feedback.

The current `EndpointsCommitted` persistence hook can remain the durable-save boundary, but the controller needs a transaction-aware editing API. Do not save from the per-update `OnValueChanged` handler. A failed final save retains the committed in-memory edit and uses the existing dirty retry/notification behavior.

Orderly departure cancels an active preview first, then retries any previously committed dirty edit while world identity is available. Rejoining restores committed endpoints, not a canceled drag preview.

### First use, incomplete rulers and relocation

Dragging existing handles does not solve creation of a ruler with no saved endpoints.

Checking Show ruler with no endpoints requests a single in-world creation click. A valid block click creates both endpoints at that cell, then exposes Start at its `(0, 0, 0)` corner and End at its `(1, 1, 1)` corner. Consume the full creation gesture; it must not also start a drag or gameplay action. Invalid targets keep creation pending with a notice.

For restored partial rulers, show the existing marker and offer the same explicit world creation gesture for the missing endpoint. Do not invent a distant endpoint or silently replace a saved partial ruler.

In another dimension, retained endpoints stay stored and hidden, without automatic overwrite on travel or checkbox toggling. A new cross-dimension relocation workflow is not part of this proposal; no extra selection/relocation key or panel action is introduced.

Display both endpoint gizmos with their own arrows. The approved corner offsets replace the suggested selected-endpoint-only/cycling scheme. Keep deterministic hit arbitration for intersecting/projected handles as a generic manager requirement; do not add a ruler-specific selection UI or alter measured coordinates. Corner offsets separate same-cell anchors, but are not a guarantee against every overlap after endpoints are moved or projected onto the screen.

## Visibility, rendering and lifecycle

Confirmed distinction:

- Keep the ruler box depth-tested and terrain-occluded.
- Render/pick gizmo handles as an in-world overlay while manipulation is enabled, so a corner handle obscured by adjacent solid blocks remains accessible.

The overlay policy keeps handles accessible even when their corner is obscured by adjacent terrain. Explicitly hidden or disabled gizmos must not retain invisible hit targets.

Share reusable arrow/icon meshes and rendering services where practical. Use camera-relative double subtraction before float matrices, explicit dimension conversion only at the rendering boundary, and balanced shader/depth/blend/line state cleanup. Avoid per-frame geometry uploads, GPU readbacks for picking, world-entity spawning or block tessellation.

For the confirmed block-following center drag, qualify the supported engine's cursor-ray/world-hit facilities before implementation. The existing center-screen `BlockSelection` alone is not sufficient for free-cursor hit testing. Reticle targeting may reuse the native selection where it represents the required ray and target semantics.

The session owner disposes the manager, instances, input subscriptions and GPU resources on departure/system disposal. Renderer/input failures must be logged and surfaced, cancel active edits safely, and leave ruler size/persistence usable. A render failure must not leave invisible interactive handles.

## Confirmed panel change

Replace the two endpoint buttons with entirely in-world editing, retaining only:

```text
[ ] Show ruler
Size: <x>, <y>, <z>
```

This intentionally replaces the previous three-row layout. Show ruler controls both the eligible box and endpoint handles, with direct manipulation available through either supported pointer mode; no additional edit-mode button is proposed.

## Verification

Use existing test infrastructure and add focused cases for:

1. Property-setter value events for both owner assignments and manipulation, duplicate suppression, external replacement, and identical callback echoes without reentrant edits.
2. Deterministic hit priority, center/arrow tolerance, both endpoints' corner offsets, overlapping/projected handles, one capture, drag outside hit bounds and correct free-cursor/reticle rays at multiple GUI scales/aspect ratios.
3. Independent X/Y/Z motion, block-following center behavior and invalid targets, center/creation targets beyond player reach through the camera far plane, preserved axis grab offsets, camera-parallel degeneracy, negative coordinates, double precision, block snapping and offset-to-cell round trips at dimension boundaries.
4. Preview updates, one durable commit, cancel rollback, dirty retries and no per-frame config writes.
5. Repeated Start and End edits after successful completion, including many consecutive drags and cancel/retry cycles, with actual manager/input/controller routing.
6. No mining/attack/use/placement during captured/creation/cancellation gestures; ordinary input outside handles and already-handled GUI input remain intact.
7. Mode/death/focus/dialog/dimension/world transitions, partial/restored rulers, no stale callbacks/resources, and size usability when gizmos fail.

Separately qualify live-game pointer routing, camera control, handle readability/picking, coincident endpoints, terrain/transparent-material behavior and restart persistence. The earlier ruler live-check waiver is not evidence that this new interaction works, and does not automatically waive validation of a future implementation.

## Decision record and remaining implementation qualification

| Decision | Confirmed behavior |
| --- | --- |
| Interaction mode | Both existing free cursor and mouse-look reticle. |
| Center movement | Follow the targeted world block; no plane fallback on a miss. |
| Block-target range | Rendered terrain up to the camera far plane, without a player-reach cap; applies to center dragging and first-use/missing-endpoint creation. |
| Ruler snapping | Whole block-cell steps throughout dragging; reusable positioning is not inherently block-only. |
| Initial creation | One world click creates a one-cell ruler; restored partial rulers complete only the missing endpoint. |
| Gizmo occlusion | Overlay handles, depth-tested measurement box. |
| Panel | Remove Set Start/Set end; keep Show ruler and Size. |
| Marker anchors | Start block offset `(0, 0, 0)`; End block offset `(1, 1, 1)`; both gizmos expose arrows without a cycle/select control. |

Before implementation, qualify the native free-cursor world raycast, reticle camera/input ordering, and stable axis-drag math against the supported engine. Final handle artwork, screen-space sizing and hit tolerances are implementation recommendations to test, not additional confirmed feature requirements. No further user decision is currently required to retain this proposal.

## Scope boundaries

This proposal covers reusable gizmo infrastructure, one positioning-gizmo type and ruler integration. Rotation/scaling gizmos, multi-object editing, undo history, numeric coordinate editors, multiplayer sharing and unrelated brush compatibility smoke tests are outside the initial scope.

The interaction decisions above are recorded; implementation still requires a separate request/checklist. No production code is changed by this proposal.
