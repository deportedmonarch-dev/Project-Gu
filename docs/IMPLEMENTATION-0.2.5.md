# Gu Framework 0.2.5 — implementation journal

## Slice 1: Species catalog — player-validated (12/12 checks)

**Scope:** Adds `GuSpeciesDef : Verse.Def` and two XML species (`GU_HopeGu`, `GU_MoonlightGu`). No ecology, awakening, existing save keys, jobs, or pre-existing gameplay behavior modified. `DefDatabase<GuSpeciesDef>` is the species catalog; no manually synchronized registry.

**Intentional constraints:**
- A species definition is **not** an individual Gu or a wild population.
- `capabilityTags` are declarative only; they do not execute abilities.
- Hope Gu have an explicit `spirit_spring` affinity. Species-neutral machinery must preserve species-specific habitat rules.
- Moonlight Gu is a contrasting, non-wild, ability-oriented declaration.
- XML Def type resolution requires an actual RimWorld 1.7 smoke test.

## Player acceptance results — 2026-11-19

The project owner reported **12/12 in-game checks passed** after the fully-qualified GuSpeciesDef XML loading hotfix. The prior startup Def resolution error no longer appears. Regression checks covered clean loading, spring ecology, flora support, awakening, stones, save/reload, and absence of unintended physical Gu spawning. **Moonlight Gu being unspawnable is expected:** only a GuSpeciesDef exists, not a ThingDef/physical entity or ability. This is player-reported acceptance, not an independently captured automated runtime test.

## Tests

1. Run `./scripts/verify-gu-framework.ps1` in PowerShell for static checks.
2. Run `./scripts/verify.ps1` for static checks plus compile against installed RimWorld 1.7 assemblies.
3. Start game with Core + Project GU. Inspect error log for unknown `GuSpeciesDef`, unexpected XML fields, class/Def resolution and rank errors.
4. Verify basic spring, flora, stone production, awakening, save/reload on a backup of the validated 0.2.4 save.
5. Confirm neither Gu species spawns automatically; this is definition-only.
6. Do **not** merge into `main` until player acceptance.

## Slice 2: Species-keyed spring cohorts — player-reported 10/10 gameplay tests passed

The new save-backed `GuPopulationRecord` and `GuPopulationLedger` provide species-independent cohort storage and consumption. Spirit springs remain authoritative owners of their own cohorts, keyed by species Def name. The `guPopulationSchema025` marker prevents old legacy save fields from reinitializing a deliberately empty new cohort. Migration imports the mature count, juveniles and ecology tick, with old reserve/stone/habitat fields retained. Stable spring IDs are persisted and duplicate IDs are diagnosed in the map registry.

A developer-only `GU_DevTestSpringGu` can be seeded once through a spring dev gizmo to verify two species persist independently. Its cohort is available for persistence testing. Slice 3B-1 also uses this species for a separate Development Mode wild projection. Wild Gu are allowed to omit habitat tags because habitats are preferential.

**Player acceptance (2026-11-19):** Project owner reports **10/10 tests passed** for Slice 2. This is player-reported runtime acceptance, not an independently reproduced CI run; the owner has not yet explicitly confirmed the output of `verify.ps1`/`dotnet build`, so **local compilation confirmation remains pending**. The normal `verify.ps1` invokes `build.ps1` using the user's RimWorld 1.7 assemblies. Do not merge into main until compilation is confirmed and pending review gates close. See `TEST-SLICE-0.2.5-2.md`.

The awakening debit issue recorded during Slice 2 was addressed in [Phase B](#phase-b-awakening-conservation).

## Slice 2 follow-up — population save integrity

A versioned save with missing cohorts, an unsupported schema, duplicate keys, or negative
cohort counts keeps the population fields that Scribe loaded and disables cohort mutations.
An existing unversioned list is authoritative, including an empty list. Only an unversioned
save without a list imports legacy scalars. Unresolved species retain their saved counts
and cannot be consumed or simulated until their definition is available.

Local verification on Debian with .NET SDK 10.0.404 passed both Python validators and
the structural stages of `scripts/verify.ps1`. An isolated harness compiled the cohort
and ledger source with Verse stubs and confirmed standard maturation and consumption,
plus no birth above capacity when the total exceeds `int.MaxValue`. The full script
stopped at the missing RimWorld 1.7 `Assembly-CSharp.dll`. Windows compilation and
gameplay remain pending, including save round-trips with explicitly empty lists, missing
versioned lists, unresolved species, and old saves with zero adults and surviving juveniles.

## Follow-up work

See the [roadmap](ROADMAP.md) for sequencing and current gates.

- Verify Slice 2 population custody, spring/site identity and migration through real game testing.
- Add persistent individual Gu identity and count-conserving materialization/capture.
- Verify at least one **functioning** second species before claiming the framework is extensible.

## Phase B awakening conservation

The installed RimWorld 1.7.5014 rev1195 assembly shows `HediffSet.AddDirect` inserting the Hediff before invoking `PostAdd`. `Pawn_HealthTracker.AddHediff` then checks health state and invokes Hediff givers. The service keeps the exact aperture reference, blocks a second attempt on the same Pawn during those callbacks, and requires a successful spring debit before committing the Pawn record. It removes the provisional aperture through native health APIs if insertion fails or the debit is no longer available. A zero-aptitude attempt commits the same debit and its failed state. The existing record, Hediff, and cohort save keys remain in use.

`scripts/verify.ps1` passed structural checks, .NET 10 compilation against the installed assemblies, and isolated managed engine fixtures. The headless native `scripts/verify-priority0-live.ps1 -Profile Awakening` fixture uses Core, Harmony 3.0.4 and Project GU. It verifies unavailable aperture creation, an injected exception after native insertion, loss of the last adult during a callback, insufficient adults for either outcome, same-Pawn reentry through both entry points, one debit for each completed outcome, and repeated attempts before and after a fresh-process save reload. The injected exception produces one expected test error message. The fixture calls the ceremony service directly. Player-driven cancellation during the waiting toil and historical save backups remain separate regression checks.

## Phase E automatic hauling

`16fce54` adds a player-controlled Hauling assignment for the opened independent surface-to-spring passage. It uses three map-local jobs and real Pawn inventory. The live fixture conserved 66 shipment stones through pickup, a blocked return, fresh-process reload, interrupted delivery, partial storage and successive trips. [Controls, custody and failure cases](UNDERGROUND-SPIRIT-SPRING.md#automatic-hauling).

## Phase F focused regression

Windows RimWorld 1.7.5014 rev1195, .NET SDK 10.0.401 and Workshop Harmony 3.0.4. `scripts/verify.ps1 -Focused` passed structural checks, game-linked compilation and isolated managed engine fixtures on the Phase E checkpoint. It sampled 64 seeds across the standard 5,760-seed range. The standard suite ran once during Phase E.

`scripts/verify-priority0-live.ps1 -Profile Regression` runs the checks below in disposable native games. Pass `-Legacy31Save` and `-Legacy39Save` with the two reference saves to include historical loading. The runner verifies the original SHA-256 hashes after loading disposable copies. It removes only the empty historical generator component node from the 31×31 test copy.

All 14 headless native stages passed across two runs. The first botanical attempt found no matching standard-soil cell in the random surface start. The fixture now supplies standard soil in its disposable map and the selective rerun passed the remaining profiles. Both historical source hashes matched. The awakening fixture logged its expected injected `PostAdd` failure and verified the rollback.

| Native profile | Check |
|---|---|
| `IdeologyOdyssey` | Independent address and map persistence, existing Pawn and inventory travel, emergency return, legacy pocket protection, removal guards, and Ideology reset preflight. |
| `IdeologyReset` | Native empty-cavern reset, sealed map and the original 137-stone stack after reload. |
| `Awakening` | Native callback failure, reentry, cohort depletion, one mature debit per completed outcome, and saved Pawn and cohort state. |
| `Botanical` | Native crop sowing, growth and regrowth, wet and dry output conservation, and restored flora, 202 stones and Hope Gu after reload. |
| `CargoPrototype` | Original 37-stone stack through the passage and into a filtered surface stockpile using vanilla hauling after reload. |
| `AutomaticHauling` | Normal Work dispatch, two shipments, full and filtered storage, reservations, carrying mass, cancellation, invalid targets, packed cargo after reload, and ordered Pawn travel. |
| `Legacy31`, `Legacy39` | Reference saves retain their owner, terrain, Building flora, reserve, Hope Gu and 19 or 24 stones. |

Owner acceptance remains separate. Exercise the new assignment, pause and Work priority controls in a rendered colony, and try cancellation while a colonist is waiting to awaken. The full Archonexus quest flow remains an interface check.

## Slice 3A checkpoint — native engine probes

The three probes are complete in the disposable verification mod. `scripts/verify.ps1 -Focused` passed structural checks, actual 1.7 assembly compilation and the isolated managed suite. The `Slice3A` profile passed both native stages on Windows RimWorld 1.7.5014 rev1195, Core, Harmony 3.0.4 and .NET SDK 10.0.401.

| Probe | Result |
|---|---|
| Wild Pawn retirement | Despawn and explicit native discard cleared the wild Hare's job and reservation. Observed zero kill calls, zero corpse creations and one discard cleanup callback. The retired ID stayed absent from world Pawns, map Pawns and corpses after reload. |
| Unstackable Thing | Two distinct unit stacks survived merge-enabled inventory operations. Rejected placement preserved the held Thing. Fresh-process reload resolved the same Thing references under the native map and inventory owners, and return travel and drop preserved the held instance. The fixture was corrected to account for the map owning spawned Things. |
| Exact aperture binding | The saved Pawn and Hediff reference pair resolved across passage travel and reload. A same-Def replacement and host death invalidated their original bindings. |

### Slice 3A regression rerun

The two profiles interrupted at the checkpoint passed when rerun from `1c52ace`. The disposable runner compiled Project GU and the verification mod against the installed Windows RimWorld 1.7.5014 rev1195 assemblies with zero warnings and errors. It ran headless with Core and Harmony 3.0.4 via `scripts/verify-priority0-live.ps1 -Profile @('Awakening','CargoPrototype')` in PowerShell.

| Profile | New | Fresh-process reload | Evidence |
|---|---|---|---|
| `Awakening` | PASS | PASS | Rejection, the injected `PostAdd` failure, late depletion and reentry preserved counts and Pawn state. Each completed outcome debited one adult Hope Gu. Reload restored the depleted cohorts and preserved once-only awakening. The injected exception was the expected error log entry. |
| `CargoPrototype` | PASS | PASS | The original 37-stone stack crossed the passage in native Pawn inventory. Reload restored the same held stack once, and native hauling delivered all 37 stones into the filtered surface stockpile. |

The disposable run wrote `Awakening/priority0-result.txt` and `CargoPrototype/priority0-result.txt` under `tests/obj/Priority0-abea2d6343614549a992b1f51b4e2c1f`. The 31×31 and 39×39 historical saves matched their Phase F SHA-256 hashes. The regressions cleared the compatibility gate for planning Slice 3B. [Slice 3B implementation plan](RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md#slice-3b-implementation-plan).

### Native retirement limits

The Slice 3A fixture observed that despawning the wild Hare stopped its job and released its reservation. Explicit discard produced zero calls to `Pawn.Kill` and the corpse factories. The one `Corpse.PostCorpseDestroy` call carried the discard flag, and the Pawn stayed out of the map and world registries after reload.

Inspection of the installed 1.7.5014 assembly shows that `WorldPawns.PassToWorld(..., Discard)` runs native destruction and discard. `Pawn.Discard` clears relations and notifies play and battle logs, tales, quests and memories. Its corpse-cleanup path can remove ownership, destroy equipment, inventory and apparel, and notify Hediffs. `Pawn.Destroy` also has a DLC-dependent Anomaly death-notification path. The Core fixture establishes the outcome for an empty, unowned, unrelated Pawn of the tested kind. Custom Hediffs, third-party callbacks and DLC behavior remain verification gates for the production projection.

Slice 3B should admit only known Gu projections whose ownership, contents and callbacks satisfy a checked retirement contract. Treat native discard as irreversible. Use despawn for reversible staging, test respawning the same Pawn on failure, and prove guarded binding and cleanup with the actual Gu projection before enabling the wild-to-dormant transition. A failed cleanup needs a recoverable record binding that blocks duplicate interaction and retains the original Gu ID.

## Slice 3B-1 — individual identity and wild Pawn binding

`GuIndividual` and its dedicated GameComponent save the canonical records under `guIndividualRecordsV1`. After load, the registry validates saved species keys, identity, custody, exact native Pawn ownership and binding revision before filling its live indexes. Quarantine retains the saved claims and the diagnostic reason. The developer species has a native wild Pawn projection with an ID and revision tag. Development Mode exposes **Spawn test Gu**, the selected Pawn's binding readout, and **Log Gu individual records**.

Verification used the production code from `ec9889c` on Windows RimWorld 1.7.5014 rev1195 with Core, Harmony 3.0.4 and .NET SDK 10.0.401. Both projects compiled with zero warnings and errors. `scripts/verify.ps1 -Focused` passed structural checks, game-linked compilation and managed engine fixtures, including the 64-seed cavern and flora sample.

| Native check | Result |
|---|---|
| `Slice3B1` new game | PASS. The production Def resolved. The spawn path rejected Development Mode off and an occupied cell. The healthy Gu displayed its ID, revision and custody. |
| `Slice3B1` fresh-process reload | PASS. The healthy Gu retained its original ID, revision, species, provenance, and native Pawn on its original map. Ten malformed claims were quarantined. The cases covered duplicate IDs, shared native owners, duplicate projection tags, unavailable owner and species keys, a stale revision and an unsupported record version. The original claim data survived reload, and resave included the quarantine reasons. The exact Pawn and record counts were conserved. |
| Existing systems with the registry loaded | PASS. The same native fixture verified a one-time awakening debit, restored aperture and Hope Gu cohort, native flora, one produced stone and its reserve debit, and return through the saved independent passage. |
| `Legacy31` and `Legacy39` | PASS. Both disposable copies restored their original map owner, terrain, Building flora, reserve, adult Hope Gu and physical stones. Each loaded an empty individual registry. Both original SHA-256 hashes matched Phase F. |

Run `scripts/verify-priority0-live.ps1 -Profile Slice3B1` and provide `-Legacy31Save` and `-Legacy39Save` to include the reference saves. Local evidence is in `tests/obj/Priority0-371574e2cc204230bc1dbaf1c77c813f` under the three profile directories. The native run exercised the spawn and inspect methods headlessly. Rendered UI acceptance is an owner check. The actual-projection retirement proof belongs to Slice 3B-2. [Retirement limits](#native-retirement-limits).

## Slice 3B-2 — wild Gu dormancy

In Development Mode, use **Project GU → Make test Gu dormant** on a wild test Gu standing on a clear cell. The registry checks the exact live native owner, Gu ID, revision, and retirement eligibility. The operation stages one inert, unstackable Thing on the same map. The Gu ID, species, rank, provenance and revision carry through the transition. The record changes to dormant ground custody before explicit native Pawn discard.

The transaction guards reentry and the native game-save entry while staging. Failure before commit clears the provisional Thing and restores the original Pawn, using the existing Pawn placement recovery if its original cell is blocked. A discard failure after commit quarantines the dormant record and retains the former Pawn load ID in the reason. Reload binds only healthy projections and keeps quarantined records inspectable through **Log Gu individual records**.

| Check | Result |
|---|---|
| Placement and rollback | PASS. Occupied placement, carried inventory and a stale requested revision preserved the wild record. A callback blocked placement after despawn. An injected exception after the native Thing spawned removed that Thing. Both staging failures restored the same Pawn in its original cell. |
| Reentry and repeat actions | PASS. Native callback attempts to run the transition again or save during staging were rejected. A repeated action after success created one dormant projection in total and made one discard attempt. |
| Actual Gu retirement | PASS. The actual development Gu stopped its job and released its reservation. The fixture observed the committed ground custody before discard, one discard cleanup and one health cleanup, and zero kill calls, corpse creations or death tales. |
| Cleanup failure and fresh-process reload | PASS. An exception injected into native discard cleanup quarantined the already committed dormant record. Reload restored one healthy and one quarantined Thing with their exact identities, revisions and map owners. Neither former Pawn nor its corpse appeared in the native world. |
| Existing systems | PASS. The original colonist returned through the saved independent passage while both dormant Things stayed underground. The Slice 3B-1 regression and both historical saves passed, with matching source hashes for the 31×31 and 39×39 originals. |

`scripts/verify.ps1 -Focused` passed structural, assembly-linked compilation and managed checks on Windows RimWorld 1.7.5014 rev1195, Core, Harmony 3.0.4 and .NET SDK 10.0.401. The final `scripts/verify-priority0-live.ps1 -Profile Slice3B2` run passed new-game and fresh-process reload stages. Its evidence is under `tests/obj/Priority0-b946352731f74bf3804d941483d58a08/Slice3B2`. The Slice 3B-1 and historical evidence is under `tests/obj/Priority0-b2f5dee91397490bbb921f9c21adc9e0`. The placement and cleanup exceptions were deliberate fixture warnings.

The development path admits an empty, unowned test projection without Hediffs, relations, quest or ownership claims. Anomaly games reject the action because native Pawn destruction has a DLC death-notification path. Rendered interaction and additional DLC or third-party cleanup behavior remain acceptance checks.

## Slice 3B-3A — physical Gu inventory custody

In Development Mode, select a dormant test Gu, choose **Project GU → Put selected dormant Gu in inventory**, and click a free colonist on a cardinally adjacent cell. For the return move, select the colonist, choose **Project GU → Drop dormant Gu from selected colonist**, click a cardinally adjacent open cell, and choose the Gu by ID.

The canonical record keeps its identity, biological state, and dormant Thing load ID. Inventory custody adds the holder Pawn's load ID. Binding checks the live Thing, its ID and revision, the Pawn's own inventory tracker, and the exact native contents of that tracker. Each successful pickup or drop advances the revision once and mirrors it to the same Thing. A stale request stays invalid after a round trip. Rejection, successful rollback, and passage travel preserve the revision. Wild-to-dormant keeps the Slice 3B-2 revision behavior. Existing saves use the same schema version and omit the optional `holderPawnId` field for ground and wild custody.

Pickup checks native capacity and carrying mass. Drop checks placement and uses the native ThingOwner drop path. Both use the existing custody and save guard while moving the physical Thing. An interrupted move restores the same Thing to its original owner when possible. If that fails, bounded ground recovery preserves a detached Thing on the same map when there is room, and quarantine retains the original canonical claim. Reload quarantines missing or mismatched native bindings. Unsupported vanilla moves can leave a mismatched physical Thing in its observed holder and its record unavailable for further custody actions.

| Check | Result |
|---|---|
| `Slice3B3A` new game | PASS. Native stack capacity and carrying mass, blocked cells, a different holder, and stale or repeated actions all preserved custody on rejection. Pickup and drop advanced the revision once each. The original revision stayed stale after the round trip. |
| `Slice3B3A` native interruptions | PASS. Exceptions after native add and native drop restored the same Thing to its original owner and kept the revision. The in-flight guard blocked action and save reentry. When the original cell became blocked, recovery placed the original Thing on the same map and quarantined its claim. |
| `Slice3B3A` fresh-process reload | PASS. Five records and all four surviving Things were conserved. The healthy ground and inventory Things kept their exact native IDs and revisions. The missing projection and the native wrong-holder move both produced quarantine. The recovered Thing retained its existing quarantine. |
| `Slice3B3A` passage travel | PASS. The colonist carried the same native Thing into the underground map before saving and brought it back after reloading. The return drop bound the same Thing to the surface map and advanced the revision from four to five. |
| Native regressions | PASS. `Slice3B1` and `Slice3B2` preserved the earlier bindings, quarantines and retirement behavior. `CargoPrototype` delivered the original 37 stones through native hauling. `Awakening` preserved its conservation and reentry checks. |
| Historical 31×31 and 39×39 saves | PASS. Both copies loaded with their original cavern contents and an empty individual registry. The original save files kept their SHA-256 hashes. |

`scripts/verify.ps1 -Focused` passed structural, assembly-linked compilation and managed checks. The live build reported zero warnings and errors. The suite passed 12 native stages through `scripts/verify-priority0-live.ps1` on Windows RimWorld 1.7.5014 rev1195, Core, Harmony 3.0.4 and .NET SDK 10.0.401. Per-profile reports and deliberate callback fault logs are in `tests/obj/Priority0-2854ac115183482c833b7e8bef872f3c`. The final fixture limited the fault hook to the selected native inventory and chose a clear test cell on each generated map.

Rendered Development Mode interaction remains a manual acceptance check. This slice exercises inventory custody on the existing surface and spring maps. If an external change destroys the Thing or prevents native recovery placement, the registry retains the quarantined identity for diagnosis.

Slice 3B-3B can build a one-slot transfer between this verified native inventory binding and the exact aperture host Pawn and Hediff pair. Keep custody and revision commits in the same guarded transaction. Cover capacity, replacement or loss of the aperture, failed withdrawal, native passage travel and fresh-process reload before enabling the move.

## Slice 3B-3B — exact aperture custody

In Development Mode, use **Project GU → Store dormant Gu in aperture** on a colonist who has a dormant Gu in native inventory. Select the Gu by ID. **Withdraw dormant Gu from aperture** on the same colonist returns the occupant to native inventory. **Inspect aperture Gu custody** logs the current host and Hediff load IDs, the Gu identity and revision, and any quarantined claims for that host. **Log Gu individual records** includes claims whose host has died or disappeared.

Aperture custody saves the exact Pawn and Hediff load IDs in the canonical record. The one-slot index rebuilds from the registry after native load. The Hediff continues to save its existing cultivation fields. Native storage detaches the original Thing, commits record-only custody, and then retires the Thing. Withdrawal stages an inert projection in the same host's native inventory and commits after checking exact ownership and carrying capacity. It preserves the Gu identity, species, provenance and biological state while assigning the new Thing its native ID. All new custody commits, including wild-to-dormant, advance the Gu's binding revision once. Earlier saves retain their original revision.

Failed staging restores the original inventory Thing or clears the provisional withdrawal Thing. A failure after commitment quarantines the committed claim at its new revision. Native aperture removal and host death immediately quarantine the exact claim. Binding validation also catches destroyed, discarded or unresolved hosts. A quarantined claim retains its ID and revision and reserves only the originally claimed slot. A same-Def replacement has a different native Hediff ID and starts empty.

| Check | Result |
|---|---|
| `Slice3B3B` storage and withdrawal | PASS. The record committed before native destruction. The first native save held seven canonical records and only one physical Gu projection, which belonged to the colonist's inventory. Withdrawal created one unstackable native Thing with the stored Gu's identity and advanced the revision once. |
| Capacity, stale requests, and rollback | PASS. An occupied aperture, a full or overloaded inventory, stale revisions, and wrong host or Hediff inputs preserved the existing claims. Native exceptions after detach and add restored the original Thing or destroyed the staged Thing. A native callback could neither reenter the transaction nor save it in flight. |
| Lost endpoints | PASS. Host death, host destruction, native Hediff removal, aperture loss during withdrawal, and an unresolved saved Hediff each retained the original claim and revision in quarantine. A same-Def Hediff replacement inherited no occupant. |
| Passage and fresh-process reload | PASS. A colonist traveled with one stored and one packed Gu. Both bindings resolved at their original revisions after restart and returned through the native passage. The stored Gu was withdrawn and stored again under the same identity. |
| `d15d997` inventory save upgrade | PASS. A disposable copy loaded its original ground and inventory bindings and their old revisions. The original save's SHA-256 stayed the same. |
| Related native profiles | PASS. `Slice3B3A`, `Slice3B2`, `Slice3B1`, `Awakening`, and `CargoPrototype` all passed new-game and fresh-process reload stages. |
| Historical 31×31 and 39×39 saves | PASS. Both disposable copies loaded with their original cavern state and an empty individual registry. The original saves kept their SHA-256 hashes. |

`scripts/verify.ps1 -Focused` passed structural checks, game-linked compilation, and managed tests. The live fixture build reported zero warnings and errors on Windows RimWorld 1.7.5014 rev1195, Core, Harmony 3.0.4, and .NET SDK 10.0.401. All 14 stages from `scripts/verify-priority0-live.ps1` and the extra upgrade reload passed. Per-profile reports are under `tests/obj/Priority0-5e96ea0dfbe3434db217775d6e52f829`, including `Slice3B3AUpgrade` for the prior checkpoint save. The first native attempt stopped on the retirement eligibility of a generated test Pawn. The final fixture clears incidental Hediffs from its test Gu before exercising the unchanged native retirement checks.

Rendered Development Mode interaction and third-party lifecycle callbacks remain unverified. Healthy custody is ready for the Slice 3B-4 release path. The next fixture should stage an active Pawn from an authorized dormant binding and prove identity, revision, rollback, and conservation through placement failure and fresh-process reload. Explicit recovery of quarantined claims remains a separate operation to design.
