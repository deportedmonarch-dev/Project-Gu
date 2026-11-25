# Underground spirit spring ecology and visuals

The owner reported successful live generation, visible flora and working travel on the preceding 31×31 prototype, with 15 of 20 acceptance checks complete. Priority 0 gives newly placed spring entrances independent map ownership and keeps saved native pocket bindings. The revised cavern composition applies to fresh generation. Saved caverns keep their terrain assignments, Thing classes, fixtures, IDs, outputs and regeneration profile. Cells and Things that use shared Defs display the updated artwork. [Lifecycle policies and testing](UNDERGROUND-INDEPENDENT-LEVELS.md).

## Terrain and layout

New spring caverns are 39×39, up from 31×31 and about 58% more total cells. The usable chamber also grows, to 766–840 exposed cells after excluding the spring footprint, compared with 360–409 in the previous isolated probe. The generic pocket probe retains its 31×31 concrete chamber.

Seeded ellipses and shoreline variation form one connected shallow basin with three smaller pools. The exposed earthen floor uses primeval qi-rich soil with blended edges, 140% fertility, the native growing affordance and a reusable terrain tag. The original mossy surface texture covers fallow ground as well as the planted banks. Walkable rough granite, walls and two seeded stone outcrops frame the soil. The 2×2 spring sits on the southern bank. A dry corridor and open cultivation pad connect the southwest passage to the spring's interaction ring. The generator spawns the exit after tracked fixtures have been placed.

Across 5,760 seeded layouts, shallow water covered 258–310 cells. The main basin held 161–204 cells, or 60.1–68.9% of all water. The sampled layouts contained 296–373 qi-rich cells. The core, dry route, wet outlets and adjacent dry fallback stayed connected. Core supplies the shallow water terrain, its shader, and the surrounding rock surfaces.

## Flora and carrying capacity

New caverns target 110 native Spirit Grass plants and five Moon Orchids within 16 cells of the spring. Distance to the connected main basin, distance to the spring and overlapping seeded noise fields shape the planting. The scoring favors irregular bands along both ends of the basin. Wild seeding and regrowth leave the dry approach and cultivation pad clear. Mature grass draws up to nine small meshes per cell in native map sections. The grass is standable and uses the native long-tick schedule. Moon Orchids accept native growing-zone sowing on terrain with the qi-rich tag. Both plants can grow in darkness and use cold-tolerant growth thresholds. A sown orchid uses native growth and persistence, including outside a spring's habitat.

| Flora | Target | Support policy | Units at target |
|---|---:|---|---:|
| Spirit Grass | 110 | Physical count contributes up to 24 units at the seeded target | 24 |
| Moon Orchid | 5 | Three units per plant | 15 |

The 39 raw units yield seven effective support points, applying five units per point and a seven-point cap. Positive support has a minimum of one point. Hope Gu capacity is `3 + 2 × 7 = 17`, within the configured maximum of 30. Saved 39×39 caverns with the older growth profile retain their nine-cell habitat radius and the 24 Building grass patches and five Building orchid colonies. Older 31×31 caverns without that growth schedule and surface springs use their established five-cell habitat rules. The spring comp owns the reserve, maturation and cohorts. Flora loss can reduce future recruitment while existing Gu remain in the ledger.

Physical Things supply all counts. The nearest eligible spring claims each patch by squared distance, with Thing ID breaking ties. Subscriptions and recount caches are map-local and transient. Spawn and despawn mark nearby springs dirty, with a bounded fallback recount. Wild placement respects occupancy, zones, reserved access cells and species spacing. The saved `guNextFloraGrowthTick` schedules one step per 30,000 ticks while reserve remains. Each step can replace one missing patch or partly heal one surviving patch when the field is full. Saved Things and their growth resume through native loading. The new `guCavernApproach` stores the original garden and approach origin, independent of later passage edits. Its missing-value default selects the saved spring's prior planting, lighting, and output rules.

## Passages and lighting

New passages use original 256-pixel sprites for a rock pit with a descending rope and a return shaft with an ascending rope. Their art spans roughly two cells and their anchors occupy one cell. The surface pit requires a clear 3×3 approach. Site profiles may choose their own entrance and return Defs. Saved endpoints retain their existing references and original marker. [Passage Defs](../Defs/ThingDefs/GU_SpringPassages.xml), [pit sprite](../Textures/ProjectGU/SpringPitEntrance.png), [return sprite](../Textures/ProjectGU/SpringRopeExit.png).

The revised spring sprite is a low, irregular teal seep with a small blue-white upwelling. Pale cyan grass uses the native mesh scatter, and the existing orchids retain their deeper blue-violet forms. The spring registers one focused light, four soft shore lights, a faint reflection over the approach, and up to five blue-violet accents at mature wild orchids. The engine glow grid caches these stationary lights. Orchid accents reconcile during existing habitat refreshes, while the fixed lights register when the spring initializes. The living soil reads through texture and reflected light. The light handles are transient. Existing profiles use their earlier native glowers. The botanical meshes and static lights leave visible moving Gu to the future individual Gu system.

The original raster assets were generated with the built-in image tool and packaged as game textures. The prompts asked for an irregular top-down dark teal seep with a restrained blue-white upwelling and submerged slate, charcoal-blue earth with distributed fine green moss and grass, and a transparent silvery cyan grass tuft with pale blade tips. [Spring seep](../Textures/ProjectGU/SpiritSpringSeep.png), [Spirit Grass](../Textures/ProjectGU/SpiritGrassBank.png), [qi-rich soil](../Textures/ProjectGU/Terrain/PrimevalQiRichSoil.png).

## Wild Gu encounters

Select the spring to see the mature and juvenile counts for each saved cohort, including the Hope Gu carrying capacity. A mature Hope Gu or Moonlight Gu can be searched for from the spring. Liquor Worm uses the colonist's lure order. The panel reads the spring's existing ledger, so an older spring shows the cohorts present in its save.

On independent levels, spring encounters require a connected player passage with live anchors and unfogged, standable landing cells. A pawn standing at a landing can temporarily block travel while the spring's search and lure options remain available. The registry checks map access again before committing the adult cohort debit and binding the new wild Gu.

On RimWorld 1.7.5014 rev1195, native campaign `4cd1edc8a5dc4be1884beb81059acbfd` reproduced the missing searches on accepted Alpha 3 `758f756` immediately after descent, with three mature Hope Gu and two mature Moonlight Gu in the ledger. With the fix, campaign `c85dcf0d225f450881ebd753cc8c3ab9` passed Alpha 1, Alpha 2, Core lifecycle and Slice 3B-5A. Alpha 1 verified both commands with Development Mode off, all three cohort rows, rejection after the entrance binding is lost, and one adult debit with the rope exit occupied. Fresh-process reloads preserved the cohort counts and the Gu's identity.

Alpha 3 passed its new-game and both fresh-process reload stages on retry in campaign `13c4892d067a4fa29414ebc3ab148c12`. The first attempt found an unbound Moonlight Gu pawn before the fixture issued a Moonlight search. On the accepted build, Moonlight Gu inherits positive petness from Core's hare Def, making it eligible for the stock random starting-pet selector.

The generation integrity follow-up excludes all wild Gu kinds from that pool and guards direct Pawn creation. [Native generation audit](GU-GENERATION-INTEGRITY.md).

## Primeval stones

The global stack limit is 200. The revised spring tries two actual Core shallow-water tiles at the lower edge of the main basin, then the two adjacent dry shoreline cells. Older maps use their established eastern and southern outlets. An output requires a standable, reachable, unfogged cell with capacity for the batch. Full or incompatible stacks send the spring to the next candidate. The native placement callback debits reserve and updates lifetime output by the actual acknowledged quantity. The normal schedule can retry after a blocked attempt.

The stone Def references Core's native `Graphic_StackCount` silver at `Things/Item/Resource/Silver` with a warm `(255,235,211)` tint. The texture path, graphic class and tint are configurable in XML. Check zoom-level legibility and the champagne tint in game. A dedicated original pebble sprite can replace it if the stock silver silhouette is too angular. [Stone Def](../Defs/ThingDefs/GU_SpiritSpring.xml).

## Cross-map cargo prototype

The Core `scripts/verify-priority0-live.ps1 -Profile CargoPrototype` fixture runs on RimWorld 1.7.5014 rev1195. A colonist picks up a 37-stone stack underground using the native carry tracker. Ordinary passage rejects the loaded hands. Native `ThingOwner.TryTransferToContainer` moves the same stack into the colonist's inventory, which accompanies the Pawn through the existing route. A fresh-process reload restores the held stack. The fixture puts that physical stack down on the surface and starts the vanilla haul job, which delivers it to a stockpile filtered for primeval stones. The final map counts total exactly 37.

### Automatic hauling

With an independent spring route already open, select a colonist who can haul and right-click either passage to **Assign to underground hauling**. Enable Hauling for that colonist and configure a surface stockpile or shelf that accepts primeval stones. The **Underground hauling** toggle on either passage pauses or resumes the assignment. Forbid a stone stack to leave it in place. Pausing retains existing packed cargo in the worker's inventory.

One colonist works each assigned route through normal Work priorities. The jobs walk to the passage, reserve and pack a loose stack using the native carry tracker, return through the existing passage, then walk to a reserved surface cell. Delivery uses native direct placement from the worker's inventory, including a partial merge if the storage capacity changed. Pickup is limited by both native carry space and inventory mass. Cargo already packed can wait safely for storage or for a clear passage. A missing passage closes the route and the existing recovery controls remain available for stranded colonists.

The site Def lists eligible resource Defs. Only the independent spring route enables this option. The saved assignment references the worker and one packed Thing, whose physical owner remains the Pawn inventory. Each job has map-local targets and releases its reservations through RimWorld. Route discovery uses the configured resource indexes and native storage groups, with a timed wait between checks and a longer wait after an empty return. Automatic travel leaves the current camera view alone.

**Live verification.** `scripts/verify-priority0-live.ps1 -Profile AutomaticHauling` passed new-game and fresh-process reload stages on Windows RimWorld 1.7.5014 rev1195 with Core and Harmony 3.0.4. The worker packed the same physical 37-stone stack and held it underground while the landing was occupied. After reload it delivered the stack through a partial merge and a second placement, then collected a separate 29-stone stack. The fixture verified counts through retries, full and filtered storage, changes to filters during a job, reservations, hauling and mass restrictions, cancellation, a destroyed source target, and a missing passage. The ordinary ordered trip still worked in both directions. Interactive player acceptance of the assignment and pause controls remains open.

## Production developer controls

Select a spawned spring in Development Mode:

- **GU: Produce stone now** runs one real production attempt, including the reserve debit. Use it while paused for a clean count comparison. The debug command leaves the scheduled tick alone.
- **GU: Show output cells** labels water and shoreline candidates as ready, blocked, inaccessible or wrong terrain. The surface version previews nearby candidates. The engine can search farther and ranks surface cells using its native nearby placement rules.
- **GU: Production diagnostics** shows placement outcome, cell, reserve and lifetime values before and after, and the next scheduled production. The last attempt display is session-local. Counts and schedule use the existing save data.
- **GU: Habitat diagnostics** reports credited patches by species, raw units and effective support, and the Hope Gu capacity.
- **GU: Simulate flora growth step** runs the same bounded placement or recovery step as scheduled growth. The button is available on newly generated spring caverns and preserves the scheduled tick.

Both callers use the same checked operation. Reserve and lifetime production change by the quantity acknowledged by native placement. Candidates with insufficient room for a full batch are excluded. A failed placement with zero stones changes neither total. The scheduled attempt still advances by the original interval.

## Infestation exclusion

The inspected Core `Underground` biome supplies underground weather and ecology. Project GU conditionally replaces three stock IncidentDef worker classes through XML. Each derived worker rejects the exact spring cavern profile, including either ownership provider, before native eligibility work and before direct execution. Other targets delegate to the vanilla workers. New spring generation closes if the expected guard classes are missing. The wastepack guard loads when that Def exists. Independent addresses also use the native layer’s incident and quest whitelists.

| Spawn path | Verified against installed 1.7.5014 rev1195 | Live check |
|---|---|---|
| Standard storyteller infestation | Core's Def targets player-home maps. The worker also requires an eligible cell under thick roof. The spring profile uses thin natural rock roof throughout and guards both worker entry points. | Force the incident in the spring cavern, then try a suitable surface control. |
| Deep-drill infestation | The native worker selects eligible player drills independently of the thick-roof test. Both eligibility and execution are guarded for this profile. | Exercise a drill or the forced incident in the cavern, and confirm normal behavior at a working surface drill. |
| Biotech wastepack infestation | The worker uses cocoon placement around the event center and does not require thick roof. The optional Def replacement guards it when Biotech is active. | Repeat with Biotech enabled separately. Check both a wastepack event and direct execution. |
| Generated hives and ordinary infestation quests | The spring generator has one chamber step and excludes Core's `CaveHives` step and DLC hive generators. Vanilla quest map selection rejects pocket maps, and the infestation quest's eligibility probe also checks the thick-roof cell finder. | Check startup for incidental hives, tunnels or cocoons. |
| Explicit spawn bypasses | Direct debug spawning, a mod or scripted quest calling the low-level tunnel utility with a forced cell can bypass incident workers. The vanilla quest part can also request a fallback cell if it has already been assigned a map out of band. These paths are outside the normal Core eligibility flow. | Treat third-party incident replacements or explicitly targeted scripts as separate compatibility work. |

Only the current synchronous generation attempt can discard a rejected cavern. Cleanup requires the returned map to be new and natively registered, with exactly known generated content or a subset, no pawns and no child pockets. The tracked objects include the spring and flora. Unknown content or registration state closes the route for diagnosis. Re-entering, loading, and revisiting existing sites have no generation or cleanup path.

## Owner acceptance

Use the [isolated Priority 0 setup](UNDERGROUND-INDEPENDENT-LEVELS.md) with **Harmony + Core + Project GU** in Development Mode and a disposable save. Keep the source settlement intact during the ecology and visual checks.

- [ ] **Generate and inspect.** In **Debug actions → Project GU**, choose **Place spirit spring cavern entrance** and enter it with a colonist. At normal zoom, check the spring silhouette, grass continuity, orchid accents, the mossy cultivation pad, and the dark stone rim under ecosystem lighting alone.
- [ ] **Layout and access.** Start at the southwestern passage and order the pawn along the dry route. Check **Attempt awakening** using an eligible unawakened colonist, then **Return to surface** and re-enter. The pawn should never have to cross a pool to reach the spring.
- [ ] **Flora and cohorts.** Check **GU: Habitat diagnostics** for 110 grass plants, five orchids, seven effective support and capacity 17. Damage one plant, then destroy one and use the growth action to check partial healing and single-plant replacement. Sow a Moon Orchid in a growing zone over open qi-rich soil. Compare an ordinary soil cell, then save and reload to check the plant and cohort.
- [ ] **Production, hauling and storage.** Pause and use **GU: Show output cells**, **GU: Produce stone now** and **GU: Production diagnostics**. Check emergence in the two lower-basin water cells, the adjacent dry fallback when both wet cells are full, and zero output and unchanged reserve when all four are blocked. Select and haul a wet stack. Save and reload to check the stone count and surface spring behavior.
- [ ] **Persistence and variation.** Alter a patch of terrain or leave a marker item, save inside, reload, return, save outside, then reload and re-enter. Verify the alteration, site and level IDs, spring habitat ID, cohort and reserve. Compare two independent new sites and verify a change in one affects only its spring. Open an older 31×31 spring cavern with a known patch and terrain count. Revisit an older generic probe and create another to check its concrete chamber and travel.
- [ ] **Rejected generation.** Select a new surface entrance, activate **Reject next generation**, and order entry. The traveler stays above ground, the fresh spring cavern is removed, and retry is available. Retry and verify exactly one spring and the same entrance IDs. Confirm the older modified cavern still contains its changes.
- [ ] **Infestations.** Confirm the spring site is ineligible and inspect for tunnels, hives or cocoons. Use a suitable surface control to check ordinary incidents. The owner observed ineligibility and a separate developer-menu error before this pass. Menu ineligibility alone does not verify every spawn path. Revisit the interface error only if this pass introduces a reproducible regression.

## Verification and release limits

On Windows, `scripts/verify.ps1` passed against the installed RimWorld 1.7.5014 rev1195 assemblies with .NET SDK 10.0.401. Compilation produced zero warnings and zero errors.

- **Structural checks** parse Defs and patches and check identifiers, save keys, graphic references, and the generic probe footprint.
- **Seeded production code checks** exercise 5,760 revised layouts. They verify grass at both ends of the basin, open approach soil, connected output cells, seven effective support, blocked regrowth and one-patch recovery. Isolated native Scribe continues to cover saved Building flora.
- **Focused native botanical checks** use `scripts/verify-priority0-live.ps1 -Profile Botanical` with Core, Harmony and Project GU. They cover rejected generation and retry, physical plant counts, damage recovery, regrowth, an actual growing-zone sowing job on the approach, native roofed growth, soil suitability away from a spring, and fresh-process reload. Production tests verify native selectability, reachability and haulability, water first, a wet stack of 200, alternate water, shoreline fallback, and zero debit when all outputs are blocked.
- **Rendered comparison** on RimWorld 1.7.5014 rev1195 used ecosystem lighting alone. The revised 39×39 save reloaded with 349 qi-rich cells, 116 native plants, seven support, three adult Hope Gu and the 202-stone wet and dry production fixture. An earlier botanical 39×39 save kept its original soil, water, 116 native plants and eastern output cells. A pre-botanical 39×39 save kept 29 Building patches, seven support, 99,999 reserve and 24 stones. A 31×31 fixture generated by the historical `bc95bbe` build kept its exact terrain counts, four legacy patches, 19 stones and valid native pocket route. The disposable copy omitted the empty test-generator component node.
- **Focused rendering timings** on the test laptop averaged about 31.5 ms for a forced full section rebuild and 15.5 ms for deregistering, rebuilding and updating all six fixed lights and five orchid accents in the revised cave. The native grid reuses stationary lights during ordinary play. The renders show continuous foliage at the basin and readable moss over the open cultivation pad. Isolated rock recesses and side pools remain dark. Visual acceptance and long-play performance remain owner checks.
- **Isolated tests using real game code** exercise direct stack placement callbacks, native absorption and the 200 limit, Scribe fixture persistence of reserve, cohorts, growth timing, physical flora and stacks, old-schema cohort migration, failed-production accounting for an unspawned spring, and physical support attribution and invalidation across isolated map grids.
- **Live lifecycle acceptance** covers ordinary rendered passage travel, ownership and save/load. The broader cavern navigation, lighting, infestation dispatch, hauling and storage checks remain part of ecology and visual acceptance. The owner results above apply to the preceding prototype. [Priority 0 test matrix](UNDERGROUND-INDEPENDENT-LEVELS.md).

The [Priority 0 test matrix](UNDERGROUND-INDEPENDENT-LEVELS.md) records the results for source settlement removal and legacy protections.

## Botanical foundation decision

The [0.2.5 botanical decision](BOTANICAL-FOUNDATION.md) records why new caverns use native Plants and Qi-rich soil while existing caverns keep their physical flora, terrain and saved spring state.

## Changed files

| Area | Files |
|---|---|
| Passages | `Defs/ThingDefs/GU_SpringPassages.xml`, `Building_GuPocketProbeEndpoint.cs`, `GuPocketSiteDef.cs` and `GuPocketProbeComponent.cs` under `Source/ProjectGu/Underground`, `SpringPitEntrance.png` and `SpringRopeExit.png` under `Textures/ProjectGU` |
| Layout | `Defs/MapGeneration/GU_SpiritSpringCavern.xml`, `GenStep_GuSpringCavern.cs` and `GuSpringCavernLayout.cs` under `Source/ProjectGu/Underground` |
| Flora | `GU_SpiritFlora.xml`, `GU_MoonOrchidHabitat.xml` and `GU_SpiritSpring.xml` under `Defs/ThingDefs`, `CompGuSpiritFlora.cs`, `CompGuSpiritSpring.cs`, `CompGuSpiritSpring.Flora.cs`, `CompProperties_GuSpiritFlora.cs`, `CompProperties_GuSpiritSpring.cs`, `GuEcologyMapComponent.cs` and `GuFloraGrowth.cs` under `Source/ProjectGu/Springs`, `Textures/ProjectGU/SpiritGrass/PatchA.png` through `PatchD.png` and `Textures/ProjectGU/MoonOrchid/ColonyA.png` and `ColonyB.png` |
| Production | `Defs/ThingDefs/GU_SpiritSpring.xml` and `Source/ProjectGu/Springs/CompGuSpiritSpring.Production.cs` |
| Documentation and verification | `.gitignore`, `ARCHITECTURE.md`, `README.md`, `docs/ROADMAP.md`, `docs/UNDERGROUND-SPIRIT-SPRING.md`, `scripts/install.ps1`, `scripts/verify.ps1`, `Source/ProjectGu/AssemblyInfo.cs`, `tests/validate_ecology.py`, and the project, runner and managed fixture checks under `tests/ProjectGu.Verification` |
