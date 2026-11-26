# Alpha 5B — Gameplay integration and usability

Alpha 5B fixes the problems the owner's first real session with Alpha 5A exposed:
- **Moon orchids** now behave like ordinary crops.
- **Petals** travel up the passage with the other cargo.
- **Automatic Gu feeding** is background colony provisioning instead of jobs that interrupt Gu Masters.
- **Hope Gu** drift around their springs as small luminous spirits.
- **Encounters and capture** explain themselves.
- **Developers** can spawn a registered Gu safely.

It also adds the [Project GU plant framework](PLANT-FRAMEWORK.md), which brings seed-gated moon orchid cultivation.

## Final owner acceptance

On 2026-11-26 the owner completed the final gameplay retest, confirmed satisfactory behavior, and officially accepted `c86bf6bdc898ce0b06ea0d9cb8db5e5342c8d740` on `feature/alpha5b-playtest-corrections`. Acceptance includes the underground dispatch corrections, independent **UG haul** priorities, and the installer upgrade fix. The owner authorized publication and integration. The [open regression records](#open-regression-records) remain open.

Alpha 5B is based on Alpha 5A at `d82c723769ab19dd9078ba3dfefd0a20ee8d10ce`. Its history includes the original gameplay candidate `5df38decbc4f0b089267de0d52f66db224ba9b5a`, engineering closeout `fd4204b68025cfd59bbc5ffe375c9861ca137ce4`, and surface dispatch follow-up `b37ac75f5b1abb6140e84332f412a0d3e0d464c2`. The final retest closes the underground logistics issue from the earlier owner session.

## Owner playtest logistics follow-up

The underlying dispatch problem was reproduced with actual orchid outputs. At the original build, a new Southern Border Clan colony harvested six petals and gathered two seeds using the native right-click jobs. The loose, allowed, unreserved stacks were reachable from the landing and accepted by a normal surface stockpile. With an eligible surface hauler and no named assignment, all eight items remained underground after 12,000 ticks. The old work giver evaluated only the site already assigned to the requesting pawn. The passage's **Assign to underground hauling** action saved a named assignment but issued no priority job. A native haul order on an item is local to its map. [Baseline native failure](../tests/obj/Priority0-202c3305e2a440799fc4e5fd6a91bde7/Alpha5B/priority0-result.txt)

A hash-verified copy of the owner's tick-60,000 checkpoint had no passage assignment, no cavern stockpile, a reachable landing, and one surface stockpile that accepted all three cargo Defs. The installed Defs and the six petals, six seeds and cavern stone in the save conformed to the rules. As a positive control on the old build, assigning a pawn and giving them Hauling as their only work delivered all the plant cargo and the cavern stones. This proves the missing dispatch for the saved state and shows how a working named assignment could transport the owner’s actual items. The checkpoint does not capture the exact live order or pawn state at every attempt. [Old-build owner-save control](../tests/obj/Priority0-5ecccd47ccf94981bbecfb5629859e9c/Alpha5B/priority0-result.txt)

The previous Alpha 5B fixture assigned a specific pawn, restricted them to Hauling, explicitly configured surface storage, and used a reachable fixture-spawned petal stack. It forbade the real petals during that delivery check and kept seeds forbidden for the sowing fixture. The earlier `AutomaticHauling` and `CargoPrototype` profiles exercised stone. Those runs verified approved portions of cargo custody and routing and missed unassigned surface dispatch and the complete real-crop shipment.

### Dispatch and work priorities

- Generated, reachable passages with configured cargo default to any eligible surface hauler. The existing native Hauling work giver looks up routes for the pawn's surface map. It uses the existing Def order, per-Def item listers and a 150-tick route-level check interval. It checks available cargo, capacity and destination storage before offering a descent and revalidates before crossing. Adding a site or removing a map invalidates the map index.
- A started job claims the route's one worker slot after obtaining the native passage reservation. Native pickup and placement reserve the actual source stack and destination cell on their own maps. The existing travel, packing, inventory and storage job drivers carry out the legs. The active worker and physical packed stack survive saving, drafting and blocked delivery. Another worker can take the next trip after delivery or a cancelled departure without cargo.
- **UG haul** in the Work tab controls the dedicated Underground hauling priority. The native work giver retains the existing jobs. Under equal manual priorities, Underground hauling has natural priority 301 and ordinary Hauling has 300. Needs, higher-priority work and player orders continue to use RimWorld scheduling. **Prioritize underground hauling** requires the dedicated work to be enabled, as native prioritized work does. **Assign to underground hauling** pins a named worker, while **Allow any hauler** restores automatic dispatch. Existing saved named assignments and pause states retain their meaning.
- Older saves initialize the dedicated priority once from the pawn's raw ordinary Hauling priority, including zero. This preserves numbers hidden by checkbox mode. New colonists get the default active priority if capable. Later saves preserve edits to either column independently.
- The dispatcher is available to sites whose Defs declare cargo. It uses a live direct surface passage and slot storage, and excludes forbidden items or items already in valid cavern storage. It uses native reservations and reachability at each local leg. Changed filters, occupied landings or unavailable cargo can defer a return or delivery. A paused, drafted or disabled worker keeps packed items in native inventory until eligible again.

The dedicated `Alpha5BLogistics` profile starts with real flower harvesting and seed gathering, then exercises dispatch, forced orders, competing haulers, scheduled stone batches and fresh-process delivery. The optional save replay takes a copy as input and keeps the owner's Work priorities and storage settings. Its fixture suppresses colony incidents, autosaves and Gu automatic feeding, and tops up pawn needs to keep the cargo-routing observation controlled.

- **Owner-save upgrade:** the corrected build autonomously stored six petals, six seeds, four existing cavern stones and one newly produced stone. The private input copy's SHA-256 is `965D5AB4C445EAA9B65D409DFA16326FF317355ED5BD455880FD680D1F724938`. [Owner-save replay](../tests/obj/Priority0-e35069feed6f4becbc3f57ea65548c4f/Alpha5BLogistics/priority0-result.txt)
- **Focused managed:** `verify.ps1 -Focused` passed on Windows with .NET SDK 10.0.401 and the real RimWorld 1.7.5014 rev1195 assemblies. The production build passed with zero errors and warnings. [Verification output](../tests/obj/alpha5b-logistics-focused.log)
- **Native logistics and upgrade:** `Alpha5BLogistics` and `Alpha5B` passed all seven stages. The logistics fixture blocked dispatch with no cargo, disabled hauling, rejecting storage and an occupied landing. It preserved a player order, reserved a prioritized departure, then released that reservation on interruption. Two eligible workers competed for one route. After a fresh-process reload the original stone load arrived, then six new petals and two new seeds, while more stone waited below. The second native production deadline was advanced in the fixture to keep a full day of underground exposure out of the draft test. The original Alpha 5B integration passed with mid-sow and mid-haul reload and the real Alpha 5A upgrade. [Focused native campaign](../tests/obj/Priority0-78ba2e1c0bab43228ddbc4b249e8c7f1/campaign-summary.txt)
- **Existing native profiles:** `AutomaticHauling`, `CargoPrototype`, `Botanical`, `Alpha1`, `Alpha3`, `Phase2Lifecycle` and `DormantHauling` passed on the corrected production build. The Alpha 1 result retains the earlier open timeout record. [Regression campaign](../tests/obj/Priority0-cf2332bd06584b1bab202e656c127869/campaign-summary.txt)
- **Feeding reruns:** the broader first run missed the enabled beer-lure menu in `Alpha5A` and failed one supply-control assertion in `Alpha5B`. The isolated `Alpha5A` rerun passed all five stages with its original assertions. The focused `Alpha5B` rerun passed with added counts in its failure message. Neither initial trigger was isolated. [Alpha 5A rerun](../tests/obj/Priority0-19159fefab2a447eb7da6aabf3783d07/campaign-summary.txt)

```powershell
$alpha5b = 'C:\Users\monve\Downloads\Project-Gu-alpha3-liquor'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$alpha5b\scripts\verify-priority0-live.ps1" -Profile Alpha5BLogistics -Parallel 1 -StageTimeoutSeconds 1500
```

## Installation regression and upgrade

After installation of `b37ac75f5b1abb6140e84332f412a0d3e0d464c2`, RimWorld reported `C6A4C1CA` for `Defs/ThinkTreeDefs/GU_GuTending.xml`. The Steam Mods directory contained the latest DLL with `GuProvisioning`, `JobDriver_GuFeedGu` and `WorkGiver_GuPassageHaul`, and the Alpha 5A ThinkTree XML that references the removed `JobGiver_GuTendOwnGu`. It matched the Alpha 5A source apart from CRLF line endings. The source at `b37ac75` had already removed the file. The normal Mods and Workshop directories checked contained one Project GU package. A disposable boot from a copy of the deployed directory reproduced the same error reference. [Pre-upgrade boot log](../tests/obj/Priority0-a5cbe878162f4d7984c2a55b730c9af3/Alpha1/new.stdout.txt)

The old installer overlaid source files and never retired deleted files. The native verification runner excluded the normal Mods directory and staged a fresh source payload, so it never exercised a stale file left by an upgrade. The corrected installer records managed relative paths and SHA-256 hashes in `.projectgu-install.json`. It removes retired paths that match the manifest's previous hash or a known historical fingerprint. The initial migration covers the Alpha 5A patch in Git and Windows newline forms. Managed file replacements are staged and each file is published atomically. The manifest is published last, so a rerun after an interrupted pass can complete known retirements. The upgrade keeps unrelated files and keeps locally edited retired files with a warning.

`verify-install.ps1` installs pinned Alpha 5A into a disposable directory, upgrades it with the regular script, and checks it against a clean deploy. It also tests manifest-based retirement, preservation of local content and a repeated install with identical installed hashes. The native runner's `-InstalledProjectGuPath` option stages the exact installer output for game verification. [Installer checks](../tests/obj/Alpha5BInstallEvidence/verify-install.log) · [Focused checks](../tests/obj/Alpha5BInstallEvidence/verify-focused.log)

- The upgraded deployment passed all five `Alpha5BVisual` stages on RimWorld 1.7.5014 rev1195 with Harmony 3.0.4. Its startup and stage error logs were empty. Native gameplay verified background feeding, mid-haul reload and the pending Alpha 5A manual feeding job, which resumed and consumed the correct quantity. The same deployment loaded the copied owner save, verified migrated Work priorities and delivered the original six petals and six seeds plus the available cavern stones. [Upgrade campaign](../tests/obj/Priority0-c8a530a8eb204bf0b4483410f1321332/campaign-summary.txt)
- The native Work tab rendered both columns independently. The captured rows show **UG haul** at 1 with **Haul** off for one colonist, and **Haul** at 2 with **UG haul** off for another. [Work tab PNG](../tests/obj/Priority0-c8a530a8eb204bf0b4483410f1321332/Alpha5BVisual/alpha5b-screenshots/50-work-tab-underground-hauling.png)
- The clean deployment passed the native logistics, named hauling, cargo prototype, passage travel, terminal lifecycle and dormant hauling profiles. Haul-only and a named worker with UG haul switched off could not dispatch. UG haul alone dispatched and delivered real crop products, and reload preserved the independent values and inventory custody. [Clean campaign](../tests/obj/Priority0-ce5b2bc9735847c5b6588aeeb325fe04/campaign-summary.txt)
- The first clean botanical run exceeded its pre-Alpha 5B sowing budget of 1,800 ticks. The fixture now uses the existing Alpha 5B allowance of 12,000 ticks for a low-skill founder and reports their work speed on failure. The clean native sow, seed consumption and fresh-process reload passed on rerun. [Botanical rerun](../tests/obj/Priority0-80e24d7f54d24a7dbfcb5c1852951a92/campaign-summary.txt)

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
   - **Dispatch:** leave **Underground hauling** on at the passage and enable **UG haul** in Work for a surface colonist. An open cavern with eligible cargo and surface storage is serviced automatically. To request immediate work, use **Prioritize underground hauling** on the passage.
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

**Future Society Core requirement.** Provisioning must obtain authorization to consume the resource on behalf of the recipient. A reachable stack or temporary physical custody grants access only. Future sources must support private property with the owner's permission, institutional supplies with role or allocation rules, and legitimate acquisition such as purchase or authorized transfer. Evaluate permission while listing supplies and revalidate it at commitment alongside availability. Denied or indeterminate permission leaves the stack untouched.

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

Environment for closeout: Windows 11, .NET SDK 10.0.401, RimWorld 1.7.5014 rev1195 (Waylith), Core and Harmony 3.0.4.0.

The closeout began with a clean checkout and verified that both the branch and its local upstream reference pointed to `5df38decbc4f0b089267de0d52f66db224ba9b5a`.

### Installation and verification

Close RimWorld before installing. Run from PowerShell. Enable Core, Harmony and Project GU: Living Cultivation in the game's Mods screen and restart for the owner playtest.

```powershell
$alpha5b = 'C:\Users\monve\Downloads\Project-Gu-alpha3-liquor'
$rimWorld = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$harmony = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'

git -C $alpha5b status --short --branch
git -C $alpha5b rev-parse HEAD
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$alpha5b\scripts\verify.ps1" -RimWorldPath $rimWorld -HarmonyPath $harmony -Focused
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$alpha5b\scripts\install.ps1" -RimWorldPath $rimWorld -HarmonyPath $harmony
```

The live harness builds and stages its own disposable mod. Verify the [Alpha 5A fixture pair](#alpha-5a-compatibility-save) before running either profile. Run the rendered profile in an unlocked Windows desktop session with the display awake through the final stage and no other RimWorld instance.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$alpha5b\scripts\verify-priority0-live.ps1" -RimWorldPath $rimWorld -HarmonyPath $harmony -Profile Alpha5B -Parallel 1 -StageTimeoutSeconds 2400
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$alpha5b\scripts\verify-priority0-live.ps1" -RimWorldPath $rimWorld -HarmonyPath $harmony -Profile Alpha5BVisual -Parallel 1 -StageTimeoutSeconds 2400
```

Check `campaign-summary.txt` for the unit result and `timing.csv` for each stage under the reported campaign directory. Rendered inspection requires opening the PNGs under `Alpha5BVisual/alpha5b-screenshots/`. A passing `visual` stage only certifies the in-game assertions.

| Kind | Result |
| --- | --- |
| Real compilation | Release build against the installed 1.7 assemblies: 0 errors, 0 warnings. Before use, these engine paths were read in decompiled 1.7 code: `Selector.SelectAllMatchingObjectUnderMouseOnScreen`, `GenUI.ThingsUnderMouse`, `FloatMenuMakerMap` and its providers (`FloatMenuOptionProvider_WorkGivers`), `WorkGiver_GrowerSow`, `JobDriver_PlantSow`, `JobDriver_PlantWork` with `GetAdditionalHarvestYield`, `Plant.LifeStage`, `ReservationManager.CanReserveStack`, `Need_Rest`, `JobDriver` save and resume. |
| Managed (real assemblies, no game process) | `verify.ps1 -Focused` passes. The new `Alpha5BChecks` has 29 assertions: the orchid's spec and the preserved harvest bounds (6 petals, 0.7, 0.85); spirit grass not gated; plant spec validation against unlimited harvests or gathering, killing gathers, wild-only or unsowable material, and bad byproducts; cargo order; provisioning sources and their order; and source guards. The source guards cover: provisioning creates no job, revalidates before consuming and consumes in one place; cultivation never touches Rest; the plant hooks return for non-framework plants; sowing spends once; the capture rules; the `PostMake` guard is unchanged. The Alpha 5A checks (46) pass with the provisioning file in their no-species-names guard. |
| Closeout structural and managed rerun | `verify.ps1 -Focused` passed using the installed 1.7 assemblies. The production build had zero warnings and errors. The managed test project emitted existing CS8632 nullable-context warnings. Alpha 5A's 46 and Alpha 5B's 29 assertions passed. |
| Native headless `Alpha5B` | All four stages passed: `new`, `integration`, `reload`, `legacy5a`. Campaign `5e2be9e53b9f4c89b8b75d62ac764963`. The Alpha 6 research review independently passed the same four stages at the exact Alpha 5B gameplay commit in campaign `801520c860974926a5ccce173c5b2460`. |
| Native rendered `Alpha5BVisual`, closeout assertions | Campaign `18044d6c57a742ce9a04870697bd8247` passed `new`, `integration`, `reload`, `legacy5a` and `visual` at the gameplay candidate. Two drawn spirits matched the two living Hope Gu. Turning the setting off drew zero. The selection path picked all seven orchids in view, and the plant menu, plant inspection and spring guide assertions passed. Zero fixture error logs, game-copy changes or isolation problems. The runner could not check Windows sleep events, so its performance timings are unverified. [Stage results](../tests/obj/Priority0-18044d6c57a742ce9a04870697bd8247/timing.csv) |
| Native rendered `Alpha5BVisual`, earlier assertions | Campaigns `f6f53430acfa40c8963568ff6c57078c` and `40f401e298684a45a53eb9d482064912` passed all five stages on the final product build and wrote no PNG files. Neither recorded a capture error. |
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
| 16 | Alpha 5A save compatibility | The [original Alpha 5A save and matching sidecar](#alpha-5a-compatibility-save) loaded with no re-activation. New history fields start empty. Its feeding job committed once with exact petals. Its orchids gained the component and offer harvesting. Provisioning fed its Liquor Worm from a stockpile with exact beer (legacy5a). |
| 17 | Performance | A hungry visit whose master has no food costs 3.95 µs with the negative cache warm. 100 hungry Gu Masters would cost about 0.4 ms per in-game hour. The first call measured 9.4 ms; that one-time cost includes JIT and building the source list. Visits are only for hungry Gu, at most hourly; Alpha 5A's scheduler cost is unchanged. Ambient spirits: a few draw calls per visible spring per frame, and nothing per tick (reload, visual). |

### Rendered inspection

Windows accepted a display and system keep-awake request while the closeout campaign ran. I inspected all nine 2560 × 1600 captures. [Final-build screenshots](../tests/obj/Priority0-18044d6c57a742ce9a04870697bd8247/Alpha5BVisual/alpha5b-screenshots/)

| Shots | Observed in the PNGs |
| --- | --- |
| `00`–`02` | Two small luminous sprites near the spring, slightly shifted in the later frame. Both sprites are absent with the setting off. |
| `10` | The selection panel reads **Moon orchid x7** and multiple plants have selection brackets. |
| `11`, `12` | The plant inspector shows growth and regrowth. The right-click menu shows disabled Harvest and Gather seeds entries, each with its regrowth reason and the current and required growth. A vanilla Architect tooltip overlaps the bottom of the inspector in shot `11`. |
| `20`, `40` | The sowing scene and the Gu page. The pawn inspector on `40` confirms the seed in hand. The Gu page shows the stockpile feed history and the full Feed button label. |
| `30` | The spring is selected and the Hope Gu search, Moonlight Gu search and Liquor Worm lure buttons are visible. The inspector starts at the top of its scroll area. |

Supplemental campaign `eb05a85ba20444ef823b45f7fe3625b8` passed the rendered stage from a copy of the closeout campaign's midpoint save. An archived copy of the candidate used a disposable verification harness to open the flowering plant menu and scroll the native spring inspector. The two additional inspected PNGs show enabled Harvest and Gather seeds actions and the full **Encounters** section with each species' method and mature count. [Flowering orchid menu](../tests/obj/Alpha5BCloseoutVisual/tests/obj/Priority0-eb05a85ba20444ef823b45f7fe3625b8/Alpha5BVisual/alpha5b-screenshots/13-orchid-flowering-actions.png) · [Scrolled encounter guide](../tests/obj/Alpha5BCloseoutVisual/tests/obj/Priority0-eb05a85ba20444ef823b45f7fe3625b8/Alpha5BVisual/alpha5b-screenshots/31-spring-encounter-guide.png)

The earlier inspection in campaign `63458ad308114931b39a9c517570dba5` led to three fixes included in the gameplay candidate.

- **Clipped Feed button:** its label was cut off, because the button was capped at 300 px. It now spans the panel.
- **Spirit size:** spirits were enlarged from 0.32–0.55 to 0.5–0.8 cells.
- **Double-click selected nothing:** the vanilla screen-rectangle enumerator returned no plants under Waylith. Framework plants now use the camera's view rectangle.

### Defects found by the native tests and fixed

- **Stale "no seeds" refusal:** the seed lookup was cached per tick. In a paused game, unforbidding seeds and right-clicking would still be refused. It is now cached per tick and frame.
- **Instant sowing:** moon orchids inherited vanilla's default of 10 sow work. It is now 400.

### Alpha 5A compatibility save

The source is Alpha 5A commit `d82c723769ab19dd9078ba3dfefd0a20ee8d10ce`, native campaign `5e684dda6d324147afb1cfd16ef77208` (`Alpha5AVisual`, RimWorld 1.7.5014 rev1195, Core and Harmony). Its `feeding` stage wrote `Saves/alpha5a-feeding.rws` from a Southern Border Clan colony with the disciple's physical Moonlight Gu feeding job carrying petals and awaiting commitment. The source sidecar is the same campaign's `alpha5a-state.json`. The later stages update other state fields in that file and preserve the cultivator, Liquor Worm and Moonlight Gu IDs used by Alpha 5B. The separate pre-5A derivative exercises activation in Alpha 5A's own historical suite.

Both local inputs match their donor files byte for byte. Keep the pair in `tests/obj/Alpha5ASaves` in the Alpha 5B worktree. These artifacts are Git-ignored. The harness copies the inputs into its per-campaign directory and renames the save copy to `autostart.rws`. It skips `legacy5a` if the save is missing, so require an explicit `legacy5a` pass in the stage evidence.

| Local input | SHA-256 of the archived fixture |
| --- | --- |
| `alpha5a-feeding.rws` | `BE8FCAD6DEFFC435B76BEC3A9AFB6D00BB244DA4E8CF7981707D02217D874A64` |
| `alpha5a-ids.json` | `D15D577EDE758E839F10CF39AA83E3E8262F91F389FA17A2C573B729674BBE27` |

```powershell
$fixture = Join-Path $alpha5b 'tests\obj\Alpha5ASaves'
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $fixture 'alpha5a-feeding.rws'),(Join-Path $fixture 'alpha5a-ids.json') |
    Format-List Path, Hash
```

To regenerate an equivalent fixture, build and run `-Profile Alpha5A -Parallel 1 -StageTimeoutSeconds 1500` using `scripts\verify-priority0-live.ps1` from a separate checkout of the pinned **Alpha 5A** commit against the same game and Harmony. From the resulting `tests/obj/Priority0-<id>/Alpha5A/`, copy `Saves/alpha5a-feeding.rws` and pair it with that run's `alpha5a-state.json` under the local names above. Confirm that `feeding` passed and emitted both files, record the new hashes and campaign ID, and verify `legacy5a` on Alpha 5B. The historical SHA-256 values identify the archived pair. A generated replacement can have different colony state and IDs.

### Open regression records

| Check | Evidence and follow-up |
| --- | --- |
| Alpha 1 passage travel | Campaign `d3239d5238974189ba5f394ea3f78c1a` timed out in `new` while the disciple carried a captured Hope Gu to the surface. The predicate waited 3,000 game ticks. Campaign `11cbfafaf91f4b4caa81c322b6ee4412` passed the rerun. This remains an unexplained intermittent regression risk. If it recurs, record the job and pawn state, elapsed ticks, both endpoints and landing-cell occupancy. |
| Historical Alpha 5A disabled beer-lure option | The Alpha 6 review's four controlled native probes in `67175b193613416a8c15b59aab4b09a7` offered the disabled option with a qualified actor, a valid route and zero eligible bait. A held beer enabled it in all four. The original trigger is unresolved and the fixture was under-specified. Retain the disabled-option assertion and establish those deterministic preconditions before evaluating the native menu. |
| Historical Alpha 5A starvation regression | The Alpha 6 review replayed the same checkpoint successfully. Its negative control `395ad20a5439423fb5f172325d6c5b48` added an unrelated native letter and reproduced the global-count failure while both Gu retained one correct terminal event and one starvation notification each. The original late mismatch remains unattributed. Future assertions should verify terminal outcomes and exactly-once notifications by Gu ID across sweeps and reload, with unrelated letters present. |
| Alpha 5A enabled beer lure and Alpha 5B supply control | The broader logistics campaign reported a missed enabled beer-lure menu and a supply-control failure. Both passed in subsequent runs. The original triggers remain unknown. [Logistics verification](#dispatch-and-work-priorities) |

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
- **SeedsPlease:** compatibility with the actual mod is unverified. The package-ID detection and local sowing bypass exist. End-to-end external seed handling has yet to be exercised in RimWorld 1.7.
- **Provisioning scope:** it covers a Gu Master's own refined Gu in their aperture or inventory, the Alpha 5A scope. A dormant Gu lying on the ground is fed by order.
- **No storage, no automatic feeding:** loose items never count. A colony without a stockpile for petals or beer is told so in the hunger message.
- **Ambient spirits:** Hope Gu only. Other species can be declared later in XML.
- **Passage travel:** the [intermittent Alpha 1 regression](#open-regression-records) remains open.
- **Downed Gu Master:** a downed or unreachable Gu Master's Gu are fed only from what that Gu Master carries, because stockpile access requires that they can reach the stack.
- **Balance values are provisional:** two seeds per gathering, one seed per orchid, 400 sow work, 14 spirits at most.

## Owner playtest

1. In the main menu choose **New colony → Southern Border Clan**. Proceed through storyteller, world and tile selection, and colonist setup with Development Mode off. Unpause and check the **Clan founded** letter, elder and disciples, and reachable spirit spring pit. Descend by right-clicking the pit and awaken a disciple at the spring.
2. At normal play zoom, look for drifting Hope Gu sprites and check that the settings toggle hides them. Select the spring, scroll its inspector to **Encounters**, and check the Hope Gu and Moonlight Gu search commands and **Lure Liquor Worm**. Try the lure without and with accessible beer. Encounter a Gu, capture it in spirit grass, pick it up and take the passage to the surface. Note any stalled order and which endpoint was involved.
3. With multiple orchids visible, double-click one. Pan and repeat with a different group near pawns and spirit grass. With a grower selected, inspect the flower's harvest and seed options. Harvest one and gather seeds from another, then check their regrowth refusals. Sow from a growing zone and check the no-seed reason, a draft interrupt with the seed retained, and one seed spent on completion.
4. Leave **Underground hauling** on at the passage. In Work enable **UG haul** and turn off **Haul** for a surface pawn. With real cargo loose below and accepting surface storage, watch the pawn descend, collect and return. Reverse the Work settings and verify ordinary hauling remains available and the pawn stops taking new passage work. If the route has an old named assignment, use **Allow any hauler** for this check. Try **Prioritize underground hauling** with **UG haul** enabled, then reload a save with a packed trip and check the quantities and priorities.
5. With a hungry refined Gu, try **Feed** from the Aperture Gu page, then enable automatic feeding and watch it use carried food or eligible same-map stockpile food without interrupting an ordered job. Confirm the source and meal in the Gu page, and check the missing-food reason when eligible food runs out. During extended cultivation, verify Rest still falls and the pawn can sleep normally.
6. Load a copy of an Alpha 5A colony if available. Check its existing Gu, feeding history, orchids and any pending feeding job. Keep the original save and report the first unexpected behavior with the save copy or screenshot.
