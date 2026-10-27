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
  - **Horizontal Only** - Snap to horizontal planes
  - **Vertical Only** - Snap to vertical planes
  - **None** - Free placement without snapping constraints
- **Visual Feedback** - Invalid placement positions are highlighted in red, valid positions render normally
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

**NOTE:** The _"Shift placement"_ keybind will offset the block placement position by 1 block in the direction of the block face you are targeting, this is useful for placing roofing or stairs, for example.

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

**Show work area** controls the selected area's box and handles; other checked areas retain their depth-tested boxes without handles. Each box needs both endpoints in the current dimension; each existing endpoint in the selected area has its own handle. Collapsing the section only hides its controls. Release commits a drag; right-click or Escape restores its baseline, or cancels pending creation until visibility/mode is re-enabled. Captured gestures suppress mining, attack, placement and use through release. Reticle motion remains available for aiming. Invalid viewing angles retain the last valid value with feedback. Prompts and errors appear outside the panel; renderer/input failures leave size and persisted endpoints usable.

All center handles and axis arrows use bright, unlit colors at 80% opacity. Hovering or dragging a handle raises it to full opacity and thickens its lines; scene lighting does not dim the controls.

Turning build mode off or dying hides the work area and cancels interaction, but retains endpoints and the session's checkbox preference. Focus loss, dialogs, pointer-mode changes and dimension/world changes also cancel active edits safely. Re-enabling mode restores eligible visibility. Another dimension hides retained endpoints without clearing their size or automatically replacing them; returning restores visibility.

Names, stable area IDs, and committed endpoints persist using the base game's mod-config APIs in `ModConfig/vanillabuildingexpanded-workareas.json` (schema 2); drag previews do not write config. Creating, renaming or deleting an area saves the collection, using the original committed endpoints if a drag is in progress. Entries are keyed by the world's `SavegameIdentifier`, keeping single-player saves, server worlds, and replacement saves at the same server address separate. They are local to this client, not shared with other players. Each join selects the first saved area, starts with build mode off, and resets all visibility checkboxes to unchecked. Selection and visibility are session-only. Missing world identity makes the session memory-only with a notice. Invalid entries are reported; unreadable or unsupported config files are not overwritten with defaults. A failed save retains the in-memory edit, warns that it may be lost after restart, and retries on the next explicit endpoint/collection edit or orderly exit.

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

## License

See [LICENSE.txt](LICENSE.txt) for details.

## Credits

- **Author:** Sisco++
- Built for Vintage Story
