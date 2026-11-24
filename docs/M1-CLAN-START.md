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

A **spirit spring pit** spawns on clear, unroofed, reachable ground within 28 cells of the colonists' centre and at least 7 cells from every colonist. Ordinary play (no Development Mode) can now:

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
| `ClanStartChecks` (managed, no RimWorld process), 67 checks | Scenario parts, `thingDef` and `stuff` references resolve against Core and mod Defs. The scenario parent exists in Core. Every XML field binds a public ScenPart field. Role stats exist. Spring `playerTravel` is set and the probe has none. Role planner edge cases. 20,000 gifted aptitudes are ordinary and graded D>C>B>A. Config errors are reported. `PreConfigure` resets progress. |
| Native RimWorld 1.7.5014 rev1195, headless, Core + Harmony, disposable read-only game copy (`scripts/verify-priority0-live.ps1 -Profile ClanStart,ClanSession`) | Passed. See below. Each stage fails on any red error in the engine log; every stage logged none. |

## Native acceptance

The automated fixture is `tests/ProjectGu.LiveVerification/LiveClanStart.cs`, profiles `ClanStart` and `ClanSession`. Stock `-quicktest` hardcodes Crashlanded, so with `-guclanstart` the fixture runs the same `SetupForQuickTestPlay` sequence with the clan ScenarioDef's own Scenario instance. The fixture pauses at `StartedNewGame` and `LoadedGame` so it can observe the state before founding. All gameplay actions after founding use the actual float-menu options with Development Mode off. The player's selection screens are not exercised.

Final evidence: `ClanStart` run `c4f1e9c0` and `ClanSession` run `d00f4fb6`, both on the final mod code. Earlier failing runs were fixture defects. The unpaused quick-test ticked through founding before checks, and a sale was signed as a positive count, which is wrong outside gift mode.

| Check | Observed |
|---|---|
| 1. Start | Temperate forest, flat. Six colonists. The elder was the oldest (68). Disciples were the two youngest aged 13+ (28, 32). Three mortal workers. Founding was pending at tick 0 with the registry ready. The pit was 7.2 cells from the nearest colonist, unroofed, and registered as an independent spring route. 60 primeval stones. The elder awakened at B-grade (67), Rank 1, essence 33.5/67, with one letter. Disciples were unawakened with hidden ordinary aptitude. Workers were unawakened. |
| 2. Ordinary travel | Development Mode was off. "Enter underground level" was offered and the disciple descended in 128 ticks. The first descent generated the cavern. The rope exit returned the disciple to the surface. |
| 3. Awakening and conservation | "Attempt awakening at spirit spring" opened a C-grade aperture (52). Mature Hope Gu went 3 to 2, and juveniles 0 to 0. The awakened elder is not offered awakening. The disciple role was kept and the unawakened marker removed. |
| 4. Save and reload | Fresh-process reload: founding stayed done, with one letter. The elder's aperture ID and record revision and the disciple's aperture ID were unchanged after 900 more ticks. A save written before the first tick loaded with founding pending at tick 0 and founded exactly once (one letter, one aperture, one pit). The elder's aptitude was then rolled at founding and differed (74 versus 67). This is fixed below. |
| 5. Second game in one session | Stock `GenScene.GoToMainMenu`, then Entry-scene setup with the same ScenarioDef, then stock `PageUtility.InitGameStart`. Game 2 reused the same ScenPart instance (identity hash `03b2bd1d` in both games). Founding was reset to pending with zero letters, then ran again with one letter and one B-grade (71) elder aperture. The game 2 tile was mountainous desert, and the pit was placed 7.1 cells from the colonists. |
| 6. Hauling and trading | A mortal worker was assigned through "Assign to underground hauling" with Development Mode off. The spring produced 1 stone after one in-game day, and it was delivered to a surface stockpile 24.3 in-game hours after assignment. The total was conserved (61). A trader caravan (`Caravan_Neolithic_WarMerchant`) bought 20 stones at 21.24 silver each for 425 silver. Total stones were conserved, and the colony lost exactly 20. |
| 7. Obstructed and mountainous | With every open cell on the map roofed, placement found no cell. No pit was placed and no error was logged. The player gets a warning message, and the Development Mode placement tool remains. A natural mountainous start placed its pit normally in an earlier `ClanStart` run (temperate forest, mountainous: 7.1 cells) and in session game 2. |

### Defect corrected during acceptance

**Pit placed among the colonists.** The first native run placed the pit 2.0 cells from a colonist. Placement measured the minimum distance from only the first colonist. It now searches around the centre of the starting group and requires every colonist to be at least `entranceMinDistance` away. The fix was verified at 7.1–7.6 cells across five later starts.

### Aptitude persistence fix (final polish)

**Founders' aptitude could change on reload.** Aptitude was rolled at founding, so a save written before the first tick founded with new rolls when loaded (elder 74 versus 67 in acceptance). Disciples and workers were affected the same way, because the registry rolled their aptitude when it first saw them at founding.

`PostGameStart` now rolls every founder's hidden aptitude once and keeps it on the scenario part (new Scribe key `guPlannedAptitudes`, keyed by pawn ThingID). Founding writes those values to the still-unattempted records and then clears the plan. The odds are unchanged: the elder rolls the configured 60–74 range; workers roll the ordinary latent-aptitude table; a gifted disciple who rolls no aptitude rerolls on the gifted table. A disciple's grade stays hidden until awakening. `PreConfigure` and `PostGameStart` discard the previous game's plan. A save made before this fix still founds, rolling at founding as before.

Evidence:
- Managed: `ClanStartChecks` grew to 67 checks. They cover reset on a new configuration, the save key, and 5,000 rolls per role (elder always B within 60–74, gifted disciple always ordinary, worker no-aptitude near one half).
- Native: campaign `23d3b4a4` passed both profiles.
  - `ClanStart`: planned elder 71 was restored by the pre-founding save, and the elder awakened at 71 both times. All six founders' records matched the plan, and the disciple's aperture used the planned 70.
  - `ClanSession`: game 2 rolled new founders and values (elder 61, then 68). None of game 1's pawns were in game 2.
- Fixture robustness: in one intermediate run the elder (the trade negotiator) died during the simulated day from an unlogged event, and `TradeDeal.TryExecute` threw on its null `mindState`. The fixture now pauses the random storyteller (`DebugSettings.enableStoryteller`) for that day and checks the elder is alive before trading. Incidents the fixture fires itself are unaffected. A second fixture assumption was also corrected: old-game pawns stay `Spawned` after leaving to the main menu.

### Not covered by automation

- The interactive scenario selection, storyteller and starting-pawn pages. The fixture reproduces their state changes.
- Rendered appearance: the letter text, health-tab markers and pit sprite.
- An owner playthrough beyond the first in-game day.
