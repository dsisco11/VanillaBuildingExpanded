# VanillaBuildingExpanded

A Vintage Story mod that adds quality-of-life improvements for building while staying true to the vanilla game experience.

![Game Version](https://img.shields.io/badge/Vintage%20Story-1.21.5+-blue)
![Version](https://img.shields.io/badge/Alpha-0.0.8-red)
![Multiplayer](https://img.shields.io/badge/Multiplayer-supported-green)

## Overview

**VanillaBuildingExpanded** enhances the building experience in Vintage Story by introducing tools and mechanics that make construction more intuitive and precise. The mod is designed to feel like a natural extension of the base game rather than a complete overhaul.

## Features

### Build Mode

Press the backtick key (`) to toggle **Build Mode**, then select a placeable block on your hotbar. The preview shows the target placement and orientation. Entering or leaving the mode displays a status notice. A top-left build-info panel contains the official mod icon at 85% opacity, the current snapping mode, and rotation state. Rotation shows the current orientation number and total available orientations for variant-based blocks, or just the angle for mesh-rotated blocks. Blocks without rotation support are labeled accordingly. Muted warm-gray labels and brighter ivory values appear in aligned columns; unavailable or unsupported rotation values use muted gray. The panel has a translucent black background with a darker title band and fades to transparent at its right and bottom edges. It remains visible while the brush has no target or is suspended; suspension is labeled and rotation is marked unavailable. It scales with the GUI and takes keyboard focus only while editing a work area dimension. Section collapse buttons and work area controls handle pointer clicks only while the mouse cursor is ungrabbed; passive text and unused panel space do not, except for the click that finishes a dimension edit.

Build mode stays enabled when you change hotbar slots. Selecting a tool, an unsupported item, or an empty slot suspends brush placement; ordinary interactions remain available unless a work area pick is armed. Selecting a supported block resumes the brush. Mode starts off when you join and turns off on death. Snapping preferences persist across toggles within the session; held face offset does not.

#### Key Features

- **Block Preview Rendering** - See exactly where your block will be placed before committing, with a real-time ghost preview of the block
- **Orientation Control** - Cycle through available block orientations to place blocks exactly how you want them
- **Snapping Modes** - Multiple snapping modes to help align blocks precisely:
  - **Horizontal & Vertical** - Full grid snapping for maximum precision
  - **Horizontal Only** - Snap along the world X/Z plane
  - **Vertical Only** - Snap along the world Y axis
  - **None** - Ordinary placement adjacent to the targeted face
- **Visual Feedback** - Invalid placement positions are highlighted in red, valid positions render normally
- **Target-mesh Snapping Indicators** - Separate chevron geometry shows the snapping directions over a faint white patch; the active chevron turns red for invalid placement
- **Client-only Work area** - Measure inclusive block-cell sizes independently of your held item, with an optional wireframe

#### Controls

| Action                       | Default Hotkey         |
| ---------------------------- | ---------------------- |
| Toggle Build Mode            | Backtick (`)           |
| Rotate Build Cursor Forward  | `R`                    |
| Rotate Build Cursor Backward | `Shift + R`            |
| Rotate Build Cursor with Wheel | `Shift + Mouse Wheel` (up: forward, down: backward) |
| Cycle Snapping Mode Forward  | `Middle Mouse`         |
| Cycle Snapping Mode Backward | `Shift + Middle Mouse` |
| Shift Placement              | `Shift`                |

**NOTE:** The _"Shift placement"_ keybind includes a one-block outward offset along the targeted face normal, independently of the snapping restriction. It applies only once, including when ordinary placement already uses that offset. This is useful for roofing or stairs, for example.

Snapping uses face-local U/V edge directions and an outward W direction. The selected mode filters movement in world space. When no permitted edge movement exists, the brush uses W only if its direction satisfies the selected restriction. An obstructed candidate remains invalid instead of falling back to a different position.

Snapping indicators stay on the original targeted world block, not on the moving
placement preview. Each directional region has one large orange chevron, rotated
to point in its face-local U/V direction, including a single diagonal chevron in
each corner. Chevrons span their rounded fill area's full width and height.
The active chevron is orange for valid placement and red for invalid placement;
inactive chevrons stay orange at 20% opacity before
antialiasing and edge fading. The center region has no chevron.
There are no grid lines. The underlying snapping regions still use the
existing `0.35` and `0.65` thresholds.
Only the selected region contains a white rounded-square patch, with 8%
peak opacity and softly feathered edges fading to transparent. Chevrons fade
toward the block-cell face perimeter. Centered and disabled axes select their
center slot; centered/W-only placement shows the soft patch with only the
surrounding inactive chevrons.
Chevrons are separate cached triangle geometry clipped onto the captured native
surface, not shapes evaluated in the fragment shader. Switching active regions
or validity changes shader inputs without rebuilding the chevrons.
The fill and chevrons use separate shader programs with their own typed shader
classes; their native depth-position calculation is shared through an include.
`None` mode hides the visualization.
Movement within the same target face, including crossing snapping regions, does
not clear the indicator. Between the existing 50 ms placement updates, it keeps
showing the current resolved preview; the highlight changes when that preview
updates. Rendering does not perform another placement check or change snapping
thresholds or destinations.

The indicator follows the target's captured native terrain mesh, including variants,
transforms, snow, and decor emitted by that pipeline. It intentionally draws over
texture holes without scanning or sampling material alpha. Pending, empty, or
unavailable terrain geometry produces no overlay, never a substitute cube.
GPU-only renderers and shader-only surface deformations are not reproduced.
The [snapping architecture](docs/BuildBrushArchitecture.md) records the
earlier user-approved client-session smoke on Vintage Story **1.22.7**.
That approval preceded the later appearance revisions. Outstanding geometry/readability,
overlay-interaction, measured rendering-cost, and live-cache qualification was
explicitly waived for Phase 6 closure, not passed. No comprehensive shape
qualification or measured-performance claim is made.

Keyboard and mouse-button hotkeys are rebindable in the game controls settings. Shift + mouse wheel is an additional fixed shortcut. Backtick also opens WorldEdit in creative mode; rebind one of those actions if you use both. Brush hotkeys pass through while mode is off or brush use is suspended. Rotation passes through for blocks without rotation support; these blocks still support brush placement and snapping. Shift-scroll only consumes successful rotations while the mouse is grabbed for gameplay, preserving scrolling in menus and normal hotbar scrolling otherwise.

The build-info panel lists the current bindings for all build-mode hotkeys and the fixed Shift + mouse wheel shortcut (up: forward, down: backward), using concise labels and the base game's keycap-style drawing. Keys within a combination have a small GUI-scaled gap without plus signs; the wheel keycap is labeled "Scroll wheel". Rotation and snapping each show their forward and backward bindings on one row, separated by a short, muted vertical divider with extra spacing (forward first). Changes made in the controls settings are reflected while the panel is open; cleared bindings show as unbound. The panel grows to fit the control names and bindings while retaining its bottom padding and background fades.

Controls starts expanded. A light horizontal divider ends the section, followed by a full-width toggle with a dark-orange triangle (up to collapse, down to expand). The toggle has no background until hovered, when a translucent gray gradient appears. Collapsing hides the keybind rows and shrinks the panel, keeping the Controls title, divider, and toggle visible. The choice is retained across build-mode toggles and GUI scaling for the lifetime of the HUD.

The Room section above Controls also starts expanded. It describes the real room at the player's position, not the brush preview. Its colored, noninteractive tags show every applicable qualification in order: Room, Shelter, Cellar, Greenhouse. Enclosed regions additionally show inclusive dimensions, sunlight sample percentage, and cooling-wall contribution percentage. Cooling walls are not a measurement of actual retained heat or a guaranteed preservation bonus.

Room information refreshes every half-second while the section is expanded and the panel is open. Open Shelter-only regions omit potentially truncated dimensions and aggregates; unqualified regions show "Not enclosed or too large". Missing or partially loaded data is explicitly unavailable, never a provisional success. Room, Work area, and Controls retain independent collapse choices. Numeric room changes update existing text textures; qualification or row changes rebuild the layout.

#### Work area

The expanded-by-default **Work area** section sits between Room and Controls:

```text
| > Work area 1            |
|   Workshop               |
|   Garden                 |
[New] [Rename] [Delete]
[ ] Show work area
Size: X [<x>] Y [<y>] Z [<z>]
```

Click a name in the compact list to switch between named areas in this world.
The selected row is highlighted. Up to three rows are visible; use the mouse wheel
or scrollbar for additional areas. Long names are clipped to keep the list compact.
**New** creates an empty,
selected, visible area after you save its name; **Rename** changes only its name.
Names are trimmed, unique within the world without regard to case, and limited to
64 characters without control characters. Naming uses a focused dialog so typing
does not trigger gameplay actions; Cancel or Escape makes no change. **Delete**
requires confirmation. Deleting the last area leaves an empty list; **New** remains
available. A fresh world initially offers one empty **Work area 1**.

Each area's **Show work area** checkbox is independent. All checked, complete areas
in the current dimension show their boxes, but only the selected area has editing
gizmos. Selecting another area cancels any current drag or pending creation without
saving its preview, and shows the selected area's size and visibility preference.

With build mode enabled and your player alive, check **Show work area**. If no endpoints exist, one world block click creates both on that cell. A restored partial work area offers the same click for its missing endpoint. Creation targets loaded terrain through the camera far plane without a player-reach cap, using the actual free cursor or mouse-look reticle, never brush placement offsets. Sky/entity/unloaded targets leave creation pending with feedback.

Drag either corner's center handle or RGB axis arrows with the free cursor or reticle. The center translates across a camera-facing plane fixed at press, preserving the grab offset; it does not follow terrain or independently change depth normal to that plane. Use arrows for independent axis/depth adjustment. Existing handles remain movable over sky, entities and absent terrain. Both endpoints snap to whole blocks throughout motion. Start always marks the minimum corner and End the exclusive maximum corner; crossing normalizes both roles without losing capture. The work area measures whole block cells, including slabs, stairs, and chiseled blocks: the same cell at both ends measures `1, 1, 1`; `(10, 65, 20)` through `(14, 67, 23)` measures `5, 3, 4`. Until both endpoints exist, the numeric size fields show dashes and cannot be edited.

Edit the **X**, **Y**, or **Z** size with positive whole-block numbers. Enter or
leaving the field applies and saves that axis; Tab/Shift+Tab applies and moves
between fields. Escape discards the draft. Click **Resize: Expansion / Centered**
to switch the shared resize policy for all work areas. **Expansion** (default)
keeps the minimum corner fixed and moves the maximum corner. **Centered** resizes
around the center; changing between odd and even sizes shifts the center half a
block toward the positive axis. The other axes remain unchanged. The setting is
saved across sessions and worlds and does not change gizmo dragging.
Invalid sizes or changes that would leave valid world coordinates are rejected
with feedback, without changing either corner. Arrow buttons and Up/Down apply
each step immediately while retaining focus; the mouse wheel adjusts the draft.
All steps use whole blocks (Shift: 10, Ctrl: 100). Typing captures
gameplay input only until the edit ends. Hiding/closing the HUD, losing its editing
context, or rebuilding it for scale/locale changes discards an unfinished draft.

A third gizmo at the midpoint of a complete selection moves the entire box. Drag its center across the captured camera plane or use its axis arrows to translate both corners together in whole-block steps without changing the size. Release saves both corners once; cancel restores both. A move that would put either corner outside valid coordinates is rejected without moving the other corner. Partial work areas have only their existing endpoint handle.

**Show work area** controls the selected area's box and handles; other checked areas retain their boxes without handles. Box lines use order-independent transparency, blending with clouds and other transparent objects. Unoccluded lines have 100% opacity; lines behind solid geometry remain visible at 30% opacity. Occlusion uses the lines' actual projected depth without a distance-amplified depth bias, so distant hidden lines remain fainter too. Each box needs both endpoints in the current dimension; each existing endpoint in the selected area has its own handle. Collapsing the section only hides its controls. Release commits a drag; right-click or Escape restores its baseline, or cancels pending creation until visibility/mode is re-enabled. Captured gestures suppress mining, attack, placement and use through release. Reticle motion remains available for aiming. Invalid viewing angles retain the last valid value with feedback. Prompts and errors appear outside the panel; renderer/input failures leave size and persisted endpoints usable.

All center handles and axis arrows use bright, unlit colors and the same OIT depth behavior as the boxes: 100% opacity when unoccluded and 30% behind solid geometry. Hovering or dragging a handle thickens its lines without overriding occlusion fading; scene lighting does not dim the controls. Lines share one reference-counted `vbe-oit-lines` shader program. Labels remain screen-space overlays rendered after the world, using the same geometry snapshots as the lines and picking.

With the free cursor, passive cursor/item-tooltip overlays do not block handle hover or dragging. Interactive HUD controls and open dialogs retain input priority.

Hovering an axis handle shows its X/Y/Z label beside the arrow tip. Endpoint center
handles show **Center**; the whole-area midpoint shows the work area's name and
updates when renamed. Labels follow the handles with fixed screen-pixel offsets
and do not intercept clicks. Hover labels disappear while a handle is being dragged.

Turning build mode off or dying hides the work area and cancels interaction, but retains endpoints and the saved checkbox preference. Focus loss, dialogs, pointer-mode changes and dimension/world changes also cancel active edits safely. Re-enabling mode restores eligible visibility. Another dimension hides retained endpoints without clearing their size or automatically replacing them; returning restores visibility.

Names, stable area IDs, committed endpoints, and each area's **Show work area** preference persist using the base game's mod-config APIs in `ModConfig/vanillabuildingexpanded-workareas.json` (schema 2); drag previews do not write config. Creating, renaming, deleting or toggling an area's visibility saves the collection, using the original committed endpoints if a drag is in progress. Entries are keyed by the world's `SavegameIdentifier`, keeping single-player saves, server worlds, and replacement saves at the same server address separate. They are local to this client, not shared with other players. **Show work area** defaults to enabled for new areas and older saved areas without a visibility setting; an explicitly saved off choice remains off. Each join selects the first saved area and starts with build mode off. Selection is session-only. Missing world identity makes the session memory-only with a notice. Invalid entries are reported; unreadable or unsupported config files are not overwritten with defaults. A failed save retains the in-memory edit, warns that it may be lost after restart, and retries on the next explicit endpoint/collection/visibility edit or orderly exit.

The rename starts fresh: saved bounds in the former `ModConfig/vanillabuildingexpanded-rulers.json` are not loaded or migrated. That old file is left untouched; new edits are saved only in the work-area config.

The previous single-area work-area format (schema 1) is also intentionally not
migrated. Its bounds are ignored, and its file is replaced with schema 2 only
when an explicit area or endpoint edit is saved. No existing single-area bounds
are imported.

Work area qualification uses the installed Vintage Story **1.22.7** API/runtime files. Automated evidence and outstanding live checks are recorded in the [gizmo implementation plan](docs/InWorldGizmos.todo). The earlier workarea-only live-check waiver does not waive gizmo qualification. Live input, comparable before/after frame timings, restart persistence and rendering checks remain separate evidence requirements; mocked tests do not establish them. Removing movement selection calls does not remove the independently identified full-panel HUD recomposition cost.

## Compatibility

- **Vintage Story Version:** 1.21.5+
- **Multiplayer:** Supported (client-server synchronized)

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

Build-overlay sections use the shared `CollapsibleSection` and `GuiElementCollapseButton` UI components. Each section supplies a stable ID, a localized title key, content-width and content-height measurement callbacks, and a content-composition callback using the native `GuiComposer`. Content is composed relative to the supplied content bounds. The section owns its collapse state independently of composer lifetime; the HUD stacks sections by measured height and applies queued toggles before recomposition. Controls-specific keycap formatting remains in the build-mode HUD.

Section content uses owning `GuiElementTable` instances. Room and Controls content is passive; Work area opts only its native checkbox and endpoint buttons into the existing table pointer/capture routing. Passive labels, size text, and unused row space do not capture gameplay input. Room snapshots copy the native registry verdict and display aggregates rather than retaining mutable rooms or reproducing their detection algorithm. All localized qualification labels are measured together so tags fit one line and the panel keeps a stable width when qualifications change.

The snapping/rotation, Room, and Controls tables use `FadingRowTableDividerRenderer` through their `DividerRenderer` property. This style draws horizontal separators only, with a faint center and transparent ends; it adds no outer border or column lines. Defaults are a one-unit white stroke at 15% opacity and a 24-unit fade at each end. Custom `TableDividerOptions` and a logical `fadeLength` can override those settings. Narrow tables shorten the fades symmetrically to meet at the center. Styling does not change row spacing, layout, or input behavior; the reusable table component's default remains divider-free.

HUD table content is vertically centered within each row, including native text, control keycaps, and room tags. Text cells reserve a measured font-line height rather than the full row height, leaving alignment to `TableOptions(verticalAlignment: EnumVerticalAlign.Middle)`. The reusable table defaults to top alignment and also supports bottom alignment; alignment never stretches cells or moves row dividers.

### Running tests

Install the .NET 10 SDK and set `VINTAGE_STORY` to your Vintage Story installation directory, then run:

```powershell
dotnet test .\VanillaBuildingExpanded.Tests\VanillaBuildingExpanded.Tests.csproj
```

On Windows, the test project copies the installed game's native Cairo and Skia libraries into the test output directory. GUI tests therefore run from the command line and VS Code without adding the game's `Lib` directory to `PATH`. These tests use real Cairo rasterization with mocked GPU services; they do not replace in-game visual and input acceptance checks.

The Cake package/install workflow runs the full Release test suite before packaging.
Its test step prints a timestamped diagnostics directory under
`VanillaBuildingExpanded.Tests/TestResults`, containing TRX results and VSTest logs.
If test activity stalls for two minutes, VSTest collects a mini hang dump and test
sequence, aborts the run, and fails the build; the watchdog is not a two-minute
limit on the entire suite. These generated diagnostics are ignored by Git.
To reproduce the same clean/publish/test path without packaging or installing:

```powershell
dotnet run --project .\CakeBuild\CakeBuild.csproj -- --target=Test
```

Keep the failed run's diagnostics for hang investigation. Dumps can contain
process memory; do not commit or publicly upload them.

To build the distributable through the existing JSON-validation, clean/publish,
and full-test workflow without installing it:

```powershell
dotnet run --project .\CakeBuild\CakeBuild.csproj -- --target=Package
```

This refreshes the generated `Releases` directory and creates
`Releases\vanillabuildingexpanded_999.99.9.zip` for the current development version.
The archive contains the mod and its assets, including the `vbe-snapping` fill and
`vbe-snapping-chevron` vertex/fragment programs and their shared depth include;
game-provided OpenTK libraries are referenced from `VINTAGE_STORY`, not bundled.
Package identity and remaining user-run checks are tracked in Phase 5 of the
[snapping indicator plan](docs/BuildBrushSnappingIndicator.todo).

## License

See [LICENSE.txt](LICENSE.txt) for details.

## Credits

- **Author:** Sisco++
- Built for Vintage Story
