# Project GU 0.2.5 — release readiness

**Status** — Prepared for owner review. **Not merged.** Merging into `main` requires explicit owner authorization.
**Branch** — `feature/spirit-spring-ecology-visual-overhaul`
**Target** — Windows RimWorld 1.7.5014 rev1195, Harmony 3.0.4, .NET SDK 10.0.401

## Summary

0.2.5 is a development milestone. It adds the species catalog, species-keyed spring cohorts, persistent independent underground spirit spring caverns with native flora, automatic underground hauling, natural awakening conservation, and the first individual Gu framework, Schema V1. Individual Gu can only be created through Development Mode actions.

> **Schema V1 is a provisional development foundation scheduled for reconstruction.** Its records load and save correctly, but its custody model quarantines individuals during ordinary gameplay events. [RFC-0004](RFC-0004-GU-FRAMEWORK-V2.md) proposes Schema V2 with migration of every V1 record. Do not build gameplay content on Schema V1.

## Contents

| Area | Delivered |
|---|---|
| Species | XML `GuSpeciesDef` catalog: Hope Gu, Moonlight Gu (declaration only) and a developer test species. |
| Cohorts | Species-keyed spring cohorts with revisions, legacy migration, ambiguous-list protection and overflow guards. |
| Underground | Independent engine-owned spring levels on a hidden address layer, saved route graph, lifecycle guards, emergency return, legacy pocket compatibility. |
| Ecology | Native Spirit Grass and Moon Orchids on primeval qi-rich soil, bounded regrowth, physical primeval stones, revised cavern visuals. |
| Hauling | Player-assigned automatic hauling of primeval stones through the passage. |
| Awakening | Completed awakening consumes one mature Hope Gu, with rollback on abort and reentry protection. |
| Individual Gu (Development Mode) | Versioned records, exact native binding, wild → dormant → inventory → aperture → released lifecycle, cohort materialization with event provenance, ordinary pickup and Gear drop. |

## Evidence

| Kind | Result |
|---|---|
| Static checks, game-linked build, managed tests | `scripts/verify.ps1 -Focused` passed on `8da9353` with zero warnings and errors. |
| Native fixtures (independently runnable) | 40 native stages passed at `0b414d3`. 16 stages passed at `fa4d05d`, including `VanillaGuInventory`. The `V2EngineProbe` characterization passed at `8da9353`. [Acceptance report](GU-FRAMEWORK-0.2.5-ACCEPTANCE.md) · [Journal](IMPLEMENTATION-0.2.5.md) |
| Historical saves | 31×31 and 39×39 copies loaded with unchanged caverns. Original hashes were preserved. |
| Owner reports | 12/12 Slice 1, 10/10 Slice 2, rendered controlled lifecycle pass, and **10/10 ordinary inventory acceptance** (below). |

### Ordinary inventory acceptance

- **Owner report.** The owner reported 10/10 for the rendered ordinary inventory checks.
- **Screenshot details, as relayed in the assignment.** Gu ID `c811b72d8fe34565b3a04cbb1f483f26`, species `GU_DevTestSpringGu`, Rank 1, revision 7, `DormantInventory` custody, no quarantine reported. The image file is not in the repository.
- **Automated evidence.** `VanillaGuInventory` independently reached inventory at revision 7 after a reload. Revision 7 in the screenshot is consistent with that sequence, but the screenshot comes from the owner's own colony and is not the fixture.

## Known limitations

1. **Schema V1 quarantines individuals during ordinary play.** `V2EngineProbe` verified this on the native engine. A Development Mode Gu in a colonist's inventory is permanently quarantined if the colonist is downed, killed, imprisoned, stripped or changes faction. A stored aperture Gu stays quarantined after its host is resurrected. A killed or destroyed Gu, a Gu on a removed map, and a Gu collected by world-pawn GC are all quarantined. Inspecting a mismatched Gu can trigger the quarantine. [Engine investigation](ENGINE-1.7-GU-V2-INVESTIGATION.md)
2. **Exposure is limited to Development Mode.** Ordinary players cannot create individual Gu in 0.2.5. Hope Gu ecology, awakening, springs and caverns do not use individual records.
3. **One Gu per aperture.** The Development Mode aperture transfer is a custody fixture, not refinement. Refinement does not exist.
4. **Anomaly** disables the dormancy, release and materialization actions.
5. **Eight independent underground levels** stay loaded and tick continuously. Map dormancy is not implemented.
6. **DLC and third-party coverage.** Individual Gu fixtures used Core and Harmony only. Underground lifecycle fixtures also covered Ideology and Odyssey.
7. **Downgrade is unsupported.** Do not load a 0.2.5 save in 0.2.4. A future V2 save will not load in 0.2.5.

## Outstanding manual acceptance

These owner checks are documented and not yet recorded as complete.

- Underground spring ecology and visual acceptance. [Checklist](UNDERGROUND-SPIRIT-SPRING.md#owner-acceptance)
- Disposable underground lifecycle checks. [Checklist](UNDERGROUND-INDEPENDENT-LEVELS.md#disposable-owner-acceptance)
- Rendered hauling assignment, pause and Work priority controls, and cancellation while waiting to awaken. [Phase F](IMPLEMENTATION-0.2.5.md#phase-f-focused-regression)
- Full-colony save baseline for empty cohort lists, unresolved species, old-save migration and repeated reloads. [Roadmap priority 1](ROADMAP.md#priorities-and-dependencies)

The owner may accept these as post-merge checks because none blocks the V2 work. That decision belongs to the owner.

## Save compatibility

- 0.2.4 saves load in 0.2.5. Legacy Hope Gu scalars and older caverns migrate on load.
- Save keys added in 0.2.5 include `guPopulationRecords025`, `guIndependentUndergroundSitesV1`, `guPocketProbeSitesV1` and `guIndividualRecordsV1`.
- Schema V2 will read `guIndividualRecordsV1` once and keep the V1 projection classes loadable. [RFC-0004 §12](RFC-0004-GU-FRAMEWORK-V2.md#12-migration-from-v1)

## Recommended merge strategy

`main` (`70b0211`, 0.2.4) is an ancestor of the release branch. So are `feature/gu-framework-0.2.5`, `feature/underground-pocket-probe` and `feature/underground-spirit-spring`.

1. Owner reviews this document and authorizes the merge.
2. Merge with `git merge --no-ff feature/spirit-spring-ecology-visual-overhaul` to keep a single 0.2.5 merge point in `main`'s history.
3. Tag the merge commit `v0.2.5`.
4. Run `scripts/verify.ps1 -Focused` on `main` after merging.
5. After confirming the tag, optionally delete the three ancestor branches locally and on `origin`.
6. Start V2 work on a new branch from `main` after gate G1. [Reconstruction plan](V2-RECONSTRUCTION-PLAN.md)

## Release checklist

- [x] Source committed and build recorded (`8da9353`, 1.7.5014 rev1195).
- [x] `scripts/verify.ps1` compiles against installed 1.7 assemblies.
- [x] Historical saves load from disposable copies with unchanged hashes.
- [x] `About.xml` advertises 1.7 only.
- [x] Build outputs and game DLLs are excluded from Git.
- [ ] Owner merge authorization.
- [ ] `v0.2.5` tag.
- [ ] Owner decision on the outstanding manual checks.
