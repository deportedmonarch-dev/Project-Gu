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

A developer-only `GU_DevTestSpringGu` can be seeded once through a new spring dev gizmo to verify two species persist independently. It is not a spawnable Gu, does not auto-reproduce and cannot be used for awakening. Wild Gu are now allowed to omit habitat tags because habitats are preferential rather than universally mandatory.

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
