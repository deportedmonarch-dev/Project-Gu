# Priority 0 underground lifecycle

**Alpha technical acceptance passed on RimWorld 1.7.5014 rev1195.** The rendered native lifecycle suites passed in Core and with Ideology and Odyssey enabled, and the ordinary right-click round trip passed in the disposable Core game. The remaining historical-save and end-to-end DLC interface checks are outside the alpha gate.

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

The independent provider allows eight loaded independent levels and checks the native signed-byte map indexing limit before generation. The address layer has 32 slots. Materialized maps remain loaded and tick, including sealed levels. Dormancy and custom snapshot serialization are deferred. Loaded maps retain their pathing, map grids, managers and standard RimWorld tick work. Route graph checks run on travel, emergency return or an applicable removal attempt. Benchmark tick time, memory, save size and load time before raising the map limit.

## Verification

**Isolated world and native managed assembly.** Windows RimWorld 1.7.5014 rev1195, .NET SDK 10.0.401 and 1.7 Harmony. The registration and Scribe tests restore a shared 32-tile layer, three independent parents and their map references. They also exercise route loss and alternate paths, legacy source protection policy and missing generated bindings. The standard verifier preserves the ecology, cohort, graphics, resource and 5,760-seed terrain checks.

**Native game.** The live verifier launches actual RimWorld in an isolated copy with Harmony and Core, then with Ideology and Odyssey added. Headless mode uses a test-only patch for `MapDrawer.Dispose` when the renderer has no sections and may log missing graphical fog, snow or sand resources. Rendered mode seeds a disposable colony headlessly, then performs lifecycle tests and reloads with the stock renderer. This avoids a graphical quick-start stall in the entry scene. The rendered probe found that rejecting generation inside a pending native load event could dispose a map before its renderer initialized. The provider now waits for native load events to finish before attempting generation. The runner writes saves and logs under ignored `tests/obj`. Each completed profile reached six loaded maps in the final fixture.

**Final rendered result.** Core and Core + Ideology + Odyssey both passed the generation, lifecycle and fresh-process reload stages with the stock renderer and zero errors logged in those stages. The headless suite also passed both profiles. The commands use disposable saves and both profiles load Harmony 3.0.4.

**Ordinary travel through the rendered game UI.** The Core fixture was opened in the stock renderer with scripted checks disabled. An undrafted colonist used the underground return passage and the surface entrance through their right-click menus. The ordinary travel job pathed from a distance, the camera switched to each destination, and the arriving Pawn stayed selected. The game was saved underground through the normal menu and reloaded in the same session. The same colonist and cavern loaded, and the right-click return worked again. The manual run logged no errors. The native fixtures separately verified exact Pawn, equipment and item conservation across a fresh-process reload and emergency recovery.

| Case | Live verification |
|---|---|
| Addressing | Shared layer, distinct addresses independent of geography, multiple maps, and native lookup by parent and tile. |
| Source and layer removal | Occupied last-access source removal blocked. Empty independent source removal retained the original spring level and physical stack. Layer, persistent parent and direct level teardown blocked before mutation. An unrelated settlement still abandoned normally. |
| Fixture rejection and spring continuity | Rejected independent and legacy generation removed only their new fixture map and preserved the retry. The new provider retained native flora support and Hope Gu. A source-less spring produced one physical stone and debited one reserve. |
| Player custody | Return and re-entry used the existing Pawn, including after a save made underground. Emergency return retained equipment and exact inventory and carried stacks. Moving a Pawn after route loss into an emergency caravan and entering a colony preserved custody. A rejected transfer restored the original Pawn and inventory. |
| Legacy provider | A generated native pocket in the legacy save schema stayed bound after ordinary abandonment, automatic removal, raw map removal and direct pocket destruction attempts. Legacy protection restored after fresh-process load. |
| Ideology and Odyssey | The reset commit was rejected before Pawn, inventory or map mutation. Unanchored takeoff into a protected source was rejected before capture. Native Odyssey abandonment removed an empty independent source and retained the cave. |

A historical prototype backup was unavailable in the project workspace, so historical-save coverage is limited to the native legacy fixture. Full anchored flight and the Archonexus quest UI remain optional alpha compatibility checks. The DLC results cover the native operations in the table. Recovery of legacy saves whose source was already lost and teardown by third-party mods that bypass the verified entry points remain unverified.

## Disposable owner acceptance

Use a disposable save with **Harmony + Core + Project GU** and Development Mode enabled. The rendered native suite can also be replayed with `scripts/verify-priority0-live.ps1 -Rendered`.

- [ ] Place two spring entrances. With a colonist selected a few cells away, right-click a shaft and choose **Enter underground level**. Check that the colonist walks to it and the camera follows. Right-click the underground passage and choose **Use underground passage** to return. Repeat at the second shaft and confirm it leads to a different level.
- [ ] Save with a colonist underground. Reload and verify the cavern, equipment and a known inventory stack. Return through the normal passage.
- [ ] In a disposable settlement, leave a colonist underground and try to abandon the last surface access. Check that abandonment is blocked. Remove the shaft and use **Emergency return to surface**. Once evacuated, abandon the source and use the underground diagnostics to confirm the sealed level still exists after reload.
- [ ] If a historical prototype backup is available, use a disposable copy to verify travel, blocked source abandonment and unchanged cavern contents after reload. Optional DLC checks are a complete anchored Odyssey takeoff and an Archonexus quest choice with a protected Pawn underground.

## Changed files

| Area | Files |
|---|---|
| Address and parent Defs | `Defs/PlanetLayerDefs/GU_UndergroundAddresses.xml`, `Defs/WorldObjectDefs/GU_UndergroundLevel.xml`, `Defs/MapGeneration/GU_SpiritSpringCavern.xml` |
| Ownership and travel | `GuUndergroundAddressLayer.cs`, `GuUndergroundLevelParent.cs`, `GuIndependentSite.cs`, `GuPocketProbeComponent.cs`, `GuPocketProbeComponent.Independent.cs`, `GuPocketProbeComponent.Recovery.cs`, `GuUndergroundLifecycleGuards.cs`, `GuPocketSiteDef.cs`, `Building_GuPocketProbeEndpoint.cs` and `JobDriver_GuPocketProbeTravel.cs` under `Source/ProjectGu/Underground` |
| Dependencies and verification | `About/About.xml`, `Source/ProjectGu/ProjectGu.csproj`, `scripts/build.ps1`, `scripts/verify.ps1`, `scripts/verify-priority0-live.ps1`, `tests/ProjectGu.Verification/ManagedEngineChecks.cs`, `tests/ProjectGu.Verification/UndergroundAddressChecks.cs`, and the harness, project and About file in `tests/ProjectGu.LiveVerification` |
| Documentation | `ARCHITECTURE.md`, `README.md`, `docs/RFC-0003-UNDERGROUND-POCKET-MAPS.md`, `docs/ROADMAP.md`, `docs/UNDERGROUND-MAP-SAFETY.md`, `docs/UNDERGROUND-SPIRIT-SPRING.md` and this report |
