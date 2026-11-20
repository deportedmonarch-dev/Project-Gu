# Underground spirit spring prototype

The owner reported successful live generation of the original native pocket probe. This branch adds a separate, developer-only spring profile. It uses the same save-backed site registry and portal travel. Established caverns keep their existing generator, terrain, fixtures and IDs.

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

The spring tries four dry shoreline cells immediately south of its footprint, from west to east. Each candidate must be standable, unfogged, free of blocking contents, and reachable from the passage. The existing production interval and reserve debit apply. If all four cells are unavailable, no stone is placed and no reserve is spent. The next attempt occurs at the following normal interval.

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

- [ ] **Generate and inspect.** In **Debug actions → Project GU**, choose **Place spirit spring cavern entrance**. Click clear, unfogged ground on the surface. Select one colonist, right-click the entrance, choose **Enter spirit spring cavern**, and unpause. Zoom out to inspect the entire chamber. Pause, move the cursor over each terrain type and check the terrain readout. Capture one overview and one close-up of the basin, pools, flora and shoreline if providing visual feedback.
- [ ] **Layout and access.** Start at the southwestern passage and order the pawn along the dry route. Check **Attempt awakening** using an eligible unawakened colonist, then **Return to surface** and re-enter. The pawn should never have to cross a pool to reach the spring.
- [ ] **Ecology and output.** Inspect the spring for four habitat patches and seven support. Record the reserve, produced count and next production tick. Let a production interval elapse and confirm the stone appears in the four-cell row directly south of the spring. In a separate save, block all four cells with distinct item stacks or solid constructions and advance another production interval. No new stone, reserve decrease or produced-count increase should occur. Unblock one cell and check the following interval. Also run one surface-spring production test.
- [ ] **Persistence and variation.** Alter a patch of terrain or leave a marker item, save inside, reload, return, save outside, then reload and re-enter. Verify the alteration, site and level IDs, spring habitat ID, cohort and reserve. Compare a second new spring site for a varied layout. Revisit a generic probe saved before this change to check its concrete chamber and original route. The generic **Place pocket probe entrance** action should still create its original chamber.
- [ ] **Rejected generation.** Select a new surface entrance, activate **Reject next generation**, and order entry. The traveler stays above ground, the fresh spring cavern is removed, and retry is available. Retry and verify exactly one spring and the same entrance IDs. Confirm the older modified cavern still contains its changes.
- [ ] **Infestations.** From the spring map, use **Do incident w/ points** in the debug actions menu and try **Infestation** and **Deep drill infestation**. No tunnel, hive or infestation letter should appear. Inspect the log for errors or repeated failures. Repeat the standard incident in a disposable surface control with an eligible overhead-mountain cell. Test the drill path with an actively used drill where possible. Repeat the wastepack test in a separate Biotech-enabled test only if that DLC is available.

## Verification and release limits

Local `verify.ps1` and XML checks run against actual Windows RimWorld 1.7.5014 rev1195 assemblies and Core Defs with .NET SDK 10.0.401. An isolated geometry check exercised 5,760 combinations of arrival, basin and phase using the compiled layout helpers. Samples had 360–409 exposed cells and 54.7–55.4% ordinary soil, a connected dry path, four dry output cells, and space for four fertile habitat patches. The geometry check did not launch RimWorld or test map rendering, Def injection, save/load, spawn callbacks, or incident dispatch in a live colony.

Source settlement removal remains an unresolved release blocker. This layout extension stays developer-only while the [native ownership decision](UNDERGROUND-POCKET-PROBE.md#source-map-removal-recommendation) is open.
