# Gu Framework V2 — implementation journal

**Branch** — `feature/gu-framework-v2`, created from the verified `v0.2.5` merge (`229df4e`).
**Authority** — Owner gates G0 and G1 after review of `98bf468`. [RFC-0004 with the G1 amendments](RFC-0004-GU-FRAMEWORK-V2.md#15-gate-g1-amendments) · [Reconstruction plan](V2-RECONSTRUCTION-PLAN.md)

## Phase 1 — record model, manifestation authority and migration

### What changed

| Area | Change |
|---|---|
| Canonical record | [`GuRecord`](../Source/ProjectGu/Domain/GuRecord.cs) replaces the V1 `GuIndividual`. Identity, existence, condition, placement, refinement, species state and integrity are separate facets. Custody and state have separate revisions. Saved under `guIndividualRecordsV2`. |
| Placement | `Manifested` (one native Pawn or Thing) or `Contained` (record-only, keyed by a durable container ID). Ground and inventory are no longer stored. They are observed from the native graph through the pure `Observe` query. |
| Unresolved and quarantine | Missing species, projections, hosts and apertures, and demonstrated native endings, set an `Unresolved` overlay that keeps the last authorized placement for recovery. Demonstrated evidence (`Destroyed`, `Killed`, `MapRemoved`, `Discarded`) is recorded but never made terminal. Quarantine is limited to contradictions: duplicate IDs, duplicate native owners, duplicate creation events, revision disagreement with the exact native object, unsupported versions, incomplete cohort evidence, and an uncertain undebited cohort cleanup. |
| Manifestation identity | [`CompGuManifestation`](../Source/ProjectGu/CompGuManifestation.cs) saves the V1 keys `guIndividualId` and `guBindingRevision`. It registers during load, so binding needs no world scan. Its native hooks only enqueue events. `Pawn_GuWildProjection` and `Thing_GuDormantProjection` remain as loadable shells. The developer Defs declare the comp and `notifyMapRemoved`. |
| Authorization | `MatchesManifestation` requires the exact native load ID, the tag's ID and custody revision, the species Def and a living, unsuppressed object. Holder and location are not checked. Tags that fail are rogue projections and never authorize. |
| Pure queries | `Resolve`, `Observe`, `View`, `TryGetView`, `ApertureOccupants` and inspection never mutate records or enqueue work. |
| Reconciliation | [Native events and a scheduled sweep](../Source/ProjectGu/GuIndividualRegistry.Reconcile.cs) every 250 ticks, budget 512 records. Pending events are also applied before every save. Resurrection and returning hosts recover contained Gu through the sweep. |
| Transitional aperture identity | `Hediff_Aperture` saves `guApertureId`, assigned only by coordinator operations and migration. Contained records save the aperture ID and the host Pawn load ID as an anchor hint. Host status is an action policy, not a binding condition. |
| Coordinators | Dormancy, aperture storage and withdrawal, release and cohort materialization keep the V1 staging, rollback, save-guard and replay machinery. Ground and inventory moves are revalidated physical moves that keep both revisions. A post-commit cleanup failure records the stale native object as rogue instead of quarantining a healthy record. |
| Migration | [`MigrateLegacyRecords`](../Source/ProjectGu/GuIndividualRegistry.Load.cs) reads `guIndividualRecordsV1` once and never writes it. Fields map one to one. The V1 revision becomes the custody revision. V1 quarantines that describe ordinary custody mismatches are re-evaluated against the live graph. Others are retained with their V1 reason. V1 aperture records receive a `guApertureId` on their exact Hediff. |
| Developer controls and underground | Debug actions use the V2 operations and log observed forms, rogue projections and the migration summary. Underground last-access protection counts authorized manifestations spawned on the level. |
| Gear guard | Removed after `VanillaGuInventory` passed on V2 (RFC-0004 §15.3). The vanilla Gear handler moves the same authorized Thing with no Project GU transaction. Ordinary right-click pickup is kept. See [Verification](#results-on-the-final-phase-1-code). |

### Intentional expectation changes in the native fixtures

Each change keeps the invariant the V1 assertion protected.

| Fixture | V1 expectation | V2 expectation | Invariant kept |
|---|---|---|---|
| All | Pickup and drop advance the revision | Physical moves keep both revisions. Stale-request checks use genuinely older revisions. | Stale requests are rejected. One authorized manifestation per ID. |
| All aperture cases | Custody owner is the Hediff load ID | Custody owner is the durable `guApertureId` | Exact host and aperture binding |
| `Slice3B1` | A duplicated projection tag quarantined both records | The record whose own Pawn matches stays authorized. The copied tag is rogue. The other record is unresolved. | A claimed ID never authorizes a native object |
| `Slice3B1` | Missing owner and missing species quarantined | Unresolved (`ProjectionMissing`, `SpeciesUnavailable`) with all data | Absence of evidence is never terminal |
| `Slice3B2` | Discard-cleanup failure quarantined the committed dormant record | The committed Thing stays authorized and the half-retired Pawn is rogue | No second actionable manifestation |
| `Slice3B3A` | Blocked rollback and an external holder move quarantined | The same Thing stays authorized at its observed location | Identity and conservation |
| `Slice3B3A` | Destroyed projection quarantined | Unresolved `ProjectionEnded` with `Destroyed` evidence | Demonstrated events are recorded, not erased |
| `Slice3B3B`, `Slice3B4` | Host death, host destruction, aperture removal and loss during withdrawal quarantined | Unresolved `HostDead`, `HostUnavailable` or `ApertureMissing` | Lost hosts never authorize a release |
| `Slice3B4`, `Slice3B5A` | Post-commit binding failure quarantined | Unresolved `ProjectionMissing`. A debited cohort individual stays counted and its event replays idempotently. | An event key never allocates twice. Conservation. |
| `Slice3B5B` | Revisions one to five | Custody revisions one to four. Dormancy and release also advance the state revision. | Each committed transition advances exactly once |
| `VanillaGuInventory` | A stale menu order failed after a revision-changing round trip | The order revalidates and acts because authorization is unchanged. A separate check proves an order is refused after its Gu loses authorization. | Menu actions never act on revoked authorization |
| `V2EngineProbe` | Characterized V1 quarantines | Asserts the approved V2 outcome for every scenario | Ordinary play never quarantines |

### Verification

Evidence is recorded below as each integration checkpoint completes.

#### Environment

Windows 11 build 26200, AMD Ryzen 9 9955HX, RimWorld 1.7.5014 rev1195, Harmony 3.0.4, .NET SDK 10.0.401. Native runs used disposable game copies with Core unless noted.

#### Results on the final Phase 1 code

| Gate | Result |
|---|---|
| `scripts/verify.ps1 -Focused` | PASS. Structural checks, game-linked build with zero warnings and errors, managed tests. |
| Gu lifecycle fixtures | PASS, new game and every fresh-process stage: `Slice3B1`, `Slice3B2`, `Slice3B3A`, `Slice3B3B`, `Slice3B4` (3 stages), `Slice3B5A`, `Slice3B5B` (4 stages), `VanillaGuInventory`, `V2EngineProbe`. Evidence under `tests/obj/Priority0-b14880e22f854458ab4e83ef78a51474`. |
| Read purity | PASS. `Slice3B1` rendered every inspect string, built every float menu and requested every view, and no record changed. `V2EngineProbe` repeated the check on a natively dropped Gu. |
| Ordinary play | PASS. Downing, death, imprisonment, stripping and a faction change left each Gu bound at its new native location with zero quarantines. Resurrection recovered the aperture occupant through the scheduled sweep. Destruction, a killed wild Gu, world-pawn GC and an unprotected map removal produced unresolved records with `Killed`, `Destroyed` or `MapRemoved` evidence. All outcomes survived a fresh-process reload. |
| Rogue and duplicate projections | PASS. A copied tag, a tag on the wrong native object and an orphan tag were rejected as rogue. Duplicate IDs, duplicate native owners, duplicate creation events and revision disagreement were quarantined. |
| V1 migration | PASS on 7 saves written by the `v0.2.5` build (41 records): `Slice3B1`, `Slice3B2`, `Slice3B3B`, `Slice3B4`, `Slice3B5A`, `VanillaGuInventory`, `V2EngineProbe`. Every field matched the V1 XML. 15 records bound in place, 14 became unresolved and 12 stayed quarantined, matching the per-source predictions written before the run. In the `V2EngineProbe` save, 7 of 11 Gu that V1 quarantined during ordinary play rebound in place. Each migrated save reloaded identically in a fresh process without migrating again. All 7 source hashes are unchanged. |
| Gear guard removal | PASS. The guard is deleted. `VanillaGuInventory` confirms that Project GU no longer patches the Gear handler, that the vanilla drop keeps the same Thing authorized with unchanged revisions, and that ordinary right-click pickup and its saved job still work. |
| Regression | PASS: `Core`, `IdeologyOdyssey`, `IdeologyReset`, `Awakening`, `Botanical`, `CargoPrototype`, `AutomaticHauling`, `Legacy31`, `Legacy39`. Evidence under `tests/obj/Priority0-7799dae9f45a496cb9d44b60381f7676`. This run used the code before the Gear guard removal. None of these profiles uses the Gear tab. |
| Benchmark | PASS. `Slice3B5BPerf`, six fresh processes. Evidence under `tests/obj/Priority0-0c1115608c394804a7cc873a8c9c10c2`. |

The V1 sources were produced by rerunning the V1 profiles at `v0.2.5` in a separate worktree. All V1 profiles passed again there. The killed wild Gu in the V1 engine-probe save migrated as `ProjectionMissing` without evidence, rather than the predicted `ProjectionEnded`. The V1 save contains no record of the death, so absence of evidence is the correct outcome. The counts were unaffected.

**Historical saves.** The original 31×31 and 39×39 historical saves could not be found on this machine. The regression used the disposable copies left by the `Slice3B3A` verification run, now kept in the ignored `tests/obj/HistoricalSources/`. These copies are verification derivatives, not the original historical saves. The 39×39 copy is byte-identical to the save that run loaded. The 31×31 copy differs only by the runner's documented removal of an empty test component. Both copies kept their hashes. Copy SHA-256: `Legacy31-copy.rws` `09f225c1…b956c`, `Legacy39-copy.rws` `7efce436…2658`. Phase F recorded that the originals matched but did not publish their hashes, so the copies cannot be tied to the originals by hash. A run against the originals remains open until their paths are available.

#### Owner rendered acceptance

The owner reviewed Phase 1 in a rendered game and accepted it. Screenshot evidence and owner reports are recorded separately.

**Documented in the supplied screenshots.** One individual, Gu `d4407117c5084396901eb9b2c91174ba`, species `GU_DevTestSpringGu`, Rank 1:

| Step | Observed form | Custody revision | State revision |
|---|---|---:|---:|
| Development spawn | Active wild Pawn | 1 | 1 |
| Dormancy | Dormant Thing on the ground | 2 | 2 |
| Ordinary pickup | Dormant Thing held in inventory | 2 | 2 |
| Aperture storage | Contained in mortal aperture `fab53ee2b0fc4fbc9e41fd2dfdece4e3` | 3 | 2 |

No unresolved or quarantine state was displayed. The revisions match the V2 rules: pickup kept both revisions, dormancy advanced both, and storage advanced only custody.

**Reported by the owner, not shown in the screenshots.** Save and reload succeeded, and active release from the aperture succeeded.

**Not separately documented.** Gear drop and repickup were not shown in the supplied screenshots. `VanillaGuInventory` covers them natively.


#### Performance

Medians of three processes per variant, 120 real hosts with apertures. The populated variant holds 120 contained records and zero physical Gu.

| Measurement | 0 records | 120 records | V1, 120 records |
|---|---:|---:|---:|
| Save size added | — | 88,222 B (735 B/record) | 59,899 B (499 B/record) |
| Registry rebuild | 2.1 µs | 664 µs | 1,374 µs |
| Sweep or validation pass (every 250 ticks) | 0.11 µs | 39.9 µs, 16,296 B | 78.6 µs, 29,792 B |
| Ordinary registry tick | 0.002 µs | 0.002 µs | 0.062 µs |
| Native save wall time | 564 ms | 563 ms | — |
| `Game.LoadGame` wall time | 4,860 ms | 4,626 ms | — |

Rebuild and periodic validation cost about half of V1. Each record costs about 236 more bytes in the save because of the separate facets and revisions. Whole-tick, save and load ranges overlap at this sample size, as in V1. The absolute save and load figures come from a different generated colony than the 0.2.5 report and are comparable only within this run.

### Remaining risks

- The Phase 1 staging path still requires the legacy `Pawn_GuWildProjection` class for tick suppression. Species using other Pawn classes need the unspawned-staging probe.
- Missing species-state classes are not yet preserved as opaque XML. No species state exists in Phase 1.
- V1 quarantines from failed V1 transactions are retained conservatively. Operator recovery tools are Phase 2 work.
- Map removal and world-pawn GC leave Gu unresolved. The approved Phase 2 policies decide their outcomes.
- DLC lifecycle passes (Royalty, Biotech, Anomaly) are still open. Anomaly continues to disable dormancy, release and materialization.
- Rendered owner acceptance covered spawn, dormancy, pickup, aperture storage, reload and release. A rendered Gear drop and repickup was not separately documented.

## Phase 2 — lifecycle outcomes, recovery and hauling

**Authority** — Owner Phase 2 authorization of 2026-11-22 and the follow-up instructions of the same day. [Owner decisions](V2-PHASE2-OWNER-DECISIONS.md) · [RFC-0004 §16](RFC-0004-GU-FRAMEWORK-V2.md#16-phase-2-lifecycle-as-implemented) · [Open decisions](V2-RECONSTRUCTION-PLAN.md#phase-2-decisions-still-required)

### What changed

| Area | Change |
|---|---|
| Terminal outcomes | Demonstrated death (`Died`) and physical destruction (`Destroyed`) are terminal. Deliberate destruction by the holder or living host, consumption and sacrifice are explicit operations with idempotent event keys. An operator can declare an unresolved record `Lost`. Map removal, world-pawn GC, discards, holder teardown and missing evidence are never terminal. A record is ended and unbound before its native object is retired, so a retirement is never counted as a second ending. |
| Tombstones | A terminal record keeps identity, species, rank, provenance, creation key, cohort debit, uniqueness key, outcome, end tick, end key and actor. Records are never deleted. |
| Species hooks | `abandonmentPolicy`, `individualPreservationMinRank` (default 6) and an optional `terminalEffectWorker` for residue. Most species leave nothing. |
| Deceased-host custody | Host death keeps aperture Gu bound and recoverable from the body. Resurrection needs no recovery. Extraction before resurrection is committed and cannot be reversed or duplicated. A mortal aperture never drops. |
| Destroyed host body | Owner decision D2.7: the Gu still in the aperture slip out as active, masterless wild Gu where the body lay. Without a live map there, the escaped Gu follows its preservation policy at the captured location. |
| Physical Gu and holders | A destroyed corpse or a holder destroyed without dying places its authorized Gu where the body stood when that is a live map. Otherwise the Gu's end is recorded as holder teardown. With no known location the record stays unresolved. No location is invented and the same Thing is moved, never copied. |
| Location evidence | `GuLocation` (tile, layer, cell, map, map parent, holder, tick) is captured before native teardown by a `MapDeiniter.Deinit` prefix, a `Corpse.Destroy` prefix, the projection classes, and the comp's last spawn. The last spawn is used only for objects outside every holder. Pocket maps use their source map's tile. |
| World locations | `Contained(WorldSite)` keeps an individual's identity without its native map. A development operation recovers it on a map at the same tile. |
| Preservation policy | `Individual` (world location), `RetireFromSimulation` (`Retired`, not a death), `ReturnToPopulation` (unresolved with evidence until regional populations exist). Unique, refined and rank 6+ Gu are always individual. |
| World-pawn retention | A GC postfix keeps only Pawns that are, carry or host Gu that must keep their identity. A Gu Pawn is kept only until reconciliation abstracts or retires it. Population-policy Gu and their carriers are left to native GC. |
| Operator recovery | Read-only inspection with evidence history, rogue candidates and the supported actions. Development Mode repairs re-check evidence and write an audit entry. Rogue adoption is never automatic: it needs a record whose projection is missing with no other evidence, exactly one live candidate carrying the record's exact last custody revision and form, and a recorded operator reason. Declaring lost applies only to unresolved records. Lifting a quarantine re-checks the contradiction and leaves the record unresolved. |
| Compaction | After one in-game year ended records drop placement fields, refinement, species state, last location, narrative detail and history. A retirement keeps its cause. Living records keep their facts and summarize narrative detail older than a year. |
| Hauling | The developer dormant Gu is haulable and storable under the new `GU_DormantGu` category. Hauling moves the same Thing without changing revisions, condition or allegiance, and respects forbidden items and storage filters. Ordinary pickup stays. |
| Anomaly | The blanket Anomaly gate on development dormancy, release and materialization is replaced by a per-Pawn check that refuses mutants, creepjoiners, duplicates, Pawns with unnatural corpses and hypnotized Pawns. Kept only because the native Anomaly runs below passed. |
| Test runner | Removes its disposable game copy after success, failure and Ctrl+C, removes copies left by killed runs when no process uses them, and refuses to start without room for one copy plus a 2 GB margin. `-Expansions` adds official expansions to any profile. `-KeepGameCopy` keeps the copy for debugging. |

### Intentional expectation changes in existing fixtures

| Fixture | Phase 1 expectation | Phase 2 expectation | Invariant kept |
|---|---|---|---|
| `Slice3B3A` | Destroyed inventory Gu unresolved with `Destroyed` evidence | `Terminal(Destroyed)` with identity, provenance and last holder | Demonstrated events are recorded. Nothing is erased. |
| `Slice3B3B`, `Slice3B4` | Host death made the occupant `Unresolved(HostDead)` | The occupant stays in deceased-host custody. A dead host still cannot withdraw or release it. | Lost hosts never authorize an action |
| `V2EngineProbe` | Killed and destroyed Gu unresolved. GC and map removal unresolved or left as a world Pawn. | `Died`, `Destroyed`, and world-location custody for GC and map removal | Absence of evidence is never terminal. Nothing quarantined. |

### Scalability risk: retained native NPC cultivators

Selective retention keeps a native world Pawn for every NPC cultivator whose aperture or inventory holds a Gu that must keep its identity, and for a wild Gu Pawn until reconciliation abstracts it. A world Pawn costs far more than a Gu record: relations, health, apparel, inventory and its save node. A long game with many NPC cultivators carrying rare Gu could retain thousands of Pawns, raising memory, save size, load time and the cost of native world-Pawn passes. The current mitigation is selectivity: common Gu do not retain their holders, and abandoned wild Gu become records. The long-term answer belongs to Phase 3 (`CultivatorRecord`): an abstract cultivator record that holds its Gu without a native Pawn, with the Pawn regenerated on demand. Until then, retention counts should be watched in long playtests. The Phase 3 benchmark should include 1,000 retained cultivators.

### Verification

Environment as in Phase 1: Windows 11 build 26200, AMD Ryzen 9 9955HX, RimWorld 1.7.5014 rev1195, Harmony 3.0.4, .NET SDK 10.0.401. Each native profile ran in a disposable game copy with a new game and a fresh-process reload of every stage. All results below are native fixtures on the final Phase 2 code.

| Gate | Result |
|---|---|
| Gu fixtures | PASS: `Slice3B1`, `Slice3B2`, `Slice3B3A`, `Slice3B3B`, `Slice3B4` (3 stages), `Slice3B5A`, `Slice3B5B` (4 stages), `VanillaGuInventory`, `V2EngineProbe`, `Phase2Lifecycle`, `DormantHauling`, `ManifestationProbe`. Evidence `tests/obj/Priority0-d6122a409a0f4cd6b55c71faf6fbcf90`. |
| Phase 2 exit criterion | PASS. `V2EngineProbe` records `Died` and `Destroyed` for death and destruction, world-location custody for map removal and world-pawn GC, deceased-host custody with resurrection, and zero quarantines. All outcomes survived a fresh-process reload. |
| Lifecycle | PASS (`Phase2Lifecycle`): host-only deliberate destruction, idempotent key replay and reuse refusal, held destruction counted once, consumption with species residue once, extraction from a corpse before resurrection with no restoration or duplicate, escape of an active masterless Gu when the body is destroyed, a physical Gu surviving its corpse and a vanished holder, a stranded Gu kept unresolved without an invented location, selective GC retention, all three preservation policies on map removal, recovery from a world location, read-only inspection, lost declaration, adoption refused without a reason and accepted with evidence after reload. |
| Hauling | PASS (`DormantHauling`): forbidden items and filters respected, the same Thing hauled and stored with unchanged revisions, condition and allegiance, ordinary pickup kept, a haul saved mid-carry resumed after reload. |
| Anomaly | PASS with Anomaly active: `Slice3B2`, `Slice3B4`, `Slice3B5A`, `V2EngineProbe`, `Phase2Lifecycle`. Evidence `tests/obj/Priority0-632a21f329214ac9b93b7a344dc98c91`. |
| Royalty, Biotech, Odyssey | PASS with all three active: `V2EngineProbe`, `Phase2Lifecycle`, `DormantHauling`. Evidence `tests/obj/Priority0-ef552723b3f14bb1807bf61c42a3b1c9`. |
| V1 migration | PASS on the same 7 V1 saves (41 records): 17 bound, 12 unresolved, 12 quarantined. Two records that V1 quarantined because their aperture host died now migrate into deceased-host custody (`Slice3B3B` 3/3/1, `Slice3B4` 2/2/1, predicted before the rerun). Every field matched the V1 XML, every migrated save reloaded identically, and all source hashes are unchanged. Evidence `tests/obj/Priority0-2d383e5a295e413792942f6d9912e7eb`. |
| Regression | PASS: `Core`, `IdeologyOdyssey`, `IdeologyReset`, `Awakening`, `Botanical`, `CargoPrototype`, `AutomaticHauling`, `Legacy31`, `Legacy39`. The historical runs used the verified copies in `tests/obj/HistoricalSources`, not the originals. Copy hashes unchanged. Evidence `tests/obj/Priority0-5ff24ac97f964808895d50a459d42c2e`. |
| Benchmark | PASS (`Slice3B5BPerf`, six fresh processes, run alone). Evidence `tests/obj/Priority0-75b5cdc30a9a40208a46493ca2d607cb`. |

#### Performance and save size

| Measurement | Phase 2, 120 records | Phase 1, 120 records |
|---|---:|---:|
| Save size added | 94,585 B (788 B/record) | 88,222 B (735 B/record) |
| Registry rebuild | 584 µs | 664 µs |
| Sweep (every 250 ticks) | 45.6 µs, 15,360 B | 39.9 µs, 16,296 B |
| Ordinary registry tick | 0.003 µs | 0.002 µs |
| Native save / `Game.LoadGame` | 545 ms / 4,555 ms | 563 ms / 4,626 ms |

Medians of three processes per variant. Living records grew by about 53 bytes for the lifecycle facets. Save and load ranges overlap with the empty variant at this sample size.

| Compaction | Before | After one year |
|---|---:|---:|
| 45 ended records (`Phase2Lifecycle`) | 51,541 B (1,145 B/record) | 26,683 B (592 B/record) |

#### Test infrastructure timing

Measured from evidence folders: copying the game and building takes 7–10 s per run. Each profile takes 45–115 s, almost all of it game startup and world generation across two to four fresh processes. The final campaign took about 40 minutes of wall time.

**Follow-up (approved infrastructure work, 2026-11-22).** The runner now records per-stage timing (runner and game side), collects complete results by default with opt-in `-FailFast`, and runs independent profiles in parallel on one read-only game copy with exclusive run ownership. On the 12 Gu fixtures, game startup is 47% of process time and test bodies 2%. `-Parallel 4` reduced the set from 653 s to 255–329 s with 12/12 passing on the final code. Two intermittent fixture defects found along the way were fixed (`Slice3B2` incidental Hediffs, `SpawnGroundGu` space). The owner accepted the milestone on 2026-11-22 after a clean final N=4 campaign (`cbda4ca7`, 12/12) and an isolated benchmark pass on the new runner (`a6ab7840`). `-Parallel 4` is the accepted recommendation; the runner default remains 1. Open: the unreproduced N=6 V2EngineProbe mismatch and a benchmark load-time difference that random quicktest worlds currently prevent attributing. Details, isolation evidence and remaining concerns: [test infrastructure](TEST-INFRASTRUCTURE.md).

### Remaining risks

- Retained world Pawns for NPC cultivators can grow in long games (scalability section above).
- `ReturnToPopulation` has no population target. Those Gu stay unresolved with evidence.
- Binding still requires the legacy wild Pawn class (F15). Missing species-state classes are not yet preserved as opaque XML.
- World-location recovery is a development operation. Expeditions, notifications and rival claims are future work.
- Caravans, transport pods and trade with dormant Gu Things are untested. Tradeability stays `None`.
- The original 31×31 and 39×39 historical saves are still unavailable. Derivative copies were exercised in earlier campaigns, but that limited evidence is not a compatibility guarantee; historical-save support is outside the current development-build acceptance gate.
- Rendered owner checks of the Phase 2 developer actions and hauling are open.

## Phase 3A-I — Durable identity and lossless migration foundation

**Status — Implemented and checked with managed verification. Native execution remains gated.** Design baseline `61b4896cb7b5172913b998e71731f9e64f7f9dbd`. [Proposal](V2-PHASE3A-PROPOSAL.md) · [Acceptance plan](V2-PHASE3A-TEST-PLAN.md)

| Review blocker | Stage 3A-I implementation and managed evidence | Native evidence still required |
| --- | --- | --- |
| Preserve inputs and load ordering | `LoadingVars` clones the complete legacy collection before the reference reader runs. The legacy DTO reads only the Pawn reference. The prepared index classifies raw cultivation fields, reserves conflicting tokens and retains the original unresolved XML. The managed Scribe fixture keeps malformed, explicit-null and unresolved-reference rows through `FinalizeLoading` and `PostLoadInit`. Both component callback orders publish once after the actual Phase 2 registry signal. | Fresh-process legacy sources, removed or suspended Pawns, corpse-held and off-map associations, and Phase 2 V1 Gu import ordering. |
| Durable identity and lifecycle facets | Versioned store separates immutable identity, native association, observed life and bounded typed facts. Managed Scribe reloads preserve IDs and distinct body and discard facts. The same bound Pawn reports changes in life without allocating a new identity. | Engine-owned dead references, resurrection, corpse destruction, corpse-less death, and discard evidence. The foundation has no new native lifecycle observers. |
| Transaction, authority and conflicts | Distinguishes absent, committed empty, incomplete, unsupported and dual-store envelopes. Aperture writes are staged and revalidated. The existing save guard and both component writers reject unsafe save authority. Failure injection after an aperture write or before publication restores preimages and leaves test custody revisions, stack counts and cohort totals unchanged. Duplicate Pawn, cultivator and aperture claims remain unresolved with evidence. A pending first awakening stays private until checked publication. | Native awakening callbacks, rejected debit, failure and save ordering, physical conservation, and two fresh-process reloads on disposable save copies. |
| Exactly-once native observations | The store provides association revision and one confirmed-discard slot. A missing reference preserves evidence and supplies no discard fact. Phase 2 custody callbacks and retention rules are unchanged. | Stage 3A-II must implement and verify native event correlation, exception cleanup, deduplication and pre-save drain behavior. |
| Benchmark equivalence | The accepted C0 plan uses independently prepared clones of the same hashed source with identical aperture IDs and comparable fingerprints. | C0 was gated. No equivalence, budget or population result is claimed. |

### Build and managed checks

- `scripts/verify.ps1 -Focused` passed structural checks, the Release build against RimWorld **1.7.5014 rev1195**, and the managed suite. The main build produced **0 warnings and 0 errors**. The pinned `Assembly-CSharp.dll` SHA-256 is `11D7C9FAF52BE22933DA840D906B1AF7B48F7EA574A886C045B1FB12BD366124`.
- **46 focused cultivator checks** passed, plus the existing managed spring, cohort, custody, underground, 64 layout and 64 botanical scenarios. Managed checks link the actual game's Scribe and other assemblies. They execute without a RimWorld game process and provide no in-game acceptance.
- The existing live verification harness compiled for API compatibility. Its profiles were not run. All build outputs were produced in an isolated snapshot. No game installs, user saves, mod profiles or packaged binaries in the active checkout were changed.

### Limits and staged decisions

- `GetOrCreate` returns the durable cultivator record. The original Pawn DTO serves the legacy read path. A first ceremony for a previously undiscovered Pawn caches its private aptitude draft for same-game retries. A save made after a failed unpublished attempt carries no private draft. Committed and legacy aptitude values remain authoritative.
- Stage 3A-I saves an object reference only through recognized native ownership. A Pawn with unsupported or unavailable ownership keeps its durable record and last evidence. Native coverage for all relevant holders remains open. Retirement and record removal are deferred until confirmed-discard observation and dependency checks are verified.
- The normal 600-tick discovery scan and Phase 2 Gu custody and retention policies are unchanged. The identity foundation adds per-load aperture intake, derived indexes, and one legacy-only Pawn pass. Runtime costs and long-save growth remain open.

Stage 3A-II requires separate owner authorization before native lifecycle work, C0, or any game execution.

## Phase 3A-II — Native lifecycle validation

The owner authorized the focused lifecycle and custody matrix against Stage 3A-I commit `8271c9a2b018d5b7d8bca8b4b2a17573c8b4d768`. Broad migration and recovery and benchmark equivalence remain separate approval gates.

### Implementation and evidence boundaries

- Observe only an already bound Pawn. Nested native calls share a scope by exact Pawn object and game. The GC marker exists for that Pawn's GC call and unwinds through Harmony finalizers even on exceptions. A discard fact requires the native false-to-true `Discarded` transition. Corpse destruction captures the prior inner Pawn, then checks that the body was destroyed. Corpse-less death requires native `Pawn.Kill` and the corpse-less `PostCorpseDestroy(false)` path. A GC interruption that removes the native save owner produces an unresolved association and an evidence row.
- Complete observations enter a queue. The existing save guard rejects saves inside an active identity scope and drains completed observations after the Phase 2 events. Regular draining occurs at the existing registry tick. Each association has one discard slot and a stable event key. Current native life and historical observations remain separate. Phase 2's retention reason and its custody callbacks use their previous rules.
- The Stage 3A-I private aptitude sample vanished on reload. The optional V1 draft list carries the sample and exact native reference for an unpublished candidate with a native save owner. The provisional cultivator ID and aperture ID remain private. Existing committed saves can omit the list. An empty Phase 2 queue now returns before accessing the game tick, including when a managed fixture writes an empty identity store.
- Tests used the pinned RimWorld **1.7.5014 rev1195** engine. `Assembly-CSharp.dll` SHA-256 was `11D7C9FAF52BE22933DA840D906B1AF7B48F7EA574A886C045B1FB12BD366124`. Ordered lifecycle profile mods were `brrainz.harmony`, `ludeon.rimworld`, `projectgu.livingcultivation`, and `projectgu.priority0test`. The runner copied the installation, used a separate save-data folder for each fixture, suppressed shared file logging, and verified source and backup hashes around destructive fixture operations.

### Iteration failures kept in the evidence

| Evidence | Result and disposition |
| --- | --- |
| `78cc3eee65034534959652a1d35909a3/CultivatorLifecycle` | **FAIL new.** The generated landing area had no empty adjacent cell. The fixture now searches a reachable area for placement. The four independent regressions in the same campaign passed. |
| `a50318fdfe1445a9b21fd2053f524b05/CultivatorLifecycle` | **PASS new, FAIL reload.** The reader correctly returned `Unresolved` for the deliberately unowned live Pawn. The fixture expected `Bound` because it read the omitted default enum as if it were a literal value. The corrected assertion checks that current life is unknown and the last live observation and original token survive. |
| First Stage 3A-II managed run | **FAIL.** The added pre-save drain accessed the native clock for an empty Phase 2 queue. The empty-queue fast path restored the prior empty-store behavior. The subsequent focused run passed all 46 cultivator checks and the existing managed regressions. Release compilation had zero warnings and errors. |

### Native matrix and observations

Campaign `3f75fc1fb27c45b8ba95d28f97984c70` records the final focused build. Its lifecycle result, trace and fixture snapshots are `CultivatorLifecycle/priority0-result.txt`, `CultivatorLifecycle/cultivator-callbacks.txt` and `CultivatorLifecycle/cultivator-fixture.json` under the runner's campaign directory.

| Case | Native result |
| --- | --- |
| Ordinary death, corpse destruction and corpse-less death | **PASS.** Corpse-owned and world-owned dead Pawns remained distinguishable. Actual body teardown and the corpse-less native path produced separate facts. The first fresh process restored both types of native ownership. |
| Native Pawn destruction, native discard, dead discard and actual GC | **PASS.** Nonlethal destruction of a held world Pawn preserved its binding. Six confirmed discards each produced one commit and one association-qualified key. The dead world discard recorded `Dead`. The collector naturally discarded the otherwise unprotected identity. |
| Nested callbacks and injected GC failure | **PASS.** GC entered `Pawn.Destroy`, then `Pawn.Discard`, then `Corpse.PostCorpseDestroy(true)`. Scope finalizers exited before the single discard commit. An unrelated Pawn discarded during the outer call received `NativeDiscard`. The injected exception after native ownership removal propagated and left one unresolved evidence row without a discard fact. A later explicit discard correctly used the native cause. |
| Save ordering and missing references | **PASS.** A save attempted inside the discard callback produced no file. A save on the same tick after native completion drained the queue before writing. The deliberately unowned live Pawn round-tripped as unresolved with an unknown current state, the original token and the last live observation. |
| Invalid store save authority | **PASS.** A separate process loaded a disposable clone with the completion marker set to false. The engine save entry point rejected both a new save and an overwrite of that clone. The attempt preserved the clone's bytes and the runner preserved the original checkpoint backup. |
| Retention and off-map transition | **PASS.** The native collector retained the Pawn marked `ProjectGuCustody` with the exact Gu Thing. The unprotected control was collected. The map-to-world Pawn restored as world-owned and returned to the map with the same cultivator ID. |
| Resurrection, contained Gu and aptitude | **PASS.** The first fresh process used ordinary resurrection and retained the cultivator ID, aperture and contained Gu revision 3. The injected `Hediff.PostAdd` failure left the spring at three adults. The first fresh process restored the unpublished aptitude of 73, committed one identity and aperture, and consumed one adult. The second fresh process kept that result and restored the exact retained physical Gu with custody revision 2. |
| Backup integrity | **PASS.** Both fresh processes confirmed the pre-event source and backup were unchanged. Their SHA-256 was `A2CE48C3CEA8D1F0A08434667013EB0ADE72529079C97D78050C4A02E4D3F581`. |

The Phase 2 lifecycle, awakening, cargo and core fixtures passed generation and fresh reload in both the final campaign and the Stage 3A-I baseline campaign `16cb3694f8e2481bbc5dfd057963ac5c`. They check the existing custody outcomes, awakening rollback, native underground transport and physical conservation. The cargo fixture transports one original 37-stone stack and delivers all 37 through vanilla hauling after a fresh load. The final campaign passed **5 profiles across 12 native processes**, with zero game-copy mutations, zero isolation problems and an unchanged normal RimWorld data folder.

This matrix covers the tested Core-only native paths and the injected failure points. Broad legacy migration, additional holder and mod combinations, generalized recovery, long-history bounds and C0/C4 measurements are open for their respective approval gates.

## Phase 3A-III — Legacy migration, conflict handling and recovery

The owner provisionally accepted Stage 3A-II and authorized the C3 matrix on `efd328257d1c95675f738e3bb118a360315361ec`, including the hash-verified historical derivatives. The baseline was pushed to `feature/gu-framework-v2` by fast-forward.

The runner validates all seven V1 sources against `SHA256SUMS` before launching the game. It denies writes and deletion to every source by holding a read-sharing handle, verifies the working copies before loading them, and hashes all held sources in cleanup even if a stage fails. Every mutation and resave targets a disposable copy. The local V1 archives and historical derivatives also have the read-only file attribute. The failed-preflight control `cc784414e9e747919951b2f933179271` stopped on a deliberately missing input and finalized matching before and after SHA-256 values for the seven sources already held.

The first native campaign, `6255242e35b8451dacdb63e4c047f6d6`, exposed a duplicate aperture-conflict entry after fresh reload. Windows Scribe can return CRLF for a diagnostic built with LF. Conflict comparison now treats those line endings as equivalent and retains the saved payload verbatim. Failed rollback diagnostics now identify the affected native Hediff and Pawn, the intended preimage, the applied ID and the observed ID. Both corrections are limited to load and recovery.

`scripts/verify.ps1 -Focused` passed structural checks, Release compilation with zero warnings and errors, **49 cultivator foundation checks** and the existing managed regressions. The new checks cover the actual upstream failure boundary and both Scribe writers, rollback failure and restoration of unaffected preimages, and repeated diagnostics with Windows line endings. The engine pin is **1.7.5014 rev1195**. The `Assembly-CSharp.dll` SHA-256 was `11D7C9FAF52BE22933DA840D906B1AF7B48F7EA574A886C045B1FB12BD366124`.

### Revised development acceptance and current-format evidence

The owner has explicitly removed compatibility with old experimental V1 saves from the development-build release gate. Historical V1 migration, the seven archived sources, the two derivatives and recovery cases specific to those formats remain unverified for this milestone; the missing original 31×31 and 39×39 saves were not tested. The legacy migration code and fixtures are retained in the working changes for future use, but no compatibility claim is made.

The retained current-format corrections are CRLF-insensitive conflict de-duplication that preserves the original saved payload, and rollback diagnostics that identify the aperture, Pawn, preimage, applied value and observed value. Managed coverage checks repeated Scribe conflict loading and failed rollback. `LiveCultivatorMigration` supplies the current Phase 2 conflict case; its optional V1 branches are not part of this acceptance.

On RimWorld **1.7.5014 rev1195**, the focused native `CultivatorLifecycle` fixture passed generation, reload, second reload and invalid-save rejection in campaign `Priority0-55d25c8814544699a4e54110ffc212af`. The runner preserved the per-fixture PASS results but its overall invocation ended during final reporting because the System event log was inaccessible; this campaign is used only for its completed fixture evidence, not as a successful overall campaign.

The complete focused C3 current-format conflict campaign `Priority0-1b2b00cced14478694ea55bae5639f20` passed migration and two independent fresh-process reloads. Each process reported 3 cultivator records, 0 bound Pawn claims, 0 aperture owners, 5 evidence entries and 56 Gu records. The saved snapshot contains 3 records, 5 evidence entries and 64 conservation facts. The harness compared record identity, raw conflict evidence and conservation against the migration snapshot on both reloads. It verified exactly one committed cultivator store, no arbitrary conflict winner, stable evidence, unchanged Gu custody and physical conservation. The current-format Phase 2 source SHA-256 was unchanged (`8AEBD9259A230255942AD5FABBD4C3B0B7DCB5E04224A549F8B2421167EEE8CE`); the disposable game-copy hash had zero changes and runner isolation checks had zero findings. Seven V1 archive hashes and both derivative hashes were also rechecked unchanged, without loading those saves.

The machine's shared-copy ACL probe cannot enforce its deny ACE in this workspace. Focused native runs used the runner's existing sequential `PerWorker` mode, one disposable game copy, and post-run tree hashing instead. RimWorld execution passed; only timing validity is unverified because Windows denied access to the System sleep event log. No throughput benchmark was run.

The revised Stage 3A development acceptance criteria are therefore met by managed verification, game-linked Release compilation and the focused current-format native evidence above. Historical migration, old-save recovery, C0/C4 measurements and broader population-cost claims remain open and are not represented as passed.

## Phase 3B — multi-Gu mortal aperture, first playable increment

The first Phase 3B slice raises the existing mortal aperture's bounded capacity from one to two distinct Gu records. It reuses the existing record-only aperture custody, durable aperture ID, transactional store/withdraw paths and native inventory projection. A player-controlled colonist with one valid aperture now has a `Manage aperture Gu` gizmo for inspection and per-record store/withdraw actions. Debug withdrawal and release also enumerate occupants and ask the player which identity to act on; the unique-occupant resolver remains ambiguous when more than one Gu is present. No species-specific behavior or tick scan was introduced.

On RimWorld **1.7.5014 rev1195**, `Slice3B3B` passed in campaign `Priority0-4a332ec975d64b9cab5edb6af6c27c46` (sequential `PerWorker`; one disposable copy; game-copy tree changes 0; isolation problems 0; normal user-data folder unchanged). The first process stored two distinct identities in one aperture, preserved one additional packed Gu, travelled underground, and saved. A fresh game process restored both aperture records and the packed Gu; withdrawing one left the other bound, re-storing preserved both identities, and only one physical packed projection existed at each expected point. The UI gizmo was present with Development Mode disabled. Focused managed checks and game-linked compilation passed; the final Release build is recorded with the implementation commit.

The current capacity is intentionally fixed at two. Ordinary player-facing ways to acquire Gu remain a separate gameplay increment; the current verification uses the existing development-test species to exercise the species-neutral custody framework. This evidence does not include a throughput benchmark.

## Phase 3B increment 2 — ordinary Gu acquisition

`GU_HopeGu` now points to species-configured living and dormant projections. On a player home map or protected underground map, a spring with a mature Hope Gu cohort exposes **Search for Hope Gu**. The action consumes one mature cohort unit through the existing event-keyed materialization transaction and places the living individual nearby. A colonist can right-click the living unrefined Gu and choose **Capture Hope Gu (unrefined)**; the capture job converts that same identity to a dormant ground Thing. The existing **Pick up** float-menu job moves the same Thing to inventory. Inspecting either physical form shows its stable individual ID, current physical placement, and refinement allegiance. Capture and inventory movement do not assign or change a cultivator bond.

The existing aperture store and withdraw transactions now require that the Gu's refinement record names the exact cultivator owning the selected mortal aperture. The inspection menu shows the stored bond, but does not create one. There is no refinement action or process in this increment; `GuRefinement` remains the save-backed extension point for the later timed will-enforcement process. No world-wide or per-tick search was added.

**Managed verification:** `scripts/verify.ps1 -Focused` passed against RimWorld **1.7.5014 rev1195**, including the XML and acquisition contract checks, 49 cultivator foundation checks and existing managed regressions. The mod Release build and `ProjectGu.LiveVerification` Release build each completed with zero warnings and errors. The updated `Slice3B3B` harness includes a fresh-game encounter, capture, pickup, unrefined allegiance and legitimate-owner aperture regression, but the native harness has not yet been executed. Native execution is deferred until it can be coordinated with Claude's M1 use of the shared RimWorld test environment. This increment is not yet natively verified or accepted.
