# Underground spirit spring prototype

The owner reported successful live generation, visible spring terrain and flora, and working travel in the spring prototype, with 15 of 20 acceptance checks complete before this polish pass. This branch uses the same save-backed site registry and portal travel. Established caverns keep their existing generator, terrain, fixtures and IDs.

## Terrain and layout

The prototype uses Core assets and Project GU's existing spring and habitat graphics. The 31×31 map has a seeded, irregular granite boundary and outcrops. Small pools, a winding shallow channel and saturated fringes cluster around the central basin, with a wet patch reaching outward. A two-cell dry path connects the southwestern passage to the dry ring around the 2×2 spring. The generator keeps the connected chamber and spawns the exit after the spring and flora.

| Exposed walkable target | Installed 1.7 terrain | Fertility and path cost |
|---|---|---|
| 55% ordinary soil | `Soil` | 100%, 2 |
| 17% fertile or marshy ground | `SoilRich`, `MarshyTerrain` | 140% and 100%, costs 2 and 14 |
| 12% saturated ground | `Mud` | 0%, 14 |
| 8% shallow water | `WaterShallow` | 0%, inherits shallow-water path cost 30 |
| 8% exposed stone | Granite's implied `Granite_Rough` | Barren natural stone, path cost 2, separate from blocking granite Things |

The terrain grid uses Core's soil and mud texture blends, shallow water ramp and depth shader, and implied rough-stone terrain. Installed Def data and the terrain generation API establish the terrain choices. In-game lighting, water rendering, perceived terrain proportions and actual pawn pathing remain visual acceptance checks.

One Moon Orchid and three Spirit Grass patches spawn within the spring's existing habitat radius. The ordinary spring comp owns the reserve, Hope Gu cohorts, fertility support, and production schedule. The generated flora supplies seven habitat support. Terrain fertility describes the ground and the existing flora comps supply ecological support.

The spring first tries two shallow basin cells just east of its footprint, then four dry shoreline cells immediately south. Each candidate must be standable, unfogged, free of blocking contents, and reachable from the passage. New caverns reserve the two water cells within the existing water budget. Saved caverns use their current terrain and fall back to dry shoreline if needed. A successful water placement adds a Core splash over the one real stone when the map is being viewed.

## Passages and lighting

The existing passage Def now uses the original [StonePassage sprite](../Textures/ProjectGU/StonePassage.png), a modest native glow and a larger graphic on the existing one-cell footprint. Surface and return passages have contextual labels. The spring and both flora Defs use native glowers enabled only in spring caverns. The spring has a 6.4-cell radius, Moon Orchid 3.5, and Spirit Grass 2.2. The native glow grid handles spawn, loading and despawn. Inspect brightness and overlap in game at normal zoom.

The sprite was generated with the built-in imagegen tool, then sized to 256×256 with alpha retained. Prompt used:

> Original game sprite for Project GU. A compact natural stone passage marker for a top-down colony sim, a rough low oval stone aperture in gray slate with a shadowed opening at its center and two small muted teal mineral inlays on the entrance stones. Slight top-down perspective, chunky silhouette and crisp graphic edges, readable at 32 pixels. Centered square composition, single object with generous transparent margin. No backdrop, ground plane, labels, arrows, text, people, neon bloom, or extra decoration.

## Production developer controls

Select a spawned spring in Development Mode:

- **GU: Produce stone now** runs one real production attempt, including the reserve debit. Use it while paused for a clean count comparison. The debug command leaves the scheduled tick alone.
- **GU: Show output cells** labels water and shoreline candidates as ready, blocked, inaccessible or wrong terrain. The surface version previews nearby candidates. The engine can search farther and ranks surface cells using its native nearby placement rules.
- **GU: Production diagnostics** shows placement outcome, cell, reserve and lifetime values before and after, and the next scheduled production. The last attempt display is session-local. Counts and schedule use the existing save data.

Both callers use the same checked operation. Reserve and lifetime production change by the quantity acknowledged by native placement. Candidates with insufficient room for a full batch are excluded. A failed placement with zero stones changes neither total. The scheduled attempt still advances by the original interval.

## Infestation exclusion

The inspected Core `Underground` biome supplies underground weather and ecology. Its biome data does not veto infestations. Project GU conditionally replaces the three stock IncidentDef worker classes through XML. Each derived worker rejects the exact spring pocket generator before native eligibility work and before direct execution. All other targets delegate to the vanilla workers. New spring generation closes if the expected guard classes are missing. Core and Project GU are sufficient. The wastepack guard is loaded into the IncidentDef only when that Def exists.

| Spawn path | Verified against installed 1.7.5014 rev1195 | Live check |
|---|---|---|
| Standard storyteller infestation | Core's Def targets player-home maps. The worker also requires an eligible cell under thick roof. The spring profile uses thin natural rock roof throughout and guards both worker entry points. | Force the incident in the spring cavern, then try a suitable surface control. |
| Deep-drill infestation | The native worker selects eligible player drills independently of the thick-roof test. Both eligibility and execution are guarded for this profile. | Exercise a drill or the forced incident in the cavern, and confirm normal behavior at a working surface drill. |
| Biotech wastepack infestation | The worker uses cocoon placement around the event center and does not require thick roof. The optional Def replacement guards it when Biotech is active. | Repeat with Biotech enabled separately. Check both a wastepack event and direct execution. |
| Generated hives and ordinary infestation quests | The spring generator has one chamber step and excludes Core's `CaveHives` step and DLC hive generators. Vanilla quest map selection rejects pocket maps, and the infestation quest's eligibility probe also checks the thick-roof cell finder. | Check startup for incidental hives, tunnels or cocoons. |
| Explicit spawn bypasses | Direct debug spawning, a mod or scripted quest calling the low-level tunnel utility with a forced cell can bypass incident workers. The vanilla quest part can also request a fallback cell if it has already been assigned a map out of band. These paths are outside the normal Core eligibility flow. | Treat third-party incident replacements or explicitly targeted scripts as separate compatibility work. |

Only the current synchronous generation attempt can discard a rejected cavern. Cleanup requires the returned map to be new and natively registered, with exactly known generated content or a subset, no pawns and no child pockets. The tracked objects include the spring and flora. Unknown content or registration state closes the route for diagnosis. Re-entering, loading, and revisiting existing sites have no generation or cleanup path.

## Owner acceptance

Use the [isolated setup for the original probe](UNDERGROUND-POCKET-PROBE.md#manual-test-setup), including the `Patches` directory when staging the mod. Start with **Core + Project GU** in Development Mode and a disposable save. Keep the source settlement intact throughout the test.

- [ ] **Generate and inspect.** In **Debug actions → Project GU**, choose **Place spirit spring cavern entrance**. Click clear, unfogged ground on the surface. Enter with a colonist, then inspect both passages and their labels at normal zoom. Check that the spring gives the strongest glow, flora produce smaller pools of light, and the perimeter stays dark. Confirm a surface spring and habitat still have their usual lighting.
- [ ] **Layout and access.** Start at the southwestern passage and order the pawn along the dry route. Check **Attempt awakening** using an eligible unawakened colonist, then **Return to surface** and re-enter. The pawn should never have to cross a pool to reach the spring.
- [ ] **Ecology and output.** Inspect four habitat patches and seven support. Pause, choose **GU: Show output cells**, then **GU: Produce stone now** and **GU: Production diagnostics**. Verify one haulable stone and one debit, plus the same scheduler countdown. Unpause briefly to see the splash, then haul the water stone into a stockpile. Repeat with the two water cells blocked to check shoreline fallback. Block both water cells and all four shore cells with item stacks or pawns, then check zero new stones and unchanged totals. Repeat on a surface spring. Use an existing disposable checkpoint with a scheduled attempt close to due to test the timer independently when available.
- [ ] **Persistence and variation.** Alter a patch of terrain or leave a marker item, save inside, reload, return, save outside, then reload and re-enter. Verify the alteration, site and level IDs, spring habitat ID, cohort and reserve. Compare a second new spring site for a varied layout. Revisit a generic probe saved before this change to check its concrete chamber and original route. The generic **Place pocket probe entrance** action should still create its original chamber.
- [ ] **Rejected generation.** Select a new surface entrance, activate **Reject next generation**, and order entry. The traveler stays above ground, the fresh spring cavern is removed, and retry is available. Retry and verify exactly one spring and the same entrance IDs. Confirm the older modified cavern still contains its changes.
- [ ] **Infestations.** Confirm the spring site is ineligible and inspect for tunnels, hives or cocoons. Use a suitable surface control to check ordinary incidents. The owner observed ineligibility and a separate developer-menu error before this pass. Menu ineligibility alone does not verify every spawn path. Revisit the interface error only if this pass introduces a reproducible regression.

## Verification and release limits

Local `verify.ps1` and XML checks run against actual Windows RimWorld 1.7.5014 rev1195 assemblies and Core Defs with .NET SDK 10.0.401. An isolated geometry check exercised 5,760 combinations of arrival, basin and phase using the compiled layout helpers. Samples had 360–409 exposed cells and 54.7–55.4% ordinary soil, a connected dry path, four dry output cells, and space for four fertile habitat patches. The geometry check did not launch RimWorld or test map rendering, Def injection, save/load, spawn callbacks, or incident dispatch in a live colony.

This polish pass rebuilt with zero warnings and errors. A second 5,760-case check used the updated layout helpers to verify the two water outputs fit the existing water budget and remain connected without crossing the spring footprint. A test against the compiled production component confirmed repeated unspawned-spring failures leave the reserve, lifetime total and scheduled tick unchanged. Actual water placement, hauling, glow appearance, production on live maps and save round-trips are owner acceptance checks.

Source settlement removal remains an unresolved release blocker. See the [local call-chain investigation and isolated removal matrix](UNDERGROUND-MAP-SAFETY.md).
