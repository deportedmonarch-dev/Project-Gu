# Alpha 5B — Gameplay integration and usability

Alpha 5B fixes the problems the owner's first real session with Alpha 5A exposed:
- **Moon orchids** now behave like ordinary crops.
- **Petals** travel up the passage with the other cargo.
- **Automatic Gu feeding** is background colony provisioning instead of jobs that interrupt Gu Masters.
- **Hope Gu** drift around their springs as small luminous spirits.
- **Encounters and capture** explain themselves.
- **Developers** can spawn a registered Gu safely.

It also adds the [Project GU plant framework](PLANT-FRAMEWORK.md), which brings seed-gated moon orchid cultivation.

Accepted base: `d82c723769ab19dd9078ba3dfefd0a20ee8d10ce`. Branch: `feature/alpha5b-playtest-corrections`. Not merged. Society Core is untouched.

## Play it

1. **Orchids are crops.**
   - **Select:** double-click a moon orchid to select every orchid on screen. Spirit grass and nearby pawns are never selected.
   - **Harvest:** with a colonist selected, right-click a flowering orchid and choose **Harvest moon orchid (moon orchid petals)**.
   - **When unavailable:** the menu still shows the option and says why: regrowing (with growth), not mature, reserved by someone, forbidden, no path, incapable or unwilling.
   - **Still native:** the Harvest designation, growing zones and grower work work as before.
2. **Seeds.**
   - **Gather:** right-click a flowering orchid and choose **Gather moon orchid seeds**. The plant's **Gather seeds** command and the Orders menu's **Gather seeds** designator do the same.
   - **Yield:** a grower collects about two seeds without uprooting the plant.
   - **Cost to the plant:** gathering spends the flowering exactly as a petal harvest does. Growth goes back to 0.7, and the plant must regrow past 0.85 before it gives petals or seeds again.
3. **Sow orchids.**
   - **How:** make a growing zone and set it to moon orchid. Growers sow it as usual, but each orchid uses one seed, carried to the cell and spent when the sowing completes.
   - **No seeds:** the grower refuses, and the zone's right-click menu says *no moon orchid seeds available*.
   - **Wild orchids:** they keep growing and harvesting as before.
4. **Haul petals up.**
   - **Assign:** use **Assign to underground hauling** on the passage.
   - **What moves:** the hauler carries primeval stone, moon orchid petals and moon orchid seeds to surface storage.
   - **Fair turns:** each trip gives the next kind of cargo first claim, so plentiful stone can never keep petals waiting.
5. **Feed own Gu automatically.**
   - **Switch:** the per-cultivator switch on the Aperture tab's Gu page now provisions in the background.
   - **What happens:** a Gu Master's own hungry, refined Gu eat real food without any job, movement or interruption. Food comes first from what the Gu Master carries, then from the colony's stockpiles on the same map.
   - **On the Gu page:** each Gu shows when it last ate and from where. If automatic feeding found nothing, it says what was missing.
   - **By hand:** **Feed** still sends a colonist to feed the Gu.
6. **Spirits at the spring.**
   - **What you see:** soft, glowing Hope Gu drift and pulse around the spring. One spirit is drawn per living Hope Gu in the spring's population, at most 14.
   - **What they are not:** scenery only. They cannot be selected or captured, and they change no count.
   - **Turn off:** Mod settings, **Ambient spirits at spirit springs**.
7. **Meet Gu at a spring.**
   - **Guide:** the spring's inspection lists **Encounters**, with how to meet each population and how many are mature.
   - **Search:** the **Search for …** command stays visible when depleted, and says why.
   - **Liquor Worm:** it has a **Lure Liquor Worm** command that explains the beer lure. It can still only be lured, never searched for.
8. **Capture in spirit grass.** A wild Gu standing in grass or on filth can now be captured where it stands. If its cell holds an item or building, the dormant Gu is laid on the open neighbouring cell nearest the captor. If capture is refused, the right-click menu says why.

## Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Automatic feeding | Runs inside the existing sustenance scheduler's bounded visit: each individual, about once per in-game hour, and only while its hunger is advancing. The job-based `JobGiver_GuTendOwnGu` and its think-tree insertion are removed. | No new scans. Off-map caravans, other factions, wild Gu and dead hosts never advance, so they are never provisioned. This follows the owner-approved off-map suspension policy. |
| Where automatic feeding may take food | Two sources: the Gu Master's inventory, excluding passage cargo in transit; then player storage on the Gu Master's map, meaning stacks that are in valid storage, unforbidden, not burning, reachable from the Gu Master and not reserved by another pawn. Loose items on the ground do not count. | "Approved local stockpiles" means storage the player chose. Taking loose items from anywhere on the map would look like teleportation. |
| Storage Gu extension point | `GuProvisionSourceDef` with a `GuProvisionSource` worker: list supplies, then revalidate one stack at commitment. | A future Storage Gu, pantry or logistics link adds a def and worker; the service never changes. |
| Meals | Whole meals only, combined across stacks and sources. A meal is accepted while at least half of it fits (the Alpha 5A rule). | Same arithmetic as manual feeding. |
| Seeds | Seed collection spends the same flowering as a petal harvest. | Renewable and bounded. Petals and seeds compete for the same flowering, which keeps them distinct. |
| Moon orchid sow work | Set to 400. It was the vanilla default of 10, which made sowing instant. | Crop-like effort for a seed-gated plant, and a real window in which sowing can be interrupted. Balance value. |
| Double-click selection | A Harmony prefix on `Selector.SelectAllMatchingObjectUnderMouseOnScreen`, active only when the cursor is on a Project GU plant and not on a pawn. | Vanilla prefers any pawn within one cell over the clicked thing. In the cavern, wild Gu and colonists stand near the orchids. |
| Capture cell | `CanPlaceDormant` accepts standable plants and filth. Capture falls back to an adjacent open cell. | Items rest on plant cells in vanilla. This was the cause of the Hope Gu capture failures in the planted basin reported for Alpha 5A. No teleport and no collision is ignored. |
| Developer spawn | **Project GU › Spawn registered wild Gu...** creates the individual through `TrySpawnDevelopmentGu(…, configuredSpecies: true)`. The individual has the development origin, and no cohort is debited. | Uses the registry's own creation path. The `PostMake` guard is unchanged; its message now explains why vanilla *Spawn pawn/thing* cannot make a Gu. |
| Encounter cell search | Unchanged. | The Moonlight Gu encounter mechanism had no defect. |

## Cultivation is not sleep

**Inspection result:**
- Wall nourishment (`JobDriver_GuNourishAperture`), essence refinement (the Liquor Worm channel) and will-enforcement never call `TickResting`, never set `asleep`, and never touch the Rest need. A managed source guard enforces this.
- Project GU adds no think-tree node, so nothing outranks rest in autonomous behaviour.
- A player may order cultivation at any hour. Vanilla's exhaustion collapse still applies.

**Native test:**
- **Run:** cultivation was ordered for 32,500 ticks, re-ordered whenever a session ended.
- **Rest:** fell from 0.32 to 0.06, *Very tired*. It never rose, and the pawn was never resting while cultivating.
- **End:** exhaustion then put the pawn into ordinary sleep.
- **No change needed:** this already behaved correctly, so no gameplay change was made.

**Future requirement:** a Gu that reduces or replaces sleep must act on the real Rest need. Depending on that species' ability, it may restore Rest, lower its fall rate through `RestFallRateFactor`, or replace sleep. General cultivation must never do this. Recorded in the [roadmap](ROADMAP.md).

## Architecture

| Piece | Where | Role |
| --- | --- | --- |
| Plant framework | [`Botany/`](../Source/ProjectGu/Botany) | Spec, seed gate, sowing driver, seed gathering, right-click provider, selection patch. See the [plant framework guide](PLANT-FRAMEWORK.md). |
| Provisioning | [`GuProvisioning`](../Source/ProjectGu/Sustenance/GuProvisioning.cs), [sources](../Defs/GuProvisionSourceDefs/GU_ProvisionSources.xml) | Plan, revalidate, consume, feed, all in one tick. History: `automaticMeals`, `lastFeedSource` (new Scribe keys, default empty). |
| Scheduler hook | [`GuSustenanceComponent.Visit`](../Source/ProjectGu/Sustenance/GuSustenanceComponent.cs) | Provision a hungry, advancing Gu before computing its level. The last attempt is kept per individual, transiently, for the interface and the hunger message. |
| Hauling fairness | [`GuPassageHauling`](../Source/ProjectGu/Underground/GuPassageHauling.cs) | `NextCargo`, a new Scribe key, rotates first claim after each pack. Cargo list in [the cavern site def](../Defs/MapGeneration/GU_SpiritSpringCavern.xml). |
| Ambience | [`GuSpringAmbience`](../Source/ProjectGu/Presentation/GuSpringAmbience.cs), drawn from `GuEcologyMapComponent.MapComponentDraw` | Count from the cohort. Only springs registered on the current map, on screen and not fogged. Deterministic per-spirit paths; a few mesh submissions per spring per frame; nothing per tick. |
| Encounters | [`CompGuSpiritSpring`](../Source/ProjectGu/Springs/CompGuSpiritSpring.cs) | Commands stay visible with reasons; inspection lists encounters. |
| Capture | [`GuIndividualRegistry.Dormancy`](../Source/ProjectGu/GuIndividualRegistry.Dormancy.cs), [`JobDriver_GuCaptureWild`](../Source/ProjectGu/Jobs/JobDriver_GuCaptureWild.cs), [`Pawn_GuWildProjection`](../Source/ProjectGu/Pawn_GuWildProjection.cs) | Plant cells, adjacent fallback, refusal reasons. |

### Provisioning transaction (one tick)

1. **Eligibility:** the scheduler visits a Gu whose hunger is advancing and whose level is Hungry or worse.
2. **Provisioner:** the Gu must be in its own cultivator's care, in the cultivator's aperture or carried by them. The cultivator must be a player colonist with the policy on.
3. **Plan:** for each accepted food in order of preference, gather supplies from every source, then plan whole meals.
4. **Commit:** revalidate each planned stack and take exactly the planned items. If anything falls short, nothing is consumed.
5. **Apply:** apply the meals with the Alpha 5A rules, which settle hunger and end starvation, and record the source.

There is no reservation, job or deferred step, so save and load, interruption and two Gu Masters sharing a stack cannot double-consume. Another pawn's reservation, including a manual feeding job's partial reservation, is respected.

**Performance:** a provisioning attempt only happens for a hungry Gu, at most once per in-game hour per individual. A search that finds nothing for a food on a map is cached for 1,250 ticks, unless that map's stack count of the food changes. See Verification for measurements.

## Art specifications

All art is original placeholder, generated by [`scripts/generate-alpha5b-placeholder-art.ps1`](../scripts/generate-alpha5b-placeholder-art.ps1). A hand-drawn replacement of the same size needs no code change.

| Asset | Path | Size | Notes |
| --- | --- | --- | --- |
| Moon orchid seeds | `Textures/ProjectGU/Items/MoonOrchidSeeds.png` | 64 × 64 | Pale, faintly luminous teardrop seeds on a soft glow. `Graphic_Single`. |
| Gather seeds command | `Textures/ProjectGU/UI/GatherSeeds.png` | 64 × 64 | Orchid bloom over a cupped hand of seeds. |
| Gather seeds designation | `Textures/ProjectGU/Designations/GatherSeeds.png` | 64 × 64 | Ring with seeds, drawn over a designated plant. |
| Hope spirit | `Textures/ProjectGU/Effects/HopeSpirit.png` | 64 × 64 | Bright core, soft halo, two faint wing flares. Drawn additively (`MoteGlow`), tinted pale cyan in XML. |

## Verification

Environment: Windows 11, .NET SDK 10, RimWorld 1.7.5014 rev1195 (Waylith), Core and Harmony.

```powershell
.\scripts\verify.ps1 -Focused
.\scripts\verify-priority0-live.ps1 -Profile Alpha5B -Parallel 1 -StageTimeoutSeconds 2400
.\scripts\verify-priority0-live.ps1 -Profile Alpha5BVisual -Parallel 1 -StageTimeoutSeconds 2400   # opens a game window
```

| Kind | Result |
| --- | --- |
| Real compilation | Release build against the installed 1.7 assemblies: 0 errors, 0 warnings. Before use, these engine paths were read in decompiled 1.7 code: `Selector.SelectAllMatchingObjectUnderMouseOnScreen`, `GenUI.ThingsUnderMouse`, `FloatMenuMakerMap` and its providers (`FloatMenuOptionProvider_WorkGivers`), `WorkGiver_GrowerSow`, `JobDriver_PlantSow`, `JobDriver_PlantWork` with `GetAdditionalHarvestYield`, `Plant.LifeStage`, `ReservationManager.CanReserveStack`, `Need_Rest`, `JobDriver` save and resume. |
| Managed (real assemblies, no game process) | `verify.ps1 -Focused` passes. The new `Alpha5BChecks` has 29 assertions: the orchid's spec and the preserved harvest bounds (6 petals, 0.7, 0.85); spirit grass not gated; plant spec validation against unlimited harvests or gathering, killing gathers, wild-only or unsowable material, and bad byproducts; cargo order; provisioning sources and their order; and source guards. The source guards cover: provisioning creates no job, revalidates before consuming and consumes in one place; cultivation never touches Rest; the plant hooks return for non-framework plants; sowing spends once; the capture rules; the `PostMake` guard is unchanged. The Alpha 5A checks (46) pass with the provisioning file in their no-species-names guard. |
| Native headless `Alpha5B` | All four stages passed: `new`, `integration`, `reload`, `legacy5a`. Campaign `5e2be9e53b9f4c89b8b75d62ac764963`. |
| Native rendered `Alpha5BVisual`, final build | Campaigns `f6f53430acfa40c8963568ff6c57078c` and `40f401e298684a45a53eb9d482064912` passed all five stages on the final product build. That is the four headless stages again, plus `visual`, with its rendered assertions: drawn spirits equal the cohort-derived count, nothing is drawn with the setting off, double-click selects every orchid in view and nothing else, and the right-click menu, inspection and encounter guide are present. These two final runs wrote no PNG files, most likely because the display was asleep. No capture error was logged. |
| Native regression | Campaign `d3239d5238974189ba5f394ea3f78c1a`, `-Parallel 3`, on the final source: 11 of 14 units passed. They were `Alpha2`, `Alpha3`, `Alpha4A`, `Alpha5A` (with its routine-feeding step rewritten for provisioning), `RankOne`, `Slice3B3B` (identity and custody), `Slice3B5A` (cohorts), `Phase2Lifecycle` (terminal outcomes), `DormantHauling`, `AutomaticHauling` (underground hauling) and `ClanStart`. Three failed:<br>• `GuGeneration` compared the guard's exception text exactly, and the message now explains itself. The test now checks the prefix.<br>• `Botanical` expected unguarded orchid sowing. It now expects the seed-carrying job and checks that the seed is spent.<br>• `Alpha1` timed out once on passage travel after a capture.<br>Campaign `11cbfafaf91f4b4caa81c322b6ee4412` reran those three, and all passed. `Alpha3` was also updated: the spring now shows a **Lure Liquor Worm** explanation command, while still offering no search. Rendered `Alpha4AVisual` passed (campaign `152ccf878c0e4ddbad76e919eb4b2d16`), so the aperture renderer and its layering are unchanged. |

### What the native fixture demonstrates

The fixture uses the Alpha 3 save chain: the Southern Border Clan scenario, a naturally generated spirit-spring cavern, and a disciple with a refined Liquor Worm and Moonlight Gu. Development Mode is off throughout, except for the developer-spawn step. Actions go through the real right-click menu (`FloatMenuMakerMap.GetOptions`), native work givers and jobs, gizmos and Aperture actions.

| # | Requirement | Evidence (stage) |
| --- | --- | --- |
| 1 | Southern Border Clan, natural cavern | Scenario start, ordinary descent and awakening (new). |
| 2, 3 | Select and harvest wild orchids | All 5 selectable orchids selected together, with no spirit grass or pawns. The right-click **Harvest** gave 5–6 petals, and the plant survived at growth 0.70. The refusal says *regrowing*. A reserved orchid names its holder. The native designator also refuses (integration). |
| 4 | Multiple selection, rendered | 7 orchids in view were all selected and nothing else. In the same frame, vanilla's screen-rectangle enumerator returned 9 things and **0 orchids** under Waylith. That is why vanilla double-click could not select orchids: the cause of the owner's report (visual). |
| — | Seeds | Right-click gathering gave 2 seeds and no petals. The plant was at 0.70 and refused petals and seeds until regrown. Its command explains why (integration). |
| — | Seed-gated sowing | **No seeds:** the native grower refused, and the zone's right-click menu explained it.<br>**With seeds:** the prioritized order carried one seed.<br>**Drafting mid-sowing:** removed the half-sown orchid and kept the seed.<br>**Afterwards:** native grower automation resumed the sowing by itself, and seeds spent equalled orchids sown.<br>**Vanilla crops:** pass through unchanged (integration). |
| 5, 6 | Petal and stone passage hauling | The assigned hauler carried stone, then petals, then stone: petals did not wait behind the second stone stack. All 115 stones and 17 petals were counted every tick and conserved, with delivery into surface storage (integration). |
| 7 | Moonlight Gu encounter and capture | **Search for Moonlight Gu**, then capture via the right-click menu, then will-enforcement and storage by the elder (integration, Alpha2, Alpha3, Alpha4A regressions). |
| 8 | Hope Gu ambience versus counts | 2–3 spirits drawn for 2–3 living Hope Gu, measured from the frame's draw count. Cohort, records and selection were unchanged while drawing. Nothing is drawn with the setting off. Encountering one Hope Gu took one adult, and the ambience followed (integration, visual). |
| — | Capture in spirit grass | A wild Hope Gu standing in spirit grass was captured by the ordinary job, and the dormant Gu lies on the grass. With an item under a wild Gu, the dormant Gu was laid on the adjacent open cell, and the item was untouched (integration). |
| — | Developer spawn | **Outside Development Mode:** refused.<br>**In Development Mode:** one registered wild Moonlight Gu with development origin, no cohort debit, and *Development test Gu* when inspected.<br>**Vanilla creation:** `ThingMaker` creation of the body is refused with the explanation, and nothing enters the registry (integration). |
| 9, 10 | Will-enforcement and storage | The elder refined the developer-spawned Gu through the Aperture actions and stored it (integration). |
| 11 | Manual feeding | The physical Feed job is unchanged. Alpha 5A's manual-feeding steps passed. The Alpha 5A save's in-progress feeding job committed once under the new build (Alpha5A regression, legacy5a). |
| 12, 13 | Automatic feeding without interruption, exact consumption | **Policy off:** nothing fed.<br>**Policy on:** the Gu Master kept an ordered job throughout, and no feeding job was ever created. 3 Moonlight meals ate exactly 6 stockpiled petals, and 2 Liquor Worm meals drank exactly 2 stockpiled beers.<br>**Not touched:** forbidden petals, loose petals, and beer on the surface.<br>**Order:** carried petals were eaten first.<br>**Reservations:** a stack reserved by another colonist was not taken and the reason was recorded; once released, it was used (integration). |
| — | Two Gu Masters, one supply | Two starving Moonlight Gu of two masters, with one meal of petals: exactly one meal eaten. With 20 petals: both sated, and exactly the 14 petals the meals needed were used (integration). |
| 14 | Save and load during feeding, cultivation and hauling | **Saved with:** a sowing in progress (seed in hand), cargo packed by the hauler, and starving or hungry Gu.<br>**After a fresh process:**<br>• identical nourishment and history for all three Gu;<br>• the sowing resumed and spent exactly one seed;<br>• the cargo was held, then delivered once storage allowed it, with nothing created or lost;<br>• provisioning worked (reload).<br>Cultivation save and load is covered by the Alpha3 and RankOne regressions. |
| 15 | Rest during prolonged cultivation | 32,500–37,000 ticks of player-ordered cultivation: wall nourishment, then essence refinement, re-ordered when a session ended. Rest fell from 0.32 to 0.04–0.06, *Very tired*. It never rose, and the pawn never rested while cultivating. Exhaustion then put the pawn into ordinary sleep (integration). |
| 16 | Alpha 5A save compatibility | A save written by the Alpha 5A build: `alpha5a-feeding.rws`, kept locally in `tests/obj/Alpha5ASaves`, SHA-256 `be8fcad6…a64`. It loaded with no re-activation. New history fields start empty. Its feeding job committed once with exact petals. Its orchids gained the component and offer harvesting. Provisioning fed its Liquor Worm from a stockpile with exact beer (legacy5a). |
| 17 | Performance | A hungry visit whose master has no food costs 3.95 µs with the negative cache warm. 100 hungry Gu Masters would cost about 0.4 ms per in-game hour. The first call measured 9.4 ms; that one-time cost includes JIT and building the source list. Visits are only for hungry Gu, at most hourly; Alpha 5A's scheduler cost is unchanged. Ambient spirits: a few draw calls per visible spring per frame, and nothing per tick (reload, visual). |

### Rendered inspection

The inspected screenshots are in `tests/obj/Priority0-63458ad308114931b39a9c517570dba5/Alpha5BVisual/alpha5b-screenshots/`, an earlier rendered campaign that passed all stages. They predate the last three fixes listed below: the Feed button, the spirit size, and view-based selection. Shot `10` therefore shows the original single-orchid selection, which is the defect that led to the fix:
- `00`–`02`: spirits at the spring, later, and with the setting off;
- `10`: orchids multi-selected;
- `11`: orchid inspection, with the Gu food relationship;
- `12`: the orchid right-click menu, showing harvest and gather seeds;
- `20`: sowing with a seed in hand;
- `30`: the spring's encounter guide;
- `40`: the Gu page with *Last fed … (automatically, from stockpile)*.

**Found during inspection and fixed:**
- **Clipped Feed button:** its label was cut off, because the button was capped at 300 px. It now spans the panel.
- **Spirit size:** spirits were enlarged from 0.32–0.55 to 0.5–0.8 cells.
- **Double-click selected nothing:** the vanilla screen-rectangle enumerator returned no plants under Waylith. Framework plants now use the camera's view rectangle.

### Defects found by the native tests and fixed

- **Stale "no seeds" refusal:** the seed lookup was cached per tick. In a paused game, unforbidding seeds and right-clicking would still be refused. It is now cached per tick and frame.
- **Instant sowing:** moon orchids inherited vanilla's default of 10 sow work. It is now 400.

### Fixture-only adjustments, not gameplay changes

- **Work assignment:** the disciple is given Growing and Plant cutting work, as a player would in the Work tab. Vanilla refuses prioritized sowing otherwise.
- **Holding control stacks:** during the provisioning-controls step, the other cavern colonists are drafted and the disciple's Hauling is off, so ordinary hauling cannot move the forbidden and loose control stacks. Passage hauling is paused during that step.
- **Holding the cargo for the save:** surface storage refuses the cargo kind after packing, so the hauler holds it through the save. The reload stage allows it again.
- **Holding still:** wild Gu are held still with a native wait job while the captor walks over.
- **Same-frame cache:** the seed cache is invalidated after the fixture changes forbidden flags within one frame.
- **Colony upkeep:** as in Alpha 5A. Autosave is off, mood breaks are off, and colonists' food, rest and mood are topped up between steps, except during the Rest test.
- **Sowing budget:** a sowing may take up to 12,000 ticks. An unskilled sower's plant work speed of 0.10 needs about 4,000 ticks for 400 work.

## Remaining limitations

- **Starting a game:** the fixture starts the Southern Border Clan scenario through the harness's quick-start path with the real scenario def, not by clicking through the main menu. Every in-game action after that uses native paths with Development Mode off.
- **SeedsPlease:** detected, but untested with a real external seed mod, because no RimWorld 1.7 release exists.
- **Provisioning scope:** it covers a Gu Master's own refined Gu in their aperture or inventory, the Alpha 5A scope. A dormant Gu lying on the ground is fed by order.
- **No storage, no automatic feeding:** loose items never count. A colony without a stockpile for petals or beer is told so in the hunger message.
- **Ambient spirits:** Hope Gu only. Other species can be declared later in XML.
- **Open observation:** in one regression run, `Alpha1`'s passage travel after a capture timed out at 3,000 ticks. It passed on rerun, and had passed in every earlier campaign. No cause was found in the logs.
- **Downed Gu Master:** a downed or unreachable Gu Master's Gu are fed only from what that Gu Master carries, because stockpile access requires that they can reach the stack.
- **Balance values are provisional:** two seeds per gathering, one seed per orchid, 400 sow work, 14 spirits at most.
