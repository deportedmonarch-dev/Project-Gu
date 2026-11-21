# Project GU roadmap

Target RimWorld 1.7 Waylith / .NET 10. See [project vision](PROJECT-VISION.md) for design requirements and [architecture](../ARCHITECTURE.md) for current save ownership and open defects.

## Delivered milestones

| Milestone | Implementation | Verification |
|---|---|---|
| Prototype 0.1–0.2.4 | Aptitude, mortal status, apertures, awakening, spirit springs, haulable primeval stones, and finite-reserve Hope Gu ecology. | The owner reported 10/10 in-game checks for the 0.2.4 compile-hotfix. |
| 0.2.5 Slice 1 | XML species catalog and validation. | The owner reported 12/12 in-game checks after the fully qualified Def XML fix. |
| 0.2.5 Slice 2 | Species-keyed spring cohorts, legacy migration, habitat IDs, and a second developer test cohort. | The owner reported 10/10 in-game checks. The test plan lists additional cases. Explicit build output for the slice remains pending. |
| Slice 2 save-integrity follow-up | Handles ambiguous cohorts, preserves unresolved species data, and prevents population overflow. | Structural and managed migration checks passed. Phase F covers native Hope Gu persistence in the historical cavern saves. |
| Underground spring ecology and visual overhaul | Expanded generation, rope passages, physical flora with bounded regrowth, and native primeval-stone stacking. | Windows RimWorld 1.7.5014 rev1195 build and managed tests passed, including 5,760 layout and flora seeds. Separate live ecology and visual acceptance remains open. See [spring verification](UNDERGROUND-SPIRIT-SPRING.md#verification-and-release-limits). |
| Priority 0 underground ownership | An independent engine parent for each new spring level, a saved route graph, native pocket compatibility, and occupant recovery. | Alpha technical acceptance passed on RimWorld 1.7.5014 rev1195. See the [live evidence and remaining compatibility checks](UNDERGROUND-INDEPENDENT-LEVELS.md). |
| 0.2.5 Phase A | Ideology colony reset retains and seals an independent cavern with physical resources when its surface colony is removed. | The rendered native reset and fresh-process reload retained the original cavern and a single 137-stone stack. See [underground lifecycle evidence](UNDERGROUND-INDEPENDENT-LEVELS.md). |
| 0.2.5 Phase B | Completed natural awakening consumes one mature Hope Gu. The service rolls back provisional aperture creation on abort and guards against reentry. | A focused Core native game fixture verified both outcomes, callback failure, depletion, repeat attempts, and fresh-process reload. See the [awakening verification](IMPLEMENTATION-0.2.5.md#phase-b-awakening-conservation). |
| 0.2.5 Phase C | Select native Plant Defs for new flora, retain saved Building Defs, and use terrain tags for Qi-rich ground sowing. | The [botanical decision](BOTANICAL-FOUNDATION.md) records the installed 1.7 assembly and Anima Grass Def evidence. Live growth, sowing, and appearance belong to Phase D. |
| 0.2.5 Phase D | New caverns grow native Spirit Grass fields and Moon Orchids on primeval qi-rich soil. The spring credits the botanical field at seven support and capacity 17. | Core native sowing, damage recovery, generation retry and reload passed. Both profiles passed 5,760 seeded layouts. See [botanical verification and owner checks](UNDERGROUND-SPIRIT-SPRING.md#verification-and-release-limits). |
| 0.2.5 Phase E | Player-controlled automatic hauling uses real stacks, native Pawn inventory and surface storage on the independent spring route. | The disposable Core fixture delivered 66 stones across successive shipments and partial storage, including a reload with packed cargo. [Player controls and verification](UNDERGROUND-SPIRIT-SPRING.md#automatic-hauling). |
| 0.2.5 spring visual revision | Mossy Qi-rich floor, basin-centered grass, open cultivation pad, layered ecosystem lighting, a natural seep sprite, and real southern water outputs. | Seeded and native Core checks passed. The owner reviewed the updated cavern. [Spring visual verification](UNDERGROUND-SPIRIT-SPRING.md#verification-and-release-limits). |
| 0.2.5 Phase F | Focused regression of underground travel and lifecycle, cargo, plants, spring production, cohorts and awakening. | 14 native stages passed on 1.7.5014, including the historical 31×31 and 39×39 saves. [Technical results and remaining owner checks](IMPLEMENTATION-0.2.5.md#phase-f-focused-regression). |
| 0.2.5 Slice 3B-1 | Versioned individual registry, exact wild Pawn binding, quarantine and Development Mode controls. | Native creation, fresh-process reload, ten quarantine cases and both historical saves passed. [Slice 3B-1 verification](IMPLEMENTATION-0.2.5.md#slice-3b-1--individual-identity-and-wild-pawn-binding). |
| 0.2.5 Slice 3B-2 | Development wild-to-dormant custody transition with reversible staging and guarded native retirement. | Actual Gu Pawn retirement, blocked and interrupted placement, cleanup quarantine, fresh-process reload and existing passage travel passed. [Slice 3B-2 verification](IMPLEMENTATION-0.2.5.md#slice-3b-2--wild-gu-dormancy). |
| 0.2.5 Slice 3B-3A | Development pickup and drop bind the same dormant Thing to the ground or the exact native colonist inventory. | Native rollback, save reentry, stale requests, fresh-process reload and passage travel passed. [Slice 3B-3A verification](IMPLEMENTATION-0.2.5.md#slice-3b-3a--physical-gu-inventory-custody). |

Evidence and test plans are recorded in the [0.2.4 release](RELEASE-0.2.4.md), [0.2.5 journal](IMPLEMENTATION-0.2.5.md), and [Slice 2 test plan](TEST-SLICE-0.2.5-2.md).

## Priorities and dependencies

Discretionary visual work is frozen following the owner's cavern review. Current development priorities are functional systems and persistence.

1. **Complete the live save baseline.** The current spring branch builds and passes isolated fixtures against actual RimWorld 1.7 assemblies. Verify intentionally empty lists, unresolved species, old-save migration and repeated reloads in a full colony. Native awakening verification covers a depleted Hope Gu cohort after reload.
2. **Retain the Priority 0 lifecycle baseline.** Use the [lifecycle matrix](UNDERGROUND-INDEPENDENT-LEVELS.md) when extending the independent map and route model to future site kinds. Generated independent levels stay loaded, with an eight-level cap. Map dormancy is deferred.
3. **Prove exact aperture custody in Slice 3B-3B.** Build from the verified inventory binding. Test one-slot deposit and withdrawal against the exact host and aperture through reload, passage travel and aperture loss. The subsequent [Slice 3B plan](RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md#slice-3b-implementation-plan) covers active release and a checked species-keyed cohort debit.
4. **Sequence further gameplay after proof of custody.** Species-specific feeding, refinement, a player cultivation interface, abilities, regional ecology, and genesis policies need scoped designs. Autonomous cultivation, clan economies and politics, and large emergent-society playtests are longer-term directions with no committed release numbering.

## Verification gates

For code or Def changes, run relevant structural checks and full compilation against actual 1.7 game assemblies when available. For save or gameplay changes, use focused in-game tests and backed-up regression saves. Record the build, environment, cases and outcome. Separate owner-reported evidence from automated and independently observed results. Cloud checks and harnesses with Verse stubs leave the game-linked gate open. The owner controls gameplay acceptance and any merge into main. Claude review is optional for high-risk architectural choices.
