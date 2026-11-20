# Developer pocket-map probe

This branch exercises native pocket generation, a single cavern per site, and a two-way route for one player-controlled colonist at a time. The chamber is empty apart from its boundary rock and return passage. Development Mode controls placement and travel. The [RFC](RFC-0003-UNDERGROUND-POCKET-MAPS.md) describes the larger framework.

## Verification

- **Automated structural checks and real compilation.** `scripts/verify.ps1` passed against RimWorld Waylith `1.7.5014 rev1195` (Steam build `26793372`) using .NET SDK `10.0.401`, with zero warnings and errors. A focused XML check resolved the probe's Def links and its terrain, rock and biome against installed Core XML.
- **Installed assembly and Def inspection.** Core contains the `PocketMap` world-object Def, `Underground` biome and the passage graphic used by the probe. The installed float menu provider calls the Thing's virtual `GetFloatMenuOptions(Pawn)`. Manual and automatic saves use the normal game save path, with autosaves queued outside the pawn's job. Third-party callbacks and live transfer rollback still need testing.
- **In-game verification pending.** Game startup, Core-only map generation, travel and rollback behavior, save round-trips, independent sites, fault handling and the source-removal behavior below require a live game.

## Prototype behavior

Each dev-placed entrance on a player-owned surface settlement receives a saved Site ID and Level ID. The site configuration Def selects the generator, passage Def and map size. The game component saves the IDs, generator configuration, generation state, and references to both maps and both passages under `guPocketProbeSitesV1`. RimWorld owns the map and pawn data. The probe resolves references and validates both live maps, the pocket parent, its source and its world registration when a job commits. A missing binding closes a previously generated route.

On first use the native generator creates the cavern. The chamber step places its exit after the floor and boundary are complete, because a reported generation-step exception can still return a map. The job checks both landing cells before despawning the pawn. It moves the same pawn, checks the actual spawn, and changes camera focus after success. If spawn fails, it attempts to restore the pawn near the original cell. For a live pawn left without map or holder custody, it attempts to preserve the pawn in the engine's world-pawn registry and reports a terminal probe failure. That state requires reloading a disposable checkpoint and needs a separate runtime fault test.

**Reject next generation** requests a failure after native registration. A freshly generated chamber can be discarded when it contains only its passage and natural rock, with no pawns or child pockets. The probe permits a retry only after both registrations are gone and the map is disposed. Unexpected exceptions or uncertain ownership close the route. The native generator can throw after adding a map and before returning a usable handle. If an unexpected generation or cleanup error appears in the log, discard that run and reload the last checkpoint.

## Manual test setup

Use an isolated game copy and a fresh save. These commands run from the repository root. Keep Steam running under the same Windows user and close any running RimWorld instance first. The copy includes the installed game's `steam_appid.txt`. The launch flag redirects saves and mod configuration to the test folder.

```powershell
$game = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -RimWorldPath $game
if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }

$testRoot = Join-Path $env:LOCALAPPDATA ('ProjectGU-PocketProbe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$testGame = Join-Path $testRoot 'RimWorld'
$testData = Join-Path $testRoot 'SaveData'
robocopy $game $testGame /E /XD (Join-Path $game 'Mods')
if ($LASTEXITCODE -ge 8) { throw 'Game copy failed' }

$probeMod = Join-Path $testGame 'Mods\ProjectGU-PocketProbe'
New-Item -ItemType Directory -Path $probeMod -Force | Out-Null
foreach ($folder in @('About', 'Defs', 'Textures', '1.7')) {
    Copy-Item -LiteralPath (Join-Path $PWD $folder) -Destination $probeMod -Recurse
}

$launchArgs = '-savedatafolder="' + $testData + '"'
Start-Process -FilePath (Join-Path $testGame 'RimWorld.exe') -WorkingDirectory $testGame -ArgumentList $launchArgs
```

1. Open **Mods** and enable Core and **Project GU: Living Cultivation** only. Apply and restart. If a relaunch is needed, rerun the final command in the same PowerShell window. Verify the title screen reports `1.7.5014 rev1195` and the load menu shows only test saves.
2. Enable **Development mode** in **Options**. Start a fresh Crashlanded colony with manual saves, and save as `GU-Probe-Baseline`. Use only this disposable colony. Leave the source settlement intact during the travel tests.
3. On the colony map, open the **Debug actions menu**, search for **Place pocket probe entrance** under **Project GU**, and left-click a clear, unfogged ground cell. Right-click to exit the placement tool. Select the entrance and record its Site ID and Level ID from the inspection pane.
4. Prepare a standing colonist. Use the existing **Test awakening on pawn** debug tool and choose a successfully awakened pawn. In the Health tab record its aperture aptitude and rank and any existing injury or scar. Draft the pawn and use **Pick up** on a small stack of medicine or meals, then verify the stack is in the Gear tab inventory. Use a stable injury or scar and account for aperture essence regeneration during elapsed game time.
5. Select only the test pawn, right-click the surface passage, select **Enter probe cavern**, and unpause. In the cavern, select the same pawn, right-click the cave passage, and choose **Return to surface**.

## Runtime acceptance checklist

- [ ] **Generation and dev gate.** First entry creates one cavern and a usable return passage. Both ends display the same IDs and **Cavern bound**. With Development Mode off, the travel option is absent. Turn it back on before continuing. Check the development log for Def, map generation or spawning errors.
- [ ] **Round trip.** The camera follows the successful arrival both ways. The same colonist returns with their injury or scar, aperture aptitude and rank, and ordinary inventory stack. No extra pawn or item stack appears. The route still works after cancelling an approach before reaching the passage.
- [ ] **Save and load.** Save after placing an unused entrance and reload before first entry. Once inside, drop a distinctive item away from the exit, save as `GU-Probe-Inside`, reload, and return. Save with the cavern empty as `GU-Probe-Outside`, reload twice, and enter again. Verify the IDs and the item persist. Also reload `GU-Probe-Baseline` to check a save without probe records.
- [ ] **Independent sites.** Place and enter a second passage on the same surface map. Verify distinct IDs and a separate cavern with none of the first cavern's marker items. Return from both to their corresponding surface entrances.
- [ ] **Failures.** On a third unused entrance, select **Reject next generation** and order entry. Expect the pawn to stay above ground, a message that the empty cavern was removed, and **Cavern pending** with the original IDs. Order entry again and expect success. For a blocked destination, send one pawn into a cavern, draft them and stand them on its exit cell. Order a second pawn to enter above ground. Expect rejection and the second pawn and their inventory to remain on the surface. Clear the exit and retry.

## Source-map removal recommendation

The probe sets `destroyOnParentMapAbandoned` to false to avoid the default destructive cascade. An orphaned native map is still unsafe. The engine retains a reference to the old source, and `StorytellerUtility.DefaultThreatPointsNow` uses it without a null check. The threat, wealth and transport code also consults the native source binding. The probe checks the source when resolving travel. It has no authority over vanilla abandonment.

| Option | Installed API evidence and decision |
|---|---|
| **Prevent removal while dependent levels exist** | Preferred for native pockets, subject to an approved and tested integration. Explicit abandonment reaches virtual `MapParent.Abandon` and then `WorldObject.Destroy`. `WorldObjectsHolder.Remove` drops the object and its caches before `MapParent.PostRemove` deinitializes the map. The void `Notify_MyMapAboutToBeRemoved` runs after that mutation on this path and cannot veto it. `ShouldRemoveMapNow` covers automatic removal only. A viable guard needs to reject both paths before their first mutation, cover gravship abandonment where applicable, and fail safely on direct teardown such as developer removal. A guard only in `Game.DeinitAndRemoveMap` can strand a map after its world object is already removed. No supported general-purpose veto was found for existing settlements. A targeted patch would need separate approval and a build-specific test matrix. |
| **Reassign to another source** | `PocketMapParent.sourceMap` is public and Scribed. A pre-removal rebind to a live, explicitly selected surface map is technically possible at the field level. There is no engine operation that atomically reassigns the source, route, wealth relationship and any portal or transport references. Probe it only as an explicit relocation operation, with the old source still live, a validated return anchor and reload tests. It cannot cover abandonment without an eligible replacement. |
| **Persistent world-object ownership** | The strongest fallback. Generate a regular underground map with `MapGenerator.GenerateMap`, a dedicated `MapParent` registered in `WorldObjectsHolder`, and `isPocketMap: false`. Base `ShouldRemoveMapNow` returns false and the map is found by its parent reference. Regular maps read biome and tile data from the world grid, and the holder expects a valid tile. Prototype tile allocation, world-map visibility, incidents, save/load and independent teardown before adopting this provider. Its separate parent avoids the native pocket cascade and source-map assumptions. |

Keep the probe dev-only until the protection or fallback passes a disposable-save test with an occupied level, an empty modified level, and two dependent levels. A cancellation must leave the settlement object, maps, route references, pawns and contents intact before and after reload. If that guarantee cannot be established for native pockets, evaluate the persistent world-object provider against the same route and save tests.
