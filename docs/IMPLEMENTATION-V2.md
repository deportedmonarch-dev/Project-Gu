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
