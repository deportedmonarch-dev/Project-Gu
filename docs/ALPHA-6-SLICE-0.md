# Alpha 6 Slice 0 · Registry transaction foundation

**Status:** implemented and natively verified on `feature/alpha6-wild-gu-ecology` (`a4d40a5`). The independent Codex review required corrections; they are recorded, reproduced and verified in [ALPHA-6-SLICE-0-CORRECTIONS.md](ALPHA-6-SLICE-0-CORRECTIONS.md), which supersedes this record where they differ (independent source authority, guard ordering, owner-side debit evidence, the production lure transaction and its `player-lure:{habitat}:{species}:{actor}:{job}` intent key, recovery ordering, the historical anchor check, the rank contract and recovery hints). Awaiting owner acceptance. Not merged. Slice 1A (Black Boar Gu) is not started.

Base: accepted integration baseline `155806c27b1f34515d235f86f70cfbfd68639c0e` (`integration/alpha5b`). Research input: `research/alpha6-wild-gu-ecology` at `a2217d5df58ff9c56264ca420fb6fa27aba01476`. Its four documents are imported unchanged: [RFC-0007](RFC-0007-WILD-GU-ECOLOGY.md), [readiness review](ALPHA-6-ENGINEERING-READINESS.md), [implementation handoff](ALPHA-6-IMPLEMENTATION-AND-VERIFICATION.md) and [baseline audit](ALPHA-6-BASELINE-AUDIT.md). Where they name the Alpha 5B candidate `5df38de` as current, this document records the reconciliation with the accepted baseline.

Environment: Windows 11, .NET SDK 10.0.401, RimWorld 1.7.5014 rev1195, the installed Harmony mod (workshop 2009463077), Core + Harmony for every native stage, disposable game copies through `scripts/verify-priority0-live.ps1`.

## 1. Reconciliation with the accepted Alpha 5B baseline

The research reviewed `5df38de`. The accepted baseline adds `fd4204b`, `b37ac75`, `c86bf6b`, `7678edd`, `e8b82d4` and `155806c`. The focused contract diff `git diff 5df38de 155806c` changes 24 files:

| Area | Change since the research candidate | Effect on Slice 0 |
| --- | --- | --- |
| Registry, `GuRecord`, `GuPopulationRecord`, materialization, cohort request, springs, capture, lure | **None.** No file in `Source/ProjectGu/*.cs`, `Domain/`, `Springs/` or `Jobs/` changed. | The research's transaction analysis applies to the accepted baseline as written. |
| Autonomous underground dispatch, route reservation, cargo rotation | `GuPassageHauling`, `GuPocketProbeComponent(.Independent)`, `JobDriver_GuPassageHaul`, `JobDriver_GuPocketProbeTravel`, `Building_GuPocketProbeEndpoint`. A started haul claims the route's single worker slot after the native passage reservation; a transient surface-to-site index is invalidated on site add and map removal. | Independent of materialization. Development draws still require `HasProtectedProjectionRoute`, which is unchanged. Covered by `Alpha5BLogistics`, `AutomaticHauling`, `CargoPrototype`. |
| Independent **UG haul** work priority and save migration | New `GU_UndergroundHauling` WorkTypeDef; `guUndergroundHaulingPrioritiesInitialized` initializes the priority once from ordinary Hauling on older saves. | Independent Scribe key in `GuPocketProbeComponent`. Exercised by both historical-save stages (the owner checkpoint migrates priorities on load). |
| Installer-managed retirement and hash verification | `install.ps1` writes `.projectgu-install.json`; `verify-install.ps1` tests clean, manifestless 5A, retirement and repeat installs. Runner `-InstalledProjectGuPath`. | Slice 0 adds two source files and changes others; the manifest picks them up. Verified clean, 5A→Slice 0 and accepted-5B→Slice 0 upgrades. |
| Historical saves and evidence | Owner logistics checkpoint (`965D5AB4…`), archived 5A save (`be8fcad6…`), Legacy31/39 derivatives. Open regression records added. | Loaded by the new `Slice0` historical units and by the regression campaign. |

**Material discrepancy found before implementation (pre-existing, not introduced by 5B):** in the baseline's `TryMaterializeCohort`, the commit window was `TryConsumeMature → MarkCohortDebitCommitted → records.Add → byId → byCreationEvent → BindProjection`. Its recovery only handled a debited record that already sat in `records`. An exception after the debit but before `records.Add` would have spent one mature unit with no counted individual — a conservation violation. The debited-failure path also ignored a failed native discard. The research identified the need to test "the gap after debit and before durable record/event publication"; reading the accepted source shows it was a real gap. Slice 0 closes it (section 2).

No other architectural discrepancy was found. The RFC's ownership table holds: the registry remains the only identity authority, established springs keep their cohort rows, and no new ledger was introduced.

## 2. Architecture and transaction ordering

```
IGuPopulationSource (read-only)          GuIndividualRegistry (sole authority)
  OwnerKind, SourceId   ── stable ──►     TryMaterializeFromSource(source, request, map, cell)
  CurrentCredential     ── live ───►        1 gate (custody or Development Mode; refuses re-entry and saves)
  HasUniqueIdentity                         2 request shape · 3 stable source identity
  CohortFor(species)                        4 receipt replay or conflict  ◄─ before revision/credential tests
  RefuseIssue(species,rank,stage,           5 live credential · 6 species policy, rank, stage, source authorization
              trigger,asset)                7 unique identity · committed-revision floor · exact revision · mature > 0
  PlacementReady(map,cell,trigger)          8 placement preflight
                                            9 stage suppressed Pawn (gated factory) → native SpawnSetup → revalidate
                                           10 callback-free window: debit → receipt/record/index → binding
                                           11 publish (release suppression) → postconditions
```

**Sources.** `IGuPopulationSource` (`Domain/GuPopulationSource.cs`) is an internal, read-only interface. A source answers for its identity, live credential, uniqueness, cohort instance, issue policy and placement. It never mutates. The registry owns the transaction, the debit, the receipt and recovery. No source is hardcoded: the shared transaction contains no spring type test (a managed source guard enforces this).

**Legacy spring adapter.** `SpringPopulationSource` wraps the existing `IGuCohortHabitat` (the spring) without moving its rows: `OwnerKind = "spring"`, `SourceId = habitatId`, credential = the anchor's load ID (the same native object across saves, which is what historical `OriginNativeId` values name), uniqueness = the existing `HasUniqueIdentity` check, placement = the shipped six-cell rule with player access or protected-route rules. It authorizes `spring-search` (no asset), `spring-lure` (asset = an `encounterLures` Def of a lure species) and `development` (Development Mode). Existing entry points (`TryMaterializeCohort`, `TryEncounterWildCohort`, `TryPrepareMaterializationRequest`) are thin adapters with the same signatures and results. Spring cohorts, revisions, seeding, ecology and populations are unchanged. Search keeps the event key `player-encounter:{habitat}:{species}:{revision}`; lure now uses `player-lure:…`, so the two triggers never share a key.

**Receipts.** `GuCreationEvidence` is saved on the canonical record as `creationEvidence`: owner kind, stable source ID, credential at commit, trigger, normalized asset scope, stage, rank, and cohort revision before and after the debit. It is additive. Records created before Alpha 6 have none and keep their original rule: same habitat, same anchor ID, same species. The record keeps `origin = "cohort"`, `originId = sourceId`, `originNativeId = credential`, so historical validation applies unchanged. Load validation quarantines a malformed receipt (wrong version, disagreement with the record's origin or native ID, a debit revision that is not `before + 1`, or a revision on an undebited claim). `Compact()` does not touch the receipt, so a compacted tombstone keeps replay protection. No history is added: one receipt per created individual, carried by a record that already existed.

**Replay and conflict.** The event index (`byCreationEvent`) is consulted before the credential and revision tests. A committed event with the same payload returns the original individual with zero effect even when its revision is stale or its credential was replaced or reacquired after load. A reused key with a different owner, source, species, trigger, asset scope, rank or stage returns `Conflict`. A quarantined, ambiguous or undebited claim refuses.

**Credentials.** A new transaction requires `request.Credential == source.CurrentCredential`. The test source issues a new credential per process and per simulated instance replacement, so an old request cannot authorize a new transaction, and a credential reacquired from the live source can.

**Ownership consistency.** Besides the source's own uniqueness check, the registry rebuilds a transient *committed-revision floor* per (owner kind, source, species) from well-formed receipts on load and raises it on commit. A live cohort whose revision is below the floor (regenerated, reseeded or restored out of order under the same identity) is diagnosed with a one-time warning and blocked. Nothing is regenerated or credited.

**Commit window and publication.** Native callbacks run only during staging (`GenSpawn.Spawn` of a suppressed Pawn from the gated `Pawn_GuWildProjection.GenerateForRegistry` factory). The source is revalidated after them: credential, identity, uniqueness, exact cohort instance, revision, maturity, cell, reservation and source placement. The debit, the record's debit mark and receipt revision, the record/event/floor indexes and the binding then commit with no native calls. Publication is the release of suppression, followed by the manifestation and cell postconditions. `CustodyTransitionInProgress` is held throughout, which refuses re-entrant custody calls, makes registry queries return nothing and makes the existing save guard refuse `SaveGame` (with `ExposeData` as fallback).

**Recovery** (`RecoverFailedSource`):

| Failure | Result |
| --- | --- |
| Before the debit, shell discarded | `RolledBack`. Nothing spent; the event may be retried. |
| Before the debit, shell cleanup unproven | `Quarantined`. The shell is marked rogue and detached; an undebited quarantined claim blocks the event key. Zero count. |
| After the debit, before publication, preimage restorable | The exact counts and revision are restored (`TryRestoreUnpublishedDebit` only accepts the exact post-debit state), the debit flag is cleared first, the receipt and indexes are removed, then the shell is discarded: `RolledBack`. Safe because nothing outside the callback-free window could observe the debited state. |
| After the debit, restoration impossible, or after publication | `Unresolved`. One counted canonical individual with its receipt, `ProjectionMissing`, the shell discarded or marked rogue. Replays return it; no second debit. |

Each recovery step is independent so a fault in one never loses the counted claim or credits twice. Recovery never grants Gu, deletes unrelated Things or adjusts counts without a receipt.

**Fault probe.** `GuTransactionProbe.Reach(point, eventKey)` is an empty, non-inlined method at seven named points (before staging, source revalidation, after staging, after debit, before publication, restore preimage, cleanup). It has no behavior in play; native fixtures patch it with Harmony to throw or re-enter.

**Out of scope, unchanged:** `Aggregated` existence and registry-to-cohort return; new ecology ledgers; Black Boar, White Boar and Stone Skin Gu; surface ecology; regional or unloaded recovery; reproduction; spring population profiles; Liquor Worm attraction; Society Core.

## 3. Changed files

| File | Responsibility |
| --- | --- |
| `Source/ProjectGu/Domain/GuPopulationSource.cs` (new) | `IGuPopulationSource` (including each source's own placement refusal text), `GuSourceRequest`, `GuCohortStage`, `GuSourceOutcome`, `GuCohortPreimage`, trigger names, the inert probe, and the `GuCreationEvidence` receipt with payload conflict and shape validation. |
| `Source/ProjectGu/GuIndividualRegistry.Materialization.cs` | The generalized transaction, receipt index and committed-revision floor, recovery, and the legacy spring entry points as adapters. |
| `Source/ProjectGu/GuIndividualRegistry.Sources.cs` (new) | The legacy spring adapter and `ForSpring`. |
| `Source/ProjectGu/Domain/GuRecord.cs` | `creationEvidence` (Scribe key `creationEvidence`), `PrepareFromSource`, `CommitSourceDebit`, `RevertUnpublishedDebit`; receipt in the debug description. `PrepareFromCohort` and `MarkCohortDebitCommitted` replaced. |
| `Source/ProjectGu/Domain/GuPopulationRecord.cs` | `Preimage` and exact-only `TryRestoreUnpublishedDebit`. |
| `Source/ProjectGu/GuIndividualRegistry.Load.cs` | Receipt shape validation on load. |
| `Source/ProjectGu/Springs/CompGuSpiritSpring.cs`, `Jobs/JobDriver_GuLureWildGu.cs` | Spring encounters pass their trigger; the lure passes its bait Def as asset scope and uses its own key prefix. |
| `tests/ProjectGu.Verification/SourceTransactionChecks.cs` (new), `Program.cs` | Managed checks (section 4). |
| `tests/ProjectGu.LiveVerification/LiveSlice0.cs` (new), `LiveClanStart.cs`, `LiveLifecycleChecks.cs` | Test-only `GuSlice0Sources`/`GuSlice0Source`, the native fault matrix, reload stages and historical-save stages. |
| `scripts/verify-priority0-live.ps1` | `Slice0` profile; hash-locked historical inputs in their own units. |
| `docs/ALPHA-6-SLICE-0.md`, research documents, `docs/ROADMAP.md` | Records. |

No Def, package ID, existing Scribe key or migration semantic changed. One Scribe key was added (`creationEvidence`, inside each record).

## 4. Verification evidence

| Kind | Result |
| --- | --- |
| Static + real compilation + managed | `scripts/verify.ps1 -Focused`: structural checks passed; production build against the real 1.7 assemblies with 0 warnings, 0 errors; all managed suites passed, including **Alpha 6 Slice 0 transaction checks (487)**: exact-only restoration, receipt conflict facets, shape validation, compaction retention, a 400-step seeded conservation sequence with restored, unresolved and replayed events, and ordering guards. The managed project still emits the existing `CS8632` nullable warnings. These are managed checks, not game behavior. |
| Native focused | Campaign `c20ece59e45e44f880afd75c51e60a62` (source build, Core + Harmony, 3 units, 7 fresh processes): `Slice0` new/reload/final, `Slice0-legacy5a` legacy/legacyreload, `Slice0-owner5b` legacy/legacyreload — **all passed**. Every injected fault appears exactly once in the engine log at its intended point; all `clan-errors-slice0-*` logs are empty. Zero game-copy or user-data changes. |
| Native regression on the upgraded install | Campaign `1c0a4cc8a4ae4cf2a48b287b459ca663` (30 units, 70 processes, the accepted-5B→Slice 0 upgraded deployment, Legacy31/39 derivatives): **28 passed**, including all three Slice0 units, `Alpha5B` (with `legacy5a`), `Alpha5BLogistics`, `Alpha2`, `Alpha3`, `Alpha4A`, `Alpha5A`, `RankOne`, `GuGeneration`, `Slice3B3B`, `Slice3B3C`, `Slice3B4`, `Slice3B5A`, `VanillaGuInventory`, `Phase2Lifecycle`, `DormantHauling`, `ClanStart`, `Core`, `IdeologyOdyssey`, `IdeologyReset`, `Awakening`, `Botanical`, `CargoPrototype`, `AutomaticHauling`, `Legacy31`, `Legacy39`. Two failed; see below. |
| Regression correction | **`Alpha1` — caused by Slice 0, fixed.** The closed-route check correctly refused with no debit and no Gu, but the generic placement message replaced the spring's shipped text "…on a protected map", which the assertion pins. Sources now supply `PlacementRefusal`; the spring keeps its exact wording; the three other unchanged runtime messages were restored verbatim. The Alpha1 assertion is untouched. Rerun `c8ec8534f639451f807a815c9c0c8efa`: `Alpha1` and all Slice0 units passed. |
| Pre-existing failure, not caused by Slice 0 | **`Slice3B5B` reload.** Its aperture step stores an *unrefined* materialized Gu; the refinement gate added in `6c93ee1` (2026-11-23) refuses that, and the fixture was last changed 2026-11-21. The untouched accepted baseline `155806c`, run in a disposable detached worktree, fails identically (campaign `e70b4156c1a541eb9e6261384339d66f`, archived under `tests/obj/Slice0Install/`). Its `new` stage (shared with `Slice3B5A`) and the reload's replay and conservation checks before the aperture step pass. Left unchanged here; it needs a separate, owner-visible fixture correction that refines the Gu before storage. |
| Final payload on the upgraded install | After the correction, the accepted-5B deployment was upgraded again with the final payload (96 files, clean-equal; DLL `BF576487…`). Campaign `f343f33141ba48078b2798c330c7dd5b`: `Slice0`, `Slice0-legacy5a`, `Slice0-owner5b`, `Alpha1`, `Alpha2`, `Alpha3`, `Alpha5A`, `Alpha5B` (incl. `legacy5a`), `GuGeneration`, `Slice3B5A`, `Slice3B3B` — **11/11 passed**. Campaign `811aeb1d8d784d8d8456192aa8b4066e`: `Alpha5BLogistics` replaying the owner checkpoint delivered stone 65/64, petals 6/6, seeds 6/6 with the original Work priorities and stockpiles — passed. All runs: zero game-copy or user-data changes; historical inputs unchanged (`BE8FCAD6…`, `965D5AB4…`, verified after the runs). |

### Slice0 stage `new` (fresh Southern Border Clan, real founding, descent and Hope Gu awakening)

Ordinary play, Development Mode off: a spring **search** commits Moonlight Gu with a `spring/spring-search` receipt; the stale legacy request replays the same Gu; the same key as a lure is a `Conflict`. A lure with a log is refused without a debit; a **beer lure** commits Liquor Worm with a `spring-lure/Beer` receipt under `player-lure:…`; a different bait or trigger under that key is a `Conflict`.

Test-only source (Development Mode, cohorts: test species 8+1, Hope Gu 2; a second source with 1): see section 5. Conservation (`cohort units + counted individuals = opening`) is asserted for both spring species and all three test cohorts after the spring encounters, the primary commit, revalidation, the fault matrix and at the end, with every spawned Gu Pawn either resolving to a counted record or a known rogue.

### Fresh-process stages

- **reload:** both test sources reload with identical counts and revisions (no reseed, no credit); 12 counted individuals; the credential is new. All eight receipts (four bound Pawns, two unresolved claims, spring search, spring lure) survive with their revisions; each binds the same native Pawn or stays unresolved; each replays with its stale revision and old credential. Conflicting payloads still conflict; the quarantined event and the losing actor's request refuse. A new event with the previous process's credential refuses; a reacquired credential commits Hope Gu once. Floor and duplicate-identity blocks hold after load.
- **final:** the post-reload commit persisted, binds its Pawn, replays; 13 counted individuals; conservation exact.
- **Historical saves** (each a hash-locked copy in its own unit, loaded, resaved, reloaded): archived Alpha 5A save — 4 historical cohort records, all without receipts, none quarantined by receipt validation, all 4 replay under the original spring rule and refuse a different anchor credential; a new spring search on the historical spring commits with a receipt, persists and replays after the resave, with spring conservation. Owner logistics checkpoint — 2 historical cohort records, both replay under the original rule; same new-commit checks. Both input hashes were unchanged after the campaign (`BE8FCAD6…`, `965D5AB4…`).

## 5. Failure injection and conservation

Test cohort opening 9 (8 mature + 1 juvenile). Counts are asserted at every step.

| Case | Injection | Outcome | Cohort / individuals |
| --- | --- | --- | --- |
| Development Mode off | — | Refused | unchanged |
| Before staging | probe throws | RolledBack, no native object created | unchanged |
| During native SpawnSetup | postfix throws; inside it, re-entry with the same and another key and `SaveGame` | RolledBack; both re-entries refused; no save file; shell discarded and absent | unchanged; retry commits **#1** (8→7, rev 0→1) |
| Replay | stale revision; native instance replaced | Replayed, same Gu, also with no cell | unchanged |
| Stale credential | request prepared before replacement | Refused | unchanged |
| Asset-scoped commit | `slice0-bait/Beer` | Committed **#2** | 7→6 |
| Conflicts | reused keys with other trigger, species, source, asset (`Wine`, none, other trigger) | Conflict ×6; request naming a different source than supplied: Refused | unchanged |
| Authorization | rank 2, juvenile, unknown trigger, wrong bait, species not held | Refused ×5, prepare refuses | unchanged |
| After staging, before debit | probe throws; inside it, re-entry and `SaveGame` | RolledBack; re-entry refused; registry queries return nothing during the transition; no save file; shell discarded | unchanged |
| During source revalidation | probe throws on the post-callback revalidation | RolledBack | unchanged |
| Source replaced during callbacks | credential replaced in SpawnSetup | RolledBack | unchanged |
| Cohort changed during callbacks | a juvenile matures in SpawnSetup (rev 3) | RolledBack, no debit | 7 mature + 0 juvenile, total conserved |
| Between debit and receipt publication | probe throws after the debit; inside it, re-entry refused | RolledBack with the exact preimage (7, rev 3), no receipt | unchanged; retry commits **#3** (6, rev 4) |
| Before publication | probe throws after the receipt, record and binding were indexed | RolledBack; receipt removed; preimage restored | unchanged |
| Debit with failed restoration | after-debit and restore probes throw | **Unresolved**: one counted living individual, receipt rev 4→5, `ProjectionMissing`; replay returns it | 5 mature; +1 counted |
| After publication | postcondition identity read throws | **Unresolved**, one counted individual | 4 mature; +1 counted |
| Native cleanup fails before debit | SpawnSetup and cleanup probe throw | **Quarantined**: shell detached and rogue, undebited quarantined claim blocks replay | unchanged; zero count for the shell |
| Two actors, last mature Gu | A and B prepare at rev 9; B tries during A's SpawnSetup | A Committed; interleaved B refused; B's request stale; B's fresh request finds none | 0 mature; one individual for A |
| Second species | Hope Gu from the same source | Committed from its own cohort | Hope 2→1 |
| Duplicate identity | second live source with the same ID | Refused by the uniqueness check | unchanged |
| Regenerated source | cohort revision forced below the committed floor | Refused as inconsistent ownership | unchanged |

## 6. Historical save and installer compatibility

- `verify-install.ps1`: clean install, manifestless Alpha 5A upgrade, manifest-aware retirement, preserved local files and repeated install all passed for the Slice 0 payload. [Log](../tests/obj/Slice0Install/verify-install.log)
- In-place upgrade of an accepted Alpha 5B deployment (`155806c` installer and payload, 96 managed files) to Slice 0 matches a clean Slice 0 install file for file by SHA-256 and keeps a local file. [Log](../tests/obj/Slice0Install/upgrade-5b.log)
- The regression campaign ran on that upgraded deployment through `-InstalledProjectGuPath`.
- Historical records: no receipt is synthesized for old records. Their validation and replay rule is unchanged, and new records on old springs gain receipts.
- **Downgrade:** an Alpha 5B build reading a Slice 0 save ignores `creationEvidence` and validates those records under the old rule, which they satisfy. Downgrade is not a supported path and was not natively tested.

## 7. Remaining limitations and regression risks

- The only production source is the spring adapter. The generalized seam is exercised in game by a test-only source. Slice 1A must add its own source with its own placement and authorization.
- Active manifestation remains Pawn-only, as in the baseline.
- The save barrier was exercised through `GameDataSaveLoader.SaveGame` (the patched entry point). The autosave path was not separately exercised in Slice 0; the registry's `ExposeData` refusal remains the fallback.
- A recovery step that itself throws in an unforeseen way is logged as an error and resolves to a quarantined event (before a debit) or the unresolved claim (after one). Those last-resort branches are covered by reasoning and managed sequencing, not by a separate native fault. *Superseded:* the review showed a diagnostic fault could escape before the outcome was recorded; the corrections publish the managed outcome first and test the combined faults natively.
- Carried forward unchanged from the accepted baseline: the intermittent **Alpha 1 passage-travel timeout**; the **enabled and disabled beer-lure assertions** in the Alpha 3 setup; the **starvation assertion** sensitive to unrelated colony letters; the **Alpha 5B provisioning supply-control assertion**; **machine-load-sensitive renderer measurements**; **unverified SeedsPlease compatibility**. No assertion was weakened or rewritten in Slice 0. None of these recurred in the Slice 0 campaigns: `Alpha1` passage travel, the Alpha 3 lure setup (inside `Alpha3`, `Alpha4A`, `Alpha5A`, `Alpha5B`, `Alpha5BLogistics`), the `Alpha5A` late starvation check and the `Alpha5B` supply control all passed; renderer measurements and SeedsPlease were not exercised. Their evidence requirements in the [open regression records](ALPHA-5B-PLAYTEST-CORRECTIONS.md#open-regression-records) stand.
- Newly recorded: the stale **`Slice3B5B`** aperture fixture (section 4), failing identically on the accepted baseline.
- A long test-runner path can make Windows file operations fail silently: a baseline worktree placed under the deep scratch directory left a runner that died without a log. Keep runner checkouts at short paths.

## 8. Reproduce

```powershell
$repo = 'C:\Users\monve\Downloads\Project-Gu-alpha6-implementation'
Set-Location $repo
git checkout feature/alpha6-wild-gu-ecology
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -Focused
dotnet build .\tests\ProjectGu.LiveVerification\ProjectGu.LiveVerification.csproj -c Release "-p:RimWorldPath=C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
# Historical inputs (copies, never originals) are read from tests\obj\Alpha5ASaves and tests\obj\Alpha5BOwnerLogistics when present.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-priority0-live.ps1 -Profile Slice0 -Parallel 3 -StageTimeoutSeconds 1500
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-install.ps1
# Regression on an upgraded deployment: install accepted 5B (git archive 155806c) into a disposable root, run .\scripts\install.ps1
# over it, then pass that Mods\ProjectGU-LivingCultivation folder:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-priority0-live.ps1 `
  -Profile Slice0,Alpha5B,Alpha5BLogistics,Alpha1,Alpha2,Alpha3,Alpha4A,Alpha5A,RankOne,GuGeneration,Slice3B3B,Slice3B3C,Slice3B4,Slice3B5A,Slice3B5B,VanillaGuInventory,Phase2Lifecycle,DormantHauling,ClanStart,Core,IdeologyOdyssey,IdeologyReset,Awakening,Botanical,CargoPrototype,AutomaticHauling `
  -Legacy31Save <Legacy31-copy.rws> -Legacy39Save <Legacy39-copy.rws> -InstalledProjectGuPath <upgraded mod folder> -Parallel 3 -StageTimeoutSeconds 1500
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-priority0-live.ps1 -Profile Alpha5BLogistics -Alpha5BLogisticsSave .\tests\obj\Alpha5BOwnerLogistics\owner-logistics-input.rws -Parallel 1 -StageTimeoutSeconds 1500
```

## 9. Readiness

Ready for independent architectural review. Review points: the receipt-first replay order; preimage restoration inside the callback-free window versus always keeping an unresolved claim; the committed-revision floor as the inconsistency signal; the spring adapter's credential being the persistent anchor ID while new sources use session credentials; and the new `player-lure` key prefix.

Slice 1A prerequisites are unchanged from the readiness review: owner approval of Black Boar Gu's habitat, unsuitable control, manifestation, discovery, diet status, origin, ability and tuning; and the wild-physiology gates (`foodType`, mating, taming, training, aging) before a new wild Pawn ships. Slice 1A would add one natural-habitat source implementing `IGuPopulationSource` and its ledger, with no change to the transaction.
