# Underground spirit spring ecology and visuals

The owner reported successful live generation, visible flora and working travel on the preceding 31×31 prototype, with 15 of 20 acceptance checks complete. Priority 0 gives newly placed spring entrances independent map ownership and keeps saved native pocket bindings. The new layout and growth schedule apply to newly generated caverns. Existing caverns keep their terrain, fixtures and IDs, and their flora use the new graphics. [Lifecycle policies and testing](UNDERGROUND-INDEPENDENT-LEVELS.md).

## Terrain and layout

New spring caverns are 39×39, up from 31×31 and about 58% more total cells. The usable chamber also grows, to 766–840 exposed cells after excluding the spring footprint, compared with 360–409 in the previous isolated probe. The generic pocket probe retains its 31×31 concrete chamber.

Seeded ellipses and shoreline variation form one connected shallow basin with three smaller pools. Distance to water and local variation grade the banks through mud, marsh, and rich soil. Ordinary soil and rough granite form the outer floor, with irregular granite walls and two seeded outcrops. The 2×2 spring sits on the southern bank. A two-cell dry approach connects the southwest passage to its dry interaction ring. The generator spawns the exit after tracked fixtures, including flora, have been placed.

Across 5,760 seeded layouts, shallow water covered 261–312 cells. The main basin held 163–206 cells, or 60.5–69.2% of all water. The sampled layouts contained 154–245 rich-soil and marsh cells. The core, dry route and both water outlets were connected in every sample. Native terrain textures and the water depth shader are supplied by Core. Final appearance and actual pawn pathing are live checks.

## Flora and carrying capacity

The stock XML targets 24 Spirit Grass patches and five Moon Orchid colonies within nine cells of the spring. Each patch is one selectable, damageable, standable Thing using the existing Building Def and the flora comp. Native `Plant.TickLong` checks leaf loss and the growing season. The dormant Building representation preserves the existing save class and lets the spring apply underground growth directly on suitable soil. Blades and blossoms are part of each sprite and carry no independent simulation.

| Flora | Target | Units per patch | Total at target | Native glow |
|---|---:|---:|---:|---|
| Spirit Grass | 24 | 1 | 24 | Soft green, radius 1.8 |
| Moon Orchid | 5 | 3 | 15 | Blue-violet, radius 3.8 |

The 39 raw units yield seven effective support points, applying five units per point and a seven-point cap. Positive support has a minimum of one point. Hope Gu capacity is `3 + 2 × 7 = 17`, within the existing configured maximum of 30. This matches the prior cavern's seven support and 17 capacity. Older caverns and surface springs keep their five-cell habitat radius and legacy values of two per grass patch and one per orchid. The spring comp owns the reserve, ecology, capacity, maturation and cohorts. Flora loss can suspend or reduce future recruitment while the population ledger retains existing mature and juvenile Gu.

Physical things supply all patch counts. The nearest eligible spring claims each patch by squared distance, with Thing ID breaking ties. Subscriptions and recount caches are map-local and transient. Spawn and native despawn mark nearby springs dirty, and each spring has a bounded fallback recount. Stock growth checks rich or marshy soil and shallow-water proximity, with extra spacing for orchids. It also checks occupancy, zones, terrain and the reserved cells around the spring. The saved `guNextFloraGrowthTick` opts newly generated springs into one step per 30,000 ticks while reserve remains. Each step can replace one missing patch or, when no patch can grow, restore part of one surviving patch's existing hit points. Loading resumes the schedule from saved Things and never seeds a cavern again. The older save default of `-1` keeps their existing flora counts and schedule behavior.

## Passages and lighting

New passages use original 256-pixel sprites for a rock pit with a descending rope and a return shaft with an ascending rope. Their art spans roughly two cells and their anchors occupy one cell. The surface pit requires a clear 3×3 approach. Site profiles may choose their own entrance and return Defs. Saved endpoints retain their existing references and original marker. [Passage Defs](../Defs/ThingDefs/GU_SpringPassages.xml), [pit sprite](../Textures/ProjectGU/SpringPitEntrance.png), [return sprite](../Textures/ProjectGU/SpringRopeExit.png).

The four original 128-pixel patch sprites and two 192-pixel colony sprites retain alpha and are selected by saved Thing ID via native `Graphic_Random`. The spring uses a localized teal glower with an 8.4-cell radius. It lights the near bank and overlaps the weaker grass glow and concentrated violet blooms. The native glow grid handles the lighting lifecycle. The production sprites were generated from text descriptions of the references and exported from original atlases. The reference artwork stays local. [Spirit Grass sprites](../Textures/ProjectGU/SpiritGrass), [Moon Orchid sprites](../Textures/ProjectGU/MoonOrchid).

## Primeval stones

The global stack limit is 200. The spring tries the two original shallow-water outlets east of its footprint, then the four dry cells immediately south. An output requires standable terrain and a reachable, unfogged cell. Native direct placement can merge into a compatible stack with room for the batch. Full or incompatible stacks send the spring to the next candidate. The physical callback counts only acknowledged stones against reserve and lifetime output. Normal production remains one stone per scheduled cycle. New caverns join the eastern outlets to the main basin, and saved caverns use their existing terrain.

The stone Def references Core's native `Graphic_StackCount` silver at `Things/Item/Resource/Silver` with a warm `(255,235,211)` tint. The texture path, graphic class and tint are configurable in XML. Check zoom-level legibility and the champagne tint in game. A dedicated original pebble sprite can replace it if the stock silver silhouette is too angular. [Stone Def](../Defs/ThingDefs/GU_SpiritSpring.xml).

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

- [ ] **Generate and inspect.** In **Debug actions → Project GU**, choose **Place spirit spring cavern entrance**. Click clear, unfogged ground on the surface. Enter with a colonist, then inspect both passages and their labels at normal zoom. Check that the spring gives the strongest glow, flora produce smaller pools of light, and the perimeter stays dark. Confirm a surface spring and habitat still have their usual lighting.
- [ ] **Layout and access.** Start at the southwestern passage and order the pawn along the dry route. Check **Attempt awakening** using an eligible unawakened colonist, then **Return to surface** and re-enter. The pawn should never have to cross a pool to reach the spring.
- [ ] **Flora and cohorts.** Check **GU: Habitat diagnostics** for 24 grass patches, five orchids, seven effective support and capacity 17. Damage a patch and use the growth action to verify graduated health recovery. Destroy a patch, save and reload, then run one growth step and verify exactly one replacement. Reload at full capacity and repeat the step. Block the vacant soil or drain the spring for a growth control. Advance the ordinary ecology action with and without support and check the mature and juvenile counts.
- [ ] **Production, hauling and storage.** Pause and use **GU: Show output cells**, **GU: Produce stone now** and **GU: Production diagnostics**. Verify 1 + 1 makes a single stack of 2 and each successful cycle debits one reserve. Set a compatible stack to 199, produce twice and check 200 plus one at the next valid cell. Fill the other water position to test dry fallback. Block all six positions with full stacks, incompatible items or pawns and check zero output and unchanged totals. Save and reload, then haul into a stockpile and a shelf. Check the Core splash and warm tint. Repeat a production cycle on a surface spring and allow a natural scheduled cycle to run.
- [ ] **Persistence and variation.** Alter a patch of terrain or leave a marker item, save inside, reload, return, save outside, then reload and re-enter. Verify the alteration, site and level IDs, spring habitat ID, cohort and reserve. Compare two independent new sites and verify a change in one affects only its spring. Open an older 31×31 spring cavern with a known patch and terrain count. Revisit an older generic probe and create another to check its concrete chamber and travel.
- [ ] **Rejected generation.** Select a new surface entrance, activate **Reject next generation**, and order entry. The traveler stays above ground, the fresh spring cavern is removed, and retry is available. Retry and verify exactly one spring and the same entrance IDs. Confirm the older modified cavern still contains its changes.
- [ ] **Infestations.** Confirm the spring site is ineligible and inspect for tunnels, hives or cocoons. Use a suitable surface control to check ordinary incidents. The owner observed ineligibility and a separate developer-menu error before this pass. Menu ineligibility alone does not verify every spawn path. Revisit the interface error only if this pass introduces a reproducible regression.

## Verification and release limits

On Windows, `scripts/verify.ps1` passed against the installed RimWorld 1.7.5014 rev1195 assemblies with .NET SDK 10.0.401. Compilation produced zero warnings and zero errors.

- **Structural checks** parse Defs and patches and check identifiers, save keys, graphic references, and the generic probe footprint.
- **Seeded production code checks** exercise 5,760 layouts and initial flora placements. The test rebuilds occupancy from fixture entries to verify full capacity, blocked regrowth, and one-patch recovery when a vacancy exists.
- **Isolated tests using real game code** exercise direct stack placement callbacks, native absorption and the 200 limit, Scribe fixture persistence of reserve, cohorts, growth timing, physical flora and stacks, old-schema cohort migration, failed-production accounting for an unspawned spring, and physical support attribution and invalidation across isolated map grids.
- **Live headless acceptance** for map travel, ownership and save/load is reported in the [Priority 0 test matrix](UNDERGROUND-INDEPENDENT-LEVELS.md). Rendered navigation, lighting, infestation dispatch, hauling and storage are separate ecology and visual acceptance checks. The owner results above apply to the preceding prototype.

The [Priority 0 test matrix](UNDERGROUND-INDEPENDENT-LEVELS.md) records the results for source settlement removal and legacy protections.

## Future botanical update

Role Set C can build on the physical flora Things. The ThingDef and `EcologyProfile` identify the species. If maturity, harvest eligibility, yield, quality, or transplanted state become necessary, the flora comp is the place to own and serialize that per-patch state. A later harvest job can create an ordinary ingredient Thing and notify ecology when it removes a patch or changes its contribution. Cultivated plants can supply separate rules for soil and recovery while sharing species metadata. Spring capacity can consume a species-specific assessment of flora and other habitat conditions. Gu feeding can reference botanical ingredient Defs, including Moon Orchid products, while the spring continues to own its aggregate Gu cohorts.

## Changed files

| Area | Files |
|---|---|
| Passages | `Defs/ThingDefs/GU_SpringPassages.xml`, `Building_GuPocketProbeEndpoint.cs`, `GuPocketSiteDef.cs` and `GuPocketProbeComponent.cs` under `Source/ProjectGu/Underground`, `SpringPitEntrance.png` and `SpringRopeExit.png` under `Textures/ProjectGU` |
| Layout | `Defs/MapGeneration/GU_SpiritSpringCavern.xml`, `GenStep_GuSpringCavern.cs` and `GuSpringCavernLayout.cs` under `Source/ProjectGu/Underground` |
| Flora | `GU_SpiritFlora.xml`, `GU_MoonOrchidHabitat.xml` and `GU_SpiritSpring.xml` under `Defs/ThingDefs`, `CompGuSpiritFlora.cs`, `CompGuSpiritSpring.cs`, `CompGuSpiritSpring.Flora.cs`, `CompProperties_GuSpiritFlora.cs`, `CompProperties_GuSpiritSpring.cs`, `GuEcologyMapComponent.cs` and `GuFloraGrowth.cs` under `Source/ProjectGu/Springs`, `Textures/ProjectGU/SpiritGrass/PatchA.png` through `PatchD.png` and `Textures/ProjectGU/MoonOrchid/ColonyA.png` and `ColonyB.png` |
| Production | `Defs/ThingDefs/GU_SpiritSpring.xml` and `Source/ProjectGu/Springs/CompGuSpiritSpring.Production.cs` |
| Documentation and verification | `.gitignore`, `ARCHITECTURE.md`, `README.md`, `docs/ROADMAP.md`, `docs/UNDERGROUND-SPIRIT-SPRING.md`, `scripts/install.ps1`, `scripts/verify.ps1`, `Source/ProjectGu/AssemblyInfo.cs`, `tests/validate_ecology.py`, and the project, runner and managed fixture checks under `tests/ProjectGu.Verification` |
