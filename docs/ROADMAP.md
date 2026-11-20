# Project GU roadmap

Target RimWorld 1.7 Waylith / .NET 10. See [project vision](PROJECT-VISION.md) for design requirements and [architecture](../ARCHITECTURE.md) for current save ownership and open defects.

## Delivered milestones

| Milestone | Implementation | Verification |
|---|---|---|
| Prototype 0.1–0.2.4 | Aptitude, mortal status, apertures, awakening, spirit springs, haulable primeval stones, and finite-reserve Hope Gu ecology. | The owner reported 10/10 in-game checks for the 0.2.4 compile-hotfix. |
| 0.2.5 Slice 1 | XML species catalog and validation. | The owner reported 12/12 in-game checks after the fully qualified Def XML fix. |
| 0.2.5 Slice 2 | Species-keyed spring cohorts, legacy migration, habitat IDs, and a second developer test cohort. | The owner reported 10/10 in-game checks. The test plan lists additional cases. Explicit build output for the slice remains pending. |
| Slice 2 save-integrity follow-up | Handles ambiguous cohorts, preserves unresolved species data, and prevents population overflow. | Local Python and PowerShell structural checks and isolated stub-based arithmetic tests passed. Actual game-linked compilation and focused game round-trips remain pending. |

Evidence and test plans are recorded in the [0.2.4 release](RELEASE-0.2.4.md), [0.2.5 journal](IMPLEMENTATION-0.2.5.md), and [Slice 2 test plan](TEST-SLICE-0.2.5-2.md).

## Priorities and dependencies

1. **Establish the current build and save baseline.** Run `scripts/verify.ps1` against actual RimWorld 1.7 assemblies. Verify saved depletion, intentionally empty lists, unresolved species, old-save migration, and repeated reloads. Investigate spring registration and awakening debit failures with targeted tests.
2. **Review and schedule reusable underground pocket maps.** This is the next major world-infrastructure priority. Start with a spring cavern. [RFC-0003](RFC-0003-UNDERGROUND-POCKET-MAPS.md) proposes the infrastructure. Establish stable site and endpoint identity, transfer ownership, save/load, and safe unload, teardown, and recovery. Keep interfaces usable for mines, shelters, inheritances, and multiple depths.
3. **Approve and verify Slice 3 custody.** [RFC-0002](RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md) has an accepted gameplay direction. Implementation still requires owner authorization. Validate the underground infrastructure before full individual custody implementation. Compare persistence options against real 1.7 saves before selecting a canonical storage mechanism. Prove identity through active/dormant transitions, capture, inventory, apertures, and travel. Resolve awakening debit safety before adding a second cohort consumer. A real surface-to-underground round trip is a Slice 3 acceptance gate.
4. **Sequence further gameplay after proof of custody.** Species-specific feeding, refinement, a player cultivation interface, abilities, regional ecology, and genesis policies need scoped designs. Autonomous cultivation, clan economies and politics, and large emergent-society playtests are longer-term directions with no committed release numbering.

## Verification gates

For code or Def changes, run relevant structural checks and full compilation against actual 1.7 game assemblies when available. For save or gameplay changes, use focused in-game tests and backed-up regression saves. Record the build, environment, cases and outcome. Separate owner-reported evidence from automated and independently observed results. Cloud checks and harnesses with Verse stubs leave the game-linked gate open. The owner controls gameplay acceptance and any merge into main. Claude review is optional for high-risk architectural choices.
