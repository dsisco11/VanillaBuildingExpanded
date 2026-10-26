# VanillaBuildingExpanded

A Vintage Story mod that adds quality-of-life improvements for building while staying true to the vanilla game experience.

![Game Version](https://img.shields.io/badge/Vintage%20Story-1.21.5+-blue)
![Version](https://img.shields.io/badge/Alpha-0.0.8-red)
![Multiplayer](https://img.shields.io/badge/Multiplayer-supported-green)

## Overview

**VanillaBuildingExpanded** enhances the building experience in Vintage Story by introducing tools and mechanics that make construction more intuitive and precise. The mod is designed to feel like a natural extension of the base game rather than a complete overhaul.

## Features

### Build Mode

Press the backtick key (`) to toggle **Build Mode**, then select a placeable block on your hotbar. The preview shows the target placement and orientation. Entering or leaving the mode displays a status notice. A top-left build-info panel contains the official mod icon at 85% opacity, the current snapping mode, and rotation state. Rotation shows the current orientation number and total available orientations for variant-based blocks, or just the angle for mesh-rotated blocks. Blocks without rotation support are labeled accordingly. Muted warm-gray labels and brighter ivory values appear in aligned columns; unavailable or unsupported rotation values use muted gray. The panel has a translucent black background with a darker title band and fades to transparent at its right and bottom edges. It remains visible while the brush has no target or is suspended; suspension is labeled and rotation is marked unavailable. It scales with the GUI and never takes keyboard focus. Only section collapse buttons handle pointer clicks, and only while the mouse cursor is ungrabbed.

Build mode stays enabled when you change hotbar slots. Selecting a tool, an unsupported item, or an empty slot suspends the brush and restores ordinary interactions; selecting a supported block resumes it. Mode starts off when you join and turns off on death. Snapping preferences persist across toggles within the session; held face offset does not.

#### Key Features

- **Block Preview Rendering** - See exactly where your block will be placed before committing, with a real-time ghost preview of the block
- **Orientation Control** - Cycle through available block orientations to place blocks exactly how you want them
- **Snapping Modes** - Multiple snapping modes to help align blocks precisely:
  - **Horizontal & Vertical** - Full grid snapping for maximum precision
  - **Horizontal Only** - Snap to horizontal planes
  - **Vertical Only** - Snap to vertical planes
  - **None** - Free placement without snapping constraints
- **Visual Feedback** - Invalid placement positions are highlighted in red, valid positions render normally

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

Room information refreshes every half-second while the section is expanded and the panel is open. Open Shelter-only regions omit potentially truncated dimensions and aggregates; unqualified regions show "Not enclosed or too large". Missing or partially loaded data is explicitly unavailable, never a provisional success. Both sections retain independent collapse choices. Numeric room changes update existing text textures; qualification or row changes rebuild the layout.

## Compatibility

- **Vintage Story Version:** 1.21.5+
- **Multiplayer:** Supported (client-server synchronized)

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

Build-overlay sections use the shared `CollapsibleSection` and `GuiElementCollapseButton` UI components. Each section supplies a stable ID, a localized title key, content-width and content-height measurement callbacks, and a content-composition callback using the native `GuiComposer`. Content is composed relative to the supplied content bounds. The section owns its collapse state independently of composer lifetime; the HUD stacks sections by measured height and applies queued toggles before recomposition. Controls-specific keycap formatting remains in the build-mode HUD.

Section content uses passive, owning `GuiElementTable` instances. Room snapshots copy the native registry verdict and display aggregates rather than retaining mutable rooms or reproducing their detection algorithm. All localized qualification labels are measured together so tags fit one line and the panel keeps a stable width when qualifications change.

All build-overlay tables (snapping/rotation, Room, and Controls) use `FadingRowTableDividerRenderer` through their `DividerRenderer` property. This style draws horizontal separators only, with a faint center and transparent ends; it adds no outer border or column lines. Defaults are a one-unit white stroke at 15% opacity and a 24-unit fade at each end. Custom `TableDividerOptions` and a logical `fadeLength` can override those settings. Narrow tables shorten the fades symmetrically to meet at the center. Styling does not change row spacing, layout, or input behavior; the reusable table component's default remains divider-free.

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
