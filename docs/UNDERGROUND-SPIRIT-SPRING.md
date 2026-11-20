# Underground spirit spring ecology and visuals

The owner reported successful live generation, visible flora and working travel on the preceding 31×31 prototype, with 15 of 20 acceptance checks complete. Priority 0 gives newly placed spring entrances independent map ownership and keeps saved native pocket bindings. The botanical profile applies to fresh generation. Existing caverns keep their terrain, Thing classes, fixtures, IDs and regeneration schedule. [Lifecycle policies and testing](UNDERGROUND-INDEPENDENT-LEVELS.md).

## Terrain and layout

New spring caverns are 39×39, up from 31×31 and about 58% more total cells. The usable chamber also grows, to 766–840 exposed cells after excluding the spring footprint, compared with 360–409 in the previous isolated probe. The generic pocket probe retains its 31×31 concrete chamber.

Seeded ellipses and shoreline variation form one connected shallow basin with three smaller pools. Distance to water and local variation grade the banks through mud, marsh, and primeval qi-rich soil. The new soil uses blended edges, 140% fertility, the native growing affordance and a reusable terrain tag. Ordinary soil and rough granite form the outer floor, with irregular granite walls and two seeded outcrops. The 2×2 spring sits on the southern bank. A two-cell dry approach connects the southwest passage to its dry interaction ring. The generator spawns the exit after tracked fixtures, including flora, have been placed.

Across 5,760 seeded layouts, shallow water covered 261–312 cells. The main basin held 163–206 cells, or 60.5–69.2% of all water. The sampled layouts contained 154–245 rich-soil and marsh cells. The core, dry route and both water outlets were connected in every sample. Project GU supplies the original soil texture. Core supplies the water depth shader and the surrounding natural surfaces.

## Flora and carrying capacity

New caverns target 110 native Spirit Grass plants and five Moon Orchids within 16 cells of the spring. Mature grass draws up to nine small meshes per cell in native map sections. The grass is standable and uses the native long-tick schedule. Moon Orchids accept native growing-zone sowing on terrain with the qi-rich tag. Both plants can grow in darkness and use cold-tolerant growth thresholds for the cavern climate. The orchid uses the ordinary native plant state after sowing, including outside a spring's habitat.

| Flora | Target | Support policy | Units at target |
|---|---:|---|---:|
| Spirit Grass | 110 | Physical count contributes up to 24 units at the seeded target | 24 |
| Moon Orchid | 5 | Three units per plant | 15 |

The 39 raw units yield seven effective support points, applying five units per point and a seven-point cap. Positive support has a minimum of one point. Hope Gu capacity is `3 + 2 × 7 = 17`, within the configured maximum of 30. Saved 39×39 caverns with the older growth profile retain their nine-cell habitat radius and the 24 Building grass patches and five Building orchid colonies. Older 31×31 caverns without that growth schedule and surface springs use their established five-cell habitat rules. The spring comp owns the reserve, maturation and cohorts. Flora loss can reduce future recruitment while existing Gu remain in the ledger.

Physical Things supply all counts. The nearest eligible spring claims each patch by squared distance, with Thing ID breaking ties. Subscriptions and recount caches are map-local and transient. Spawn and despawn mark nearby springs dirty, with a bounded fallback recount. The botanical profile uses qi-rich or marshy ground for native grass and qi-rich soil for orchids, with extra natural spacing for orchids. Cavern regrowth respects occupancy, zones and reserved access cells. The saved `guNextFloraGrowthTick` schedules one step per 30,000 ticks while reserve remains. Each step can replace one missing patch or partly heal one surviving patch when the field is full. Saved Things and their growth resume through native loading. A missing botanical profile flag selects the prior rules.

## Passages and lighting

New passages use original 256-pixel sprites for a rock pit with a descending rope and a return shaft with an ascending rope. Their art spans roughly two cells and their anchors occupy one cell. The surface pit requires a clear 3×3 approach. Site profiles may choose their own entrance and return Defs. Saved endpoints retain their existing references and original marker. [Passage Defs](../Defs/ThingDefs/GU_SpringPassages.xml), [pit sprite](../Textures/ProjectGU/SpringPitEntrance.png), [return sprite](../Textures/ProjectGU/SpringRopeExit.png).

The four original grass sprites and two orchid sprites retain alpha and use native `Graphic_Random` variants. The spring's localized teal glower lights the near bank, and one broad blue-violet accent light reveals the wider botanical field. The light derives from the saved spring profile, and legacy flora retain their original lights. The new soil texture is an original wrapped noise tile with small blue-violet mineral flecks. [Spirit Grass sprites](../Textures/ProjectGU/SpiritGrass), [Moon Orchid sprites](../Textures/ProjectGU/MoonOrchid), [qi-rich soil texture](../Textures/ProjectGU/Terrain/PrimevalQiRichSoil.png).

## Primeval stones

The global stack limit is 200. The spring tries the two original shallow-water outlets east of its footprint, then the four dry cells immediately south. An output requires standable terrain and a reachable, unfogged cell. Native direct placement can merge into a compatible stack with room for the batch. Full or incompatible stacks send the spring to the next candidate. The physical callback counts only acknowledged stones against reserve and lifetime output. Normal production remains one stone per scheduled cycle. New caverns join the eastern outlets to the main basin, and saved caverns use their existing terrain.

The stone Def references Core's native `Graphic_StackCount` silver at `Things/Item/Resource/Silver` with a warm `(255,235,211)` tint. The texture path, graphic class and tint are configurable in XML. Check zoom-level legibility and the champagne tint in game. A dedicated original pebble sprite can replace it if the stock silver silhouette is too angular. [Stone Def](../Defs/ThingDefs/GU_SpiritSpring.xml).

## Cross-map cargo prototype

The Core `scripts/verify-priority0-live.ps1 -Profile CargoPrototype` fixture runs on RimWorld 1.7.5014 rev1195. A colonist picks up a 37-stone stack underground using the native carry tracker. Ordinary passage rejects the loaded hands. Native `ThingOwner.TryTransferToContainer` moves the same stack into the colonist's inventory, which accompanies the Pawn through the existing route. A fresh-process reload restores the held stack. The fixture puts that physical stack down on the surface and starts the vanilla haul job, which delivers it to a stockpile filtered for primeval stones. The final map counts total exactly 37.

The native pickup clamp uses carrying capacity divided by the item's volume per unit, bounded by the Def's stack limit. The pack step also needs to respect inventory mass. `Pawn.DeSpawn` stops the current job and clears queued work during transfer. The automated implementation should use map-local jobs and queue each destination leg after successful arrival. Save active job targets and cargo through their native owners. Before dispatch, check the existing route, source item availability and surface storage. Revalidate each leg locally. Interruption must release reservations and leave the cargo recoverable with the Pawn or on the same map. Automatic discovery, full and filtered storage during dispatch, blocked and lost passages, cancellation, and several-stack automation remain the completion gates for Phase E.

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

- [ ] **Generate and inspect.** In **Debug actions → Project GU**, choose **Place spirit spring cavern entrance**. Click clear, unfogged ground on the surface. Enter with a colonist and inspect the field at normal zoom. Check grass coverage, orchid placement, qi-rich soil transitions, and visibility of the dry approach and passage.
- [ ] **Layout and access.** Start at the southwestern passage and order the pawn along the dry route. Check **Attempt awakening** using an eligible unawakened colonist, then **Return to surface** and re-enter. The pawn should never have to cross a pool to reach the spring.
- [ ] **Flora and cohorts.** Check **GU: Habitat diagnostics** for 110 grass plants, five orchids, seven effective support and capacity 17. Damage one plant, then destroy one and use the growth action to check partial healing and single-plant replacement. Sow a Moon Orchid in a growing zone over open qi-rich soil. Compare an ordinary soil cell, then save and reload to check the plant and cohort.
- [ ] **Production, hauling and storage.** Pause and use **GU: Show output cells**, **GU: Produce stone now** and **GU: Production diagnostics**. Verify 1 + 1 makes a single stack of 2 and each successful cycle debits one reserve. Set a compatible stack to 199, produce twice and check 200 plus one at the next valid cell. Fill the other water position to test dry fallback. Block all six positions with full stacks, incompatible items or pawns and check zero output and unchanged totals. Save and reload, then haul into a stockpile and a shelf. Check the Core splash and warm tint. Repeat a production cycle on a surface spring and allow a natural scheduled cycle to run.
- [ ] **Persistence and variation.** Alter a patch of terrain or leave a marker item, save inside, reload, return, save outside, then reload and re-enter. Verify the alteration, site and level IDs, spring habitat ID, cohort and reserve. Compare two independent new sites and verify a change in one affects only its spring. Open an older 31×31 spring cavern with a known patch and terrain count. Revisit an older generic probe and create another to check its concrete chamber and travel.
- [ ] **Rejected generation.** Select a new surface entrance, activate **Reject next generation**, and order entry. The traveler stays above ground, the fresh spring cavern is removed, and retry is available. Retry and verify exactly one spring and the same entrance IDs. Confirm the older modified cavern still contains its changes.
- [ ] **Infestations.** Confirm the spring site is ineligible and inspect for tunnels, hives or cocoons. Use a suitable surface control to check ordinary incidents. The owner observed ineligibility and a separate developer-menu error before this pass. Menu ineligibility alone does not verify every spawn path. Revisit the interface error only if this pass introduces a reproducible regression.

## Verification and release limits

On Windows, `scripts/verify.ps1` passed against the installed RimWorld 1.7.5014 rev1195 assemblies with .NET SDK 10.0.401. Compilation produced zero warnings and zero errors.

- **Structural checks** parse Defs and patches and check identifiers, save keys, graphic references, and the generic probe footprint.
- **Seeded production code checks** exercise 5,760 layouts for both flora profiles. The test rebuilds occupancy from fixture entries to verify full capacity, blocked regrowth, one-patch recovery, and seven effective support on every seed.
- **Focused native botanical checks** use `scripts/verify-priority0-live.ps1 -Profile Botanical` with Core, Harmony and Project GU. They cover rejected generation and retry, physical plant counts, damage recovery, regrowth, a native growing-zone sowing job, native growth on the roofed bank, Qi suitability away from a spring, and fresh-process reload with a planted orchid and the Pawn underground.
- **Rendered comparison** reloaded a fresh botanical cavern and a disposable 39×39 cavern saved before the botanical changes. The older save kept 29 Building patches, ordinary rich soil, seven support, three adult Hope Gu, 99,999 reserve and its 24-stone total. The new save kept 116 native plants, including the sown orchid, and 82 qi-rich cells. No errors were logged in either rendered run. On the test laptop the new cave averaged about 31.5 ms for a forced full section rebuild and 0.20 ms for a direct long-tick batch over all 116 plants. These timings describe one loaded map and a warmed renderer. Long-play performance and visual acceptance remain owner checks.
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
