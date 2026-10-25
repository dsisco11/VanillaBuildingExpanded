# GUI table layout component proposal

Status: Design direction approved: row-oriented content, child ownership, direct inheritance from `GuiElement`, and interface-based divider rendering. The component is not implemented.

## Purpose

Introduce `GuiElementTable`, a reusable owning GUI container that arranges a supplied collection of GUI elements into rows and aligned columns.

The table owns its accepted children, including their composition, rendering, input routing, focus management, and disposal. The outer composer registers only the table, not its individual children.

Rows are the content-facing abstraction. Callers supply a collection of elements for each row; they do not flatten the entire table, calculate cell indices, or provide placeholders for every column.

The component is independent of the build-mode HUD and collapsible sections. A table may be content within a section, dialog, or another layout, but it never references those owners or manages their behavior.

## Current layout

The [build-mode HUD](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs) contains two table-like arrangements:

- Snapping and rotation use manually positioned label/value pairs.
- Controls use measured action-label and keybinding columns, with manually positioned rows.

The controls implementation calculates column widths in `MeasureControlsColumns()`, total content height separately, and row coordinates in `ComposeControlsContent()`. Labels have a minimum width of 88 logical units, bindings have a minimum width of 200, the column gap is 12, and each row is 20 units high.

This distributes related layout rules across measurement and composition. The proposed table centralizes those rules while leaving label creation, keybinding measurement, and keycap drawing with the caller.

The [collapsible-section helper](../VanillaBuildingExpanded/src/UI/CollapsibleSection.cs) already separates section chrome from content. It does not need table-specific behavior.

## Responsibilities and boundaries

| Owner | Responsibilities |
| --- | --- |
| `GuiElementTable` | Own child elements, group them into rows, measure and arrange them, delegate divider artwork, compose and render child content, route input and focus, and dispose them. |
| Divider renderer | Render row and cell dividers from supplied layout geometry, encapsulating all style-specific drawing decisions. |
| Cell elements | Draw their content and retain their existing dynamic-update and input behavior. |
| `GuiComposer` | Register, compose, render, route input to, and dispose the table as one element. It does not manage the table's individual children. |
| Consuming HUD or dialog | Create content, supply preferred measurements, request layout invalidation, and coordinate composition. |
| Collapsible section, when used | Manage its title, divider, toggle, collapse state, and content visibility. |

The table does not manage collapse state, section measurement policies, panel rebuilding, localization keys, colors, keybindings, or room information.

The table has no decorative background by default, but owns a texture for its children's composed static artwork. Informational tables remain non-focusable and input-transparent. Tables containing focusable or interactive children provide nested focus and input routing without consuming events merely because the pointer is over the table rectangle.

## Row-oriented content API

The following illustrates the intended API; exact signatures may be refined during implementation:

```csharp
table.AddRow(labelElement, valueElement);
table.AddRow(otherLabel, otherValue, statusIcon);
table.AddRow(elementsForOneRow);
```

Support both a convenient `params GuiElement[]` form and an enumerable collection form. Also support providing the complete collection of rows at construction time.

Each supplied collection defines exactly one row:

- Preserve row order and element order within each row.
- Infer the column count from the longest row.
- Place elements into consecutive columns starting at column zero.
- Do not spill elements into another row.
- Do not require callers to pad shorter rows.
- Leave missing trailing positions empty.
- A single-element row occupies the first column; it does not implicitly span the table.

For example:

```text
Row 1: [Snapping] [Enabled]
Row 2: [Rotation] [90 degrees] [Status icon]
Row 3: [Room]     [Enclosed]

         Column 1  Column 2     Column 3
```

Rows describe content grouping. Columns align corresponding positions across those rows.

Snapshot supplied row collections when accepted so deferred enumeration or external collection mutation cannot change content between measurement and composition.

Ownership transfers when a row is successfully accepted. Validate the complete row before accepting any of its children; if acceptance fails, those elements remain the caller's responsibility. Accepted elements must not be independently registered, disposed, or attached to another owner. Callers may retain non-owning references for normal value updates.

## Column and row sizing

Column configuration is optional. By default, all inferred columns size to their content.

The initial column modes are:

- **Auto:** use the greatest preferred width in that column, subject to an optional minimum width.
- **Fixed:** use an explicitly configured width.

Column configuration does not establish the content's shape or require callers to supply a cell for each configured column. Reject configuration targeting a nonexistent column when preparing the completed table for composition.

Each row sizes to its tallest element, subject to an optional table-wide minimum row height. Column and row gaps are table-level settings, defaulting to zero.

```text
Auto column width = max(column minimum, preferred cell widths)
Row height        = max(minimum row height, preferred cell heights)

Table width  = sum(column widths) + column gap * max(0, column count - 1)
Table height = sum(row heights)   + row gap * max(0, row count - 1)
```

Cells are top-left aligned within their allocated positions and retain their preferred sizes in the initial version. Smaller cells do not automatically stretch.

An entirely empty table measures zero by zero. Reject empty individual rows rather than giving them an implicit spacer height; use row gaps for spacing. Reject content that exceeds a fixed column's width rather than silently clipping, overlapping, or shrinking it.

### Preferred-size contract

The base `GuiElement` API has no universal intrinsic-size measurement method. The table must not assume it can determine arbitrary content dimensions automatically.

- For ordinary fixed-size elements, use their declared unscaled bounds as the preferred size.
- For content whose size requires calculation, allow per-cell preferred-size metadata through a row overload accepting cell descriptors.
- A descriptor may also carry an optional table-local child key.
- Keep the ordinary row API convenient; descriptors are only needed when bounds-based measurement is insufficient or keyed lookup is desired.
- Preserve preferred measurements separately from arranged coordinates so repeated layout does not measure its own previous output.

Custom keybinding drawing remains measured by the HUD. Text measurement remains the responsibility of the element or its creator; the table consumes the resulting dimensions.

All layout dimensions use unscaled logical GUI units. Convert scaled Cairo measurements back to logical units once. `ElementBounds` performs the final GUI scaling.

The initial version supports fixed-size children. Percentage sizing, wrapping-dependent measurement, and automatic text height are outside this contract; reject unsupported sizing rather than producing unreliable measurements.

## Divider rendering styles

Support different rendering styles for row and cell dividers through an injected interface. The table supplies geometry and delegates drawing; it does not contain style-specific rendering branches, style enums with switches, concrete-style type checks, or separate drawing implementations for each style.

The following illustrates the contract; exact type names may be refined during implementation:

```csharp
internal interface ITableDividerRenderer
{
    void Draw(Context context, TableDividerGeometry geometry);
}
```

`TableDividerGeometry` is a read-only snapshot of the arranged layout, not a reference to the mutable table or its children. It contains the table's local drawing extent and divider segments, with segments identified as row or cell boundaries.

The table holds a non-null renderer and always invokes the same interface method during static composition. Use a no-op implementation as the default, preserving the overlay's existing divider-free content without a special branch in the table.

### Geometry and drawing contract

- Row dividers occupy boundaries between successive rows and span the table's content width.
- Cell dividers occupy boundaries between adjacent populated cells within each row and span that row's allocated height.
- Shorter rows do not generate dividers around nonexistent trailing cells.
- Single-row tables have no row dividers; single-cell rows have no cell dividers. Empty tables produce no divider artwork.
- Place boundaries at the midpoint of any inter-row or inter-column gap. With no gap, use the shared track edge.
- Supply table-local drawing coordinates already converted to device units, together with the current GUI scale for converting logical stroke widths and dash lengths. Renderers must not scale the supplied coordinates again.
- Render dividers into the table's static surface before child composition. Child artwork remains above the dividers; dynamic child rendering remains unchanged.
- The table saves and restores the Cairo context around the renderer call and clips drawing to the table extent. Renderer implementations also keep any temporary Cairo state and drawing resources local to the call.
- Dividers are decorative. They do not change preferred sizes, row or column allocations, gaps, hit testing, focus, or child ownership. Callers provide sufficient spacing when a style needs room between cells.

Compute each boundary once. Concrete renderers handle stroke placement, pixel alignment, and intersections without double-darkening shared edges.

### Style implementations and configuration

The architecture must allow separate implementations such as:

- `NoTableDividerRenderer`: draws nothing and is the default.
- A solid-line renderer: draws selected row and/or cell boundaries with configured color and logical stroke width.
- A dashed-line renderer: encapsulates its own dash pattern, stroke setup, and drawing.

Solid and dashed lines are initial style candidates, not a restriction on future implementations or approved final artwork. A custom implementation may draw a different treatment without modifying `GuiElementTable`.

Choosing row-only, cell-only, or both boundary kinds, and configuring colors, stroke widths, and dash patterns belong to the renderer or its immutable options. The table must not interpret those style settings. Any selection UI or style-name-to-renderer mapping belongs outside the table.

Renderer implementations are immutable configuration objects with no retained Cairo surfaces, contexts, or GPU resources, so they may be shared between tables. The table owns the resulting texture, not the injected renderer's lifetime.

Replacing the renderer invalidates the table's static artwork but does not require layout remeasurement. Recompose through the existing safe lifecycle rather than drawing or uploading immediately from an input callback. Invalid renderer configuration must fail explicitly; do not silently fall back to a different style.

## Composer integration and lifecycle

Provide a small composer extension, conceptually `AddTable(...)`, to register the table as one interactive element. This registration is required for runtime rendering even when all its content is informational.

The sequence is:

1. Build the rows, validate each addition, and transfer accepted children to the table.
2. Measure the table so the caller can size its surrounding layout.
3. Arrange its elements relative to the table bounds.
4. Register only the table with the composer, transferring ownership of the table to it.
5. Run normal composer composition, which invokes the table's child lifecycle handling.

Until composer registration succeeds, the caller owns the table and must dispose it if it is abandoned. That disposal also releases all accepted children. Measuring content does not require registering it or allocating GPU resources.

Complete measurement and arrangement before the composer calculates world bounds and allocates its drawing surface. Do not resize the parent panel late in `ComposeElements()`.

### Child composition and rendering

Use the native owning-container pattern:

- Prepare child measurements and bounds before final world-bound calculation.
- Mark the table bounds as a drawing surface so child static drawing coordinates are local to it.
- Delegate divider drawing through `ITableDividerRenderer` before composing children.
- Compose every child into the table's own Cairo surface and upload the static artwork to a table-owned texture.
- Render that texture, then invoke child dynamic rendering in stable draw order.
- Preserve clipping, premultiplied-alpha conventions, and child draw order across composition and runtime rendering.
- Propagate clip bounds to children both on addition and when the table's clip bounds change.
- Avoid zero-sized texture allocation for an empty table; it has no artwork to render.

No per-child static/dynamic registration mode is necessary. Every child receives its composition and dynamic-render hooks; static-only elements have no dynamic artwork to render. Dynamic text continues rendering even though it is not registered with the outer composer.

Keep optional child keys local to the table, exposed through a lookup such as `table.GetElement(key)`. The composer can retrieve the table by its own key, but cannot retrieve nested cells directly. Callers may alternatively retain typed, non-owning child references.

### Input and focus

Forward relevant input to children and respect handled flags, clipping, and draw order. Do not invoke base mouse handling that consumes otherwise unhandled events over empty table space or passive content.

The table must be non-focusable when it has no focusable children. When it does, expose a focusable group to the outer composer and manage child focus and tab traversal internally. At the group's traversal boundary, allow focus to move through the outer composer. Propagate focus loss and expose the hovered child's cursor.

Mouse release and cancellation must reach an active child even when the pointer moves outside its bounds, so controls cannot remain pressed. Do not make informational content capture pointer input merely because its native base handler marks events handled; input eligibility must distinguish passive cells from interactive controls. This policy is separate from child rendering, and must not use `Focusable` alone as a clickability test.

The containing HUD or dialog retains the final say over whether the table receives input at all.

### Disposal and validation

Make table disposal idempotent. Release its texture and dispose each owned child once, cancel active input, and reject lifecycle work after disposal. This is important because the native composer can reach an interactive element through both its interactive and static collections during disposal.

Validate parentage before accepting children. Native `ElementBounds.WithChild()` does not replace an existing parent automatically. Reject already-parented elements, duplicate element instances, duplicate table-local keys, self-referencing bounds, and non-finite or negative dimensions and spacing with explicit argument errors.

### Approved inheritance decision

Implement the table directly on `GuiElement`, not on a specialized base-game container:

```csharp
internal sealed class GuiElementTable : GuiElement
```

Child ownership is required. Direct inheritance is chosen because the specialized containers do not expose suitable extension points for replacing their layout and collection-management policies, not because their ownership behavior is unnecessary.

| Candidate | Useful behavior | Reason not to subclass |
| --- | --- | --- |
| `GuiElementContainer` | Arbitrary child elements, a composition texture, dynamic rendering, nested input and focus, and child disposal. | Its non-virtual height calculation is called by private composition code; its public mutable flat child collection and non-virtual mutation methods bypass row invariants. |
| `GuiElementCellList<T>` | Owned cells, vertical layout, filtering, rendering, and clip-bound propagation. | It accepts factory-created `IGuiElementCell` objects rather than arbitrary `GuiElement` rows, and imposes a non-virtual vertical layout calculation. |
| `GuiElementConfigList` | Key/value presentation, text sizing, row highlighting, and click callbacks. | It renders `ConfigItem` data itself rather than hosting arbitrary child GUI elements. |
| `GuiElementControl` | An enabled flag. | It provides no child ownership or layout infrastructure; the table is not inherently a single enabled/disabled control. |
| `GuiElementParent` | Bounds calculation. | It provides no child lifecycle management or table layout. |
| Inventory and skill grids | Domain-specific slot arrangement and interaction. | Their content and interaction contracts are inventory or skill specific, not general GUI-element collections. |

#### Why not derive from `GuiElementContainer`?

This is the closest candidate, but its layout policy is coupled to composition. `CalcTotalHeight()` calculates the greatest child bottom edge plus `unscaledCellSpacing`. It is non-virtual, and the private `ComposeList()` calls it again during composition.

Overriding `BeforeCalcBounds()` alone therefore cannot establish the table's final height: inherited composition recalculates it using the native container's policy. For example, a minimum row height of 20 must remain 20 even if the final row's tallest child is only 12 units high. A child-bottom calculation does not necessarily preserve that allocated row height.

The composition routine and texture are private. Replacing composition to avoid that calculation would require replacing much of the rendering infrastructure that inheritance was intended to reuse.

The public `Elements` list and non-virtual `Add()`, `Clear()`, and `RemoveCell()` methods also permit flat mutations without updating row membership, preferred measurements, or table-local keys. Hiding those members would not enforce the row contract through a base-class reference.

Do not work around these mismatches with synthetic children, altered child dimensions, hidden inherited APIs, or a second independent texture pipeline in a subclass.

#### Why not adapt rows to `GuiElementCellList<T>`?

A row adapter implementing `IGuiElementCell` would still need to own its GUI elements and implement their static composition, dynamic rendering, input, focus, and disposal. Shared column widths would still require table-wide measurement across all row adapters.

The list's own composition hook is empty, so inheriting from it does not provide the general child-artwork composition required here. Adapters would introduce a second content abstraction without removing the table's core implementation work.

#### Reuse patterns, not incompatible base classes

Use the native general container as reference for child composition and nested lifecycle behavior, and the cell list as reference for propagating clipping changes. Preserve the base game's coordinate, rendering, and input conventions while implementing one coherent row-owned collection.

Ownership does not inherently require a separate texture; this design chooses the native general-container drawing model to support arbitrary static and dynamic GUI elements.

The trade-off is explicit: the table must implement and test child rendering, routing, focus, clipping, and idempotent disposal itself. Do not introduce a new general-purpose container base class solely for this component; extract shared infrastructure only if another concrete consumer requires it.

### Updates and invalidation

- A value-only change that fits the reserved preferred size updates the existing element without reflow.
- A divider-renderer change invalidates static artwork only; it does not change table dimensions or child positions.
- A preferred-size change invalidates table layout and artwork. The table handles its own recomposition; the surrounding owner coordinates remeasurement and recomposition when the table's outer dimensions affect the surrounding layout.
- Rows and column configuration may be built before registration; structural mutation after registration requires rebuilding the table through its owner in the initial version.
- GUI-scale or localization changes are handled by the owner using the existing composition lifecycle.
- Do not rebuild a HUD or dialog from inside a child input callback.

No table API accepts or references a collapsible section.

## Build-mode HUD adoption

First replace the controls section's manual row positioning:

```csharp
foreach (var hint in keybindHints)
{
    table.AddRow(
        CreateActionLabel(hint),
        CreateKeybindingElement(hint));
}
```

This is illustrative; the element factory names are not existing APIs.

Configure two auto columns with minimum widths of 88 and 200, a column gap of 12, a minimum row height of 20, and no row gap.

Use the no-op divider renderer for the initial HUD migration so extracting the table does not silently introduce new artwork. Consumers may opt into other divider styles independently of the table layout or collapsible-section implementation.

Retain custom keybinding drawing and its content measurement in the HUD. Replace the HUD's column maxima, row-position arithmetic, and independent table-height calculation with the table's measurements.

The HUD wires table measurements and composition into the section's existing content callbacks. This is external integration: the table and section remain unaware of each other. Preserve the section's existing policy of measuring expanded content width while collapsed.

After controls are verified, migrate snapping and rotation into another two-column table, preserving update behavior. Move their dynamic element keys into table-local lookup or retain typed references, and update the HUD's existing composer lookups accordingly. Leave the icon, title, panel background, and section chrome outside the table.

The HUD currently routes pointer input only to collapse buttons. Preserve that restriction; registering the owning table for rendering must not make the entire panel consume gameplay input.

Future room-information content can use the same row-oriented API without adding room-specific behavior to the table.

## Scope

Initial scope includes row collections, inferred columns, auto/fixed widths, minimum sizes, gaps, preferred-size metadata, table-local child keys, child ownership, static and dynamic rendering, interface-based row/cell divider styles, nested input and focus, idempotent disposal, and composer integration.

Initial scope excludes column or row spanning, interior empty-cell placeholders, sorting, selection, headers, outer table borders, scrolling, virtualization, automatic shrinking, wrapping, stretch alignment, and cross-table column synchronization.

These features can be proposed separately when a concrete consumer requires them.

## Verification and acceptance criteria

Add focused layout tests using supplied dimensions rather than requiring live GPU rendering:

- Row order and element order are preserved for both collection and convenience overloads.
- One-, two-, and three-column tables place elements correctly.
- Shorter rows need no placeholders and do not shift or spill content.
- Column widths use the greatest preferred size at each position.
- Row heights use the tallest cell and respect minimum height.
- Gaps occur only between tracks, not after the final row or column.
- Empty tables measure zero by zero; empty rows are rejected.
- Fixed columns and unsupported sizing fail explicitly when their contracts are violated.
- Invalid dimensions, duplicate elements or keys, and conflicting parentage are rejected before ownership transfer.
- Failed row acceptance leaves the entire rejected row owned by the caller.
- Repeated measurement and arrangement are stable and do not overwrite preferred sizes.
- Only the table is composer-registered; child keys resolve through the table.
- Static children compose into the table surface and dynamic children render through the table without duplicate calls.
- Empty tables allocate no zero-sized texture.
- Clipping changes reach existing children, and alpha and draw-order behavior remain consistent.
- Disposing the table twice releases each child once; disposing an unregistered table also releases its children.
- Informational tables remain non-focusable and input-transparent, including passive text cells.
- Interactive children receive input once, cursor propagation works, and release outside the table clears active presses.
- Nested tab traversal and focus loss work, including traversal back to the outer composer.

For divider rendering, verify:

- A recording renderer receives the expected boundary geometry without needing a GPU.
- Row and cell boundaries respect allocated track sizes, gaps, and shorter rows.
- No-op rendering preserves the existing appearance.
- A new renderer implementation can be injected without modifying or adding style branches to the table.
- Renderer replacement invalidates artwork without changing layout or child ownership.
- Stroke widths and dash lengths scale once at fractional GUI scales, and boundary coordinates are not scaled twice.
- Divider drawing is clipped, Cairo state is restored, shared edges are not drawn twice, and child content renders above it.
- Row-only, cell-only, and combined treatments remain decisions of the renderer rather than table branches.
- Invalid style options fail explicitly rather than silently selecting a fallback style.

For HUD integration, verify visually in game:

- Controls retain their current spacing, keycap artwork, and expected dimensions.
- Long localized labels and alternate key combinations remain aligned without overlap.
- Fractional GUI scales apply scaling exactly once.
- Collapse preserves panel width and removes only section content height.
- Dynamic snapping and rotation values update without rebuilding the entire panel.
- Keybinding and GUI-scale changes rebuild with fresh measurements.
- Gameplay input continues passing through non-button areas.

Existing [section tests](../VanillaBuildingExpanded.Tests/UI/CollapsibleSectionTests.cs) and [collapse-button tests](../VanillaBuildingExpanded.Tests/UI/GuiElementCollapseButtonTests.cs) remain behavioral regression coverage; table layout must not alter their responsibilities.
