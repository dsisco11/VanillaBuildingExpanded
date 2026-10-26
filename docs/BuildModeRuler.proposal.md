# Build-mode ruler proposal

Status: Proposed. No ruler functionality is implemented by this document.

## Purpose and agreed behavior

Add a client-only ruler to the existing build-mode panel. It measures an axis-aligned volume between two selected world blocks, independently of the held item, brush preview, snapping mode, and placement eligibility.

The requested controls are:

- A **Ruler** section showing only the visibility checkbox, endpoint-setting buttons, and X/Y/Z sizes, in that order.
- **Set start** and **Set end** buttons that arm a one-shot world-block selection.
- A **Show ruler** checkbox controlling the in-world wireframe.
- A custom `IRenderer` registered with the client render pipeline while the ruler is eligible to be shown.

The following decisions were confirmed during proposal preparation:

- Bounds include both selected block cells, rather than describing a line between block centers.
- Turning build mode off hides the ruler and cancels endpoint picking.
- Endpoints persist in a client mod config file, with separate entries for each world/server world.
- Terrain occludes the wireframe; it is not an always-visible, through-wall overlay.

The remaining details below are recommendations for approval with this proposal.

## Existing integration points

- [BuildModeHud](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs) already composes a non-focusing panel with collapsible Room and Controls sections. Its current mouse routing deliberately forwards input only to section collapse buttons. Adding native controls alone will therefore not make the ruler interactive.
- [CollapsibleSection](../VanillaBuildingExpanded/src/UI/CollapsibleSection.cs) supplies the section heading, measurements, collapse button, and deferred collapse processing.
- [GuiElementTable input routing](../VanillaBuildingExpanded/src/UI/GuiElementTable.Input.cs) supports opt-in child pointer input, captures, release outside bounds, and cancellation. Reuse it for the ruler controls instead of introducing a second table-input mechanism.
- [BuildModeIndicatorBinding](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeIndicatorBinding.cs) owns the HUD and observes local mode independently of brush eligibility. Extend this composition path to supply ruler presentation and commands without putting ruler state in the HUD.
- [BuildBrushSystem_Client](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Client.cs) owns input and world lifecycle callbacks. Its existing `InWorldAction` handler owns supported right-click placement; ruler picking must be routed before that placement path when applicable.
- [BuildBrushWheelInput](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushWheelInput.cs) demonstrates a disposable client event adapter that leaves already-handled and non-gameplay input alone.
- [VbeConfig](../VanillaBuildingExpanded/src/Config/VbeConfig.cs) uses the engine's mod config API. Ruler coordinates should use a separate client-only file, rather than extending brush networking or mixing per-world client data with existing shared settings.

The [project backlog](../VanillaBuildingExpanded/src/docs/project.todo) is context, not an implementation checklist for this proposal. This feature does not implement the broader design-mode work.

## Ruler section

Insert an expanded-by-default **Ruler** section between Room and Controls, following the existing section style and GUI scaling rules.

The section content is limited to these three rows:

```text
[ ] Show ruler
[Set Start] [Set end]
Size: <x>, <y>, <z>
```

For example, a complete ruler displays `Size: 5, 3, 4`, in X/Y/Z order and measured in blocks. Do not add endpoint coordinates, minimum/maximum bounds, dimension information, status rows, or extra actions to the section.

Initially display `Size: -, -, -` until both endpoints exist. An incomplete ruler does not draw a speculative box. The checkbox can still be enabled before endpoints exist; use the existing in-game notification mechanism for explanatory feedback such as `Set both endpoints to display the ruler`, without adding section content.

While picking, use an in-game notification for an unambiguous prompt: `Select start: left-click a world block. Right-click to cancel`, or its end equivalent. Highlight the armed button without changing the three-row layout; clicking it again cancels, and clicking the other button switches the pending endpoint. No endpoint changes until a valid world click succeeds.

Collapsing the section only hides its content. It must not clear bounds, cancel picking, or change wireframe visibility. Picking feedback remains outside the panel, so it does not depend on the section being expanded.

All labels, prompts, size formatting, and error messages use localization entries in [en.json](../VanillaBuildingExpanded/assets/vanillabuildingexpanded/lang/en.json). Account for large sizes and translated strings without clipping or overlapping adjacent sections.

## Bounds contract

Store each endpoint as a copied block position with explicit X, local Y, Z, and dimension. Never retain the engine's mutable selection position by reference.

Start is the user-selected origin, not necessarily the minimum corner. Preserve start/end identity even when the end is lower than the start on an axis. Minimum/maximum bounds remain internal state, not additional UI readouts.

For endpoints in the same dimension, calculate independently on each axis:

```text
minCell = min(start, end)
maxCell = max(start, end)
size = maxCell - minCell + 1
renderMin = minCell
renderMaxExclusive = maxCell + 1
```

Thus `(10, 65, 20)` through `(14, 67, 23)` measures `5 x 3 x 4` blocks. Selecting the same block twice measures `1 x 1 x 1` and renders that cell's complete outline. Reversed endpoints produce the same box and sizes without swapping the stored origin.

Use wide integer arithmetic before subtraction/addition and double-precision world coordinates for render bounds. Do not mix dimension-encoded internal Y with local Y. The wireframe follows block-cell bounds even for slabs, stairs, and chiseled blocks; it does not trace their mesh or collision shape.

When selecting an endpoint in a different dimension from the retained opposite endpoint, begin a new ruler in the selected dimension: set the requested endpoint and clear the incompatible opposite endpoint in the same saved update. Notify the user to set the other endpoint. This permits relocation using only the two existing buttons, without adding a Clear ruler action or constructing cross-dimension bounds.

## Endpoint-picking interaction

Use a small explicit state machine: `Idle`, `PickingStart`, and `PickingEnd`.

1. A panel button arms the requested endpoint only while local build mode is enabled and the player is alive.
2. The button's own mouse gesture is fully handled by the GUI. It cannot also choose an endpoint.
3. Use the existing free-cursor workflow to operate the panel; do not make the HUD focusable or forcibly grab the cursor. World picking starts only after the player returns to grabbed-mouse gameplay.
4. On a fresh left-button press during gameplay, read the player's current world `BlockSelection` and copy its position. Do not use the brush's resolved placement destination, add a face-adjacent offset, or snap the result.
5. On success, replace only the requested endpoint, return to `Idle`, refresh the readout/renderer, and save the changed entry.
6. With no valid block target, consume the picking click, leave the prior endpoint unchanged, keep picking armed, and show `Aim at a block`. Entity targets and sky clicks do not become endpoints.
7. Right-click during grabbed-mouse picking cancels without placement or item use. Mode-off, death, world departure, and disposal also cancel. Opening a dialog or releasing the cursor pauses world capture; it does not choose a block or consume dialog input.

Consume the entire owned left-button gesture, including held/repeated action and release, even though a successful press returns the picker to `Idle`. This prevents a measurement click from becoming mining, an attack, or another held-item action immediately after selection. Apply the same gesture ownership rule to right-click cancellation.

The input adapter should use handled client mouse events for physical gesture capture and the existing `InWorldAction` path where needed to prevent downstream world actions. Respect already-handled input and GUI priority. Verify the supported engine's event ordering rather than assuming suppression at one layer blocks every native action. Keep ownership until release, but remove it safely on focus loss or teardown.

Ordinary left-click and right-click behavior remains unchanged in `Idle`. While picking, right-click cancellation takes precedence over `TryPlaceBlock`; other build controls keep their existing behavior. Ruler interaction does not require an eligible block in the active slot and never sends placement packets.

Here, "a block anywhere in the world" means a block available to the normal player world-selection raycast, not just the brush footprint. Unlimited-distance targeting, selecting unloaded terrain, and a separate long-range raycaster are outside this first version.

## Client state and persistence

Use one client-owned ruler controller for the active world session. Its state contains nullable endpoints, the visibility preference, pending endpoint mode, and derived presentation data. GPU resources, GUI controls, and pending mouse gestures are not saved.

Persist endpoints through `LoadModConfig` / `StoreModConfig` in a dedicated `vanillabuildingexpanded-rulers.json` file:

- A schema version and a dictionary keyed by the engine's globally unique `World.SavegameIdentifier`.
- Each entry contains nullable `start` and `end` records with X, local Y, Z, and dimension.
- The savegame identifier scopes both single-player saves and multiplayer server worlds. Do not use a display name, seed, or server address alone; a replacement world on the same server must not inherit the previous world's ruler.
- Load only after the current world identity is available. If it is unavailable, report the persistence problem and keep this session's ruler in memory; do not substitute a global entry.
- Save after successful endpoint changes, including clearing an incompatible opposite endpoint during dimension relocation, not each frame or on entering pick mode. Preserve other worlds' entries.
- Missing files are normal first use. Malformed files, unsupported versions, invalid coordinate records, and cross-dimension pairs require explicit logging and user feedback. Do not overwrite an unreadable or unsupported file with empty defaults.
- On a write failure, retain the in-memory edit, mark it unsaved, and report that it will not survive restart. Retry on the next explicit edit or orderly world exit if still dirty; do not report success until writing succeeds.

Validate saved coordinates against the active world's coordinate/dimension rules before use. A valid saved box does not require its endpoint blocks to still exist or their chunks to be loaded: the ruler measures fixed coordinates, not tracked block objects.

Recommended initial visibility is unchecked on each world join. Only endpoints persist in this version. During the same session, turning build mode off preserves both endpoints and the checkbox preference; re-enabling mode restores the chosen visibility. Loading coordinates never enables build mode automatically.

Moving to another dimension cancels picking and hides a ruler belonging to the previous dimension without deleting its endpoints. Keep the saved size readout unchanged and report the dimension mismatch through an in-game notification, not another section row. Returning restores rendering if mode and visibility remain enabled, unless the user has selected an endpoint to begin a new ruler in the other dimension.

## Wireframe renderer

Introduce `RulerRenderer : IRenderer`, owned by the client ruler composition rather than by a server preview entity. Register exactly one instance only while all of these are true:

```text
build mode enabled
AND Show ruler checked
AND both endpoints valid and in the same dimension
AND player currently in that dimension
AND client world/session available
```

Unregister when any condition stops being true, using the same render stage used for registration. Endpoint edits update the existing renderer's bounds instead of registering duplicates. Keep at most one reusable mesh per session, and dispose it on world departure/system disposal. Repeated hide/show, GUI rebuilds, and teardown must be safe and idempotent.

Render the 12 edges of the axis-aligned box with the engine's wireframe shader and a reusable unit-cube line mesh. The API's `WireframeCube` / `LineMeshUtil` are prior art; a custom renderer may reuse those facilities without inventing a shader or tessellating world blocks.

Use camera-relative translation computed in double precision before converting to the renderer's float matrix. Scale the unit cube by the inclusive X/Y/Z sizes. Do not allocate meshes, sample blocks, or rebuild geometry every frame.

Use an opaque-stage overlay order after terrain, with depth testing enabled, depth writes disabled, and a restrained contrasting color/line width. The final order must be verified with the brush preview, native selection outline, and transparent terrain. Transparent material occlusion follows the engine's depth pipeline and needs explicit in-game qualification.

Use a small, tested outward visual expansion or appropriate depth bias to avoid coplanar edge flicker. This affects drawing only, never measured coordinates. Restore shader, line width, depth, and blend state to the render-stage contract after drawing, including on failure. Do not assume the engine's wireframe helper restores arbitrary prior state.

`RenderRange` is not a reliable culling mechanism in the API contract. Do not cull solely by distance to one endpoint: a large box can intersect the view while both endpoints are far away. Explicit culling is unnecessary for the initial fixed-size line mesh; if added, base it on the whole box.

GPU creation or rendering setup failures must be logged and surfaced through an in-game notification as an unavailable wireframe, not silently presented as a successfully visible ruler. Keep the size readout usable.

## Implementation boundaries

Recommended responsibilities, with names provisional:

| Component | Responsibility |
| --- | --- |
| `RulerController` and small state/config records | Endpoint commands, normalized bounds, pick mode, visibility, change notifications, and per-world config coordination. Keep bounds logic testable without GUI/GPU dependencies. |
| Client input adapter | Capture physical click gestures, sample the original world selection, cancel picking, and clean up subscriptions. |
| `BuildModeHud.Ruler.cs` | Compose/localize ruler content and issue commands; integrate existing native button/checkbox controls and table pointer routing. |
| `RulerRenderer` | Wireframe mesh ownership, camera-relative drawing, and render-state discipline. |
| Existing client system / indicator binding | Supply shared controller state to the HUD, observe local mode/lifecycle, and coordinate renderer eligibility and disposal. |

Use the existing table pointer opt-in for interactive cells. Extend `ShouldReceiveMouseEvents` and mouse down/up/move routing to recognize actual ruler control hits and outstanding captures, including releases outside their bounds. Passive readouts, section headings, and unused panel space remain non-interactive. Cancel table captures before collapse, hide, recompose, or disposal. Do not route the entire panel through native dialog defaults, which would consume gameplay input over passive content.

No server controller, brush placement packet, entity registration, world block, inventory item, or claim permission changes are needed. Do not attach ruler persistence to the synchronized brush state. This is one local ruler per saved world, not a shared multiplayer measuring system.

## Verification and acceptance

Add focused tests beside the existing [build-brush tests](../VanillaBuildingExpanded.Tests/BuildBrush) and [HUD control tests](../VanillaBuildingExpanded.Tests/UI/BuildModeHudControlsTests.cs).

1. **Bounds:** Same-cell selection yields `1 x 1 x 1`; the example above yields `5 x 3 x 4`; reversed and negative coordinates normalize correctly; arithmetic does not overflow; stored endpoints cannot change when the original selection position is mutated.
2. **Picking:** Either endpoint can be set first; changing one preserves the other in the same dimension; switching/canceling is deterministic; empty targets report feedback without mutation; selecting in another dimension starts a new partial ruler and persists clearing the incompatible opposite endpoint.
3. **Input safety:** Button activation cannot select a world block. Valid and invalid picking clicks never mine, attack, or use items. Holding the successful click does not begin mining after the picker becomes idle. Right-click cancellation never places a block. Already-handled events and ordinary idle input retain their existing behavior.
4. **GUI:** The expanded section contains exactly the checkbox, the two buttons, and the size readout in the specified three-row order, with no extra readouts/actions. Incomplete sizes display `Size: -, -, -`. Buttons and checkbox work with a free cursor, including drag/release outside controls. The size readout stays passive. Collapse/reopen, hidden HUD, GUI-scale/locale changes, and recomposition do not leave captures or issue duplicate commands. Active-pick feedback uses in-game notifications and remains independent of collapse state.
5. **Persistence:** Round-trip complete and partial rulers, negative coordinates, and dimensions. Isolate two worlds and two server worlds, including a replacement save at the same address. Missing, malformed, unsupported, or unwritable config produces the documented behavior without destroying other entries. Reloaded endpoints match exactly after game restart.
6. **Lifecycle:** Mode off/death hides and cancels without deleting endpoints. Re-enable restores the session preference. Dimension changes hide/restore correctly. World leave/rejoin and repeated disposal leave no stale input callbacks, renderer registrations, or mesh references.
7. **Rendering:** Mockable registration tests prove zero renderers while ineligible, exactly one while eligible, and balanced unregistration/disposal. Verify the exact min and exclusive max passed to drawing, not merely that a render method ran.
8. **In-game smoke test:** Confirm native gameplay suppression, normal selection reach, terrain occlusion, edge stability, large/negative coordinates, camera-inside-box views, brush/selection overlap, and transparent-material behavior in the supported game build. These checks cannot be replaced by mocked renderer tests.

Run the smallest existing test selections covering the changed behavior and the normal build. The feature is complete only when these tests pass and the in-game input/rendering checks are recorded.

## Out of scope

Multiple rulers per world, distance lines/diagonals, axis tick marks, in-world text labels, draggable handles, numerical coordinate editing, ruler-driven placement restrictions, remote sharing, custom long-range targeting, and schematic/design-mode integration are excluded from the initial implementation.
