# DESIGN-0.2.5-SLICE-2: Species-independent wild Gu populations

**Status:** Design only — review required, implementation and compilation not started  
**Scope:** Slice 2 population records, habitat authority, 0.2.4 migration and conservation  
**Baseline:** 0.2.4 passed 10/10 player tests; Slice 1 species catalog is committed but unverified in RimWorld 1.7.

## Decision summary (proposed)

1. **Persist anchored wild populations with their habitat owner**, beginning with each `CompGuSpiritSpring`, using **one owner and one authoritative collection**. This is *one population mode*, not a requirement that all Gu need a habitat object. Free-roaming Gu, regional background populations, and special emergence events require independent support. Use `GuEcologyMapComponent` for a transient, reconstructed *index and notification dispatcher*, **not** a second persistent spring-population store.
2. The generic framework operates on population/source abstractions that include `IGuPopulationHabitat` / `GuPopulationRecord` for *anchored* cohorts. Spirit springs are one host, not the only source of Gu. Species suitability is a weighted preference/eligibility policy, not necessarily a mandatory habitat lock; Hope Gu keep spring-local reproduction and awakening availability in our current implementation.
3. A spring owns any number of records, indexed by stable Gu species Def name. Hope Gu's current counts stay exactly the same after migration; Water/Earth species and other spring residents are supported without creating them by default.
4. Migration from v0.2.4 runs **once per spring**, only when a new authoritative list was absent in the saved data. It copies mature and juvenile Hope Gu counts, daily ecology schedule and existing production/reserve/habitat fields, without rerolling or regrowing.
5. Slice 2 is **storage/behavior-preserving**. It will not spawn physical Gu, change economic yields or implement new species populations. Those require later slices and separate tests.
6. Give each saved habitat a stable persistent ID. Detect and resolve duplicate IDs explicitly; never silently merge population lists from different springs.

These are design recommendations, **not validated RimWorld 1.7 API contracts**. The final C# structures and save callbacks must be tested against actual game assemblies before implementation is accepted.

## Why per-habitat ownership now?

A spring currently saves all ecology state in `CompGuSpiritSpring.PostExposeData`. Moving the record's source of truth to a GameComponent would create extra lifecycle concerns (spring destruction, map loading/unloading, save ordering, duplicate records and orphan cleanup). Instead, persistence follows the `Thing` owner and naturally survives its supported RimWorld save lifecycle. The map component provides fast lookups of live habitats without owning their data.

This is **not** a rule that all Gu in the mod belong to a spring or even a discrete habitat object. Free-roaming Gu, regionally distributed cohorts, creatures that host Gu, and special natural refinement events need their own sources and identity ownership. Future Gu carried by pawns and unique Immortal Gu are individual Gu with separate persistence rules from aggregated wild cohorts. Non-spring anchored habitats can implement the same host interface; regional populations must have an explicit persistent map/world-region owner, not be shoehorned into a fake spring.

### Proposed storage map

```text
Map (loaded)
  GuEcologyMapComponent — transient index only
    spring ID -> current live spring host
    cell changed -> nearby hosts invalidated
  CompGuSpiritSpring (persistent ThingComp)
    habitatId (stable)
    spring reserve / purity / stone timer (existing)
    saved list<GuPopulationRecord> (AUTHORITATIVE)
      GU_HopeGu: matureCount, juvenileCount, nextStepTick
      [other species later: their own counts and policies]
  CompGuSpiritFlora (habitat contributor, no populations)

World or unloaded maps (future)
  no new global population ownership in Slice 2
```

### Data records, sketches only

```csharp
// Illustrative shape; NOT executable C#.
interface IGuPopulationHabitat {
    string PersistentHabitatId { get; }
    string HabitatKind { get; } // "spirit_spring", future habitats
    GuHabitatContext SnapshotConditions(); // read-only value snapshot
    IReadOnlyList<GuPopulationRecord> Populations { get; }
}

sealed class GuPopulationRecord { // IExposable later, subject to load proof
    string SpeciesDefName;  // stable XML defName for migration/add-on compatibility
    int MatureCount;
    int JuvenileCount;
    int NextEcologyTick;
    int StateSchemaVersion;
}

// Shared engine API (only after storage tests)
PopulationResult TryConsume(HabitatId id, SpeciesDef species, Cohort mature, int count);
PopulationResult ApplyEcologyStep(HabitatId id, SpeciesDef species, GuHabitatContext context);
PopulationResult TryTransfer(...); // design only: not implemented until individual Gu
```

Use `DefDatabase<GuSpeciesDef>` to resolve species. If a save references a missing optional add-on species, do **not** silently delete its record or reassign it to another species. Retain its Def name and counts as **dormant/unknown** data where technically possible; disable simulation and report a diagnostic until the definition returns. Assess actual Scribe serialization and XML behavior.

Tag strings in Slice 1 are descriptive metadata, **not** sufficient to implement habitat suitability by themselves. Add a future pluggable rule interface such as `IGuHabitatSuitabilityPolicy`, with a default spring affinity rule; no large hard-coded Gu-name switch in the generic engine.

## Expanded ontology: Gu exist beyond named habitats (owner-approved)

**Core correction:** *Gu are creatures of heaven and earth, not merely products of habitat buildings.* The engine must permit an individual Gu to exist and roam freely, even without a visible spring, mushroom patch or nest. Habitat and energy conditions provide weighted suitability, emergence rates and survival benefits; **hard requirements are optional species rules, never a universal restriction.**

### Distinct coexistence modes

| Mode | Example | Authoritative state | Planned simulation |
|---|---|---|---|
| Anchored ecological cohort | Hope Gu around a spring; a Gu-bearing Lingzhi forest | One named habitat/site owner | Aggregated counts with per-species lifecycle |
| Regional/background wild cohort | Uncommon wild Gu in ordinary wilderness, higher density where dao conditions suit it | Map or persistent region owner (future, not a spring) | Sparse probability sampling, aggregate cohorts |
| Individually roaming Gu | An independent Liquor Worm wandering a map | Single persistent Gu identity/Thing ownership | Lightweight movement/behavior, capture, feeding; later slice |
| Specialized breeding site | Imperial Court Blessed Land-like towers acting as breeders/hives | Site structures plus explicit output and population ledgers | Capacity, resources, breeding cycles, dispersal |
| Gu hosted by a living organism | Beast-associated Gu, parasite or symbiont | Host-linked Gu ownership or population record, never duplicative | Host lifecycle / transfer / death outcomes |
| Event-derived Gu | Natural refinement, mutation/evolution, ascension-related Gu | Explicit genesis transaction creates identity or cohort delta | Trigger-driven, seeded and logged |

**No single spawn formula** should be used for every one of these. A species may be eligible for more than one mode; the distribution engine chooses *which source* generated it and where its authoritative state resides. Creating visible entities from cohorts transfers counts rather than adding independent duplicates.

### Generation versus reproduction versus migration

Use an extensible **Gu genesis / appearance policy** with explicit provenance and conservation semantics:

- **Breeding and reproduction:** existing organisms/Gu and a breeding habitat produce offspring under defined resource and suitability rules. Towers and concentrated habitats may amplify this process.
- **Natural heaven-and-earth refinement:** defined environmental prerequisites and energy/resource budgets generate a Gu from a process that is *not parent-based reproduction*. Dense dao marks, qi/primeval-energy conditions, unique weather and landscape can influence it.
- **Biological emergence:** parasitism, symbiosis, mutation or evolutionary development in beasts/flora can produce or host Gu, with host linkage and consequences.
- **Special events:** an immortal ascension or world/story event may refine or grant a specific Gu (e.g. the owner's Tai Bai Yun Sheng / Landscape as Before example); treat this as a scoped event with preconditions and unique-result rules.
- **Arrival / dispersal:** a wandering Gu can come from another population or map/region without *newly being created*. Record departure and arrival or defer a single individual identity across map transitions.
- **Initial world generation:** seeded baseline populations or Gu placements may be created as an explicit starting allocation, distinct from ongoing births/refinement.

Every appearance must specify **source**, **species eligibility**, **rank**, **destination**, **conservation ledger treatment**, and **failure handling**. Strong habitats bias rates and survival; not all species require any one feature.

### Environmental fields, including dao-mark concentration

Design a **sparse environmental influence layer** (future): fields such as qi/primeval-energy density, Water/Earth and other path-associated dao-mark concentrations, terrain, biome, season, flora, weather, ruins and blessed-land effects. Use a read-only environmental snapshot consumed by species/source policies; allow plugins to add influence providers. Avoid per-tick per-cell scans or recalculating each Gu's suitability continuously.

Important: a high dao concentration does not automatically create Gu. It can alter eligibility, probability, potency, survival, mutation or natural-refinement opportunity depending on species and genesis mode. Exact dao-mark mechanics and balance remain unimplemented.

### No exclusivity by default

- **Soft preference:** a wild Gu may occur broadly but is substantially more common or more successful in optimal conditions.
- **Hard prerequisite:** selected Gu truly require a spring, host, special dao marks, environment, or event to form/reproduce.
- **Multiple pathways:** one Gu can be naturally refined, bred, discovered or otherwise appear through different sources without violating identity conservation.
- **Uniqueness:** restricted Immortal Gu must respect the relevant unique scope across *all* genesis pathways.

Hope Gu are anchored to springs for **the existing 0.2.4 reproduction/awakening rules**. This is a species-specific gameplay rule, **not a universal requirement** that future Hope Gu or all Gu must be permanently confined to one structure. A physical mature Hope Gu may eventually leave the spring via a conserved transfer; that must not grant a free replacement in the source population.

### Architectural implications and staged scope

The generic model needs two related but distinct layers:

1. **Population custody:** anchored, regional, host-linked or individually owned state with precisely one authoritative persistence owner.
2. **Appearance/genesis orchestration:** pluggable mechanisms for births, refinement, events, dispersion, world seeding and host-origin emergence. Each invokes the same checked creation/transfer gateway; it cannot directly increment counts and also instantiate physical Gu.

Do **not** force this entire ecology simulator into 0.2.5. Slice 2 should preserve spring populations while defining contracts for non-spring custodians and genesis provenance; implement roaming individuals and environmental fields in subsequent testable slices. Testing must eventually prove (a) a species capable of spawning without any habitat object, (b) boosted incidence at a premium habitat, (c) transfer without duplication, and (d) a special genesis event that cannot fire twice after reload.

## Spring / Hope Gu ecology contract

Hope Gu's association with springs is a species-specific ecological constraint.

- Only a record attached to a concrete spirit spring may supply the local awakening job.
- Baseline spring fresh state stays **3 mature / 0 juvenile**, with no automatic new species.
- Habitat support uses the *existing* sum of flora contribution within current radius; carrying capacity stays `max(3, min(maxHopeGu, 3 + 2 * floraSupport))`.
- Current day step (60,000 ticks): mature existing juveniles into available adult space first; generate at most one juvenile if flora support >0, reserve >0 and total count < cap.
- Habitat removal stops new births but does not kill existing adults; capacity may fall below extant counts without clipping them.
- Stone production, finite reserve, inspect UI, ceremony time, dev-day button and rare-tick scheduling remain behaviorally equivalent to 0.2.4.
- Multiple springs, even on one map, retain separate Hope Gu pools. No global Hope Gu counter. A Gu cannot be consumed by a ceremony from a different spring.
- Species-specific habitat affinity doesn't mean any species may reproduce at every spring. Region, purity, resources and specialized lifecycle determine future suitability.

### Hope Gu and other special spring interactions (deferred)

Future `Heavenly Essence Treasure Lotus` can affect a specified spring via an explicit, balanced site modifier; `Spring Egg Gu` can attempt to create a new spring through a validated site-creation ability; selected Water/Earth Path Gu can breed at suitable spring habitats. **None** is implemented in Slice 2 and none should become a spring-specific `if GuName == ...` branch.

## Migration contract from 0.2.4

Legacy field names must remain readable: `guHopeGuAvailable`, `guMatureHope024`, `guJuvenileHope024`, `guHopeCapacity024`, `guFloraSupport024`, `guNextEcology024`, `guReserve024`, `guLifetimeProduced`, `guNextProductionTick`, `guNextHopeRecoveryTick`, `guHabitatPatches` and `guNextHabitatTick`.

The old `matureHopeGu` uses **-1 as an absent/uninitialized sentinel**, and `hopeGuAvailable` is the older fallback. Preserve the distinction between missing new fields, deliberately zero adults and corrupt negatives.

### Migration state machine (proposed)

- **Load old save (no new list/version key):** read old scalar values; after `PostLoadInit` and definition resolution, create exactly one `GU_HopeGu` record per spring using existing values (mature from `matureHopeGu` if >=0, else legacy `hopeGuAvailable`; juveniles from `juvenileHopeGu`; ecology tick from existing `nextEcologyTick`, else valid recovery tick, else schedule next day). Preserve reserve, purity preset, production and habitat fields.
- **New save (new list present, including deliberately empty):** records are authoritative; do not import legacy Hope Gu counters and do not initialize a second starter cohort. Marker/list-presence semantics must distinguish absent old save from a valid empty new population.
- **Migration completion:** update the schema marker only after the new list is successfully built. Never add existing scalars to an already migrated record; never reset consumed population to 3.
- **Subsequent save:** write the new collection. Legacy scalars may be written as *read-only shadows* for diagnosis/compatibility, derived from the authoritative Hope record; they are never independently mutated.
- **Invalid state:** duplicate species keys, missing species Def or corrupt negative counts produce deterministic diagnostics. Never silently replace depleted population with full starting population.
- **Load order:** resolve `Scribe_Collections.Look(..., LookMode.Deep)` and callback ordering in RimWorld 1.7 before implementing this algorithm. Check `PostLoadInit` behavior on spawned and despawned springs.

### Habitat identity

A persistent `habitatId` is created once for a *new* spring, not on every spawn. For old saves, allocate it at migration without changing the spring's records. Use a verified stable RimWorld Thing ID or a saved GUID; do not assume `ThingID` or `GetUniqueLoadID` is globally suitable without verifying collision behavior. A registry may detect duplicate IDs after load, but must not accidentally merge or throw away a spring's population. Removal/destroy calls must not delete unrelated habitats.

### Idempotence and partial failure

Loading the same old save repeatedly should deterministically migrate it in memory each time, never changing totals, while new saves persist the completed migration. A crash or save/load boundary must not leave *both* legacy and new records authoritatively active. No split tick where population exists twice. If species Def resolution fails, retain legacy scalars and return a clear diagnostic rather than consume them.

## Population conservation accounting

For each habitat and species:

```text
final_count = starting_count + births + arrivals - deaths - departures
              - consumed - individually_materialized + individually_returned
```

Maturation only changes cohort allocation; it **does not** change total population. Individual materialization/return operations are future work, but the invariant is already part of the contract. Resource production from reserve is separately conserved. Failed operations change neither count nor irreversible pawn state.

Operations should return explicit `Success / MissingSpecies / MissingHabitat / InsufficientAdults / InvalidCount / InvalidState`-style results. Write through a single mutation gateway, not direct public fields.

## Suggested staging and tests

**Do not implement this slice until Slice 1 is tested or the user explicitly authorizes combined unverified work.** Design can proceed independently.

1. Verify exact RimWorld 1.7 Scribe and Thing lifecycle APIs on the user's Windows game; record results.
2. Implement record serializer and species lookup without switching gameplay. Test save/reload and absent-vs-empty keys.
3. Build habitat ID and live index; test multiple springs, destruction and load.
4. Implement deterministic single-host migration with old `0.2.3/0.2.4` saves, including adults=0, juveniles>0, no flora, depleted reserves and partially initialized timers.
5. Cut over all spring ecology reads/mutations to the new service, retaining the precise 0.2.4 numerical behavior and inspect strings.
6. Test repeated load/save/load, with and without dev tools; verify no duplicate records or unexplained population gain.
7. Add a **second test population species** in a controlled dev-only habitat scenario, proving no species-name branches are required. Distinguish this from Moonlight Gu's definition-only Slice 1 test.
8. Run standard structural checks, Windows .NET/RimWorld 1.7 compilation, actual game log inspection, existing 10/10 0.2.4 regression, and additional stress/legacy cases. Keep draft PR unmerged until accepted.

### Exit gates

- 0.2.4 old save and fresh game produce exactly the same Hope Gu counts and behavior as before.
- Two distinct population species can share one habitat, and two separate springs do **not** share counts. The schema does not preclude non-spring regional cohorts or free-roaming independent Gu.
- Successful and failed consumption preserve conservation. Failed attempt with no Gu has no irreversible side effect.
- All saved populations survive multiple reloads, including zero counts.
- Flora notifications, fallback recount, finite reserve/stone yields and awakening all regress cleanly.
- No species-specific code in the **generic** registry; species-specific rules stay pluggable.
- No new per-tick map-wide scans; bounded simulation at established cadence.
- Compilation/gameplay **not claimed** until run and logged.

## Review blockers / choices before coding

1. Confirm `Scribe` behavior distinguishing a nonexistent collection from an intentionally empty saved collection.
2. Confirm the chosen habitat ID scheme across save/load, map transfers and duplicate detection.
3. Decide what to do with missing add-on species definitions before considering the serialized record schema frozen.
4. Distinguish habitat destruction from a temporary non-destructive despawn: migration should not accidentally reset either.
5. Confirm behavior for empty mature adults but nonzero juveniles and lowered carrying capacity matches 0.2.4.
6. Clarify whether cohort schedules are per species (recommended) while spring production remains per spring.
7. Keep the single-operation awakening fix in the later coordinated transaction slice, not quietly bury it in migration.

## Status

This is a **design document only**. No C# / XML gameplay code changed and no 0.2.5 in-game validation is claimed.
