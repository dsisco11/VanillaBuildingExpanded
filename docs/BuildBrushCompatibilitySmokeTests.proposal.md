# Build brush compatibility smoke test proposal

Status: Proposed. This document describes a test suite, not an implemented compatibility guarantee.

## Purpose

Create an asset-driven smoke suite that provides useful indications of which base-game collectibles our build-brush systems support and where those systems fail.

The suite should exercise real production brush code against reasonably faithful, stateful doubles for game services. It should detect problems in source selection, placement preparation, rotation, snapping, preview construction, placement dispatch, and rejection handling without requiring a running game.

The intended assurance is:

> Our systems can process this asset-derived collectible and issue the expected game-service calls under a documented simulated environment.

This is not proof that the collectible renders correctly or behaves correctly in every live-game environment.

## Scope

### Included

- Enumerate base-game block and item definitions and account for their concrete variants.
- Classify collectibles as brush-supported, intentionally unsupported, or not yet modeled.
- Preserve the definition metadata that affects our production decisions.
- Run a small set of representative smoke scenarios for each supported catalog entry.
- Verify simulated placement state and the arguments passed to game placement APIs.
- Verify preview creation and handoff to the relevant native tessellation boundary.
- Exercise accepted and rejected placement decisions.
- Produce a per-collectible coverage report with actionable failure stages.
- Reuse the existing xUnit/Moq test project and focused algorithm tests.

### Excluded

- Exhaustive combinations of orientations, faces, hit regions, properties, and world states.
- Validation of native game placement rules, physics, claims, collision, or attachment algorithms.
- Actual shape tessellation, texture-atlas initialization, mesh appearance, or visual correctness.
- GPU upload, rendering, screenshots, or graphics-device tests.
- A running client, dedicated server, or live-game automation.
- Adding brush support for item-interaction placement paths that the brush does not currently support.
- Fixing every incompatibility discovered by the suite as part of building the suite itself.

Actual mesh-generation tests are deferred and low priority. GPU tests are not part of this proposal.

## Existing foundation

The implementation should extend the existing test project rather than introduce another test framework.

- [BrushTestInputs](../VanillaBuildingExpanded.Tests/BuildBrush/BrushTestInputs.cs) supplies independent inventory, selection, and placement-validation inputs.
- [TestHelpers](../VanillaBuildingExpanded.Tests/BuildBrush/TestHelpers.cs) provides world, API, block, and mini-dimension doubles.
- [BrushPlacementValidationTests](../VanillaBuildingExpanded.Tests/BuildBrush/BrushPlacementValidationTests.cs) covers candidate selection, fallback, and selection cloning.
- [BuildBrushPlacementTests](../VanillaBuildingExpanded.Tests/BuildBrush/BuildBrushPlacementTests.cs) covers prepared stacks and orientation integration.
- [MiniDimensionTessellatorTests](../VanillaBuildingExpanded.Tests/BuildBrush/Tessellation/MiniDimensionTessellatorTests.cs) demonstrates synthetic meshes and native tessellation-service doubles.
- [BrushToTessellationFlowTests](../VanillaBuildingExpanded.Tests/E2E/BrushToTessellationFlowTests.cs) provides event-flow examples, but manually simulates part of the production connection.

The new suite must not mistake a test-authored simulation of brush orchestration for execution of that orchestration. Where the production path can be invoked headlessly, invoke it. Where it cannot, document the untested connection instead of claiming end-to-end coverage.

## Proposed architecture

### 1. Asset-backed compatibility catalog

Build a reusable catalog from a configured local base-game asset installation. Do not commit copies of the base-game asset collection.

Each entry should contain:

| Field | Purpose |
| --- | --- |
| Collectible code and kind | Stable identity and block/item distinction. |
| Source definition | Trace failures and loading limitations to their origin. |
| Concrete variant values | Supply real variant metadata and lookup relationships. |
| Block and entity class identities | Describe relevant placement and preview contracts. |
| Behavior identities | Support explicit classification and representative scenarios. |
| Brush-relevant attributes | Preserve inputs consumed by our orientation and stack logic. |
| Representative stack attributes | Exercise attribute-dependent processing where applicable. |
| Classification and reason | Make the support boundary reviewable. |
| Expected capabilities | Describe applicable rotation, preview, and placement expectations. |

Registry IDs may be assigned deterministically within the test catalog. Reports and expectations should use collectible codes, not installation-specific numeric IDs.

First investigate whether existing local game-loading code can provide resolved definitions without booting a game. If that is impractical, use a deliberately limited importer for the metadata our code consumes.

An importer must handle the definition constructs needed to resolve those fields, including relevant variant expansion, referenced variant properties, and per-type overrides. It must also state how applicable vanilla patches are handled. Unsupported constructs must produce explicit catalog diagnostics; they must not silently become empty attributes or generic blocks.

The importer is not intended to reproduce the entire game loader. Keep its scope limited and test its transformation rules using small authored fixtures. If a required loading semantic cannot be modeled reliably, mark the affected entries as not modeled.

Record the asset installation/version when available, an input fingerprint, and the test configuration in the report. Missing assets should fail the requested compatibility run clearly, not yield a passing run with zero cases.

### 2. Explicit support classification

The brush currently obtains its source through `ItemStack.Block` in [BuildBrushInstance](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs). Ordinary items that place content through custom interactions are not automatically brush-supported.

Classify the whole catalog, but do not force every collectible through a supported-block scenario:

- Supported block stacks run the applicable brush scenarios.
- Ordinary unsupported items run a safe-suspension or pass-through scenario where appropriate.
- Air, missing definitions, and other explicit exclusions receive documented expectations.
- Item-driven placement, such as planting and ground storage, is not reimplemented.
- Specialized block behaviors receive reviewed expectations rather than automatic acceptance based only on their block identity.

Classification must not be derived solely from the production result being tested. If a collectible unexpectedly resolves as non-rotatable or unsupported, that should be detectable rather than automatically accepted.

Use generic expectations for ordinary families and small, explicit overrides where necessary. Every exclusion needs a reason. Avoid building a second placement-rule engine inside the tests.

### 3. Stateful game-service doubles

Compose real brush components with a small in-memory world, collectible registry, inventory, and mini-dimension implementation or equivalent coordinated mocks.

The doubles should provide:

- Block lookup by ID and code, plus variant searches used by the brush.
- Mutable active inventory slots and independently cloned stack attributes.
- Block storage by position and dimension, with air for unoccupied cells.
- Block-entity creation, lookup, replacement, removal, and minimal serialized state.
- Explicitly advanceable deferred callbacks.
- Configurable placement acceptance/rejection and recorded call arguments.
- Placement mutations consistent with the configured result.
- Synthetic tessellation output and recorded native-boundary calls.
- Logs and operation records attributable to the current case.

Native placement overrides and entity lifecycle behavior should be controlled at the boundary. Preserve asset-derived metadata while substituting the engine behavior that is outside scope.

Do not use unconditional success returns as a substitute for state. A successful simulated placement must be observable through subsequent reads; a rejected placement must not create a block.

Unexpected service calls and unmodeled required operations should fail explicitly. Deliberate defaults, such as air at an empty position, should be documented harness behavior.

### 4. Real production code under test

Exercise the relevant production surfaces:

- Source refresh and prepared stacks in [BuildBrushInstance](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushInstance.cs).
- Orientation resolution and application in [BlockOrientationResolver](../VanillaBuildingExpanded/src/BuildBrush/BlockOrientationResolver.cs) and [BrushOrientation](../VanillaBuildingExpanded/src/BuildBrush/BrushOrientation.cs).
- Snapping and candidate fallback in [BrushPlacementValidation](../VanillaBuildingExpanded/src/BuildBrush/Placement/BrushPlacementValidation.cs).
- Preview contents and lifecycle in [BrushDimension](../VanillaBuildingExpanded/src/BuildBrush/BrushDimension.cs).
- Placement dispatch in the [client system](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Client.cs) and [server system](../VanillaBuildingExpanded/src/BuildBrush/BuildBrushSystem_Server.cs), to the extent their composition can be supplied headlessly.
- Native tessellation handoff in [MiniDimensionTessellator](../VanillaBuildingExpanded/src/BuildBrush/Tessellation/MiniDimensionTessellator.cs).

Prefer existing injection boundaries. If a production path requires a new seam, keep it narrow and behavior-preserving; do not broadly refactor gameplay code merely to accommodate the harness.

## Smoke scenarios and assertions

Use one deterministic baseline selection and simulated placement environment. Add a small number of targeted variations only when a capability requires them.

| Scenario | Required observations |
| --- | --- |
| Select supported source | The actual inventory path selects the expected block and produces usable brush state. |
| Prepare placement | The selected variant and relevant attributes reach a cloned prepared stack without changing the held source. |
| Apply rotation | One applicable rotation updates the expected variant or angle consistently; non-rotatable expectations are explicit. |
| Resolve snapping | A representative hit and snapping mode produce the expected destination and cloned selection. |
| Reject snapped candidate | The configured rejection exercises the production unsnapped fallback and forwards its resolved selection. |
| Construct preview | The expected block, support state, stack, and applicable block entity reach the mini-dimension; active bounds remain correct. |
| Refresh source | Representative attribute or slot changes refresh preview state without retaining stale properties. |
| Reach tessellation boundary | The production tessellator invokes the expected default-mesh, direct-tessellation, or block-entity hook with the expected input. |
| Accept placement | The production placement path passes the expected block, destination, prepared stack, and rotation; the simulated world reflects placement. |
| Reject placement | No placement mutation occurs, and the brush retains the intended handled/rejected semantics rather than falling through unexpectedly. |
| Select unsupported source | The brush suspends or leaves ordinary interaction available according to its existing contract. |

Not every entry needs every scenario. Basic selection, preparation, preview, and placement checks apply broadly; capability-specific scenarios apply to the corresponding families.

Detailed snapping boundaries, full orientation cycles, and controller edge cases remain in focused tests. The catalog sweep should not duplicate their entire input matrix.

### Tessellation boundary, not actual meshing

Run our tessellation orchestration, but replace native tessellation services and relevant block-entity hooks with recording doubles returning small synthetic meshes.

Assert the expected route and arguments. An ordinary block may use cached default mesh lookup without calling direct tessellation; a block entity may supply its own synthetic mesh.

Do not count "no exception" or a null result as sufficient evidence of a successful handoff. Account for caught failures and fallback routes explicitly. Synthetic geometry exists only to allow our orchestration to complete; it makes no claim about the vanilla shape.

### Placement result semantics

Distinguish "interaction handled" from "block placed." In particular, the server placement entry point can report that the brush handled an attempt even when native validation rejects it.

Assertions must inspect recorded calls and simulated world state, not interpret a handled return value as proof of placement.

## Reporting and failure policy

Produce a machine-readable report and a concise test-run summary. Each catalog entry should report applicable scenarios, their outcomes, and any coverage limitations.

Use these explicit outcomes:

- **Covered/pass:** applicable assertions succeeded under the modeled contracts.
- **Covered/fail:** our code produced an unexpected result or failed to reach an expected boundary.
- **Expected rejection:** the configured rejection contract behaved correctly.
- **Intentionally unsupported:** a reviewed support exclusion, with its reason and any pass-through assertion.
- **Unclassified/not modeled:** coverage is incomplete because classification or harness fidelity is missing.

Failures should include collectible code, source definition, representative attributes, expected capability, failing stage, expected/actual values, and relevant calls or logs.

Unexpected new catalog entries, unsupported required loading constructs, and missing mandatory scenarios must be visible failures until explicitly classified or acknowledged as coverage gaps. Acknowledged gaps must remain in the report and must not be presented as covered passes.

Keep known production defects distinct from harness limitations. Any temporary known-failure allowance should be explicit and should detect when the case unexpectedly starts passing so the allowance can be removed.

## Execution and isolation

- Run in the existing test project with a focused category/filter.
- Keep catalog loading reusable, but isolate mutable world, inventory, brush, entity, and callback state between cases.
- Dispose subscriptions and preview resources and discard pending callbacks after each case.
- Make case ordering deterministic and avoid assumptions that depend on previous cases.
- Report catalog/scenario counts so an accidentally empty or shrinking sweep is apparent.
- Keep ordinary focused unit tests runnable without a base-game asset installation.
- Measure runtime during the pilot before choosing parallelism or setting a runtime budget.

## Implementation phases

### Phase 1: Catalog and representative pilot

Establish asset-loading feasibility and build the minimum stateful harness.

Exercise ordinary blocks, variant rotation, rotatable block entities, an attribute-driven preview, a specialized placement family, and an unsupported item interaction.

Exit criteria: these cases execute real brush code through simulated placement and native tessellation boundaries, with useful diagnostics and clean isolation.

### Phase 2: Broad catalog sweep

Expand enumeration and classification across the configured base assets. Add the shared smoke scenarios and per-collectible report.

Exit criteria: every discovered entry is accounted for, applicable scenarios execute, and gaps are distinguishable from production failures.

### Phase 3: Hardening and regression coverage

Add family-specific expectations where generic smoke cases are insufficient. Exercise source refresh, rejection paths, and failure diagnostics. Retain focused regressions for discovered defects.

Exit criteria: repeated runs are deterministic, no cross-case state leaks remain, and the report supports practical investigation of broken collectibles.

## Estimated effort and risks

Planning estimate for one developer familiar with the repository:

| Work | Estimate |
| --- | --- |
| Asset catalog and support classification | 2-4 working days |
| Stateful placement/preview harness | 2-3 working days |
| Shared smoke scenarios and reporting | 2-3 working days |

Allow approximately one to two working weeks for a useful first version, with some overlap between tasks. This is not a commitment and excludes fixing discovered gameplay defects.

The largest risks are resolving asset metadata faithfully without booting the game, supplying representative entity contracts without invoking native initialization, and avoiding tests whose expectations merely mirror the implementation.

The pilot should resolve those risks before expanding the sweep. Do not respond to loading difficulties by silently reducing fidelity, or to harness difficulties by replacing production brush logic with test-authored equivalents.

## Acceptance criteria

The initial suite is successful when:

1. It runs headlessly without GPU or native rendering services.
2. Every discovered base-game collectible is represented in the coverage accounting.
3. Supported cases are backed by real definition metadata rather than names attached to generic synthetic blocks.
4. Applicable smoke scenarios execute our production brush logic.
5. Placement doubles provide observable accepted/rejected state transitions and argument verification.
6. Preview cases verify the expected native tessellation handoff without generating real vanilla meshes.
7. Support exclusions, known defects, and modeling gaps are explicit and distinguishable.
8. Failures identify the collectible and failing stage sufficiently to reproduce and investigate them.
9. Cases are isolated and repeatable, and existing focused tests remain usable independently.

The deliverable is a practical compatibility signal for our systems, not a certification of base-game behavior.
