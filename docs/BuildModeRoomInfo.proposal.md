# Build-mode room information proposal

Status: Implemented with automated coverage. Separate in-game visual, input, and live registry-invalidation acceptance remains pending.

## Purpose

Add a collapsible **Room** section to the [build-mode HUD](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs). Give builders a simple, readable summary of what their room qualifies for, without exposing detection internals.

The initial section describes the room at the player's block position in the real world. It does not inspect the brush mini-dimension or predict the effects of unplaced blocks.

## Proposed information

| Row | Meaning |
| --- | --- |
| Type | A single line of colored tags listing all current qualifications: `Room`, `Shelter`, `Cellar`, and/or `Greenhouse`. |
| Dimensions | Inclusive detected bounds: `max - min + 1` on each axis. |
| Sunlight | Sunlight-classified column samples as a percentage of all classified column samples. A useful indication of greenhouse suitability, not literal roof transparency. |
| Cooling walls | A plain-language summary of the boundary's cooling-material contribution, displayed as `Cooling walls: N%`. Do not label this percentage as actual heat retained: the room API does not supply that measurement. |

Example layout (brackets represent styled tags, not literal text):

```text
Room
  Type:        [Room] [Shelter] [Greenhouse]
  Dimensions:  7 x 4 x 6
  Sunlight:    65%
  Cooling walls: 80%
```

Do not display room-cell counts, raw exit counts, raw wall/sample counts, or separate cellar/greenhouse eligibility rows. Keep useful sunlight and cooling-wall summaries, while leaving technical classification rules in this proposal and implementation tests.

Calculate sunlight as `SkylightCount / (SkylightCount + NonSkylightCount)` and cooling-wall composition as `CoolingWallCount / (CoolingWallCount + NonCoolingWallCount)`, formatted as percentages. A zero denominator means `Unavailable`, not zero percent. Cooling-wall composition helps explain cellar performance; it is neither a preservation bonus nor a measurement of stored warmth.

## Qualification tags

Tags are additive, not mutually exclusive. Show every applicable tag on the same `Type:` line, in the stable order `Room`, `Shelter`, `Cellar`, `Greenhouse`.

| Tag | Qualification |
| --- | --- |
| Room | The detected region is enclosed: `ExitCount == 0`. |
| Shelter | The player-temperature shelter condition is met: `ExitCount == 0 || SkylightCount < NonSkylightCount`. This can apply to an open, mostly shaded region such as a cave. |
| Cellar | `IsSmallRoom` is true. This indicates the game's cellar-size/enclosure qualification, not guaranteed preservation quality. |
| Greenhouse | The region is enclosed and `SkylightCount > NonSkylightCount`. This indicates room-level greenhouse conditions, not confirmation of every plant's current bonus. |

A space can qualify for both Cellar and Greenhouse; do not choose one at the expense of the other. The Room tag remains present alongside specialized qualifications.

Every enclosed Room also qualifies as Shelter, but Shelter does not imply Room. An open cave may show only `[Shelter]`. Shelter indicates the game's binary protection from the normal ambient-temperature and wind contribution to player body-temperature change, not a heat-retention percentage or a guarantee of rain cover.

Draw each tag as a compact filled rounded box with a visible border and centered text, visually similar to the base game's keybind keycaps. Use a distinct, consistent tint for each qualification, with readable text and border contrast. Labels must communicate meaning without relying on color alone. Tags are informational, not buttons, and must not capture input. Reuse the HUD's existing keycap drawing approach where suitable.

Size boxes to the measured text ink, with 3 logical units of horizontal padding, 2 of vertical padding, and a 1-unit border. Keep the table's row footprint independent of this compact box height.

The Room table, like the snapping/rotation and Controls tables, uses faint row separators that fade to transparent at both ends. These are table-content dividers, independent of the collapsible section's divider and toggle. Existing row spacing and passive input behavior are unchanged.

Keep the tags on one line by measuring localized labels and allowing the panel to accommodate them. Verify the layout at supported GUI scales.

When no qualifications apply, show plain text `Type: None` and a short explanation, `Not enclosed or too large`. Do not invent a precise leak count or claim the space is definitely outdoors. Omit dimensions, sunlight, and cooling-wall summaries in this state because the detected region may be truncated and its aggregates may not describe the intended room.

Also omit those summaries for Shelter-only regions with nonzero `ExitCount`: their detected bounds may be truncated even though the player-temperature shelter condition is met.

When `AnyChunkUnloaded != 0`, replace the qualifications with `Type: Unavailable` and `Room data incomplete`, and omit dimensions, sunlight, and cooling-wall summaries. Never display stale or provisional success tags.

## Base-game rules and limitations

These findings are based on the installed VSEssentials 1.22.7 implementation of `RoomRegistry`, `Room`, and `InWorldContainer.GetPerishRate`, together with the local survival source.

- Detection is bounded to 14 blocks per axis. Nonzero `ExitCount` can result from unsealed continuations or detection limits; it is not a count of doors, holes, or leaks.
- `IsSmallRoom` requires enclosure and dimensions no larger than 7 on every axis. Alternatively, one axis may reach 9 while the other two remain at most 7, provided actual room-cell count is at most 150.
- Sunlight counts classify sampled X/Z columns, not glass blocks or roof coverage.
- Cooling and non-cooling counts accumulate heat-retention contributions, not necessarily unique wall blocks. The default [block retention logic](../../vsapi/Common/Collectible/Block/Block.cs) treats solid ore, stone, soil, and ceramic faces as cooling; behaviors may override retention.
- The [plant growth system](../../vssurvivalmod/BlockEntity/BlockEntityFastForwardGrowth.cs) additionally checks rain exposure and caches plant room state. Its greenhouse adjustment is +5 degrees to growth temperature, not a universal room-temperature increase.
- Actual food preservation also depends on climate, sunlight at the container, and container-specific behavior. Do not display a universal room temperature or spoilage multiplier.
- The [player-temperature behavior](../../vssurvivalmod/Entity/Behavior/BehaviorBodyTemperature.cs) uses the Shelter condition independently of cellar size and cooling-wall composition. It replaces the ambient/wind contribution with a positive warming term and makes heat-source influence fall off more slowly with distance. Nearby heat and being on fire still affect the player; rain exposure is checked separately. Do not describe Shelter as heatstroke immunity or rain protection.

The sibling-source links above assume the existing local checkout layout.

## Integration approach

- Query the client-side `RoomRegistry.GetRoomForPosition` after world initialization, only while the panel needs room information. No new server protocol is proposed.
- Refresh at a modest interval, initially 0.5 seconds, and re-query rather than retaining a room object indefinitely. The registry caches detection and invalidates affected entries on chunk changes.
- Convert the result into a small immutable HUD snapshot. Update dynamic text only when displayed values change; do not recompose the whole panel on each sample.
- Represent qualifications together in the snapshot and render them as one tag row, not independent status rows. Recalculate layout only when tag content, localization, or GUI scale requires it.
- Reuse the existing collapsible-section, styling, localization, and non-focusing input patterns.
- Stop sampling when build mode closes and clear stale information on world exit. Treat unloaded or unavailable information explicitly, never as a successful room verdict.

## Scope and verification

Initial scope excludes target-side room selection, leak-location diagnostics, world-space room highlighting, and placement simulation. The membership bitmap could support highlighting later, but the room API does not directly provide leak coordinates.

Acceptance contract (automated evidence below does not replace live-game checks):

- Enclosed, open, oversized, and partially loaded regions produce distinct, honest output.
- Cellar boundaries cover 7-block dimensions, the alternate 9-block rule, and 150 versus 151 room cells.
- Greenhouse classification requires strictly more than 50% sunlight samples; zero samples remain unavailable.
- Room, Shelter, Cellar, and Greenhouse tags are additive, including spaces that qualify for all four.
- Every enclosed Room has Shelter. Open regions qualify for Shelter only when shaded samples strictly outnumber sunlit samples; equal counts do not qualify. Shelter does not depend on cooling-wall composition or rain cover.
- The panel exposes no room-cell counts or raw detection statistics. Dimensions describe inclusive bounds, not exact irregular-room volume, and are hidden for unqualified, Shelter-only, or incomplete regions.
- Sunlight and cooling-wall percentages match their source aggregates, handle zero totals explicitly, and are hidden for unqualified, Shelter-only, or incomplete regions. Cooling-wall composition is not presented as actual heat retention or a preservation bonus.
- Placing/removing blocks and changing retention-relevant block states refresh the displayed result after registry invalidation.
- Moving between rooms, collapsing the section, closing build mode, and leaving the world do not leave stale text or active sampling.
- Colored tags have filled rounded backgrounds, visible borders, and readable localized labels on one line at supported GUI scales.
- The section preserves HUD scaling, localization, and gameplay input behavior; tags do not behave as clickable controls.

## Implementation evidence

- [BuildModeRoomInfo](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeRoomInfo.cs) copies the native room verdict into an immutable snapshot. It trusts `IsSmallRoom`, including the registry's membership-dependent cellar rule, instead of inferring cell count from bounding-box volume. Inclusive dimensions and rounded whole percentages are copied only for enclosed detections. Zero denominators remain unavailable.
- [Room HUD composition](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.Room.cs) provides a passive owning table inside the existing collapsible section. Rounded tags use distinct palette tints, filled backgrounds, borders, and centered localized text. All possible localized tags are reserved on one line to avoid width jitter. No room cells are independently registered with the composer.
- Sampling runs on the existing GUI render callback, once per half-second while open and expanded, with immediate fresh sampling on reopening or expansion. There are no new tick listeners or network messages. Existing session teardown disposes the HUD and clears its snapshot.
- Numeric changes update native dynamic text and availability colors without recomposing. Qualification/status changes rebuild the row structure and artwork; locale and GUI-scale changes rebuild measured text and tags. Controls retain their independent collapse state and input routing.
- [Snapshot tests](../VanillaBuildingExpanded.Tests/BuildBrush/BuildModeRoomInfoTests.cs) cover additive qualifications, strict sunlight comparisons, native cellar verdicts at representative bounds, inclusive dimensions, incomplete/missing data, detached snapshot data, zero totals, rounding, and overflow-safe aggregate totals. They do not re-test the registry's flood-fill or its 150-cell boundary algorithm.
- [Native HUD tests](../VanillaBuildingExpanded.Tests/UI/BuildModeHudControlsTests.cs) cover tag footprints and raster output at scales 1, 1.25, 1.33, 1.5, and 2, long localized labels, passive input, structural transitions, numeric texture reuse, sampling suspension/resumption, localization changes, repeated lifecycle cleanup, scale rebuilds, and upload-failure cleanup/retry.

Validation command:

```powershell
dotnet test .\VanillaBuildingExpanded.Tests\VanillaBuildingExpanded.Tests.csproj --no-restore --filter "FullyQualifiedName~VanillaBuildingExpanded.Tests.UI|FullyQualifiedName~BuildModeRoomInfoTests|FullyQualifiedName~BuildModeKeybindsTests|FullyQualifiedName~BuildModeIndicatorTests" --verbosity minimal
```

Result: **281 passed, zero failures or skips**, including 38 new room cases. This builds production and tests against the installed game without a manual native-library `PATH` adjustment. Existing unrelated compiler warnings remain.

Pending live-game acceptance: inspect actual GPU presentation and gameplay input, move between enclosed/open/oversized regions, verify partial-chunk handling, place/remove blocks and change retention-relevant states, and check native cellar membership boundaries (including 150 versus 151 cells). These checks have not been claimed as completed.
