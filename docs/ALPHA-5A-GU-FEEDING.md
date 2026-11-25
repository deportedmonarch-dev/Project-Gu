# Alpha 5A — Gu Feeding and Sustenance

Alpha 5A makes owning a Gu an ongoing obligation. Each individual Gu with a feeding policy keeps its own nourishment, gets hungry over game time, and must be fed physical RimWorld items by a pawn who carries the food to it. A neglected Gu starves, withers until it cannot act for its master, and eventually dies. Moonlight Gu now depend on moon orchids, and Liquor Worms on alcohol.

Accepted base: `95725cbb0d5b454c589effbd6b05bf05c4bf5692` (Alpha 4A with the optimized aperture renderer). Branch: `feature/alpha5a-gu-feeding`. Society Core is untouched.

## Play it

1. **See hunger.**
   - **Where:** select a Gu Master and open the **Aperture** tab's **Gu** page.
   - **Each card:** a thin nourishment meter and a level: *Sated*, *Hungry*, *Starving* or *Withering*.
   - **The selected Gu's Nourishment panel:**
     - the level and how many days of nourishment it has stored, with a meter marked at the hunger threshold;
     - how long until it gets hungry, runs out of food, withers or dies;
     - any starvation effect;
     - its accepted food, with the meal size, how long one meal lasts and how much is at hand;
     - a **Feed** button.
   - **Elsewhere:** a dormant Gu's inspection text shows the same level.
2. **Get food.**
   - **Liquor Worm:** drinks beer. One bottle sustains it about two days. The Southern Border scenario starts with four bottles, and beer can be brewed or traded.
   - **Moonlight Gu:** eats moon orchid petals, two per meal, a meal every half day. Designate a mature moon orchid for **Harvest**. The wild orchids in the spring cavern work, and so do orchids sown in a growing zone. The plant gives petals and survives, then needs to regrow before it flowers again. Petals are ordinary plant matter: they can be hauled, stockpiled and carried up through the passage.
3. **Feed.**
   - **Order:** press **Feed** on the Gu page, choose **Feed …** in **Manage aperture Gu**, or right-click a dormant Gu on the ground with any colonist.
   - **What happens:** the colonist fetches the food from their inventory or the map, carries it to the Gu and offers it for a few seconds.
   - **When food is used:** only when the meal is actually given.
   - **Interruption:** drafting, another order or a lost route keeps every item. The colonist drops what they carried.
4. **Let the Gu Master tend their own Gu.**
   - **The policy:** **Feed own Gu automatically** sits at the top of the Gu page. It is on by default and set per cultivator.
   - **What it does:** the Gu Master feeds their own hungry, refined Gu from their inventory or the stockpiles of the map they are on.
   - **When:** only while choosing a new job. It never interrupts a job, a cultivation session, combat, a drafted pawn or a lord duty, and it does nothing with enemies within 25 cells.
5. **Neglect has consequences, slowly.**

   | Level | When | What it means |
   | --- | --- | --- |
   | Hungry | stored nourishment falls to the hunger threshold | a caution message |
   | Starving | nourishment runs out | it still serves |
   | Withering | after 2 days of starvation | its techniques are refused, for example Moonblade or Liquor Worm refinement |
   | Dead | after 10 days of starvation | — |

   - **Recovery:** feeding at any point before death ends the starvation at once.
   - **No sudden death:** missing one or several meals never kills a Gu.

## Species policies

| Species | Policy | Food | Meal | Lasts | Storage | Hungry at | Withers after | Dies after |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Liquor Worm | Simulated | Beer | 1 bottle | 2 days | 4 days | 1 day left | 2 days starving | 10 days starving |
| Moonlight Gu | Simulated | Moon orchid petals | 2 petals | 0.5 day | 2 days | 0.5 day left | 2 days starving | 10 days starving |
| Hope Gu | Not implemented | — | — | — | — | — | — | — |
| Test spring Gu (developer) | Exempt | — | — | — | — | — | — | — |

- **Hope Gu:** reports *No individual feeding policy*. Nothing is simulated or invented. Its awakening use and spring population are unchanged.
- **The developer species:** declares its exemption in XML.

### Lore and balance, kept apart

| | Canonical lore (Reverend Insanity) | Balance choice in this release |
| --- | --- | --- |
| Liquor Worm | Drinks wine; a jar of green bamboo wine sustains it about four days. | Beer is accepted as a lower-quality substitute at two days per bottle. Future wines are new entries in the same list with their own duration. |
| Moonlight Gu | Two moon orchid petals in the morning and two in the evening: four a day. | One meal is two petals and lasts half a day. That is exactly the lore rate. |
| Storage, hunger threshold, withering and death times | Not specified. | 4 and 2 days of storage. Withering after 2 days and death after 10 days of starvation, chosen to be forgiving for a first Gu collection. |
| Initial nourishment | Not specified. | A Gu newly taken into care starts with 2 days (Liquor Worm) or 1.5 days (Moonlight Gu). |
| Older saves | — | Existing Gu start full and do not get hungry for 3 days (grace period). |
| Moon orchid harvest | Not specified. | 6 petals at full growth (fewer if harvested early or the plant is damaged). Harvest leaves growth at 0.7, and the plant must regrow past 0.85 before the next harvest. That takes about 1–3 in-game days in the cavern. 0.7 keeps a harvested wild orchid above the 0.65 growth that lights the cavern. |

**Sustainability estimate.** RimWorld plants grow during 55% of the day. Each orchid therefore gives about 2.2 petals a day at full growth.

- **The cavern's 5 wild orchids:** about 11 petals a day, against the 4 a day one Moonlight Gu needs. Allowing a 25% margin for failed harvests and slower growth, that is about 2 Moonlight Gu.
- **Measured in game:** an orchid harvested at growth 0.7 regrew to harvestable in the time reported in Verification.
- **Grown orchids:** sown moon orchids on qi-rich or ordinary fertile soil add more.

## Architecture

| Piece | Where | Role |
| --- | --- | --- |
| Feeding specification | [`GuFeedingProperties`](../Source/ProjectGu/Domain/GuFeeding.cs), the `feeding` block of [`GuSpeciesDef`](../Source/ProjectGu/Domain/GuSpeciesDef.cs) | Status (NotImplemented, Exempt, Simulated), accepted foods in order of preference with meal size and duration, storage, thresholds, grace, withering and death times, and the feeding worker class. A missing block means *not implemented*, never a default diet. Validated by `ConfigErrors`: death sooner than 3 days of starvation, withering after death, no food, or an undeclared exemption are rejected. |
| Extension boundary | [`GuFeedingWorker`](../Source/ProjectGu/Domain/GuFeeding.cs) | Plans a meal and counts available food. The default [`GuFeedingWorker_Items`](../Source/ProjectGu/Domain/GuFeedingWorker_Items.cs) uses physical items from the feeder's inventory or unforbidden, reservable, reachable stacks on the feeder's own map. A future Gu that eats emotions, souls, qi-rich soil or lifespan supplies another worker. The scheduler and rules contain no species or food names (managed check). |
| Individual state | [`GuNourishment`](../Source/ProjectGu/Domain/GuFeeding.cs), a `GuStateComponent` on the canonical `GuRecord` | Stored nourishment and starvation in ticks, the tick it was last settled, the grace end, the observed mode, and the meal history: last fed, last food, meals, items, feeding revision. It lives on the record, not on a projection, so it follows the individual through every manifestation, container, holder and map. |
| Rules | [`GuNourishmentRules`](../Source/ProjectGu/Domain/GuFeeding.cs) | Pure arithmetic: create, project (read-only), settle, level, terminal test, meals wanted and feed. Any elapsed interval settles in one subtraction. |
| Authority | [`GuIndividualRegistry.Sustenance`](../Source/ProjectGu/GuIndividualRegistry.Sustenance.cs) | Observes the sustenance mode from placement and the native graph. Decides who may feed a Gu: its own cultivator for an aperture Gu, the colonist holding a dormant Gu, or any colonist who can reach a dormant Gu on the ground. Reports starvation impairment, and ends a starved individual through the terminal transaction. |
| Scheduler | [`GuSustenanceComponent`](../Source/ProjectGu/Sustenance/GuSustenanceComponent.cs) | Every 250 ticks, settles a bounded slice of records, so each individual is visited about once per 2,500 ticks (one in-game hour). The cap is 400 visits per call. It sends hunger warnings and the starvation letter. It also keeps feeding intents for planned jobs and the per-cultivator auto-feed policy. |
| Service | [`GuSustenance`](../Source/ProjectGu/Sustenance/GuSustenance.cs) | Snapshot (read-only), settle, plan and order a feeding job, and the single commitment step. |
| Job | [`JobDriver_GuFeedGu`](../Source/ProjectGu/Jobs/JobDriver_GuFeedGu.cs), `GU_FeedGu` | Fetch, carry, offer for 240 ticks, commit. Native reservations on the food stack (partial counts, so several feeders can share a stack) and on a ground Gu. The toil list never depends on runtime state, so a saved job resumes at the right toil. |
| Routine feeding | [`JobGiver_GuTendOwnGu`](../Source/ProjectGu/Sustenance/JobGiver_GuTendOwnGu.cs), [`GU_TendOwnGu`](../Defs/ThinkTreeDefs/GU_GuTending.xml) | Inserted at `Humanlike_PreMain`. Throttled to one look per 1,250 ticks per pawn. Only the pawn's own refined Gu in their aperture or inventory. |
| Ability boundary | [`GuIndividualRegistry.Abilities`](../Source/ProjectGu/GuIndividualRegistry.Abilities.cs) | `IsAbilityGrantedBy` refuses a withering Gu. Every cast, channel start and channel tick goes through it, so the refusal is authoritative, not just a disabled button. The command stays visible and shows the reason. |
| Interface | [Gu page](../Source/ProjectGu/Interface/ITab_Pawn_Aperture.Gu.cs), [actions](../Source/ProjectGu/Interface/GuApertureActions.cs), [view](../Source/ProjectGu/Interface/ApertureView.cs) | Nourishment snapshot, foods with counts at hand, and the Feed action per entry. The tab, the **Manage aperture Gu** fallback and the dormant Gu's float menu all build feeding from the same `GuApertureActions.Feed`. |
| Petals and harvest | [`GU_MoonOrchidPetals`](../Defs/ThingDefs/GU_MoonOrchidPetals.xml), [`GU_MoonOrchid`](../Defs/ThingDefs/GU_BotanicalPlants.xml) | Native non-destructive harvest (`harvestAfterGrowth` below `harvestMinGrowth`), harvest tag `Standard`, so the vanilla Harvest designator and grower work apply. |

### Feeding transaction

| Stage | What happens |
| --- | --- |
| Order | `TryMakeFeedJob` checks feeding authority, then asks the worker to plan a meal: the item, the food option, the meals and the count. It registers an intent under the job's ID, holding the individual, its custody revision, its feeding revision, the food and the meals. Nothing is reserved or consumed. |
| Start | The driver takes the intent and saves it with the job. It reserves part of the food stack (the job's count) and, for a ground Gu, the Gu itself. |
| Every tick | The job fails if this exact individual can no longer be fed by this pawn: unresolved, custody revision changed, or out of reach. |
| Commit | `TryCommitFeeding` runs in one tick:<br>1. Revalidate the individual and its custody revision, the feeder's authority, the food in hand and the remaining appetite. If the Gu was fed meanwhile, the meals are recomputed; a sated Gu refuses.<br>2. Settle hunger to now.<br>3. Consume exactly the meals' items.<br>4. Apply the meal: storage is capped and starvation ends. |
| Failure | Any failed check consumes nothing. An interrupted job drops the carried food natively. |

A meal is accepted while at least half of it fits in storage. A Gu that is almost but not quite empty enough is never refused, and the excess is lost.

### Scheduling, save and load, and off-map policy

Hunger is stored as absolute game ticks and settled lazily.

- **A visit:** projects `stored − (now − settled)`. Any overflow goes into starvation. The settled tick moves to now.
- **Save and load:** reloading never repeats a deduction, and there are no catch-up loops.
- **No interface needed:** the interface reads a projection and never writes.

**Modes.** Hunger advances only in the player's care, on a loaded map:

| Placement | Hunger |
| --- | --- |
| In a player cultivator's live aperture on a map | Advances |
| Dormant, carried by a player pawn or lying on any loaded map | Advances |
| Wild and active | **Paused.** Its habitat sustains it. Wild Gu carry no state at all until first taken into care, so thousands of wild Gu cost nothing to save. |
| Off map (caravan, world pawn, abstract world location) | **Paused.** Nothing is eaten, nothing is restored. |
| Held by another faction's pawn | **Paused.** No non-player Gu economy exists yet. |
| In a dead host's aperture | **Paused** until extracted or resurrected. |
| Unresolved or quarantined | **Paused.** |

An interval counts as hunger only when the record was observed advancing at both ends. A change of mode is therefore attributed at the next visit, within one in-game hour, and always in the player's favour. No food is ever created to feed a paused or off-map Gu.

**Custody changes.** Storing, withdrawing, carrying, dropping, picking up, passing to another colonist or cultivator, travelling between the cavern and the surface, capture and release never touch nourishment or history. Only a meal adds nourishment and resets starvation. A Gu released to the wild keeps its numbers frozen, and resumes them if captured again.

**Starvation death** goes through `TryEndLifeByStarvation`:
- **Event key:** one idempotent key per individual, `starvation:<id>`.
- **Outcome:** `Died`, with a history entry.
- **Native body:** the record is unbound before its dormant body is retired, so the retirement is never mistaken for physical destruction.
- **Aperture place:** freed, because the occupant is no longer living.
- **Tombstone:** identity, provenance and accounting stay on the record. Nothing is removed.
- **Repeats:** a repeated call, or a reload after the death, never produces a second event.

**Older saves.**
- **Activation:** a save without the feeding component activates feeding on load.
- **Existing Gu:** every Gu created before that tick starts full, with the 3-day grace period. Ended records get no state.
- **Items:** none are created or removed.

**Liquor Worm luring** is unchanged and separate.
- **The beer:** the lure's beer is bait. It is consumed by the lure as before, and feeds neither the lured Gu nor any stored Liquor Worm.
- **The lured Gu:** a newly captured Gu starts at the species' initial nourishment.

### Southern Border start

A fresh colony can sustain its first Gu:
- **Beer:** the scenario's four bottles (already there for luring) feed a Liquor Worm for about a week after the lure. Brewing or trade keeps it going.
- **Petals:** the cavern's wild orchids support a Moonlight Gu once harvested. A newly captured Gu has 1.5–2 days stored, and death is 10 days of starvation further away.

## Art specifications

| Asset | Path | Size | Notes |
| --- | --- | --- | --- |
| Moon orchid petals (item) | `Textures/ProjectGU/Items/MoonOrchidPetals.png` | 64 × 64, transparent | A loose heap of pale blue-violet petals, darker at the base, with a faint inner glow and a few luminous motes. Reads at item scale on dark cavern soil. `Graphic_Single`; a hand-drawn replacement of the same size needs no code or XML change. Generated placeholder: [`scripts/generate-feeding-placeholder-art.ps1`](../scripts/generate-feeding-placeholder-art.ps1). |

No other new art. The Gu page uses each food's own item icon (vanilla beer, the petals above). The orb artwork, its surface pattern and the optimized renderer (`95725cb`) are unchanged.

## Verification

Environment: Windows 11, .NET SDK 10, RimWorld 1.7.5014 rev1195 (Waylith), Core and Harmony.

```powershell
.\scripts\verify.ps1 -Focused
.\scripts\verify-priority0-live.ps1 -Profile Alpha5A -Parallel 1 -StageTimeoutSeconds 1500
.\scripts\verify-priority0-live.ps1 -Profile Alpha5AVisual -Parallel 1 -StageTimeoutSeconds 1500   # opens a game window
```

| Kind | Result |
| --- | --- |
| Real compilation | Release build against the installed 1.7 assemblies: 0 errors. These engine behaviours were checked against decompiled 1.7 code before use: non-destructive harvest (`harvestAfterGrowth`), plant resting, `Designator_PlantsHarvest`, `Toils_Haul.StartCarryThing`, carried items dropped when a job ends, `TryTakeOrderedJob` reservations, the `Humanlike_PreMain` think-tree hook, `ForbidUtility` and `Autosaver`. |
| Managed (real assemblies, no game process) | `GuFeedingChecks`, 46 assertions: species policies as declared, orchid harvest bounds and the sustainability estimate, settling, suspension, grace, levels, thresholds, meals, overflow, configuration validation, the absence of species names in the scheduler, and source guards on the commitment order and the ability boundary. Settling 10,000 individuals takes 0.03–0.05 ms. The whole `verify.ps1 -Focused` suite passes. |
| Native headless `Alpha5A` | Campaign `00919f31a3d845d4b6c236870793bf24` passed `new`, `feeding`, `reload`, `final` and `legacy`. The same stages passed again inside the rendered campaign on the final build. |
| Native rendered `Alpha5AVisual` | Campaign `5e684dda6d324147afb1cfd16ef77208` passed all six stages on the final build. The screenshots were inspected (below). |
| Native regression | Campaign `548c261f3bbe48b38374463d673ed52b` (`-Parallel 3`) passed all 12 units: `Alpha1`, `Alpha2`, `Alpha3`, `Alpha4A`, `RankOne`, `GuGeneration`, `Slice3B3B` (identity and custody), `Slice3B5A` (cohorts), `ClanStart`, `Botanical` (cavern flora with the changed orchid), `Phase2Lifecycle` (terminal outcomes) and `DormantHauling`. Rendered `Alpha4AVisual` passed as campaign `f1e6ace93a88421ba338c4cb9dc74d90`. |

### What the native fixture demonstrates

The fixture builds on the Alpha 3 save, with Development Mode off. Every player action goes through the Aperture tab, the fallback menu or a native float menu.

| # | Requirement | Evidence (stage) |
| --- | --- | --- |
| 1 | Feed Liquor Worm with beer | From the **Manage aperture Gu** fallback: exactly one beer per meal consumed, nourishment arithmetic exact (feeding). |
| 2 | Feed Moonlight Gu with petals | From the Gu page: exactly two petals per meal consumed, starvation ended, nourishment exact (feeding). |
| 3, 4 | Harvest real orchids; bounded regrowth | 3 wild cavern orchids harvested through the native designation and job. Runs yielded 6–18 petals in total, because harvests can fail at low Plants skill. Each plant survived at growth 0.70, the orchid count did not fall, and a second designation was refused. The orchid regrew to harvestable in 1.1–1.3 days at 126–140% growth rate (feeding). |
| 5 | Exact item consumption | Totals counted across ground, inventory and hands before and after every meal (feeding, reload). |
| 6 | Interruption keeps items | Drafting a disciple carrying petals: every petal kept, nothing fed (feeding). |
| 7, 8 | Decline, hungry, starving | Over about 1.5 real in-game days with auto-feeding off, nourishment fell by exactly the elapsed ticks. The Gu went Sated → Hungry → Starving, the starvation counter matched exactly, and the player was warned (feeding). |
| 9 | Starvation at the ability boundary | A withering Moonlight Gu: `CanCast` and `TryOrderCast` refused and `IsAbilityGrantedBy` was false, while the command stayed visible but disabled with the reason. A withering Liquor Worm: the channel was refused and the Cultivation page explained why. Allegiance and custody were unchanged, and feeding restored both. A merely starving Gu still acts (feeding). |
| 10 | Terminal starvation | A carried dormant Gu and a stored Gu each died once. Event key `starvation:<id>`, outcome Died, one Terminal history entry. The dormant body was retired, the aperture place freed, the record count unchanged, one letter each. A repeated call was refused, later sweeps added nothing, and a fresh-process reload showed no replay (reload, final). |
| 11 | Store and withdraw | Same state object, identical nourishment and history at the same tick; hunger continued while carried (feeding). |
| 12 | Custody transfer | Withdrawn, carried to the surface, dropped, picked up by another cultivator (the elder) and fed by them, handed back, carried down and stored. History unchanged by every transfer; only the elder's meal was recorded (feeding). |
| 13 | Save and load while hungry, fed, feeding and starving | Saved with one Gu sated, one hungry, one starving, and a feeding job part way through its offer. Values identical after reload, and the job resumed and committed once (reload). |
| 14 | Underground and surface | Map changes kept nourishment exactly. On the surface, only surface and carried beer counted as available (feeding). |
| 15 | Spring population accounting | The lure debited exactly one adult from the cohort; feeding changed no cohort (feeding). `Botanical`, `Slice3B5A` and `Alpha1` regressions passed. |
| 16 | Luring is independent | The lure consumed one beer and fed neither the lured Gu nor the stored Liquor Worm. The captured Gu started at the species' initial nourishment (feeding). |
| 17 | UI accuracy | The view's snapshot equals the canonical record. Foods, meal sizes and counts at hand are shown, and disabled reasons are specific (feeding, visual). |
| 18 | No invented diets | A Hope Gu carries no state, level, feeding action or inspection line, in wild form and, when it could be captured, dormant (feeding). |
| 19 | Missing, forbidden, unreachable food | With every petal forbidden: refused. Petals sealed behind walls: counted but refused at ordering, with nothing consumed. No petals at all: refused with a reason (feeding). |
| 20 | Performance | Live: one individual visit costs 0.9–2.2 µs, a sweep about 20 µs. That puts 10,000 simulated Gu at about 9–22 ms per in-game hour, spread over ten sweeps. Managed: 10,000 settles take under 0.1 ms (final, managed). |
| — | Routine feeding | An ordered job was not interrupted and a drafted Gu Master did not feed. Afterwards the Gu Master fed the hungry Liquor Worm on their own (feeding). |
| — | Older saves | The final save with every Alpha 5A element removed, as a build without the feeding code writes it, loaded with feeding activated. The living Gu started full with a 3-day grace period, ended records got no state, item totals were unchanged, and there was no hunger after half a day (legacy). |

### Rendered inspection

Screenshots in `tests/obj/Priority0-5e684dda6d324147afb1cfd16ef77208/Alpha5AVisual/alpha5a-screenshots/`:

| Shots | What they show |
| --- | --- |
| 00 | Feeding in the world, with the pawn carrying petals. |
| 10–13 | The Gu page: cards with meters for a sated, a hungry and a starving Gu, and a Hope Gu marked *No feeding policy*. The Moonlight Gu panel during feeding and after the meal. |
| 20, 32 | The Techniques sustenance line. |
| 30, 31 | A withering Liquor Worm on the Cultivation and Gu pages. |
| 40, 41 | UI scales 1.5 and 2. |
| 50, 51 | A regrown orchid and harvested petals. |
| 60 | A dormant Gu's **Feed** and **Pick up** float menu. |

**Defects found during inspection and fixed:**
- The Grants line said "(available)" for a withering Gu.
- The Nourishment panel sat below the description, so the Feed button needed scrolling. It now comes first.

### Defects found by the native tests and fixed

- **Ordered feeding never started.** Vanilla `TryTakeOrderedJob` calls `TryMakePreToilReservations` on a job-cached driver before the job starts, and the driver took its intent there. The driver now only reads the intent at reservation and takes it in `Notify_Starting`.
- **Silent Feed failures.** A Feed order whose job could not start was reported as success. It now reports the failure.
- **Over-reservation.** Meal planning now reduces the count to what the feeder can actually reserve.

### Fixture-only adjustments, not gameplay changes

- **Autosave off.** The fixture ticks several in-game days inside one frame, which queues the native autosave long event, and that event correctly blocks custody operations until the frame ends.
- **Colony upkeep.** Mood breaks are disabled, and the colonists' food, rest and mood are topped up, so days of untended cavern life don't down or break the pawns under test.
- **Faster starvation.** Withering and death are reached by draining the hunger clock directly. Hungry and starving were reached by real ticking.
- **Retries.** Captures and passage travel retry when a wild Gu wanders or the landing cell is momentarily occupied.
- **Alpha 4A visual budget.** The pre-existing `Alpha4AVisual` will-enforcement budget was raised from 10,800 to 21,600 ticks: a disciple with aptitude 20 needs 12,000.

### Observations outside Alpha 5A

- **Hope Gu capture in the planted basin.** Capture needs an open cell under the wild Gu (an existing rule). In the densely planted spirit-grass basin a wild Hope Gu can stay uncapturable for a long time, with the order ending without explanation in the world. This is unchanged by Alpha 5A.
- **Renderer benchmark flake.** One managed run failed the Alpha 4A renderer micro-benchmark ("incremental pass cheaper than full pass") under machine load, and passed on rerun. The renderer is unchanged on this branch.

## Remaining limitations

- **Mode changes take effect at the next sweep.** A Gu that leaves or returns to a map changes mode at the next visit, within one in-game hour, always in the player's favour.
- **Off-map Gu don't get hungry.** Caravans, world pawns, other factions' holders and dead hosts are paused. There is no caravan feeding or non-player Gu economy yet.
- **Wild Gu are habitat-sustained.** A released Gu keeps its numbers frozen until it is captured again.
- **Auto-feeding is narrow.** It covers only a Gu Master's own refined Gu in their aperture or inventory. Unrefined or ground Gu are fed by order, and there is no Gu Keeper work type (as specified).
- **One stack per feeding job.** A meal larger than the nearest stack is fed in several jobs.
- **Petals don't rot.** Old caverns that used the pre-botanical moon orchid colony building still have habitat-only orchids; players can sow moon orchids instead.
- **Balance values are provisional** and need owner review.
