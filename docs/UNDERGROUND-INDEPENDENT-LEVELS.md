# Priority 0 underground lifecycle

## Independent engine addresses

The provider uses one lazily registered `GU_UndergroundAddresses` planet layer. The layer creates a small native address grid at subdivision 1 and runs the native tile population step. It uses the Core Underground biome, 19 °C tile baseline, no world connections, and no caravan or world-map selection. Its addresses carry engine identity. Surface geography and passage destinations belong to site records. One layer serves every level, including disconnected sites and future depth links.

RimWorld saves the layer in its world grid. An independent parent uses `GU_UndergroundLevel` and saves its level ID and generator Def. The native map saves its parent cross-reference. Project GU installs a Harmony prefix on `WorldGrid.RemovePlanetLayer` before the engine removes the layer registration and destroys its world objects. The layer is retained for the life of the world.

## Ownership and policies

**Place spirit spring cavern entrance** creates a pending site in Development Mode. First entry creates one engine-owned map and parent. The entrance Thing identifies a route endpoint, and a separate saved level ID identifies the parent. Site, level, endpoint and route IDs are saved in `guIndependentUndergroundSitesV1`. Map terrain, buildings, flora, resources, Pawns, inventories and spring state remain in the native save graph. The original generic probe and existing native pocket sites use `guPocketProbeSitesV1`, preserving their map, IDs and recorded source. There is no provider conversion during load.

- **Lost or blocked access.** A despawned entrance loses its resolved Thing binding. A removed surface map loses its endpoint reference. The level stays registered with its original parent. Travel validates the native map, route and landing cell before spawn. A missing generated map closes the route instead of starting generation again. An empty site with no surface route stays sealed. A replacement entrance will require an explicit binding action.
- **Occupied levels.** A supported source removal is blocked if it would close the last usable surface route for a level with protected Pawns or custody. Future custody systems can expose protected individuals through `IGuUndergroundCustodyProtection`. Settlement abandonment, automatic removal and DLC guards check this before destructive mutations.
- **Emergency return.** Select an affected player Pawn underground. When ordinary passage is unavailable, **Emergency return to surface** returns the same Pawn to a safe cell near the saved entrance on a surviving source map. If that map is gone, the action can place the same Pawn in an actual caravan at the saved passable surface tile. It moves a carried item into that Pawn’s inventory through native ThingOwner APIs. A carried Pawn must be put down and recovered separately.
- **Retention and retirement.** Every materialized spring and generic legacy probe is currently retained. They have no content-deleting retirement action. A pending unused entrance has no generated dependency and can close when its source disappears. The only discard path is the current rejected generation, after verifying that it owns all fixtures and contains no Pawns or child pockets. Future site profiles can add deliberate checked retirement without changing ownership or route identity.
- **Colony reset and gravships.** Ideology reset is blocked for legacy dependencies or protected occupants and player homes underground. An unanchored Odyssey launch is blocked if its removal would affect protected underground access. The preflight permits an anchored surface launch. Underground levels are portal-only.

The lifecycle implementation uses the installed RimWorld 1.7 Harmony mod. The build detects the Steam Workshop assembly in the game's library. For a different game or library layout, pass `-HarmonyPath` to the verification scripts or `-p:HarmonyPath` to MSBuild. Harmony and the game assemblies remain external to Project GU.

The independent provider allows eight loaded independent levels and checks the native signed-byte map indexing limit before generation. The address layer has 32 slots. Materialized maps remain loaded, including sealed or unvisited-since-generation maps. Loaded maps retain their pathing, map grids, managers and standard RimWorld tick work. No path scans run per tick. Route graph checks run on travel, emergency return or an applicable removal attempt. Benchmark tick time, memory, save size and load time before raising the map limit.

## Verification

**Isolated world and native managed assembly.** Windows RimWorld 1.7.5014 rev1195, .NET SDK 10.0.401 and 1.7 Harmony. The registration and Scribe tests restore a shared 32-tile layer, three independent parents and their map references. They also exercise route loss and alternate paths, legacy source protection policy and missing generated bindings. The standard verifier preserves the ecology, cohort, graphics, resource and 5,760-seed terrain checks.

**Native game.** The live verifier launches actual RimWorld in an isolated copy with Harmony and Core, then with Ideology and Odyssey added. Headless mode uses a test-only patch for `MapDrawer.Dispose` when the renderer has no sections and may log missing graphical fog, snow or sand resources. Rendered mode seeds a disposable colony headlessly, then performs lifecycle tests and reloads with the stock renderer. This avoids a graphical quick-start stall in the entry scene. The rendered probe found that rejecting generation inside a pending native load event could dispose a map before its renderer initialized. The provider now waits for native load events to finish before attempting generation. The runner writes saves and logs under ignored `tests/obj`. Each completed profile reached six loaded maps in the final fixture.

**Final rendered result.** Core and Core + Ideology + Odyssey both passed the generation, lifecycle and fresh-process reload stages with the stock renderer and zero errors logged in those stages. The headless suite also passed both profiles. The commands use disposable saves and both profiles load Harmony 3.0.4.

| Case | Live verification |
|---|---|
| Addressing | Shared layer, distinct addresses independent of geography, multiple maps, and native lookup by parent and tile. |
| Source and layer removal | Occupied last-access source removal blocked. Empty independent source removal retained the original steam spring level and physical stack. Layer, persistent parent and direct level teardown blocked before mutation. An unrelated settlement still abandoned normally. |
| Fixture rejection and spring continuity | Rejected independent and legacy generation removed only their new fixture map and preserved the retry. The new provider retained native flora support and Hope Gu. A source-less spring produced one physical stone and debited one reserve. |
| Player custody | Return and re-entry used the existing Pawn, including after a save made underground. Emergency return retained equipment and exact inventory and carried stacks. Moving a Pawn after route loss into an emergency caravan and entering a colony preserved custody. A rejected transfer restored the original Pawn and inventory. |
| Legacy provider | Existing generated native pocket stayed bound after ordinary abandonment, automatic removal, raw map removal and direct pocket destruction attempts. Legacy protection restored after fresh-process load. |
| Ideology and Odyssey | The reset commit was rejected before Pawn, inventory or map mutation. Unanchored takeoff into a protected source was rejected before capture. Native Odyssey abandonment removed an empty independent source and retained the cave. |

No owner saves were used. Rendered click-to-travel, full anchored ship flight, and the Archonexus quest UI are manual acceptance checks. This suite verifies native lifecycle entry points, including blocked DLC transitions, and does not execute an entire rendered ritual or quest. Third-party mods that bypass these verified entry points, and old saves already missing their native source, remain outside the verified recovery scope.

## Disposable owner acceptance

Run `scripts/verify-priority0-live.ps1` to stage and test an isolated copy. Add `-Rendered` to run the suite with visible game windows. For manual acceptance, use a separate disposable save with **Harmony + Core + Project GU** and enable Development Mode.

- [ ] Create two spring entrances and travel through the normal pawn job both ways. Keep one Pawn underground with equipment and a carried stack, then save, reload and verify the same Pawn and contents. Verify the two levels and their resources remain independent.
- [ ] Remove the occupied site’s entrance and verify emergency return. Attempt to abandon its last surface map first. Once evacuated, abandon the source and verify the inaccessible level keeps its terrain and items after reload.
- [ ] Open a backed-up legacy test save. Verify travel, source-abandonment rejection, native map removal and no regeneration. Confirm a settlement with no dependency can still be abandoned or left by caravan.
- [ ] With Odyssey, exercise the anchored flight and unanchored rejection through the normal interface. With Ideology, exercise the Archonexus choice and verify protected underground Pawns and inventories stay in place.

## Changed files

| Area | Files |
|---|---|
| Address and parent Defs | `Defs/PlanetLayerDefs/GU_UndergroundAddresses.xml`, `Defs/WorldObjectDefs/GU_UndergroundLevel.xml`, `Defs/MapGeneration/GU_SpiritSpringCavern.xml` |
| Ownership and travel | `GuUndergroundAddressLayer.cs`, `GuUndergroundLevelParent.cs`, `GuIndependentSite.cs`, `GuPocketProbeComponent.cs`, `GuPocketProbeComponent.Independent.cs`, `GuPocketProbeComponent.Recovery.cs`, `GuUndergroundLifecycleGuards.cs`, `GuPocketSiteDef.cs`, `Building_GuPocketProbeEndpoint.cs` and `JobDriver_GuPocketProbeTravel.cs` under `Source/ProjectGu/Underground` |
| Dependencies and verification | `About/About.xml`, `Source/ProjectGu/ProjectGu.csproj`, `scripts/build.ps1`, `scripts/verify.ps1`, `scripts/verify-priority0-live.ps1`, `tests/ProjectGu.Verification/ManagedEngineChecks.cs`, `tests/ProjectGu.Verification/UndergroundAddressChecks.cs`, and the harness, project and About file in `tests/ProjectGu.LiveVerification` |
| Documentation | `ARCHITECTURE.md`, `README.md`, `docs/RFC-0003-UNDERGROUND-POCKET-MAPS.md`, `docs/ROADMAP.md`, `docs/UNDERGROUND-MAP-SAFETY.md`, `docs/UNDERGROUND-SPIRIT-SPRING.md` and this report |
