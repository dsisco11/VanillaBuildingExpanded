# GUI table layout component proposal

Status: Phases 1 through 7 complete; the user directed Phase 7 closeout on 2026-10-25 after automated acceptance and controls visual approval. F1 snapping/rotation table adoption is also implemented and passes automated acceptance, with separate in-game approval pending. Earlier detailed manual checks are retained as follow-up notes, not claimed passes.

Implementation progress is tracked in the [implementation TODO](GuiElementTable.implementation.todo).

## Purpose

Introduce `GuiElementTable`, a reusable owning GUI container that arranges a supplied collection of GUI elements into rows and aligned columns.

The table owns its accepted children, including their composition, rendering, input routing, focus management, and disposal. The outer composer registers only the table, not its individual children.

Rows are the content-facing abstraction. Callers supply a collection of elements for each row; they do not flatten the entire table, calculate cell indices, or provide placeholders for every column.

The component is independent of the build-mode HUD and collapsible sections. A table may be content within a section, dialog, or another layout, but it never references those owners or manages their behavior.

## Original layout before table adoption

The [build-mode HUD](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs) contains two table-like arrangements:

- Snapping and rotation used manually positioned label/value pairs.
- Controls used measured action-label and keybinding columns, with manually positioned rows.

Before Phase 6 adoption, the controls implementation calculated column widths in `MeasureControlsColumns()`, total content height separately, and row coordinates in `ComposeControlsContent()`. The replacement table preserves the minimum widths of 88 and 200 logical units, column gap of 12, and 20-unit rows without duplicating that layout arithmetic in the HUD.

This distributed related layout rules across measurement and composition. The implemented tables centralize those rules while leaving label creation, keybinding measurement, and keycap drawing with the caller.

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

The ordinary row API accepts elements directly:

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

## Phase 1 contracts

Phase 1 finalizes the contracts below without implementing the component. Passive defaults, explicit input flags, and caller-supplied cancellation callbacks were selected during Phase 1. Later implementation must preserve these contracts or document and obtain approval for a material change.

### Phase 2 implementation status

[GuiElementTable](../VanillaBuildingExpanded/src/UI/GuiElementTable.cs) implements row acceptance, measurement, arrangement, preferred-size updates, ordinal child lookup, coalesced layout-invalidated notifications, and idempotent child cleanup. [TableLayout.cs](../VanillaBuildingExpanded/src/UI/TableLayout.cs) contains the immutable metadata and option snapshots.

The constructor accepting all rows validates the complete layout, including aggregate dimension overflow, before accepting any element. Incremental additions validate each row atomically; complete column-index configuration is validated during measurement so rows can be built progressively.

Child footprints include native fixed padding once. Table bounds themselves require fixed sizing, no existing children, and zero padding; use an enclosing layout for outer padding. Arrangement owns child positioning: it clears offsets, margins, and sibling-relative positioning and sets top-left alignment while preserving preferred footprints and internal child bounds.

External replacement, detachment, shared bounds, cycles, and changes to accepted root padding are rejected rather than repaired silently. Native percentage/fit-to-children sizing and dynamic-text auto-height are rejected. Custom children must honor the fixed-size contract; the table cannot infer arbitrary custom wrapping logic.

The internal `MarkRegistered()` transition freezes row structure and is covered by tests. It is reserved for the P4 registration helper; the component is not yet ready for native composer registration or HUD use. Divider and artwork-invalidation APIs are implemented in Phase 3 as described below; public input-routing APIs remain contracts for later phases, not implemented stubs.

The focused UI suite passed 61 tests, including existing section/button regressions. See the TODO for the command and native Cairo test prerequisite.

### Component surface

The following surface spans the full design; the Phase 2 layout/ownership and Phase 3 divider subsets are implemented:

```csharp
GuiElementTable(ICoreClientAPI api, ElementBounds bounds,
    TableOptions? options = null);
GuiElementTable(ICoreClientAPI api, ElementBounds bounds,
    TableOptions options, IEnumerable<IEnumerable<GuiElement>> rows);

void AddRow(params GuiElement[] elements);
void AddRow(IEnumerable<GuiElement> elements);
void AddRowCells(IEnumerable<TableCell> cells);

TableSize Measure();
void Arrange();
GuiElement GetElement(string key);
void SetPreferredSize(GuiElement element, TableSize size);
TableDividerGeometry GetDividerGeometry(double guiScale);
void InvalidateArtwork();
void CancelInput();

ITableDividerRenderer DividerRenderer { get; set; }
bool IsRegistered { get; }
event Action? LayoutInvalidated;
```

- Keep element-array and enumerable forms under `AddRow`. Use the distinct name `AddRowCells` for descriptors, avoiding competing element/descriptor `params` overloads and ambiguous empty collection expressions.
- The complete-row constructor takes options before rows and has four required arguments. It cannot compete with the optional three-argument constructor. Descriptor-based tables can be assembled through `AddRowCells`.
- `TableCell` is immutable metadata containing an element, optional key, optional preferred size, input flags, and optional cancellation callback. Constructing a descriptor does not transfer ownership.
- `TableOptions` is immutable configuration containing column definitions by zero-based index, column gap, row gap, and minimum row height. Snapshot supplied collections; do not retain mutable caller-owned dictionaries.
- `TableColumn.Auto(minimumWidth = 0)` and `TableColumn.Fixed(width)` describe column policies. Unconfigured inferred columns use auto sizing with minimum zero.
- `TableSize` is an immutable width/height pair in logical GUI units. It describes the cell's reserved outer footprint, including any native fixed padding. Arrange must translate that footprint to native content dimensions without counting padding twice.
- `Measure()` validates the complete layout and returns its preferred dimensions without changing bounds or allocating drawing resources. It can cache its result.
- `Arrange()` applies that measurement to the table and child bounds at the table's configured origin. It does not move or resize the surrounding panel. Mark affected bounds dirty before native world-bound calculation.
- `GetElement()` uses case-sensitive ordinal keys and throws for an unknown key; it does not return a silent default. Caller-held typed child references are also supported.
- `SetPreferredSize()` accepts only an element owned by this table. It updates the reserved footprint rather than inferring a new size from already-arranged bounds.
- Reject null inputs, invalid flag values, and invalid dimensions with explicit argument exceptions. Reject structural changes after registration with `InvalidOperationException`; reject public mutating operations after disposal with `ObjectDisposedException`.
- `Dispose()` remains idempotent. Native render/input callbacks after disposal do no work; they must not invoke children or recreate resources.

### Explicit child-input configuration

Define flags independently of rendering:

```csharp
[Flags]
internal enum TableInput
{
    None = 0,
    Pointer = 1,
    Keyboard = 2
}
```

`TableCell.Input` defaults to `None`. Ordinary `AddRow` elements are therefore passive, including dynamic text and elements whose native `Focusable` property is true. Descriptors opt children into pointer input, keyboard input, or both.

`TableCell.CancelInput` is an optional `Action` which becomes required when `Pointer` is enabled. Validate that requirement before accepting the row. Its contract is idempotent, non-activating cleanup: clear any press, drag, or pointer-capture state without clicking, committing, or changing a value. Stateless controls may supply an explicitly documented no-op callback; the table never invents one.

Native `GuiElement` has `OnFocusLost()` but no universal cancellation method. Its default focus-loss implementation only clears focus. Native button and slider mouse-up handlers can invoke callbacks, so synthetic mouse-up is not a safe generic cancellation strategy.

Do not use reflection, concrete-control switches, fabricated events, or assume `CancelPress()` exists. A caller must provide a safe adapter/callback for pointer-enabled controls. If a native control cannot expose non-activating cancellation, it cannot be opted into pointer interaction under this contract until a suitable adapter exists; it can still be displayed passively.

The table forwards pointer events only to opted-in children, never passive cells. Respect handled flags and reverse visual order for hit testing. Track every child/button interaction to which a down event was delivered, including native controls that may alter private drag state without marking the event handled.

Normal matching mouse-up reaches those tracked children even outside their bounds. The table then clears tracking and invokes their cancellation callbacks to ensure cleanup. If another recipient has already handled the release, cancel the tracked interaction without forwarding an activating release. Do not forward the same release twice to one child.

`CancelInput()` cancels tracked pointer interactions, clears cursor/capture state, and removes nested child focus. Invoke it on table focus loss and disposal. The host must also call it when hiding, disabling routing, or grabbing the gameplay mouse without disposing the table; non-focusable pointer controls cannot rely on keyboard focus-loss notifications.

Rendering remains independent of input eligibility: every owned child receives composition and dynamic-render hooks.

### Nested focus and traversal

- Only children with `Keyboard` enabled and native `Focusable == true` participate in nested focus. Preserve row-major insertion order for tab traversal, independently of draw order.
- Determine group focusability before composer registration, since the composer assigns tab indices at registration time. Input flags and row structure are immutable after registration.
- Route keyboard events only to the focused eligible child while the table has focus. Do not broadcast activation keys to unrelated children.
- On keyboard entry, focus the first eligible child for forward traversal or the last for reverse traversal. Observe unhandled Tab/Shift-Tab before composer focus assignment to retain entry direction.
- Pointer activation may select an eligible child before the composer focuses the group; group focus gain preserves that selection.
- Give the focused child first opportunity to handle Tab. Otherwise move within the group and mark handled only when an internal move occurs.
- At either end, leave Tab unhandled for the outer composer. Do not wrap internally. With no other outer focus target, preserve existing focus rather than inventing a new destination.
- On exit, call `OnFocusLost()` on the focused child and cancel pointer interaction. Group focus loss must not reactivate a child.

### Ownership and registration failures

| Situation | Ownership result |
| --- | --- |
| Descriptor created or a row collection enumerated | Caller still owns its elements. |
| Entire `AddRow`/`AddRowCells` succeeds | Table owns that row's elements. |
| Row enumeration or validation fails | No element in that addition transfers; previously accepted rows remain table-owned. A duplicate reference to an already-owned child remains owned by its original owner. |
| Complete-row constructor fails during enumeration or validation | Validate the entire input before accepting any child; caller retains every supplied element. |
| Table exists but is never registered | Caller owns and must dispose the table; disposal releases all accepted children. |
| Table is successfully registered | Composer owns the table; table continues owning all children. |
| Conditional composer addition is skipped | Caller still owns the unregistered table. |
| Registration throws | Determine whether native registration already inserted the table, as described below; do not assume rollback. |

Constructor validation and pre-registration layout failures must not leak native resources. Allocate no texture until composition. Complete predictable validation before committing ownership or changing bounds parentage.

Use a composer extension with an explicit result:

```csharp
bool TryAddTable(this GuiComposer composer, GuiElementTable table, string key);
```

Return `true` only when the table is registered. Return `false` only when a native conditional-add block skips registration; ownership remains with the caller. Invalid keys, duplicate registration, a composed composer, conflicting parentage, and invalid layout throw rather than returning a success-shaped result.

Require an explicit nonblank composer key and preflight key collisions, bounds cycles, table state, and layout before calling native `AddInteractiveElement()`. Afterward verify identity through `composer.GetElement(key)` before setting `IsRegistered` and freezing structure.

Native registration inserts into element collections before completing all bounds work, so exceptional failure is not inherently atomic. If the composer already exposes this table after an exception, mark it registered/frozen and let that composer retain disposal responsibility; propagate the error and require the caller to abandon/dispose the failed composer rather than continue using a partial graph. If it was not inserted, the caller retains disposal responsibility. Do not clear or dispose unrelated composer content inside the registration helper, and do not claim to roll back private native collections.

`TryAddTable` is the supported registration path. Direct native registration of the table bypasses this ownership/freeze contract and is not supported.

### Deferred invalidation

- Maintain separate layout-dirty and artwork-dirty state.
- Row additions before registration and preferred-size changes invalidate layout and artwork. Coalesce `LayoutInvalidated` notifications until `Arrange()` consumes the pending layout.
- The owner subscribes before registration and processes invalidation at its next safe GUI composition boundary: call `Measure()`, resize any affected surrounding layout, then `Arrange()` before composer bounds calculation/rendering.
- Do not silently resize a registered table during rendering while its surrounding layout is stale. The owner must settle pending layout before invoking its composition/render lifecycle.
- Renderer replacement and `InvalidateArtwork()` mark only static artwork dirty. At the next safe table render/composition entry, rebuild artwork once using settled bounds; do not call the outer composer's rebuild methods from a setter or input callback.
- A renderer change uses the same non-null interface path and does not raise `LayoutInvalidated`.
- Dynamic child value updates within reserved dimensions use the child's existing update method. Static child content changes require `InvalidateArtwork()`. Neither requires table reflow unless the caller changes preferred size.
- The host cancels active input before rebuilding or disposing composition. GUI-scale and localization changes use the host's existing rebuild path, with new content measurements as required.

### Native API evidence

These contracts account for the local API implementation:

- [GuiElement](../../vsapi/Client/UI/Elements/Impl/GuiElement.cs) provides focus loss but no generic pointer-cancellation hook, and its default mouse-down handler consumes an event over the element.
- [GuiElementTextButton](../../vsapi/Client/UI/Elements/Impl/Interactive/Controls/GuiElementTextButton.cs) and [GuiElementSlider](../../vsapi/Client/UI/Elements/Impl/Interactive/Controls/GuiElementSlider.cs) can activate or commit during mouse-up, so release and cancellation are distinct operations.
- [GuiComposer](../../vsapi/Client/UI/GuiComposer.cs) assigns group tab indices on addition, delegates unhandled Tab traversal, can skip conditional registration, and can throw after inserting an element.
- The existing [collapse button](../VanillaBuildingExpanded/src/UI/GuiElementCollapseButton.cs) demonstrates explicit non-activating cancellation through `CancelPress()`, but that capability is not universal.

Sibling-source links assume the existing local checkout layout.

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
- For content whose size requires calculation, allow per-cell preferred-size metadata through `AddRowCells`.
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

Solid and dashed implementations demonstrate the extension point; they are not a restriction on future implementations or approved final HUD artwork. A custom implementation may draw a different treatment without modifying `GuiElementTable`.

Choosing row-only, cell-only, or both boundary kinds, and configuring colors, stroke widths, and dash patterns belong to the renderer or its immutable options. The table must not interpret those style settings. Any selection UI or style-name-to-renderer mapping belongs outside the table.

Renderer implementations are immutable configuration objects with no retained Cairo surfaces, contexts, or GPU resources, so they may be shared between tables. The table owns the resulting texture, not the injected renderer's lifetime.

Replacing the renderer invalidates the table's static artwork but does not require layout remeasurement. Recompose through the existing safe lifecycle rather than drawing or uploading immediately from an input callback. Invalid renderer configuration must fail explicitly; do not silently fall back to a different style.

### Phase 3 implementation status

[TableDividerGeometry.cs](../VanillaBuildingExpanded/src/UI/TableDividerGeometry.cs) provides immutable device-space segments, boundary kinds, drawing extents, and GUI scale. `GetDividerGeometry(guiScale)` snapshots the last settled arrangement; requesting it before arrangement or after layout invalidation fails explicitly. Existing snapshots remain independent of subsequent layout changes.

[TableDividerRenderers.cs](../VanillaBuildingExpanded/src/UI/TableDividerRenderers.cs) implements `ITableDividerRenderer`, `TableDividerOptions`, and no-op, solid, and dashed strategies. Options validate RGBA components, boundary masks, positive logical stroke/dash dimensions, finite offsets, and representable scaled dimensions and dash periods. Renderers retain no Cairo or GPU resources.

For example, selecting solid row dividers requires no table-specific style branch:

```csharp
table.DividerRenderer = new SolidTableDividerRenderer(
    new TableDividerOptions(
        new Color(1, 1, 1, 0.25),
        strokeWidth: 1,
        boundaries: TableDividerKind.Row));
```

The table's internal `DrawDividers(context, guiScale)` supplies clipping and state/path isolation and calls only the injected interface. Line strategies align stroke edges to device pixels and paint selected segments in one Cairo stroke so translucent intersections do not accumulate alpha. Colors, boundary selection, dash phase, and line mechanics remain outside the main table class.

Measured extents, child positions, and divider endpoints now share one track-origin calculation, avoiding rounding drift in long fractional layouts. Zero drawable extent produces no divider segments.

Replacing the renderer or calling `InvalidateArtwork()` marks static artwork dirty without changing child positions or raising layout invalidation. P4 will consume that dirty state, call divider drawing before child composition, and upload the resulting texture; this phase does not yet render a complete table through the native composer.

[TableDividerTests.cs](../VanillaBuildingExpanded.Tests/UI/TableDividerTests.cs) verifies geometry, strategy replacement, fractional scaling, offscreen pixel output, dash phase, intersection alpha, clipping, and state/path restoration. The combined UI suite passed 110 tests; no live GPU or in-game visual claim is made for this phase.

## Composer integration and lifecycle

Provide `TryAddTable(...)` as specified in the Phase 1 contracts to register the table as one interactive element. This registration is required for runtime rendering even when all its content is informational.

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
- Compose every child into the table's own Cairo surface and upload the static artwork to a table-owned texture, except when the complete table has zero drawable width or height.
- Render that texture, then invoke child dynamic rendering in stable draw order; forward the native post-render stage in the same order.
- Preserve clipping, premultiplied-alpha conventions, and child draw order across composition and runtime rendering.
- Propagate clip bounds to children both on addition and when the table's clip bounds change.
- Avoid zero-sized texture allocation for an empty or zero-extent table; release a previous texture and skip child composition/rendering until it has drawable extent again.

No per-child static/dynamic registration mode is necessary. For a drawable table, every child receives its composition and dynamic-render hooks; static-only elements have no dynamic artwork to render. Dynamic text continues rendering even though it is not registered with the outer composer.

#### Phase 4 implementation evidence

- [GuiElementTableComposerExtensions.cs](../VanillaBuildingExpanded/src/UI/GuiElementTableComposerExtensions.cs) implements `TryAddTable()`, including conditional skips, preflight validation, and ownership tracking after partial native insertion. It arranges the table before native world-bound calculation or surface allocation. It does not register children or alter unrelated composer content.
- [GuiElementTable.cs](../VanillaBuildingExpanded/src/UI/GuiElementTable.cs) owns a lazily allocated, reusable `LoadedTexture`. Native child preparation runs before composer bounds calculation; preparation does not replace the reserved logical footprints. Static child composition and both dynamic render stages use stable ascending draw order, with row-major insertion order retained for ties.
- Static drawing uses table-local coordinates. Cairo clips to the exact drawable extent; pixel allocation rounds fractional extents outwards. Runtime rendering draws the cached texture pixel-for-pixel and intersects table/enclosing scissors, restoring them even if a child throws.
- Renderer replacement and static artwork invalidation rebuild lazily at the next composition/render entry without requesting an outer composer rebuild. Invalidations raised during child composition survive for the next pass. Pending layout must still be settled by the owner; rendering never silently calls `Arrange()`.
- Child clip/alpha conventions are propagated. Cairo artwork stays premultiplied for native premultiplied rendering; straight-alpha mode uses an explicit native-endian ARGB32 conversion before upload. Pixel tests exposed an incorrect alpha-byte selection in the installed Cairo wrapper's `DemulAlpha()` helper on little-endian Windows, so the table does not use that helper. The local conversion preserves alpha, all three color channels, and fully transparent pixels without modifying the game's libraries.
- [TableCompositionTests.cs](../VanillaBuildingExpanded.Tests/UI/TableCompositionTests.cs) adds 38 cases covering concrete native composer registration/composition, actual static/dynamic text, sizing before fit-to-children composer allocation, local coordinates, draw ordering, clipping, scale, alpha pixels, deferred invalidation, zero extent, failure cleanup, and repeated disposal. The focused UI suite passes 148 tests with zero failures or skips; existing unrelated compiler warnings remain.
- Texture uploads and GPU rendering are mocked in these tests; CPU Cairo rasterization is real. Verify actual GPU upload/display in the later in-game pass. Input/focus routing and HUD migration have not been implemented in Phase 4.

Keep optional child keys local to the table, exposed through a lookup such as `table.GetElement(key)`. The composer can retrieve the table by its own key, but cannot retrieve nested cells directly. Callers may alternatively retain typed, non-owning child references.

### Input and focus

Forward relevant input to children and respect handled flags, clipping, and draw order. Do not invoke base mouse handling that consumes otherwise unhandled events over empty table space or passive content.

The table must be non-focusable when it has no keyboard-enabled focusable children. When it does, expose a focusable group to the outer composer and manage child focus and tab traversal internally. At the group's traversal boundary, allow focus to move through the outer composer. Propagate focus loss and expose the hovered input-eligible child's cursor.

Mouse release and cancellation must reach an active child even when the pointer moves outside its bounds, so controls cannot remain pressed. Do not make informational content capture pointer input merely because its native base handler marks events handled; input eligibility must distinguish passive cells from interactive controls. This policy is separate from child rendering, and must not use `Focusable` alone as a clickability test.

The containing HUD or dialog retains the final say over whether the table receives input at all. Apply the explicit passive-default flags and cancellation-callback contract above; do not infer input eligibility from child type.

#### Phase 5 implementation evidence

- [GuiElementTable.Input.cs](../VanillaBuildingExpanded/src/UI/GuiElementTable.Input.cs) contains the table's input lifecycle as a partial class, separate from layout and texture composition. Production routing uses only native element APIs and immutable input metadata, without control-type switches or reflection.
- Press/wheel hit testing uses reverse visual order, including reversed insertion order for equal draw orders. Passive cells, gaps, clipped content, and outside points do not consume events. Captured movement precedes hover-only movement, and the previous hover recipient receives an exit move to clear hover state.
- Every press recipient is tracked by button, including children that leave the press unhandled. Matching release reaches captured children outside bounds. Already-handled releases cancel without forwarding activation; once a recipient handles release, remaining recipients only receive cancellation. Repeated presses cancel the previous interaction, and a move is not duplicated for a child captured under multiple buttons.
- `CancelInput()` clears capture, cursor, group focus, nested focus, and pending entry selection before invoking cleanup. Cancellation callbacks cannot reenter routing to create new captures. Cleanup processes remaining recipients after failures and surfaces aggregate errors. Event delivery stops appropriately when a child cancels or disposes its owning table.
- Keyboard focus eligibility is independent of pointer eligibility. Traversal stays row-major regardless of draw order; the focused child gets first opportunity to handle Tab. Unhandled boundary Tab remains available to the native composer, preserving current child focus if no other outer target exists.
- Pointer activation selects only an eligible clicked child before native composer group-focus assignment. Clicking a pointer-only control does not select an unrelated keyboard child; Tab can subsequently enter the group's keyboard participants. Focus loss and internal focus changes cancel outgoing capture without fabricating mouse-up.
- Cursor propagation runs after dynamic child rendering as well as during pointer routing, so the native composer sees current child cursors without requiring a mouse-move event. Leaving eligible content, clip changes, cancellation, and disposal clear stale cursor state.
- [TableInputTests.cs](../VanillaBuildingExpanded.Tests/UI/TableInputTests.cs) adds 41 cases, with another native composer cursor case in [TableCompositionTests.cs](../VanillaBuildingExpanded.Tests/UI/TableCompositionTests.cs). Coverage includes real collapse-button activation/cancellation, concrete native composer forward/reverse entry and exit, scaled world-coordinate hits, handled input, captures, failure cleanup, and reentrant cancellation/disposal.
- The combined UI suite passes 190 tests with zero failures or skips and builds both projects. Existing unrelated compiler warnings remain. HUD pointer/keyboard routing is unchanged; its controls-content migration is still Phase 6, and in-game verification remains Phase 7.

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
- A preferred-size change invalidates table layout and artwork and raises the coalesced `LayoutInvalidated` notification. The surrounding owner settles measurement and arrangement at a safe composition boundary; the table rebuilds its artwork with those settled bounds.
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

### Phase 6 implementation evidence

- [BuildModeHud.cs](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs) now builds a passive two-cell row per keybinding hint through `CreateControlsTable()`. Both dimensions come from the table's measurement through the existing section callbacks. The section and table classes were not changed for this integration.
- Auto-column minimums are 88/200, column gap 12, minimum row height 20, and row gap zero. The default no-op divider renderer adds no new artwork. The HUD retains text/keybinding preferred-footprint measurement and the existing keycap/alternate-binding drawing logic.
- Action labels use resource-free custom-drawing children with composition-scoped fonts. Since cells retain their preferred widths rather than stretching, label artwork reads the table-arranged binding origin to obtain the allocated shared label-column width. This preserves native static-text drawing, origin rounding, and existing line-breaking behavior instead of introducing narrower per-cell wrapping or changing text appearance. No column maxima or row placement are calculated by the HUD.
- `ComposeHud()` scopes the measurement table to a single build. Expanded content transfers ownership through `TryAddTable()`; collapsed content is measured for stable expanded width and then disposed unregistered. The pending reference is cleared after every build. If composition fails, the local composer and any still-unregistered table are disposed before propagating the error.
- Successful tables remain composer-owned across hide/reopen; the existing composer-clear paths release them on toggles, rebinding, GUI-scale changes, and HUD disposal. No input flags are enabled for controls cells. HUD routing remains exclusively to collapse buttons with a free cursor; keyboard, mouse movement, wheel, and passive controls still pass through to gameplay.
- [BuildModeHudControlsTests.cs](../VanillaBuildingExpanded.Tests/UI/BuildModeHudControlsTests.cs) adds 20 cases covering row layout, legacy dimension formulas, native label pixel equivalence at scales 1, 1.25, 1.33, 1.5, and 2, section wiring, collapsed measurement ownership, native HUD composition/cleanup, rebinding, scale changes, collapse/expand, hide/reopen, and passive input. Upload/render APIs and the icon bitmap are mocked; native GUI lifecycle and Cairo drawing are exercised.
- The combined UI and keybinding command passes 216 tests with zero failures or skips and builds both projects. Existing unrelated compiler warnings remain. Actual GPU display and in-game appearance/input acceptance are still Phase 7, not claimed by these automated results. Snapping/rotation content and update lookups remain unchanged.

### F1 implementation evidence: snapping and rotation

- [BuildModeHud.cs](../VanillaBuildingExpanded/src/BuildBrush/Hud/BuildModeHud.cs) creates a separate passive information table at logical (12,54). Fixed 88/200 columns, gap 8, and 24-unit rows preserve the previous label/value origins and 296x48 reserved footprint. The table owns native static labels and dynamic values and retains the no-op divider default.
- Only `information-table` is registered with the composer. `snapping` and `rotation` are table-local keys used by `Update()`. Title, icon, panel background, and section chrome remain outside both tables, and HUD input routing is unchanged.
- Value updates preserve snapping localization, indexed/angle rotation strings, suspension and unsupported-state colors, and native force-redraw on availability changes. Dynamic textures update within their fixed bounds without table reflow, static-artwork invalidation, or HUD rebuilds. Updates while hidden or while controls are collapsed remain supported.
- Information-table ownership follows registration even on failure: local cleanup handles an unregistered table, while the successful or failed composer owns any registered table. Existing rebuild paths release both tables.
- [BuildModeHudControlsTests.cs](../VanillaBuildingExpanded.Tests/UI/BuildModeHudControlsTests.cs) adds 13 cases and extends repeated lifecycle checks to both tables. Exact native value world coordinates and static-label pixels match at scales 1, 1.25, 1.33, 1.5, and 2. Tests also cover passive input, initial and updated state text/colors, texture stability, collapsed/hidden updates, child detachment, and retry after dynamic/table upload failures.
- The combined UI, keybinding, and mode-indicator filter passes 243 tests with zero failures or skips, builds both projects, and requires no manual PATH adjustment. Existing unrelated compiler warnings remain. This is real Cairo/native composition with mocked GPU services and icon bitmap, not in-game F1 approval.

Future room-information content can use the same row-oriented API without adding room-specific behavior to the table.

## Scope

Initial scope includes row collections, inferred columns, auto/fixed widths, minimum sizes, gaps, preferred-size metadata, table-local child keys, child ownership, static and dynamic rendering, interface-based row/cell divider styles, nested input and focus, idempotent disposal, and composer integration.

Initial scope excludes column or row spanning, interior empty-cell placeholders, sorting, selection, headers, outer table borders, scrolling, virtualization, automatic shrinking, wrapping, stretch alignment, and cross-table column synchronization.

These features can be proposed separately when a concrete consumer requires them.

## Verification and acceptance criteria

### Phase 7 verification evidence (2026-10-25)

- The explicit production build succeeds under .NET SDK 10.0.401 with the installed game references, zero errors, and nine existing warnings.
- The focused UI/keybinding suite passes 222 tests with no failures or skips. New acceptance cases in [TableCompositionTests.cs](../VanillaBuildingExpanded.Tests/UI/TableCompositionTests.cs) execute no-op/solid/dashed/no-op transitions with actual Cairo pixels, dynamic values, short rows, different row heights, stable geometry, texture reuse, and single cleanup at GUI scales 1, 1.33, and 2.
- New cases in [BuildModeHudControlsTests.cs](../VanillaBuildingExpanded.Tests/UI/BuildModeHudControlsTests.cs) run eight cycles per scale through collapse/expand, close/reopen, and rebinding. They verify old child detachment, stable dimensions, pending-table cleanup, and exactly one deletion per owned texture.
- A normal test run reproduced the user's missing native Cairo failure, which earlier PATH-adjusted validation had hidden. The Windows [test project](../VanillaBuildingExpanded.Tests/VanillaBuildingExpanded.Tests.csproj) now copies the installed game's native Cairo and Skia DLLs into test output. This supports ordinary CLI/VS Code runs without manual PATH setup or new dependencies.
- The complete suite then passes 663 tests with zero failures or skips using `dotnet test .\VanillaBuildingExpanded.Tests\VanillaBuildingExpanded.Tests.csproj --no-restore --verbosity minimal`, without changing PATH. The reported failures and shared test-host dependency fix justified expanding beyond the focused suite.
- Automated coverage uses real native GUI composition and Cairo rasterization but mocked GPU services and icon bitmap. It does not establish actual GPU upload/display, full icon/background fidelity, gameplay pass-through, or real shutdown resource behavior.
- After the native test-library fix, the user reported "okay it looks fine", approving the displayed controls appearance, then explicitly directed Phase 7 closeout on 2026-10-25. The first-delivery gate is closed on that basis. Exact before/after dimensions, the full in-game GUI-scale/localization matrix, gameplay pass-through, and shutdown resource behavior remain unverified follow-up notes in the [implementation TODO](GuiElementTable.implementation.todo), not claimed passes. The subsequent F1 migration has its own implementation evidence above and separate pending in-game approval.

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
