# Build brush snapping indicator proposal

Status: Approved. Phases 1-6 are complete, with installed-game smoke verification
confirmed and approved by the user on 2026-10-29 and distributable verification
accepted by the independent completion audit. On 2026-10-29 the user accepted
the implemented appearance/readability/performance and explicitly directed Phase 6
closure while skipping outstanding qualification requirements. The independent
closure audit accepted this revised contract. These checks are
waived, not passed; no comprehensive geometry or measured-performance claim is made.

## Purpose

Show which directions the build brush is snapping toward by drawing a mesh-anchored indicator over the existing world block under the crosshair. The indicator should make the relationship between the aimed-at surface and the placement preview easier to understand.

The world block is the agreed anchor. The placement preview remains at its resolved destination and is not the surface used for this overlay.

## Proposed approach

Render the targeted world block's tessellated mesh a second time with a small custom shader. Use interpolated block-local vertex positions to anchor procedural markings to the mesh. Pass the selected face, snapping directions, and presentation settings as shader inputs.

The mesh supplies the surface on which markings appear. The brush supplies their meaning. Mesh geometry alone does not describe the active snapping mode or identify semantic snapping edges.

This approach avoids dependence on the block's texture UV layout. The overlay requires no authored indicator texture for each supported block.

## Existing behavior that defines the indicator

- [BrushSnapping](../VanillaBuildingExpanded/src/BuildBrush/BrushSnapping.cs) projects the raycast hit into two coordinates determined by the selected face's axis. Each coordinate selects a negative, centered, or positive direction using a threshold of `0.15` around the block-cell center. For ordinary cell-local coordinates, the boundaries are `0.35` and `0.65`.
- Face-local U/V directions are converted to world-space offsets. Horizontal snapping permits X/Z movement; vertical snapping permits Y movement. On top/bottom faces, both U and V are horizontal.
- Face-local W supplies outward placement when no permitted U/V movement exists, provided W satisfies the selected restriction. The explicit face-offset flag includes W independently of the restriction, without doubling it. `None` resolves to ordinary face-adjacent placement.
- [BrushPlacementValidation](../VanillaBuildingExpanded/src/BuildBrush/Placement/BrushPlacementValidation.cs) validates the resolved candidate once. A rejected candidate remains invalid without an unrestricted fallback.
- [BuildBrushInstance](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs) stores a resolved selection whose position has already been replaced with the placement destination. The overlay's world anchor must use the original target selection instead.

The proposal preserves these placement rules. Rendering markers against mesh bounds must not silently redefine snapping around those bounds.

## Recommended visible behavior

Show the indicator only while local build mode is enabled, the held block is eligible for brush placement, and there is a current world-block target with supported geometry.

Draw directional bands or chevrons on the selected-facing part of the target mesh. Emphasize the negative or positive direction for each enabled snapping axis. A centered coordinate contributes no directional marking; disabling an axis removes its directional marking. With both axes active, the two markings may combine into a corner cue.

Use the same face-coordinate convention as `BrushSnapping`, including opposite faces and top/bottom faces. A marker must communicate the world-space direction used by placement even when the camera orientation changes.

The visual describes the resolved tangential snap. Valid snapping positions use cyan; invalid snapping positions retain the same directional markings in red. This user-directed behavior supersedes the earlier invalid-placement suppression. Rejected placement remains invalid, and its applied directions remain zero; rendering uses the resolved candidate directions without retrying validation. When resolution uses only W, suppress the tangential indicators. No directional markings are shown for `None`, or when neither U nor V contributes permitted movement.

These rules intentionally leave face-normal offset without its own glyph. It changes placement depth while the tangential markings continue to describe horizontal and vertical snapping. A separate face-normal glyph can be considered if visual evaluation shows that it is needed.

Keep the overlay attached to the target while the brush preview moves to its destination. Clear it when the target is lost, mode is disabled, the held item becomes unsupported, or the player leaves the world. It should not take input or introduce another control.

Validity colors are cyan for valid placement and red for invalid placement. The
user accepted the implemented bands, width, and opacity on 2026-10-29; further
candidate comparison is waived under the Phase 6 closure decision.

## Shader and drawing model

The proposed shader inputs are:

| Input | Purpose |
| --- | --- |
| Target mesh vertex positions and triangle indices | Supply the overlay surface. |
| Target transform and camera matrices | Place that surface at the original world target. |
| Selected face and in-plane axes | Orient the markings consistently with snapping. |
| Resolved horizontal and vertical directions | Select the directional markings to draw, including rejected candidates. |
| Color, opacity, and width parameters | Control readability. |
| Surface-facing information | Restrict markings to appropriate parts of the mesh. This may come from mesh attributes or triangle derivatives. |

The vertex shader preserves block-local coordinates for the fragment shader while transforming vertices for drawing. The fragment shader projects those coordinates into the face plane, evaluates procedural marking masks, and discards fragments outside the markings. Screen-space derivatives can soften the marking boundaries and help control their apparent width.

The mesh clips the markings to its surfaces. A simple procedural mask does not automatically find the true boundary of an arbitrary shape, distinguish meaningful edges from internal triangle edges, or guarantee that a narrow or recessed surface intersects a marking. Those limitations must be visible in the supported-geometry decision.

Keep depth testing enabled so intervening geometry occludes the indicator. Use a small, validated depth bias to avoid fighting the target surface, disable depth writes for the overlay, and restore changed render state after drawing. Select a render stage and ordering compatible with the native selection outline, brush preview, and transparency handling. A depth bias alone does not establish ordering over other overlays.

The indicator intentionally covers the target mesh even where its material texture
has transparent holes. Texture alpha does not clip the indicator or determine
target eligibility. Do not scan texture bitmaps or add material-alpha sampling to
the indicator shader. The procedural marking mask and the mesh geometry define
coverage; ordinary depth testing still handles intervening geometry. This
user-approved clarification supersedes the earlier visible-silhouette requirement.

## Mesh acquisition and support boundary

The engine's `ITesselatorManager.GetDefaultBlockMesh` does not promise the geometry
rendered at a particular world position. Following the user's implementation
correction, acquire target-instance geometry from native world tessellation instead
of qualifying default meshes through a block-class/behavior whitelist.
`GetDefaultBlockMeshRef` is an inventory mesh, not a world-instance source.

Use the target's actual terrain triangles and baked world transforms, regardless
of its class, behaviors, snow, shape variants, or declared draw type. Cubes and
non-cubic shapes should both be evaluated. Non-cubic support requires that the
markings remain meaningful under existing block-cell snapping rules; matching the
shape alone is insufficient.

Let native world tessellation resolve position-dependent variants, block entities,
chiseled geometry, and custom tessellation. Do not reject those categories before
acquisition. Texture cutouts and material transparency do not disqualify a mesh.
GPU-only/custom renderers that emit no terrain geometry, and material-shader
deformations not represented by CPU positions, remain concrete acquisition/fidelity
limitations rather than a class blacklist. The mini-dimension tessellator produces
preview geometry and does not establish exact world-target coverage.

When accurate geometry or meaningful markings cannot be supplied, omit the overlay for that target. Do not substitute a cube that implies unsupported shape fidelity. Unqualified categories remain outside the initial support claim.

## Ownership and update behavior

Use a client-owned overlay renderer associated with the local brush session. The client system should compose and retire it; target-mesh handling and shader drawing should have their own clearly named responsibilities. The server preview entity renderer remains responsible for the placement preview.

Supply a coherent presentation snapshot containing the original target identity, face, and the snapping outcome from the corresponding local placement resolution. Do not combine a newly aimed-at face with a previous target's resolved result. A newly observed target should replace or clear stale presentation until its matching result is available.

The placement resolver remains the owner of the applied outcome. Its result should distinguish requested snapping, accepted snapping, ordinary fallback, and invalid placement sufficiently for presentation, even when candidate positions coincide. This is a proposed addition to local result reporting, not a new placement rule or network requirement. Rendering must not invoke native placement validation again merely to determine its appearance; those checks can have side effects.

Cache mesh resources while their geometry remains valid. Changing the face, snapping mode, or hit region should update presentation inputs without retessellating. Moving between instances with identical supported geometry may reuse that geometry and update its transform. Geometry changes, world/resource lifecycle events, and shader reloads must invalidate the relevant resources. Rebuilds and uploads should occur only when required and on the appropriate engine thread.

Treat engine-cached mesh data as borrowed. Any mutation requires an owned copy; only resources owned by the overlay may be disposed. Release owned GPU resources and subscriptions on world departure and system disposal, and replace shader resources safely after reload or compilation failure. A failed overlay should leave ordinary brush operation available.

## Scope

This proposal adds local visual feedback using the existing snapping and placement results. It does not change snapping thresholds, selection raycasting, placement destinations, native validation, preview synchronization, or network packets.

Automatic discovery of arbitrary mesh edges, snapping to custom shape boundaries, additional input modes, and universal support for custom block renderers are outside the proposal.

## Evaluation criteria

The design is acceptable when supported targets demonstrate all of the following:

- Markings remain attached to the existing world block as the camera, target face, and preview destination change.
- All six faces agree with the existing snapping directions and center thresholds, including horizontal-only, vertical-only, combined, and `None` modes.
- Ordinary placement fallback shows no tangential marking. Invalid tangential placement shows red, never the successful cyan indication.
- Non-cubic supported shapes carry readable markings without implying shape-based placement rules.
- Unsupported targets omit the overlay without affecting selection or placement.
- The native selection outline and preview remain readable; markings do not flicker against the surface or show through intervening blocks.
- Repeated face/hit-region changes do not rebuild geometry, and target changes do not retain stale indicators.
- Build-mode transitions, snapping-mode changes, item changes, death, world departure, resource reload, and shader failure leave no stale overlay or obsolete or leaked owned resources. Snapping-mode changes may retain valid cached geometry; resource invalidation and release follow Ownership and update behavior.

Focused automated checks can establish direction mapping, visibility decisions,
coherent result reporting, and cache invalidation. Shader compilation can establish
shader compatibility. In-game evaluation is needed to establish supported shape
readability and measured rendering cost; automated results cannot substitute for it.
The user's explicit Phase 6 closure decision waives the remaining structured
visual/interaction, named geometry, comparative timing, and live cache evidence
requirements. Prior runtime approval and current visual acceptance are retained,
but no comprehensive geometry qualification or measured rendering cost is claimed.

## Decision

The mesh-projected directional-overlay approach and implemented appearance are
user-approved, including the recommended fallback behavior and native-mesh support
boundary. Outstanding Phase 6 qualification was explicitly waived, not implicitly
satisfied. This document does not define an implementation checklist; the linked
implementation plan records verification and the user-authorized closure.
