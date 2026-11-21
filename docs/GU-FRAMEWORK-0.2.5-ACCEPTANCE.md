# Project GU 0.2.5 framework technical acceptance

**Status** — Automated technical verification and owner-reported rendered acceptance passed for the controlled lifecycle and ordinary inventory. Schema V1 is provisional and scheduled for reconstruction under [RFC-0004](RFC-0004-GU-FRAMEWORK-V2.md). [Implementation journal](IMPLEMENTATION-0.2.5.md#owner-acceptance-and-inventory-follow-up)

## Automated lifecycle verification

`Slice3B5B` ran in four native processes. The first materialized one adult and moved that Gu from wild to dormant to inventory. The second restored the exact Thing, traveled through the independent passage, and stored the Gu in its host's aperture. The third restored record-only custody, rolled back an interrupted release, and then committed release. The fourth restored the exact released native Pawn. One Gu ID and its cohort provenance persisted throughout revisions one, two, three, four and five.

The disposable fixture kept Gu ID `5f058d42c7f640498678bee30791a339`. Its native binding was `Thing_GU_DevTestDormantGu23819` at inventory revision three, `Hediff_91` at aperture revision four, and `Thing_GU_DevTestWildGu33742` at released revision five.

The fixture exercised occupied landings and cells, wrong habitats, stale requests, native save and action reentry, a duplicate habitat ID, insufficient adults and duplicate event evidence. With reproduction disabled, the source finished at one juvenile plus two debited records, one of which was quarantined after a committed debit. The undebited quarantines reserved their event without counting as adults. Replay in inventory, aperture and released wild custody returned the same Gu. The released Gu alone protected the last surface access before and after the final reload.

## Compatibility and regression evidence

| Coverage | Result |
|---|---|
| Structural checks, game-linked builds and managed tests | PASS. The builds reported zero warnings and errors. Managed checks covered legacy cohort migration, native Scribe, production accounting, map-local habitat, underground addresses and route protection. |
| Earlier Gu identity and custody | PASS. `Slice3B1`, `Slice3B2`, `Slice3B3A`, `Slice3B3B`, `Slice3B4` and `Slice3B5A` passed 13 native stages, including the earlier release reload. |
| Unresolved records | PASS. `Slice3B1` quarantined ten invalid or ambiguous claims and retained them on resave. `Slice3B5A` and `Slice3B5B` covered ambiguous event keys and precommit and postcommit quarantine. |
| Awakening and spring ecology | PASS. `Awakening` and `Botanical` preserved Hope Gu consumption, native flora and spring production across reload. |
| Independent underground lifecycle | PASS. `Core` and `IdeologyOdyssey` exercised native map ownership, route protection, loss and recovery. `IdeologyReset` retained the sealed cavern and original 137-stone stack. |
| Cargo and underground hauling | PASS. `CargoPrototype` delivered the original 37 stones. `AutomaticHauling` conserved 66 stones through capacity, filters, interruption, missing routes and successive shipments after fresh-process reload. |
| Historical saves | PASS. The 31×31 and 39×39 cavern copies retained native owners, terrain, flora, reserve, Hope Gu and stones and loaded an empty individual registry. Both original saves kept their SHA-256 hashes. |

The final runs passed 40 native stages across integrated lifecycle, benchmark preparation and measurement, earlier Gu profiles, and world compatibility. Run `scripts/verify.ps1 -Focused` for structural, assembly-linked and managed checks. Run `scripts/verify-priority0-live.ps1 -Profile Slice3B5B` for the four-process lifecycle check. The results are under `tests/obj/Priority0-1e2445bfe9f64c5db10803d66bb13771` for the integrated lifecycle, `tests/obj/Priority0-5854d8aedc0545658191d43f9600faba` for earlier Gu profiles, and `tests/obj/Priority0-538037be328e46ca9626407f972ab924` for the world and compatibility profiles.

## Conservation and custody invariants

For a fixed habitat and species, the conserved total is the cohort's adults plus juveniles plus non-retired individuals with a committed debit from that cohort. The total changes only for separately authorized events such as a recorded birth, deliberate developer seeding, or a terminal consumption. Maturation changes the adult and juvenile counts while preserving their sum. Natural awakening is a terminal consumption of one adult Hope Gu and adds no individual. The standalone developer spawn has independent development provenance and sits outside the cohort total. [Materialization](../Source/ProjectGu/GuIndividualRegistry.Materialization.cs) · [Awakening service](../Source/ProjectGu/GuAwakeningService.cs)

One successful materialization exchanges an adult for one canonical identity with one persisted event key. The lifecycle starts at revision one. Each committed custody transfer advances the revision once and updates the authorized projection when one exists. Rejection, clean rollback, native travel and reload preserve the existing custody revision. During aperture custody the registry alone authorizes the occupant. The native Hediff supplies the endpoint, and the occupant has no Pawn or Thing projection. [Identity and custody RFC](RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md)

A quarantine with a committed debit remains a counted individual when its event evidence is unambiguous. An undebited quarantine blocks the event and leaves the unit in the source cohort. Duplicate saved event claims block all claimants and preserve the raw evidence. If a corrupt save contains conflicting committed claims, the recorded flags do not establish how many debits occurred. That case requires reconciliation against external evidence before any repair. Reload, quarantine and replay never grant a replacement allocation. [Registry event validation](../Source/ProjectGu/GuIndividualRegistry.Materialization.cs)

## Architectural audit

| Area | Finding and limit |
|---|---|
| Canonical ownership | The registry deep-saves one versioned record per Gu. Lookup indexes derive from those records. Native Pawns and Things carry the Gu ID and binding revision. Exact inventory and aperture owners are validated against the engine graph. |
| Binding revision | Projection changes advance the binding revision. Cohort requests use the separate cohort revision. A replay uses its original creation event and provenance even after custody changes. The save guard covers the synchronous staging window. |
| Save and load | Reconciliation runs after native map and holder loading. Missing Defs, missing or mismatched endpoints, conflicting projections and duplicate events retain their raw keys in quarantine. The current rebuild enumerates the native Pawn graph and looks up configured dormant projection Defs on loaded maps. Measure broader species catalogs and larger worlds before changing that strategy. |
| Recovery | Native host death and Hediff removal invalidate aperture custody. A periodic pass checks bound apertures for silent loss. Quarantined claims are inspectable. Operator-assisted recovery, terminal outcomes and reconciliation of corrupt debits need a separate policy. |
| Species extension | The cohort request and ledger are species-neutral. The only exposed materialization action selects the developer species. A new species still needs a projection Def and a verified native lifecycle contract. The canonical biology currently records condition, and release creates a fresh adult Pawn from the species Def. |
| Runtime cost | Record-only occupants use registry state and an amortized endpoint validation pass. Active projections use ordinary Pawn simulation. A benchmark with contained occupants cannot establish a budget for 120 active projections. |

## Performance methodology

The `Slice3B5BPerf` profile saves one Core colony twice at the same game tick. Both variants contain 120 native free colonists with 120 exact aperture Hediffs and identical host and Hediff IDs. The populated variant adds 120 synthetic development-origin records, one per distinct aperture. The fixture uses synthetic records to isolate registry costs from cohort actions and projection creation. The normal production load validator must authorize every record and the one-slot constraint holds throughout. Native census and serialized tag checks verify zero Gu Pawn or Thing projections.

Each snapshot is loaded in three fresh headless processes in the order empty, populated, populated, empty, empty, populated. Save timings are medians of three native saves after one warm-up per process. The load probe times native Scribe initialization and `Game.LoadGame` separately. These scopes exclude executable and mod startup. Filesystem caches are warm across runs. The allocation counters measure managed bytes on the executing thread.

Registry rebuild is measured after warm-up in five batches of 32 calls. Ordinary registry ticks use five batches of 20,000 calls at a non-poll tick. Validation uses five batches of 100 calls with the clock fixed at a due tick. Both variants also run 64 warm-up native simulation ticks and time five batches of 50 `DoSingleTick` calls. The synthetic tick experiment measures CPU work from the headless path. Renderer work, play-speed scheduling, unmanaged memory and long-session retained memory remain outside these measurements.

## Performance results

Windows build 26200 on an AMD Ryzen 9 9955HX with 32 logical processors. RimWorld 1.7.5014 rev1195, Core, Harmony 3.0.4, and .NET SDK 10.0.401. Values are medians of the three process results per variant. Brackets show the minimum and maximum process results where variability matters.

| Measurement | 0 Gu records | 120 Gu records |
|---|---:|---:|
| Native save size | 20,309,044 B | 20,368,943 B |
| Native save wall time | 1,021 ms [804–1,139] | 1,205 ms [982–1,336] |
| Scribe initialization wall time | 1,603 ms [1,590–1,781] | 1,631 ms [1,522–1,666] |
| `Game.LoadGame` wall time | 8,098 ms [7,875–8,154] | 8,371 ms [7,970–8,386] |
| `Game.LoadGame` managed allocation | 436,529,472 B | 436,811,592 B |
| Warm registry rebuild wall time | 0.091 µs | 1,374 µs |
| Warm registry rebuild managed allocation | 0 B | 106,936 B |
| Ordinary registry tick | 0.037 µs | 0.062 µs |
| Due validation pass wall time | 0.087 µs | 78.61 µs [78.20–93.32] |
| Due validation pass managed allocation | 0 B | 29,792 B |
| Whole native simulation tick | 2.720 ms [2.702–2.908] | 2.904 ms [2.560–3.778] |
| Physical Gu projections | 0 | 0 |

The native save pair differs only at the registry list. The populated save adds 59,899 bytes, about 499 bytes per record and 0.295% of this save. Amortizing the registry fast path and validation pass over 250 ticks gives an estimated additional 0.34 µs and 119 managed bytes per game tick. This is an estimate from the isolated registry measurements. The overlapping save, load and whole-tick ranges leave the end-to-end timing differences inconclusive at this sample size.

Run `scripts/verify-priority0-live.ps1 -Profile Slice3B5BPerf` to reproduce the matched benchmark. The six raw JSON samples and the pass report are under `tests/obj/Priority0-fc03d1596a02449e9ded4f26e98d48b9/Slice3B5BPerf`. An earlier harness run was discarded when its loaded starting state failed the exact-match assertion. The measured run pauses on native load completion, before the fixture checks the starting tick and host bindings.

## Owner acceptance and next milestone

- **Owner-reported rendered acceptance.** The owner completed Cohort → Wild → Dormant → Inventory → Aperture → Released Wild and reloaded successfully while the Gu was in aperture custody. The final inspection showed an active Rank 1 developer Gu at revision 5 with cohort provenance and its committed adult debit. The owner reported no red errors during that run. An earlier vanilla Gear drop exposed a separate custody mismatch. [Owner acceptance and inventory follow-up](IMPLEMENTATION-0.2.5.md#owner-acceptance-and-inventory-follow-up)
- **DLC and third-party scope.** The individual lifecycle and performance runs use Core and Harmony. Underground verification additionally covers Core with Ideology and Core with Ideology plus Odyssey. Anomaly remains gated for native cleanup transitions. Broader DLC combinations, including Royalty and Biotech, and third-party lifecycle patches need their own validation.
- **Future framework work.** Define explicit terminal lifecycle and recovery rules, then validate cohort and individual conservation for those outcomes. Larger aperture capacity, feeding, refinement, abilities and the cultivation interface remain separately scoped.
- **Ordinary inventory acceptance.** The owner reported 10/10 for the rendered ordinary pickup and Gear controls. [Release readiness](RELEASE-0.2.5.md#ordinary-inventory-acceptance)
- **Schema status.** Schema V1 is a provisional development foundation scheduled for reconstruction. `V2EngineProbe` verified that ordinary native events quarantine V1 records. [Engine investigation](ENGINE-1.7-GU-V2-INVESTIGATION.md) · [RFC-0004](RFC-0004-GU-FRAMEWORK-V2.md)
- **Merge decision.** Owner merge authorization is pending. [Release readiness and merge strategy](RELEASE-0.2.5.md)
