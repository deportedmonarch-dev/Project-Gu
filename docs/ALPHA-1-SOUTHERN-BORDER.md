# Playable Alpha 1 — Southern Border cultivation loop

Alpha 1 combines the accepted Phase 3B checkpoint `2b1e366` with the accepted M1 clan start `bd45fef` on `feature/alpha1-clan-gu-loop`. The merge keeps both parents and resolves the native harness's profile list and reload dispatch so both checkpoints' fixtures remain available.

## Play the loop

1. Enable Core, Harmony and Project GU in RimWorld 1.7. Start a new game as **Southern Border Clan** and unpause to receive **Clan founded**. The letter points to the spirit spring pit.
2. Select the elder and open **Health**. Hover over the aperture to see B-grade aptitude, Rank 1 and current primeval essence. The disciples' Health tabs show their clan role and unawakened status. Awakening reveals a disciple's aptitude grade.
3. Select a disciple, right-click the **spirit spring pit** and choose **Enter underground level**. Right-click the spring and choose **Attempt awakening at spirit spring**. Keep one mature Hope Gu for each disciple you plan to awaken.
4. Select the spring and choose **Search for Hope Gu**. The discovery message points to the wild Gu. Select the awakened disciple, right-click the wild Hope Gu and choose **Capture Hope Gu (unrefined)**. Right-click the dormant item and choose **Pick up dormant Gu**. You can refine it underground or carry it up via **Use underground passage** at the rope exit.
5. Select the awakened Gu Master holding the Gu and choose **Manage aperture Gu → Enforce will over Hope Gu**. The menu quotes the duration and essence cost. Rank 1 Hope Gu costs 12 essence, and aptitude affects the work time. Essence is paid as work advances.
6. Draft the refiner to interrupt the job. Save and reload, then undraft and choose **Resume will-enforcement** in the same aperture menu. The entry shows saved progress and the remaining essence cost. **Abandon will-enforcement** discards progress and leaves already-spent essence spent.
7. When the Gu recognizes the refiner, choose **Manage aperture Gu → Store Hope Gu**, then **Inspect aperture Gu**. The original Gu is stored in the refiner's two-slot mortal aperture.

The starting spring has three mature Hope Gu. Awakening and finding an individual each use one adult. Refinement uses aperture essence. The clan's 60 starting primeval stones are available for the separate stone economy.

## Verification commands

```powershell
.\scripts\verify.ps1 -Focused
.\scripts\verify-priority0-live.ps1 -Profile Alpha1,Slice3B3B,Slice3B3C,ClanStart,ClanSession -Parallel 2
```

`Alpha1` starts a new colony using the Southern Border ScenarioDef and runs the gameplay commands with Development Mode off. It saves after drafting has interrupted refinement, resumes through the aperture menu in a fresh process, and confirms the stored Gu in a second fresh process. The fixture checks the Gu's cohort debit and identity, the exact native inventory Thing, work and essence accounting, the owner bond, the aperture claim, and the starting stone count. The other profiles cover the accepted multi-Gu, acquisition, active-refinement reload, clan economy and second-game scenarios.

### Results

| Evidence | Outcome |
| --- | --- |
| Windows, .NET SDK 10.0.401, RimWorld 1.7.5014 rev1195 | Release build passed with zero warnings and errors. The focused managed suite passed, including 67 clan checks, repeated-tick charge protection and refinement Scribe round trips. The installation script placed the compiled mod and scenario in a disposable staging directory. |
| Native headless `Alpha1`, campaign `67d1de1b95bf4ee4816af95ae69cf23d` | All three stages passed with Core and Harmony. The elder began at B-grade 73. The disciple awakened at D-grade 22, carried the same Gu to the surface and interrupted its 16,364-tick refinement after 1,000 ticks. The first reload preserved progress, paid essence and the original inventory Thing. Completion paid exactly 12 essence within the fixture's float tolerance. The second reload found one owner bond and one aperture claim for that Gu with zero physical projections. The spring's mature cohort went 3 → 2 → 1, and all 60 primeval stones were conserved. Zero red errors were recorded in the three Alpha 1 stages. |
| Native headless `Slice3B3B`, `Slice3B3C`, `ClanStart`, `ClanSession`, campaign `ae31e847d97c45db9ec54d4ca92a85c7` | All eight regression stages passed. Coverage includes two-Gu aperture custody, ordinary capture and inventory, active refinement reload, founding and pre-founding reload, awakening, stone hauling and trading, and a second clan start in the same session. That campaign also caught an incorrect Alpha 1 test expectation about inventory revisions. The successful Alpha 1 campaign above used the corrected assertion. |

Campaign reports are kept locally under `tests/obj/Priority0-<campaign ID>/`.

## Limits and graphical playtest

The graphical playtest should exercise the scenario, storyteller, tile and founder selection screens, then follow the steps above using the rendered controls. The test harness configures a new game directly and invokes the same command actions.

On an obstructed start, the founding placement rule may find no open, reachable ground for the pit. The game reports this and ordinary cavern entry requires a valid pit. This milestone ends with an owned Hope Gu in the mortal aperture.
