# RFC-0003 — Persistent underground pocket maps

**Status** — The owner authorized a developer-only pocket-map probe. The [probe report and test plan](UNDERGROUND-POCKET-PROBE.md) records installed 1.7 evidence, the prototype, and source-map removal options. Gameplay integration remains gated on lifecycle and runtime verification.
**Target** — RimWorld 1.7 Waylith / .NET 10
**First playable target** — A surface entrance, a persistent spirit spring cavern, and safe travel in both directions.

Priority 0 replaces native pocket ownership for new spirit spring entrances with independent level parents. The native pocket design below is the legacy compatibility baseline. [Current architecture and verification](UNDERGROUND-INDEPENDENT-LEVELS.md).

The [1.7 removal audit](UNDERGROUND-MAP-SAFETY.md) records the pre-mutation coverage required for release. The developer prototype detects forced removal and closes broken routes. Ordinary removal prevention requires further review.

## Engine evidence

The table records the references used in the initial proposal. The [local probe report](UNDERGROUND-POCKET-PROBE.md) supersedes its provisional API and lifecycle findings for the installed build, RimWorld 1.7.5014 rev1195. Core-only generation and the runtime cases still require an in-game test. The [Ludeon 1.7 announcement](https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/) and [1.7 modding notes](https://rimworldwiki.com/wiki/Modding_Tutorials/RimWorld_1.7_Mod_Updates) establish the engine and build target.

| Area | Observed in references | Verification needed |
|---|---|---|
| Native generation | The November 2025 [decompiled PocketMapUtility](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/Verse/PocketMapUtility.cs#L11-L28) uses `GeneratePocketMap(IntVec3, MapGeneratorDef, IEnumerable<GenStepWithParams>, Map)`. It creates a `PocketMapParent`, generates a map marked as a pocket map and registers the parent in `World.pocketMaps`. [YuudongDeepMining's 1.7-targeted source](https://github.com/yuudong123/YuudongDeepMining/blob/04f5d36fbfff3a99b57a7352d8c9101f7ad3e916/YuudongDeepMining/Source/YuudongDeepMining/Building_MineShaft.cs#L30-L50) uses the same call. | Compile the exact call and verify required Core Defs with all DLC disabled. Probe nested pocket generation and generator failure after partial registration. |
| Save graph | The older [Game](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/Verse/Game.cs#L589) deep-saves maps. [World](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/RimWorld.Planet/World.cs#L137) deep-saves pocket parents, and [PocketMapParent](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/RimWorld.Planet/PocketMapParent.cs#L13-L18) saves a source-map reference and generator Def. | Check map/parent cross-reference timing, load with a missing source, and whether an empty cavern stays in the save graph. |
| Portal links | The old [MapPortal](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/RimWorld/MapPortal.cs#L112-L128) serializes map and exit references. Its [GetOtherMap](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/RimWorld/MapPortal.cs#L247-L259) can generate a map on lookup. The [PocketMapExit](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/RimWorld/PocketMapExit.cs#L22-L45) binds through transient generation state and routes to the entrance. The 1.7 mod subclasses both portal types. | Verify extension points and save behavior before reusing them. Test potentially null links and generation-context cleanup. |
| Pawn movement | The older [JobDriver_EnterPortal](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/RimWorld/JobDriver_EnterPortal.cs#L22-L74) uses `DeSpawnOrDeselect` then `GenSpawn.Spawn` and changes draft, jobs and inventory unload state. It may drop a carried object. [1.7-targeted hauling source](https://github.com/yuudong123/YuudongDeepMining/blob/04f5d36fbfff3a99b57a7352d8c9101f7ad3e916/YuudongDeepMining/Source/YuudongDeepMining/Levels/HaulAcross.cs#L186-L198) uses the same movement calls in a separate path. | Verify actual carry and inventory behavior, holders, reservations, lords, world-pawn registration, spawn failure, and callback reentry. |
| Removal | The older [Game.DeinitAndRemoveMap](https://github.com/IlyaChichkov/RimworldDecompiled/blob/d2d70e57a8042c08c3f7ac41659b4988d63e2491/Verse/Game.cs#L723-L779) disposes maps and can destroy linked pockets based on `destroyOnParentMapAbandoned`. | Verify that flag, legal removal hooks, and abandonment with occupied or nested pockets. Confirm whether a supported pre-removal guard exists. |

No decompiled code or external mod assets are included in Project GU. Third-party usage establishes a candidate API path. It does not establish Project GU compilation, compatibility or runtime validation.

## Decision and alternatives

| Option | Tradeoff |
|---|---|
| **Native pocket map with Project GU site and route records** | Preferred if the Core-only probe passes. Uses engine map generation and serialization, keeps the cavern separate from the world map UI, and gives Project GU control of persistence and transfer policy. Native abandonment and residence semantics must pass the tests below. |
| **Persistent world-object MapParent** | Candidate fallback if native pocket behavior cannot meet lifecycle or dependency requirements. Useful for future independent locations reached from the world map. Verify tile collision, map-removal policy, quest visibility and caravan interactions before using it for vertical travel. |
| **Existing underground mod adapter** | Strata or another provider may supply richer layers. An optional adapter can resolve the same site endpoints if it passes the target build and save tests. Provider ownership must be exclusive per site. Core functionality should have no external mod requirement. |
| **Same-map cave region** | Simpler and cheaper to simulate. Cannot validate separate map identity or cross-map custody and does not deliver the first playable target. |

Prototype native generation with Project GU-owned entrance and exit routing. Borrow a native portal UI or job only if probes demonstrate the required result and failure semantics. An engine portal reference can be a resolved link. The saved route remains valid independently of the lifetime of any one entrance Building.

## Site, level and route ownership

- **Site** — A versioned record in the game save with a stable Site ID, site kind, and references to its levels. Distinct locations can exist beneath one surface map or the same world tile. Site kinds point to generation configuration for spring caverns, mines, inheritances or shelters.
- **Level** — A stable Level ID within a site, depth metadata, generator identity and a verified engine map binding. Depth is descriptive, allowing branching levels and several entrances at the same depth. The first cavern is one level. Earth Abyss layers use explicit links between levels when that gameplay is approved.
- **Endpoint and route** — A stable Endpoint ID resolves a surface map or level and its arrival anchor. A route links two endpoint IDs with explicit travel permissions in each direction. Anchor Things are replaceable. A route can close without deleting a map. Route lookup is side-effect free.
- **Save authority** — The engine owns maps, terrain, pawns, inventory and spawned buildings. Project GU owns site topology, generation status and transfer intent if needed. Each map binding is checked against the restored engine graph. No deep copy of a Map, Pawn, inventory, or spring cohort belongs in the site registry.

Use one configurable cavern generator with reserved walkable arrival space, a working exit, and one existing spirit spring Thing. Register a generation intent keyed by Level ID and block reentry before generation. Commit the level after the map and exit validate. Failed generation leaves the traveler at the surface. Clean up an incomplete map only after checking ownership and contents. If generation crossed a save or registered a partial map, resolve or quarantine that binding before retrying. A missing map in an already generated site is a recovery case. Regenerating could erase progress or create a fresh reserve and Hope Gu population.

## Persistence and map lifecycle

Keep the first cavern in the engine save graph after departure, even when no pawn is present. Re-enter the same map. No independent map snapshots, background save files, off-map catch-up or automatic collapse in the first slice. Loaded but unviewed maps may still tick. Measure spring production, ecology and simulation cost and document that behavior. Keep map size and generated site counts small until profiling establishes a budget.

Load topology and stable IDs, resolve engine references, then reconcile after the verified map and Thing initialization point. Disable travel until the route is usable in both directions. Recover a broken endpoint via a bounded landing cell near its original safe anchor on the paired surviving map, or rebind a replacement entrance. Losing a Building never silently deletes its level. Block deliberate abandonment or teardown when it would strand occupants or destroy persistent content. If the base engine can remove a source without a safe interception or recovery point, that route is a release blocker. An unexpected missing map closes the route and keeps the record for diagnosis. Do not redirect travelers to an arbitrary map.

## Transfer protocol

1. **Request.** An explicit enter or return job approaches its endpoint and verifies reachability, pawn eligibility and the route. Initial scope is one player-controlled pawn and their ordinary inventory. Reject a carried pawn or object before departure until its custody path is verified. Avoid cross-map job scanning or automatic hauling.
2. **Prepare.** Resolve both live maps at commit time. Reserve the traveler against duplicate requests and validate standable arrival and return cells, capacity or occupancy, and a safe rollback cell. Generate a new level before touching pawn custody. Block teardown while the operation is pending.
3. **Move.** Move the same Pawn through verified engine operations. Preserve its engine-owned inventory and health. Verify destination spawn before reporting success or changing camera and selection. Clear invalid source-map reservations and jobs through normal lifecycle calls. A failed or reentrant callback cannot start a second move.
4. **Recover.** Cancellation before departure leaves the pawn on the source map. A spawn failure restores the same pawn at the validated source or recovery cell. A retry checks the transaction ID and the pawn's actual engine location. If callbacks or saving can interleave after despawn, require a tested save-owned transit holder for that interval, with only one engine owner. Resolve the pending move once on load. If neither a synchronous rollback nor durable transit can be proven, stop before allowing travel.

The first release may use a synchronous move if the target engine proves the boundary cannot be saved mid-transfer. A stable endpoint and explicit commit outcome also let a later Gu custody adapter move with its host or resolve transit without redrawing the map topology.

## Minimal acceptance plan

| Test | Pass condition |
|---|---|
| API and Core-only smoke | Compile with actual 1.7 assemblies using `scripts/verify.ps1`. Record the exact game build, DLC configuration and relevant lifecycle observations. Generate and enter a native pocket with Core + Project GU. |
| Surface ↔ cavern | Same pawn, aperture and ordinary inventory after both directions. Pending jobs and reservations resolve cleanly. Camera follows only a successful transfer. |
| Persistence | Save outside before first use, save inside, then save after returning. Reload twice. Re-entry preserves the same level, spring ID, cohort, reserve, and player changes. Compare ecology against elapsed loaded ticks. |
| Independent sites | Create two entrances on the same surface map, each bound to its own site. Modify one cavern and verify no effect on the other. Use a developer fixture to probe a second depth and return route before adopting nested pockets. |
| Failure and lifecycle | Cancel before commit. Block a landing, destroy an entrance, inject failed generation or failed spawn, try duplicate orders, and attempt source abandonment with occupants. Every failure preserves a single pawn and item set, with a usable return path or documented recovery. If durable transit is used, save in transit and exercise both resume and rollback. |
| Baseline and cost | Load a backed-up existing save. Verify prior springs, awakening, stone production, and saves without underground data. Record save size and tick cost with one cavern and then several unviewed caverns. |

## Relationship to RFC-0002

The owner accepted the gameplay direction in [RFC-0002](RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md). Three implementation gates remain distinct:

1. Verify canonical Gu identity against the RimWorld 1.7 save graph.
2. Make Hope Gu consumption failure-safe before adding cohort materialization.
3. Validate underground infrastructure and real cross-map transfers before full individual Gu custody implementation.

This RFC proposes the third gate's infrastructure and acceptance test. It does not authorize implementation of underground gameplay or Slice 3.
