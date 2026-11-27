# Alpha 6 Slice 0 · Transaction integrity corrections

**Status:** corrections implemented and natively verified on `feature/alpha6-wild-gu-ecology`. Awaiting independent review and owner acceptance. Not merged. Slice 1A is not started.

- **Base:** Slice 0 candidate `a4d40a5670c10300401737d108a080824485ae71`. Accepted baseline `155806c27b1f34515d235f86f70cfbfd68639c0e`. Research checkpoint `a2217d5df58ff9c56264ca420fb6fa27aba01476`.
- **Review:** [the independent Codex review](evidence/ALPHA-6-SLICE-0-CODEX-REVIEW.md), preserved byte for byte (SHA-256 `a881add4…42024be`). Its verdict was corrections required before Slice 1A: five High, two Medium and one Low finding, plus the pre-existing `Slice3B5B` fixture failure.
- **Owner authorization:** corrections to Slice 0 only, plus the wildlife-population independence audit.
- **Environment:** Windows 11, .NET SDK 10.0.401, RimWorld 1.7.5014 rev1195, the installed Harmony mod (workshop 2009463077), disposable game copies through `scripts/verify-priority0-live.ps1`, and ILSpy 11.2 for the engine audit.

## 1. How each finding was established

Every finding was reproduced **natively against the unchanged candidate** before anything was corrected. A detached worktree at `a4d40a5` received only a test-side reproduction profile, `Slice0Repro`. It observes each scenario and reports it as DEMONSTRATED or NOT DEMONSTRATED rather than asserting.

**Result:** all 19 scenarios were demonstrated, 15 in-process and 4 after a fresh-process load. None was source-derived only. Evidence: [reproduction fixture](evidence/alpha6-slice0-review-repro/LiveSlice0Repro.cs.txt), [harness diff](evidence/alpha6-slice0-review-repro/repro-harness.diff), [raw results](evidence/alpha6-slice0-review-repro/priority0-result.txt) and [campaign summary](evidence/alpha6-slice0-review-repro/campaign-summary.txt) (campaign `ee17a451…`). The corrected `Slice0` profile asserts the opposite outcome of each scenario.

No finding is disputed. Each correction section below gives the root cause confirmed in source and the native observation.

## 2. Corrected architecture

```
TryMaterializeFromSource(source, request, map, cell, out individual, out reason, asset = null)
  0 custody guard acquired before ANY source, asset or native call (refuses re-entry, saves, registry queries)
  1 Preflight probe · request shape · stable identity matches the supplied adapter
  2 receipt replay first (zero effect); historical records also need the exact live anchor
  3 independent authority: the owners' directories resolve exactly one live source; the supplied adapter must be that binding
  4 Development Mode (asked of the authority) · live credential
  5 species policy · rank == species minRank (Slice 0 contract) · asset scope agreement · RefuseIssue(asset) · asset.Refusal()
  6 unique identity · cohort row of the requested species · revision floor · owner-side debit evidence · exact revision · mature > 0
  7 placement preflight (re-resolves authority)
  8 stage the suppressed Pawn → native SpawnSetup → full revalidation: authority binding, credential, row, revision,
    species, RefuseIssue, asset, evidence, event key still free, placement
  9 callback-free window: event key still free → TryDebitForReceipt (count, revision, receipt digest, seal) →
    receipt → TryRecordSourceCreation (refuses an existing claim) → binding
 10 publish → postconditions
 11 for a debit that stands (committed or unresolved): settle the asset once, recording Settled or Interrupted on the receipt
 recovery: the managed outcome (unresolved claim or quarantined event claim) is published before any fallible native
           cleanup or diagnostic; diagnostics cannot throw out of recovery
```

**Owner-side authority.** `IGuPopulationSourceDirectory` (`Domain/GuPopulationSource.cs:102`) is implemented by the component that already owns and saves a kind of source:

- Springs: `GuEcologyMapComponent`, which registers live spring components.
- The test-only source: its own saved game component.

The registry asks every live owner for the sources claiming `(OwnerKind, SourceId)` and needs exactly one. The supplied adapter must present that exact `Binding` (the owning object, compared by reference). It is never a second registry: it reads the owners' own collections. Spring ownership is unchanged.

**Owner-side debit evidence.** Each cohort row carries a compact, additive record (`Domain/GuPopulationRecord.cs`):

- `receiptDebits`: how many receipt-backed debits it took.
- `receiptDigest`: a chained digest of `(revision after the debit, event key)` in commit order.
- `evidenceSeal`: over counts, revision and evidence, refreshed only by the row's own mutations.

The registry recomputes the chain from its receipts. A row that is substituted, regenerated, restored out of order or edited no longer agrees. Its scope is frozen: no new issue, and nothing credited, deleted or adjusted.

The new Scribe keys are additive: `debitEvidenceVersion`, `receiptDebits`, `receiptDigest` and `evidenceSeal` on rows; `assetId` and `settlement` on receipts. A row that has never taken a receipt-backed debit, which includes every historical spring row, has version 0 and keeps the historical rules.

**Production lure.** `JobDriver_GuLureWildGu` passes the actor, its native job and the exact held bait to `CompGuSpiritSpring.TryLureWildGu`.

- The event key is the durable lure intent `player-lure:{habitat}:{species}:{actor load ID}:{job load ID}`. Job IDs come from the unique-ID manager per `MakeJob`, are saved with the job and survive reload.
- The bait is a `GuHeldLureAsset` (`Springs/GuHeldLureAsset.cs`). Its exact stack ID and quantity are captured with the intent. Before staging and again after native callbacks, it checks the stack's identity, quantity, holder, the actor and the job. The registry spends the bait once inside the custody guard, after the debit.
- A failed spend is a durable `Interrupted` receipt outcome. A replay of the same intent with the same bait retries it; a different bait is never spent for it.
- The job no longer splits or destroys bait itself.

### Tradeoffs

- **Settlement after the debit, not before.** Spending the bait before the debit would need a reversible bait detach, and a destroyed Thing cannot be restored. Settling afterwards makes failure explicit: one counted Gu with an `Interrupted` settlement. The cost is that a native failure inside `Destroy`/`SplitOff` grants the Gu before the bait is spent. That is recorded durably, and the same intent retries the spend.
- **Freeze, do not repair.** An evidence mismatch only refuses new issue and logs once at load. Repair stays an operator action, as the RFC requires. Saves written by the never-accepted `a4d40a5` candidate carry receipts without row evidence, so their receipt scopes freeze. Historical (pre-Alpha 6) records and rows are unaffected.
- **The seal detects edits, not a forger.** It binds row state to the row's own mutations. Someone recomputing it by hand is out of scope, as with any save edit.
- **Rank contract narrowed.** Slice 0 issues only the species' minimum (wild) rank. A future source that needs higher-rank issue must extend record creation and validation together.
- **Wildlife:** no production change was needed (section 4).

## 3. Finding-by-finding disposition

Line references are to the corrected tree.

### High 1 · Re-entry during preflight could commit one event twice

- **Root cause (confirmed).** `TryMaterializeFromSource` looked up the receipt, then called the adapter (credential, policy, uniqueness, cohort) and native placement checks, and only then set `CustodyTransitionInProgress`. A callback inside preflight could run a complete nested transaction under the same key. The outer call then passed its own unchanged checks and `RecordSourceCreation` overwrote `byCreationEvent[key]`.
- **Native reproduction.** Re-entry from the credential read, using the same key against a second stocked source: nested and outer both `Committed`, two records, two debits. After a fresh load both records were quarantined ("Multiple individuals claim this creation event").
- **Correction.**
  - The guard is taken before any source, asset or native call (`GuIndividualRegistry.Materialization.cs:225–237`). The whole preflight runs inside it (`Preflight`, `:279`).
  - Event ownership is checked again in revalidation (`SourceReady`, `:430`) and inside the callback-free window before the debit (`:415`).
  - `TryRecordSourceCreation` (`:475`) refuses an existing claim. Recovery never overwrites another record's claim: `QuarantineUndebitedEvent` (`:547`) and `KeepUnresolvedClaim` (`:563`) freeze an ambiguous key as a load would.
- **Regression test.** `LiveSlice0Corrections.cs:385`. Re-entry during credential resolution, at the Preflight point, before staging and during SpawnSetup, each using the same key against a second stocked source. Each yields a refused nested call, one event claim, one debit and one canonical individual, and the same key on the second source is afterwards a payload conflict. The existing SpawnSetup and after-staging re-entry checks still pass.
- **Verified:** native `Slice0` PASS, including both fresh-process reloads.
- **Limitations:** none known.

### High 2 · The seam trusted the caller-supplied adapter's own authority

- **Root cause (confirmed).** The registry took credential, uniqueness, Development Mode, policy and the cohort row from the adapter it was handed. It never resolved an owner or checked the row's species. `RefuseIssue` ran only before staging.
- **Native reproduction.** All three committed:
  - A detached `GuSlice0Source` with the registered ID debited its unsaved row and created a canonical Gu.
  - An adapter returning another species' row debited the Hope row for a dev-species record.
  - Revoking authorization in SpawnSetup did not stop the debit.
- **Correction.**
  - `ResolveSource`/`Authority` (`:61`, `:77`) resolve through `IGuPopulationSourceDirectory` owners (`GuEcologyMapComponent`; `CollectSpringSources`, `GuIndividualRegistry.Sources.cs:16`). They require exactly one live binding and require the supplied adapter to be it (`Binding`, `Sources.cs:41`). Every later call goes to the authority, never to the supplied adapter.
  - The cohort's species must match (`:341`, and revalidation).
  - `RefuseIssue`, the asset and the evidence are revalidated after native callbacks, together with the authority binding and the credential (`SourceReady`, `:430`).
  - Actor and asset eligibility enter through `IGuSourceAsset.Refusal()`.
- **Regression test.** `LiveSlice0Corrections.cs:418`. Refused, with no debit on any row:
  - a detached instance with the legitimate identifier;
  - a fabricated adapter with its own binding;
  - a registered source returning another species' row;
  - authorization revoked during SpawnSetup;
  - the source removed from its owning component during SpawnSetup;
  - an asset withdrawn during SpawnSetup.

  An adapter that presents the real binding but lies about its row and policy is answered by the owner: one correct debit from the authoritative row.
- **Verified:** native `Slice0` PASS.
- **Limitations:** a future source kind needs its owning component to implement the directory; this is documented in the interface.

### High 3 · The production lure job could publish a Gu without spending bait, with no stable replay identity

- **Root cause (confirmed).**
  - The job passed only a bait Def, then split or destroyed the held item after the registry released its guard.
  - The key was revision-derived, so a re-run toil was a new event.
  - Nothing revalidated the bait's holder.
  - The search adapter authorized `spring-search` for lure-only species.
- **Native reproduction.** All five demonstrated:
  - A direct SpringSearch for Liquor Worm committed.
  - A `SplitOff` fault left the Gu and the beer.
  - Running one job's final toil twice drew two Liquor Worms and spent two beers.
  - Bait moved to another colonist during staging was still drawn, then consumed from the other colonist.
- **Correction.**
  - `CompGuSpiritSpring.TryLureWildGu`/`LureEventKey` (`CompGuSpiritSpring.cs:320`, `:336`) and `GuHeldLureAsset` (`Refusal` `:55`, `SettlementRefusal` `:46`, `TrySettle` `:67`). The job calls it (`JobDriver_GuLureWildGu.cs:62`).
  - `SettleAsset` (`Materialization.cs:634`) runs inside the guard, after publication, for any debit that stands. It records `Settled` or `Interrupted` on the receipt (`GuAssetSettlement`, `GuPopulationSource.cs:31`) and retries on a replay of the same intent.
  - The spring adapter refuses `spring-search` for any `UsesLureEncounter` species and requires a `GuHeldLureAsset` for a lure (`Sources.cs:57`, `:62`).
  - The beer-lure gameplay, labels, options and 600-tick wait are unchanged. No expanded alcohol attraction was added.
- **Regression test.** `LiveSlice0Corrections.cs:115–335`, all through the real job ordered from the float menu:
  1. An ordinary lure: one Gu, one beer, a settled receipt, the key naming actor and job.
  2. An interrupted lure: drafted mid-wait, beer kept.
  3. A transaction fault before staging: rolled back, beer kept.
  4. The final toil executed twice: one Gu, one beer. A save requested while the bait was being spent was refused.
  5. A `SplitOff` failure: Gu counted, `Interrupted`, then the same intent spends the same bait once.
  6. Bait moved to another holder, or its quantity changed, during staging: rolled back, nothing spent.
  7. Two colonists luring the last mature worm: one commit, one beer.
  8. A destruction failure persisted as `Interrupted` and settled exactly once in the next fresh process.

  A lure saved mid-wait completes after reload under the saved intent key (`:617`). Direct unauthorized `spring-search` and asset-less `spring-lure` requests for Liquor Worm are refused (`LiveSlice0.cs:194`, `:204`). `Alpha3`, `Alpha4A`, `Alpha5A`, `Alpha5B` and `Alpha5BLogistics` exercise the ordinary lure UI.
- **Verified:** native `Slice0` PASS (all three stages). Regression results are in section 5.
- **Limitations.** A bait whose spend failed and which is later lost stays `Interrupted` (durable, visible in the debug description). No automatic charge is taken from another stack.

### High 4 · A revision floor could not prove the owner applied a debit

- **Root cause (confirmed).** The registry's only owner-side check was a transient floor of the highest committed revision. Nothing bound the row's contents to the receipts, and two receipts claiming one debit revision were never compared.
- **Native reproduction.** Pre-debit stock substituted at an equal revision and at revision +5 issued again. Two receipts claiming one debit revision passed in-process and after a fresh load, with neither quarantined.
- **Correction.**
  - Owner-side debit evidence on the row (`TryDebitForReceipt`, `GuPopulationRecord.cs:74`; `ChainDigest` `:139`; `EvidenceIntact` `:43`; seal maintenance `:173`).
  - The registry's `ScopeEvidenceRefusal` (`Materialization.cs:97`) recomputes the chain from receipts, detects duplicate debit revisions and freezes the scope. It runs in preflight and after native callbacks.
  - `ReconcileSourceEvidence` (`:125`) runs at `LoadedGame`, once both owners have loaded (`GuIndividualRegistry.cs:75`; maps finish `FinalizeLoading` before `GameComponentUtility.LoadedGame`). It reports frozen scopes without changing counts.
  - The floor is retained as the first, cheaper signal.
  - Historical rows (version 0, no receipts) keep their rules. The `Slice0-legacy5a` and `Slice0-owner5b` historical units pass.
- **Regression test.**
  - Native, `LiveSlice0Corrections.cs:463`: equal- and higher-revision substituted rows, a count edited outside the owner (frozen, then issuing again once repaired), duplicate debit claims, and legitimate intervening maturation and a birth. Frozen scopes keep counts and every claim after the first and second fresh processes (`CheckFrozenScopes`). The load-time reconciliation warning is observed.
  - Managed, `SourceTransactionChecks.cs:144`: the registry's own `ScopeEvidenceRefusal` against substituted, restored, inflated and duplicated cases. A 400-step seeded sequence of production debits, restorations and unresolved claims is checked by the same registry method after every step (`:202`).
- **Verified:** native `Slice0` PASS; managed 525 checks PASS.
- **Limitations.** A hand-forged seal is out of scope. Saves from the unaccepted candidate freeze their receipt scopes (see Tradeoffs).

### High 5 · A second native fault during recovery could escape before the outcome was recorded

- **Root cause (confirmed).** `KeepUnresolvedClaim` indexed and unbound the record, then called `MarkRogue`. That method's log line evaluated `GetUniqueLoadID()` before `MarkUnresolved` ran. Separately, the undebited branch could reach `Quarantined` without a durable claim when `tx.Record` was never assigned.
- **Native reproduction.**
  - After-debit fault plus failed restore plus failed cleanup plus a throwing identity lookup during rogue logging: the exception escaped `TryMaterializeFromSource`, leaving a debited, actionable, unbound record.
  - An identity fault before the record was assigned, plus failed cleanup and logging: `Quarantined` with no durable claim, and the same key then committed.
- **Correction.**
  - `RecoverFailedSource`, `QuarantineUndebitedEvent` and `KeepUnresolvedClaim` (`Materialization.cs:514`, `:547`, `:563`) publish the managed outcome first. For the unresolved case that is the debit flag, index, unbinding, recovery-hint removal and `MarkUnresolved` (`:583`). For the quarantine case it is a quarantined, undebited claim of the key, built without native calls even when no record was assigned. Only then do native cleanup and diagnostics run.
  - `TryDiscardStaged`, `SafeMarkRogue` and `SafeWarning` contain native failures.
  - `MarkRogue` records the rogue state before an isolated identity lookup and log (`GuIndividualRegistry.cs:290`).
- **Regression test.** `LiveSlice0Corrections.cs:511`. The two combined-failure scenarios: no exception escapes. One gives one counted, unresolved, non-actionable, unbound claim owning its key and replaying; the other gives a durable quarantined undebited claim that refuses the key. Both persist through two fresh processes (`CheckRecoveryClaims`).
- **Verified:** native `Slice0` PASS.
- **Limitations:** none known. A rogue shell whose cleanup could not be proven stays detached and rogue, as before.

### Medium 1 · Historical replay stopped verifying the supplied anchor

- **Root cause (confirmed).** For a record without a receipt, `ReceiptRefusal` compared the request to the saved origin and returned the record before consulting the live anchor. The baseline had compared the anchor's actual load ID.
- **Native reproduction.** A replacement spring Thing given the old habitat ID replayed a historical event under the old credential.
- **Correction.** In `Replay` (`Materialization.cs:373–388`), a historical record also requires the resolved authority to be the supplied binding and its live credential to equal the request's (the saved anchor ID). Modern receipt replay stays independent of the live credential.
- **Regression test.** `LiveSlice0Corrections.cs:664`, in the final fresh process: the historical replay works from the original anchor. After the anchor is replaced by another spring with the same habitat ID, it is refused under both the old and the new credential. A modern receipt still replays. The historical units still replay archived records under their original rule.
- **Verified:** native `Slice0` PASS; `Slice0-legacy5a` and `Slice0-owner5b` PASS.
- **Limitations:** none.

### Medium 2 · An authorized rank could disagree with the canonical record

- **Root cause (confirmed).** The registry accepted any rank in `[minRank, maxRank]`, the receipt stored it, and `CreateManifested` always wrote `minRank`. `ShapeError` did not compare the two.
- **Native reproduction.** A rank-2 authorization committed a rank-1 record with a rank-2 receipt, which loaded without quarantine.
- **Correction.**
  - The narrower Slice 0 contract is enforced: a request must name the species' minimum rank (`Materialization.cs:326`).
  - `ShapeError` quarantines a receipt whose rank differs from the record's (`GuPopulationSource.cs:259`).
  - A test-only species spanning ranks 1–2 exercises it (`tests/ProjectGu.LiveVerification/Defs/Slice0.xml`).
- **Regression test.** Native `LiveSlice0Corrections.cs:453`: rank 2 is refused even when the source permits it, and the rank-1 record and receipt agree. Managed `SourceTransactionChecks.cs:54` covers the rank-disagreement shape error.
- **Verified:** native and managed PASS.
- **Limitations:** higher-rank issue is future scope, and needs record creation and validation extended together.

### Low · Rollback retained discarded Pawn graphs in recovery hints

- **Root cause (confirmed).** `BindProjection` set `lastProjection`. `UnrecordSourceCreation` removed the record and binding but not the hint, so each rolled-back attempt left one discarded Pawn.
- **Native reproduction.** Five BeforePublication faults raised the hints from 13 to 18, all pointing at discarded Pawns.
- **Correction.** `ForgetRecoveryHint` (`Materialization.cs:499`) runs in `UnrecordSourceCreation`, `KeepUnresolvedClaim` and the quarantine path.
- **Regression test.** `LiveSlice0Corrections.cs:564`: five rolled-back creations leave the hint count unchanged, with no discarded references.
- **Verified:** native `Slice0` PASS.
- **Limitations:** none.

### Pre-existing · `Slice3B5B` historical aperture fixture

- **Root cause (confirmed, pre-existing).** Since `6c93ee1` the mortal aperture refuses an unrefined Gu. The fixture still deposited the cohort Gu without refinement, so it failed identically on the accepted baseline and the candidate.
- **Correction (fixture only, separate commit).** `LiveSlice3B5B.cs:20`:
  1. The unchanged production gate is exercised first: the unrefined deposit is refused, and the Gu stays in ordinary inventory.
  2. The same canonical individual is refined for the host's durable cultivator identity, with the established `BindFixtureRefinement` used by `Slice3B3B` and `Slice3B4`. Its custody and event evidence are unchanged.
  3. The deposit proceeds.

  Expected custody revisions are unchanged, because refinement allegiance is outside custody (revision 3 at deposit, 4 at release). The production refinement requirement is untouched.
- **Verified.** All four stages pass (new, reload, aperture, released), including the release, passage and final-restart assertions that were previously unreached.
- **Limitations.** The fixture binds allegiance through the test helper rather than a full will-enforcement job. The production job path is covered by `Alpha1`–`Alpha5B`.

## 4. Wildlife population independence (owner requirement)

Audit and native controls are recorded in [RFC-0007](RFC-0007-WILD-GU-ECOLOGY.md#independent-gu-and-vanilla-wildlife-populations-owner-requirement) and the [verification matrix](ALPHA-6-IMPLEMENTATION-AND-VERIFICATION.md#verification-matrix). Summary:

- **Accounting is not affected.** The only gameplay consumer of `ecoSystemWeight` sums factionless spawned Pawns, and every Gu kind is weight 0, enforced by the registry factory. Desired weight and density read no Pawns, and pools never contain Gu.
- **No correction was required.** This is now a mandatory Slice 1A gate.
- **Native profile `Slice0Wildlife`.** Sixteen manifested Gu leave occupancy, desired weight, density and the full gate bit-identical. An ordinary animal changes them (positive control). The engine's spawner tick under a fixed seed replenishes the same wildlife with and without Gu, in the spring cavern too, and after a fresh-process reload.
- **Inherited eligibility, pre-existing and unchanged.** Hunt designation, tame designation, animal-insanity target and self-tame candidate are all **true** for wild Gu. Taming and self-tame change faction outside the registry. These need an owner decision before Slice 1A ships a new wild Pawn.

## 5. Verification

| Check | Result |
| --- | --- |
| Native reproduction against `a4d40a5` (`Slice0Repro`, campaign `ee17a451…`) | 19/19 scenarios demonstrated |
| `scripts/verify.ps1 -Focused` | PASS: static checks, game-linked compilation, isolated managed tests; Alpha 6 Slice 0 transaction checks 525 |
| `scripts/verify-install.ps1` | PASS: clean install, manifestless 5A upgrade, manifest-aware retirement and repeated install |
| Accepted 5B → corrected in-place upgrade | PASS: 96/96 files equal to a clean corrected install, local file kept (`tests/obj/Slice0CorrectionsInstall/upgrade-5b.log`) |
| Native `Slice0` (+ `Slice0-legacy5a`, `Slice0-owner5b`) | PASS, campaign `594242ce…` (new, reload, final; both historical saves load, resave and reload) |
| Native `Slice0Wildlife` | PASS, campaign `e35d68d3…` (new, reload) |
| Native `Slice3B5B` with the fixture correction | PASS, campaign `a664df63…` (new, reload, aperture, released) |
| Regression campaign on the upgraded deployment | **PASS 31/31**, campaign `046377d4…` (770 s, 76 processes). Units: `Slice0`, `Slice0-legacy5a`, `Slice0-owner5b`, `Slice0Wildlife`, `Alpha5B` (with `legacy5a`), `Alpha5BLogistics`, `Alpha1`, `Alpha2`, `Alpha3`, `Alpha4A`, `Alpha5A`, `RankOne`, `GuGeneration`, `Slice3B3B`, `Slice3B3C`, `Slice3B4`, `Slice3B5A`, `Slice3B5B`, `VanillaGuInventory`, `Phase2Lifecycle`, `DormantHauling`, `ClanStart`, `Core`, `IdeologyOdyssey`, `IdeologyReset`, `Awakening`, `Botanical`, `CargoPrototype`, `AutomaticHauling`, `Legacy31`, `Legacy39`. Read-only game copies, zero game-copy or user-data changes. |
| Owner 5B logistics checkpoint replay on the upgraded deployment | **PASS**, campaign `c10c350f…`: stone 65/64, petals 6/6, seeds 6/6 with the original Work priorities and stockpiles |

Historical inputs were not modified (SHA-256 verified after all runs): `Legacy31-copy.rws` `09F225C1…`, `Legacy39-copy.rws` `7EFCE436…`, archived 5A save `BE8FCAD6…`, owner 5B checkpoint `965D5AB4…`. The runner loads hash-checked copies.

## 6. Changed files

- **Production:**
  - `Source/ProjectGu/Domain/GuPopulationSource.cs`: directory, binding, asset contract, settlement, rank shape check.
  - `Source/ProjectGu/Domain/GuPopulationRecord.cs`: debit evidence and seal.
  - `Source/ProjectGu/Domain/GuRecord.cs`: asset ID and initial settlement.
  - `Source/ProjectGu/GuIndividualRegistry.Materialization.cs`: transaction, authority, evidence, recovery, settlement.
  - `Source/ProjectGu/GuIndividualRegistry.Sources.cs`: spring adapter.
  - `Source/ProjectGu/GuIndividualRegistry.cs`: safe rogue logging, load reconciliation.
  - `Source/ProjectGu/Springs/GuEcologyMapComponent.cs`: spring directory.
  - `Source/ProjectGu/Springs/GuHeldLureAsset.cs`: new.
  - `Source/ProjectGu/Springs/CompGuSpiritSpring.cs`: search and lure entry points.
  - `Source/ProjectGu/Jobs/JobDriver_GuLureWildGu.cs`.
- **Tests:**
  - `tests/ProjectGu.LiveVerification/LiveSlice0.cs`
  - `LiveSlice0Corrections.cs` (new)
  - `LiveSlice0Wildlife.cs` (new)
  - `Defs/Slice0.xml` (new)
  - `LiveClanStart.cs`, `LiveLifecycleChecks.cs`: dispatch
  - `tests/ProjectGu.Verification/SourceTransactionChecks.cs`
  - `scripts/verify-priority0-live.ps1`: the `Slice0Wildlife` profile
  - `LiveSlice3B5B.cs`, in its own commit
- **Docs:** this record, `docs/evidence/`, RFC-0007, the verification matrix and the Slice 0 record.

## 7. Reproduce

```powershell
$repo = 'C:\Users\monve\Downloads\Project-Gu-alpha6-implementation'
Set-Location $repo
git checkout feature/alpha6-wild-gu-ecology
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -Focused
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-install.ps1
dotnet build .\tests\ProjectGu.LiveVerification\ProjectGu.LiveVerification.csproj -c Release "-p:RimWorldPath=C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-priority0-live.ps1 -Profile Slice0,Slice0Wildlife,Slice3B5B -Parallel 3 -StageTimeoutSeconds 1500
# Regression on an upgraded deployment (accepted 5B 155806c installed into a disposable root, then .\scripts\install.ps1 -HarmonyPath <1.7 0Harmony.dll> over it):
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-priority0-live.ps1 `
  -Profile Slice0,Slice0Wildlife,Alpha5B,Alpha5BLogistics,Alpha1,Alpha2,Alpha3,Alpha4A,Alpha5A,RankOne,GuGeneration,Slice3B3B,Slice3B3C,Slice3B4,Slice3B5A,Slice3B5B,VanillaGuInventory,Phase2Lifecycle,DormantHauling,ClanStart,Core,IdeologyOdyssey,IdeologyReset,Awakening,Botanical,CargoPrototype,AutomaticHauling `
  -Legacy31Save <Legacy31-copy.rws> -Legacy39Save <Legacy39-copy.rws> -InstalledProjectGuPath .\tests\obj\Slice0CorrectionsInstall\Upgrade\Mods\ProjectGU-LivingCultivation -Parallel 3 -StageTimeoutSeconds 1500
# Review reproduction against the candidate: a detached worktree at a4d40a5 plus docs/evidence/alpha6-slice0-review-repro
# (copy LiveSlice0Repro.cs.txt to tests/ProjectGu.LiveVerification/LiveSlice0Repro.cs, Slice0.xml.txt to Defs/Slice0.xml, apply repro-harness.diff), then:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-priority0-live.ps1 -Profile Slice0Repro -Parallel 1 -StageTimeoutSeconds 1500
```

## 8. Remaining limitations and risks

- These items are carried forward unchanged and reported separately from this pass:
  - the Alpha 1 passage-travel timeout;
  - the Alpha 3 enabled/disabled beer-lure setup assertions;
  - the starvation assertion's sensitivity to unrelated letters;
  - the Alpha 5B provisioning supply-control assertion;
  - machine-load-sensitive renderer measurements;
  - unverified SeedsPlease compatibility.

  None recurred in campaign `046377d4`: `Alpha1` passage travel, the Alpha 3 lure setup (inside `Alpha3`, `Alpha4A`, `Alpha5A`, `Alpha5B` and `Alpha5BLogistics`), the `Alpha5A` late starvation check and the `Alpha5B` supply control all passed. Renderer measurements and SeedsPlease were not exercised. They remain open regression risks, not results of this pass.
- Inherited hunt, tame, insanity and self-tame eligibility of wild Gu (section 4) is pre-existing. It is recorded for an owner decision and was not changed in this pass.
- Rendered (windowed) inspection was not part of this pass. Every result is headless native.
- Slice 1A remains gated as before, now including the wildlife-independence controls.
