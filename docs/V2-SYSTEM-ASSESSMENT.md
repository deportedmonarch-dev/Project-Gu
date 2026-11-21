# Existing-system assessment for V2

**Baseline** — Commit `8da9353` on `feature/spirit-spring-ecology-visual-overhaul`. Production code is unchanged from `fa4d05d`.
**Inputs** — The independent review of `fa4d05d`, re-derived from the source because its text was not stored in the repository, the [engine investigation](ENGINE-1.7-GU-V2-INVESTIGATION.md), and the current docs and fixtures.

## Classification key

| Class | Meaning |
|---|---|
| **Retain** | Keep as is. It already fits V2. |
| **Extend** | Keep the design and add capability. Save keys stay. |
| **Adapt** | Keep the proven mechanics and change the contract or data model around them. |
| **Replace** | A new implementation takes over, with migration. |
| **Remove** | Delete once its replacement ships. |
| **Investigate** | A probe or decision is needed first. |

## Individual Gu framework

| Component | Class | Reasoning |
|---|---|---|
| `GuIndividual` (Schema V1) | **Replace** | Location as authority, fused biology and custody, a single revision, no refinement, terminal or species state. Migration preserves every field. [RFC-0004 §12](RFC-0004-GU-FRAMEWORK-V2.md#12-migration-from-v1) |
| `GuIndividualRegistry` binding and validation (`RebuildBindings`, `Resolve`, `MatchesProjection`, `FindProjections`) | **Replace** | Reads mutate saves. Exact-holder checks fail on ordinary native moves. Load and transaction paths scan the world. |
| Registry transaction machinery (transition flag, staged placement, rollback, quarantine on uncertain cleanup) | **Adapt** | Native fixtures proved it. It becomes the generic coordinator's commit protocol. |
| Creation event index and cohort debit evidence (`Materialization`) | **Adapt** | Generalizes to idempotent creation requests for every origin. |
| Dormancy, inventory, aperture and release coordinators | **Adapt** | Their placement, rollback and retirement code becomes the `Manifest`, `Unmanifest` and `ChangeCondition` operations. Ground and inventory moves stop being transitions. |
| `GuCustodySaveGuard` | **Retain** | Still needed for synchronous staging windows. |
| `GuGearInventoryGuard` | **Remove** | It exists only to prevent a V1 mismatch. In V2 a native Gear drop is harmless. |
| `JobDriver_GuPickupDormant` and the pickup float menu | **Adapt** | Becomes an ordinary physical pickup with no custody commit, or is replaced by native hauling if the owner makes Gu haulable. |
| `Pawn_GuWildProjection`, `Thing_GuDormantProjection` | **Adapt** | Retained as loadable legacy types, because saved Things bind by class name. Identity moves to `CompGuManifestation`. |
| `IGuProjection` | **Replace** | Replaced by the comp. |
| `GuIndividualMapCustody` (underground protection) | **Adapt** | Derives from the manifested-on-map index instead of re-validating every binding. |
| `GuDebugActions` | **Adapt** | Keep developer controls. Point them at V2 operations. Add recovery tools for unresolved and quarantined records. |
| `Contracts/GuContracts.cs` (unused DTOs) | **Remove** | Never used. V2 IDs and views replace them. |

## Species, cultivators and apertures

| Component | Class | Reasoning |
|---|---|---|
| `GuSpeciesDef` | **Extend** | Keep Def names and fields. Add manifestation Defs with the comp, capabilities, uniqueness, aggregation and host-loss policies. The tag lists stay descriptive. |
| Species XML (Hope, Moonlight, developer test) | **Retain** | Def names are save keys. |
| `GuWorldComponent` and `GuPawnRecord` | **Adapt** | Becomes `CultivatorRecord` with a durable ID, lazy creation and faction-agnostic rules. `guPawnRecords` is read for migration. [RFC-0005 §3](RFC-0005-CULTIVATORS-REFINEMENT-APERTURES.md#3-cultivator-identity) |
| 600-tick discovery scan | **Adapt** | Kept for mortal status markers until cultivator records replace it, then narrowed. |
| `Hediff_Aperture` | **Extend** | Add `guApertureId`, resurrection and corpse-destroyed notifications. All existing keys stay. |
| V1 one-slot aperture custody | **Replace** | Becomes the `MortalAperture` container with many occupants. |
| `GuAwakeningService` | **Retain** | Its commit, rollback and reentry discipline is the model for processes. Awakening stays a cohort consumption. Later it can emit a recorded consumption event. |
| `AptitudeRules`, `LatentAptitudeRules` | **Retain** | Pure rules. |

## Ecology and cohorts

| Component | Class | Reasoning |
|---|---|---|
| `GuPopulationRecord`, `GuPopulationLedger` | **Extend** | Add an aggregation credit and per-cohort event history. The conservation invariant carries over. |
| `IGuCohortHabitat` | **Extend** | Generalize beyond springs to regions, beasts and environments. |
| Spring components, flora, production and lighting | **Retain** | Validated gameplay baseline with native tests. |
| `GuEcologyMapComponent` | **Retain** | Event-driven and map-local. |

## Underground infrastructure

| Component | Class | Reasoning |
|---|---|---|
| Address layer, level parent, site and route graph | **Retain**, then **Extend** for environments | Proven in rendered and fresh-process native tests. [RFC-0006 §3](RFC-0006-PERSISTENT-IMMORTAL-ENVIRONMENTS.md#3-reuse-of-the-underground-system) |
| Lifecycle guards | **Retain** | The same prefixes protect future environment parents. |
| Passage travel, emergency return, passage hauling | **Retain** | Native Pawn and inventory custody. Unaffected by V2. |
| Legacy pocket provider | **Retain** | Historical saves depend on it. |
| Map dormancy and snapshot | **Investigate** | Not implemented. Needed for environment scale. |

## Verification infrastructure

| Component | Class | Reasoning |
|---|---|---|
| `verify.ps1`, structural scripts, managed tests | **Retain** | Baseline gate. |
| `verify-priority0-live.ps1` and the native harness | **Retain**, then **Extend** | The disposable-game runner is one of the project's strongest assets. |
| Slice 3B profiles | **Adapt** | Expectations change. Native moves no longer quarantine, and reasons change. Their staging, rollback and replay cases remain valuable. |
| `V2EngineProbe` | **Adapt** | Becomes the V2 acceptance fixture. Each observation turns into an expected V2 outcome. |
| `Slice3B5BPerf` benchmark | **Extend** | Scales to 1,000 and 10,000 records and mixed placements. |
| Historical 31×31 and 39×39 saves and V1 checkpoint saves | **Retain** | Migration regression inputs. Originals are never modified. |

## Impact on existing investment

- **Kept unchanged.** Spring ecology, flora, production, awakening, cohorts, underground maps and travel, hauling, the save guard and the verification harness. These make up most of the 0.2.5 delivery.
- **Kept as mechanics.** V1 staged placement, rollback, retirement contract, event-key replay and conservation accounting move into the V2 coordinator. Most of the work in Slices 3B-2 to 3B-5 survives as code paths and test cases.
- **Replaced.** The V1 record schema, binding validation, one-slot aperture custody and the Gear guard.
- **Rewrite versus incremental reconstruction.** A full rewrite would discard proven native transaction code and the harness for no gain. Incremental patches would leave location-as-authority in place. The recommendation is targeted reconstruction: a new record model and query layer, with the transaction machinery reused behind the new operations.
