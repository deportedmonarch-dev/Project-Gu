# Project GU roadmap

Target RimWorld 1.7 Waylith / .NET 10. See [project vision](PROJECT-VISION.md) for design requirements and [architecture](../ARCHITECTURE.md) for current save ownership and open defects.

## Delivered milestones

| Milestone | Implementation | Verification |
|---|---|---|
| Prototype 0.1–0.2.4 | Aptitude, mortal status, apertures, awakening, spirit springs, haulable primeval stones, and finite-reserve Hope Gu ecology. | The owner reported 10/10 in-game checks for the 0.2.4 compile-hotfix. |
| 0.2.5 Slice 1 | XML species catalog and validation. | The owner reported 12/12 in-game checks after the fully qualified Def XML fix. |
| 0.2.5 Slice 2 | Species-keyed spring cohorts, legacy migration, habitat IDs, and a second developer test cohort. | The owner reported 10/10 in-game checks. The test plan lists additional cases. Explicit build output for the slice remains pending. |
| Slice 2 save-integrity follow-up | Handles ambiguous cohorts, preserves unresolved species data, and prevents population overflow. | Local Python and PowerShell structural checks and isolated stub-based arithmetic tests passed. Actual game-linked compilation and focused game round-trips remain pending. |
| Underground spring ecology and visual overhaul | Expanded generation, rope passages, physical flora with bounded regrowth, and native primeval-stone stacking. | Windows RimWorld 1.7.5014 rev1195 build and managed tests passed, including 5,760 layout and flora seeds. Separate live ecology and visual acceptance remains open. See [spring verification](UNDERGROUND-SPIRIT-SPRING.md#verification-and-release-limits). |
| Priority 0 underground ownership | An independent engine parent for each new spring level, a saved route graph, native pocket compatibility, and occupant recovery. | Alpha technical acceptance passed on RimWorld 1.7.5014 rev1195. See the [live evidence and remaining compatibility checks](UNDERGROUND-INDEPENDENT-LEVELS.md). |
| 0.2.5 Phase A | Ideology colony reset retains and seals an independent cavern with physical resources when its surface colony is removed. | The rendered native reset and fresh-process reload retained the original cavern and a single 137-stone stack. See [underground lifecycle evidence](UNDERGROUND-INDEPENDENT-LEVELS.md). |
| 0.2.5 Phase B | Completed natural awakening consumes one mature Hope Gu. The service rolls back provisional aperture creation on abort and guards against reentry. | A focused Core native game fixture verified both outcomes, callback failure, depletion, repeat attempts, and fresh-process reload. See the [awakening verification](IMPLEMENTATION-0.2.5.md#phase-b-awakening-conservation). |
| 0.2.5 Phase C | Select native Plant Defs for new flora, retain saved Building Defs, and use terrain tags for Qi-rich ground sowing. | The [botanical decision](BOTANICAL-FOUNDATION.md) records the installed 1.7 assembly and Anima Grass Def evidence. Live growth, sowing, and appearance belong to Phase D. |
| 0.2.5 Phase D | New caverns grow native Spirit Grass fields and Moon Orchids on primeval qi-rich soil. The spring credits the botanical field at seven support and capacity 17. | Core native sowing, damage recovery, generation retry and reload passed. Both profiles passed 5,760 seeded layouts. See [botanical verification and owner checks](UNDERGROUND-SPIRIT-SPRING.md#verification-and-release-limits). |

Evidence and test plans are recorded in the [0.2.4 release](RELEASE-0.2.4.md), [0.2.5 journal](IMPLEMENTATION-0.2.5.md), and [Slice 2 test plan](TEST-SLICE-0.2.5-2.md).

## Priorities and dependencies

1. **Complete the live save baseline.** The current spring branch builds and passes isolated fixtures against actual RimWorld 1.7 assemblies. Verify intentionally empty lists, unresolved species, old-save migration and repeated reloads in a full colony. Native awakening verification covers a depleted Hope Gu cohort after reload.
2. **Retain the Priority 0 lifecycle baseline.** Use the [lifecycle matrix](UNDERGROUND-INDEPENDENT-LEVELS.md) when extending the independent map and route model to future site kinds. Generated independent levels stay loaded, with an eight-level cap. Map dormancy is deferred.
3. **Approve and verify Slice 3 custody.** [RFC-0002](RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md) has an accepted gameplay direction. Implementation still requires owner authorization. Validate the underground infrastructure before full individual custody implementation. Compare persistence options against real 1.7 saves before selecting a canonical storage mechanism. Prove identity through active/dormant transitions, capture, inventory, apertures, and travel. A real surface-to-underground round trip is a Slice 3 acceptance gate.
4. **Sequence further gameplay after proof of custody.** Species-specific feeding, refinement, a player cultivation interface, abilities, regional ecology, and genesis policies need scoped designs. Autonomous cultivation, clan economies and politics, and large emergent-society playtests are longer-term directions with no committed release numbering.

## Verification gates

For code or Def changes, run relevant structural checks and full compilation against actual 1.7 game assemblies when available. For save or gameplay changes, use focused in-game tests and backed-up regression saves. Record the build, environment, cases and outcome. Separate owner-reported evidence from automated and independently observed results. Cloud checks and harnesses with Verse stubs leave the game-linked gate open. The owner controls gameplay acceptance and any merge into main. Claude review is optional for high-risk architectural choices.
