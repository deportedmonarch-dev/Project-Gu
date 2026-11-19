# RFC-0001: Gu Framework Foundation

**Status:** Proposed for architectural review  
**Milestone:** 0.2.5 · **Target:** RimWorld 1.7 / .NET 10  
**Branch:** `feature/gu-framework-0.2.5`  
**Baseline:** player-validated 0.2.4 (10/10 manual tests); no claims of automated 1.7 runtime validation

## Mission and non-negotiable design goals

Project GU must enable developers to add *ordinary and extraordinary* Gu species without editing a central species switch or copying the Hope Gu implementation. The system is the common foundation for all future Gu, Gu inventory/refinement, wild ecosystems, abilities, Gu Houses, and compatible add-ons.

**Universal means extensible, not universal behavior:** Gu may be immobile, individually unique, swarming, consumable, owned, unowned, parasitic, environmental, or require specialized world effects. Do not force all Gu to use pawn AI, reproduction, essence costs, feeding, ownership or a physical Thing. XML defines common data; isolated C# behaviors implement irreducibly exceptional effects.

**Invariant:** No creation, disappearance, or duplication of individual Gu, except explicitly modeled birth, synthesis, destruction and consumption.

## Concrete findings from source review

1. `CompGuSpiritSpring` owns Hope-Gu-only `matureHopeGu`, `juvenileHopeGu`, `carryingCapacity` and `TryConsumeHopeGu`. The ecology model is not yet species-general.
2. Spring save keys include `guHopeGuAvailable`, `guMatureHope024`, `guJuvenileHope024`, `guHopeCapacity024`, `guFloraSupport024`, `guNextEcology024`, `guReserve024`, production and legacy recovery fields. Maintain migration compatibility.
3. `GuEcologyMapComponent` maintains a transient spring registry; `CompGuSpiritFlora` sends event notifications with a periodic safety recount. This is appropriate for loaded-map ecology, not an off-map world simulator.
4. `GuWorldComponent` persists `guPawnRecords`. That is a cultivator/awakening registry, **not** a registry of Gu instances. Do not conflate them.
5. **Transaction risk:** `GuAwakeningService.TryAwakenAtSpring` changes the pawn through `TryAwaken()` then invokes `spring.TryConsumeHopeGu()` without checking its return value. Another system should coordinate eligibility, Gu consumption and irreversible awakening at one commit point. This is a design defect, not evidence of a currently observed player bug.
6. `GuContracts.cs` holds educational/institution adapter contracts, not Gu-species types.
7. Existing `ThingComp.PostDestroy(DestroyMode, Map)` is compiler-supported in the tested hotfix; `PostDeSpawn(Map)` was **not** supported. Confirm all new hooks against actual RimWorld 1.7 assemblies.

## Proposed bounded contexts

### Species catalog

`GuSpeciesDef : Verse.Def` (name preliminary) contains immutable species identity, rank constraints, tags/paths, presentation hints and optional behavior configuration. Species definitions refer to reusable specialized behaviors, e.g. activation, feeding, ecological lifecycle, ambient influence. Validate unknown/missing behavior types and conflicting capabilities at Def resolution.

A species may exist at more than one rank, but **rank is not assumed to equal species identity**. Do not model every unusual Gu as an enum variant. All cross-mod references use stable Def names and optional dependencies.

### Individual identity and containment

An individually materialized Gu receives a stable, save-backed identity. Identity, species, rank, condition, refinement state, refiner relationship, current holder and materialization location are distinct data, **not a pile of independent flags**. Ownership and physical possession may differ.

**Authority choice is a gating decision:** either the Thing/ThingOwner save graph is the canonical persistence owner with IDs indexed elsewhere, or a central registry owns records and physical objects are projections. Never serialize *two mutable authorities*. Prototype both choices against RimWorld 1.7 Scribe lifecycle before adopting one.

Location supports ground, conventional holder, aperture, special container, world/site and deferred/unloaded states. World-level and unloaded support can remain adapter interfaces in 0.2.5; do not claim functionality before implementation.

### Wild population and habitat

Use a habitat-keyed, species-keyed persisted population record for aggregate cohorts (mature/juvenile initially). Population counts are *not* individual Gu IDs. Species can decline aggregation altogether (e.g. unique Gu). A habitat can host many species with different caps and requirements.

The existing spring contributes resources and habitat quality, but species lifecycle logic belongs to the ecology/Gu services. Avoid per-tick scanning of every map Thing or off-map Gu. Favor notifications, scheduled simulation and bounded catch-up policy.

### Abilities and specialized effects

Expose narrow capabilities for activation, costs, prerequisites, effect execution, feeding, growth, refinement, ambient effect and reproduction. These are **optional behaviors**, not mandatory methods on an enormous `GuBase` subclass.

An ability's preflight checks and effect execution require explicit consumption semantics; never charge essence on a failed effect without the intended game-design rule. Cross-map powers need dedicated adapters with save/rollback semantics.

Gu Houses, formations and killer moves are **compositions/users** of Gu, not Gu species by default.

### External integration

Expose stable public extension interfaces and Def-driven behavior registration. Avoid third-party DLL dependencies in the core. Royalty, Ideology, Biotech, underground systems and add-on mods use optional adapters. Document compatible API versions and migration rules.

## Transaction and state-transition invariants

- **Materialize from population:** population count -= 1 and individual creation succeeds together; on spawn failure, both are unchanged.
- **Release to population:** individual destruction/retirement and population count += 1 happen exactly once.
- **Capture/transfer:** one individual ID; no clone when moving between map Thing, ThingOwner and aperture.
- **Refine:** refiner relationship changes independently of inventory possession.
- **Consume:** deliberate terminal transition, with recorded outcome; no resurrection on reload.
- **Awakening:** an eligible pawn consumes one available mature Hope Gu for a single irreversible attempt, whether the attempt succeeds or fails; cancellation before commit consumes none; internal failure consumes none. A narrowly scoped authoritative coordinator enforces this rather than a premature generalized distributed transaction system.
- **Unique Gu:** defined uniqueness scopes and invariant checks; may refuse aggregation, crafting or duplication.
- **Save/load:** repeated round-trips and interrupted transitions never double-generate or lose Gu.

Use explicit, testable results for invalid transitions. Keep side effects centralized, idempotent where reentry can occur and bounded in runtime cost.

## Migration of the 0.2.4 baseline

Preserve all existing 0.2.4 scalar save keys as inputs to a versioned, *exactly-once* migration. Introduce stable spring/habitat identity and a per-entity migration marker. Maintain `guPawnRecords` and aperture hediffs unchanged.

1. Load legacy counts and timers without rerolling.
2. If no migration marker, map legacy Hope Gu cohorts to a single new species-keyed population record for that spring. Preserve the actual depleted count, juveniles, ecological schedule, reserve, habitat and lifetime stone production.
3. Mark migration only after successfully creating the new canonical record.
4. On subsequent loads, new records take precedence; obsolete scalar values can remain as derived compatibility shadows, never as independent authoritative state.
5. Handle spring destruction, multiple springs, zero counts, malformed counts and interrupted migration.
6. Run reproducible golden-save tests from 0.2.4 plus new-save round-trip tests. Keep untouched backup saves.

**Migration blockers:** a reviewed storage-authority choice and explicit `Scribe` loading order. Do not silently overwrite unknown data.

## Test species matrix (architecture stress test)

| Species/system | Capability | Must *not* require |
|---|---|---|
| Hope Gu | aggregated spring ecology, awakening consumption, later visual materialization | pawn AI/combat |
| Moonlight Gu | essence-powered projectile or active effect, owned instance | reproduction |
| Liquor Worm | feeding, essence/cultivation support, individual ownership | combat |
| Spring Autumn Cicada | exceptional identity and aperture interaction, specialized time behavior | ordinary wild populations |
| Wisdom Gu | conditional ambient area effect, unusual relationship to holder | manual activation |
| Fixed Immortal Travel Gu | world-map travel requirements/effect | projectile |
| Gu House / killer move | composition of multiple Gu and coordinated activation | Gu House treated as species |

A second minimal dummy/test species *with different lifecycle constraints* must compile and persist in 0.2.5; it should fail meaningfully if the framework still hardcodes Hope Gu.

## Implementation slices and gates

0. **Review this RFC first.** Invite independent adversarial review, record unresolved decisions.
1. Species Def metadata, validation, catalog and two test Defs — build, XML load, no gameplay regression.
2. Population records and idempotent legacy migration — unit/integration save tests, old saves, multiple springs.
3. Switch spring ecology read/write to new population API without changing numbers — 0.2.4 regression suite.
4. Individual identity, location and controlled population-to-instance transition — conservation tests and save/load.
5. Rewrite awakening's consumption coordinator to be failure-safe — interrupted ceremonies, zero Gu, repeated jobs, successful/failed awakening.
6. Explicit **Windows 1.7 compilation** and manual in-game acceptance before merge to `main`.
7. Visible Hope Gu and richer entity behavior follow in **0.2.6**, *not* inside the framework core.

## Open decisions requiring proof, not speculation

1. Is canonical individual identity best stored on a ThingComp, `ThingOwner` or GameComponent in 1.7, given map unload and save semantics?
2. Where is the canonical wild population stored: spring ThingComp, map component or world component, and how are persistent habitat IDs assigned?
3. What rank representation is most compatible with XML Mods, higher-ranked Gu and evolving individual Gu?
4. How do we avoid duplicate effects on game reentry, interrupted jobs and loading mid-transition?
5. What is the smallest public plugin API capable of expressing special effects while still allowing backward-compatible extension?
6. What do we guarantee for off-map/unloaded Gu now, versus explicitly deferring?

## Definition of done

- No hard-coded Hope Gu conditionals required to register another ordinary species.
- At least two structurally different Gu species work through the same definition/registration pipeline.
- Every surviving individual has one authoritative identity; aggregate-to-instance transitions conserve counts.
- All 0.2.4 player-tested behavior and saves remain valid after migration.
- Automated structural and deterministic conservation tests, real RimWorld 1.7 compilation, manual smoke/regression, save/reload and multi-spring stress tests are recorded separately.
- No unlicensed third-party assets, game DLLs, generated binaries, secrets or personal saves committed.
