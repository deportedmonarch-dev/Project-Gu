# M1: First playable cultivation clan start

Branch `feature/m1-clan-start`, based on accepted Stage 3A commit `09555fb`. This milestone adds a new scenario. It does not change the Gu identity, aperture custody or multi-Gu aperture code that Codex is developing.

## What the player gets

New game → scenario **Southern Border Clan** (`GU_SouthernBorderClan`). The player chooses six starting colonists from ten candidates. They arrive standing (no drop pods) with the `PlayerColony` faction.

| Role | Who | At start | Marker (health tab) |
|---|---|---|---|
| Clan elder | Oldest colonist | Awakened by `GuAwakeningService` with a B-grade aptitude (60–74, configurable). Rank 1, half essence. Uses no Hope Gu. | `GU_ClanElder`: +0.15 social impact, −0.04 mental break threshold |
| Prospective disciples | The two youngest colonists aged 13 or older | Unawakened. Born with an ordinary aperture: D45 C35 B15 A5. The grade stays hidden until awakening. | `GU_ClanDisciple`: +0.15 global learning |
| Mortal workers | Everyone else | Unawakened, with the ordinary 50% no-aptitude roll | `GU_ClanMortalWorker`: +0.05 global work speed |

Role stat offsets and the disciple distribution are provisional M1 tuning, not canon.

A **spirit spring pit** spawns on clear, unroofed, reachable ground 7–28 cells from the colonists. Ordinary play (no Development Mode) can now:

1. Right-click the pit → **Enter underground level**. The first entry generates the 39×39 spring cavern.
2. Right-click the spring with a disciple → **Attempt awakening at spirit spring**. This is the existing job and consumes one mature Hope Gu.
3. Return through the rope exit, or right-click a passage → **Assign to underground hauling** to bring up the daily primeval stones.

A **Clan founded** letter names each role and explains these steps.

### Starting economy

60 primeval stones (market value 30 each), 300 silver, 300 pemmican, 200 raw rice, 25 herbal medicine, 150 cloth. A steel gladius, two steel spears, two short bows and two random animals. 450 wood and 200 steel scattered near the start. The spring yields one stone per day to the hauling route. Traders buy surplus stones at market value.

## Implementation

- `Source/ProjectGu/Clan/ScenPart_GuClanStart.cs` (ScenPartDef `GU_ClanStart`). `PostGameStart` assigns roles and places the pit. RimWorld runs `PostGameStart` before `GameComponent.StartedNewGame`, and the cultivator store only opens after both Gu game components finish. The awakening therefore waits for `Scenario.TickScenario` to find `GuWorldComponent.CanSaveCultivators`. It retries every 60 ticks and gives up with a log warning after 600 attempts. Progress is saved in the scenario (`guFoundingPending`, `guFoundingDone`, `guFoundingAttempts`). `PreConfigure` resets that progress, because new games use the ScenarioDef's own instance and do not copy it.
- The part never creates records or apertures directly. It sets the hidden `LatentAptitude` of a record that is still `Unattempted` and then calls `GuAwakeningService.TryAwaken`.
- `Source/ProjectGu/Clan/GuClanRoles.cs`: role markers and a pure `PlanRoles` age planner.
- Underground: `GuPocketSiteDef.playerTravel` (new, default false) opts an independent route into ordinary travel. Only `GU_SpiritSpringCavern` sets it. The developer pocket probe remains limited to Development Mode. Entrance spawning is shared through `Building_GuPocketProbeEndpoint.TrySpawnEntrance` and `CanPlaceEntrance`. The Development Mode placement tool now calls the same helpers.

No existing Def names, Scribe keys or package IDs changed. New Scribe keys exist only inside the new ScenPart.

## Verification

| Evidence | Result |
|---|---|
| `scripts/verify.ps1` (full and `-Focused`) against RimWorld 1.7 assemblies in `C:\Program Files (x86)\Steam\steamapps\common\RimWorld` | Passed: static checks, game-linked build with 0 warnings, all existing managed suites |
| `ClanStartChecks` (managed, no RimWorld process), 61 checks | Scenario parts, `thingDef` and `stuff` references resolve against Core and mod Defs. The scenario parent exists in Core. Every XML field binds a public ScenPart field. Role stats exist. Spring `playerTravel` is set and the probe has none. Role planner edge cases. 20,000 gifted aptitudes are ordinary and graded D>C>B>A. Config errors are reported. `PreConfigure` resets progress. |
| Native RimWorld | **Not run.** Codex is using the machine for native tests. |

### Native checks still required

1. Start the scenario. There should be no red errors, six colonists and the role markers. The elder should show a B-grade aperture after the first second of play. The letter should appear and the pit should be visible near the colonists.
2. Without Development Mode, enter the pit. The cavern should generate, and a disciple should awaken at the spring with one Hope Gu consumed.
3. Save and reload before the first tick and after founding. Founding should run exactly once, with no duplicate aperture and no second letter.
4. Return to the main menu and start a second Southern Border game in the same session. Founding should run again.
5. Assign underground hauling, check that stones reach a surface stockpile, and sell stones to a trader.
6. Use a map where the start area is enclosed or mountainous. A warning message should appear if no pit can be placed, and the Development Mode tool should still work.
