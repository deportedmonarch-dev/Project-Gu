# Project GU architecture

This document describes the framework and the developer-only implementation on the underground spring development branches. See the [roadmap](docs/ROADMAP.md) for verification and sequencing and the [project vision](docs/PROJECT-VISION.md) for design requirements.

## Implemented subsystems and save ownership

| Subsystem | Source and authority |
|---|---|
| Mortal status and awakening | [GuWorldComponent](Source/ProjectGu/GuWorldComponent.cs) is a GameComponent that deep-saves `guPawnRecords`. Records link to pawns via Scribe references and own latent aptitude and awakening status. The lookup is rebuilt after loading. Loaded-map pawn discovery runs every 600 ticks. The [awakening job](Source/ProjectGu/Jobs/JobDriver_GuAwakenAtSpring.cs) reserves its spring and invokes the service in the final toil. |
| Aperture | [Hediff_Aperture](Source/ProjectGu/Hediff_Aperture.cs) owns its aptitude, rank, essence, and physique fields. The component's pawn records and the Hediff serve distinct roles. Aperture Gu custody is proposed. |
| Species definitions | [GuSpeciesDef](Source/ProjectGu/Domain/GuSpeciesDef.cs) and [XML Defs](Defs/GuSpeciesDefs/GU_GuSpecies.xml) supply rank ranges and descriptive tags through `DefDatabase`. The catalog contains Hope Gu, Moonlight Gu, and a developer test species. |
| Spring ecology and production | Each [spring ThingComp](Source/ProjectGu/Springs/CompGuSpiritSpring.cs) owns reserve, production and habitat fields, a persistent habitat ID, and the deep-saved `guPopulationRecords025` list. The [ledger](Source/ProjectGu/Springs/GuPopulationLedger.cs) operates on the spring's list. Legacy population scalars are migration inputs or derived shadows. Species keys preserve unresolved add-on counts. Current gameplay updates the Hope Gu cohort only. The spring produces haulable stones from its own reserve. |
| Map ecology | The [map component](Source/ProjectGu/Springs/GuEcologyMapComponent.cs) holds transient spring subscriptions and attributes each physical patch to one eligible spring. Native flora spawn and despawn invalidate local counts, with a bounded fallback recount. Each newly generated cavern spring keeps its own [growth schedule](Source/ProjectGu/Springs/CompGuSpiritSpring.Flora.cs) and computes support from the flora Things. |
| Underground prototype | The [pocket registry](Source/ProjectGu/Underground/GuPocketProbeComponent.cs) saves site IDs, level IDs and engine map and passage references under `guPocketProbeSitesV1`. RimWorld owns maps and pawns. The [spring production operation](Source/ProjectGu/Springs/CompGuSpiritSpring.Production.cs) handles scheduled and debug production from the existing spring's saved reserve. The [source removal audit](docs/UNDERGROUND-MAP-SAFETY.md) describes the open gameplay integration gate. |

The save-integrity follow-up preserves recoverable cohort data and disables mutation for ambiguous or invalid versioned lists. Its real save round-trips remain pending. See the [implementation journal](docs/IMPLEMENTATION-0.2.5.md).

## API evidence and open risks

- The player-tested 0.2.4 hotfix exercises a baseline using `ThingComp.PostSpawnSetup(bool)`, `PostDestroy(DestroyMode, Map)`, `PostExposeData()`, and `CompTickRare()`. The prior `PostDeSpawn(Map)` override failed in that target. Check signatures again on the game build used for a new change. See the [compatibility policy](docs/COMPATIBILITY-1.7.md) and [baseline release](docs/RELEASE-0.2.4.md).
- The 0.2.5 branch uses deep Scribe for cohorts and runs migration in post-load/spawn initialization. Player reports cover Slice 2 gameplay. The current branch compiles against the installed game, and isolated Scribe fixtures cover legacy cohort migration and saved Things. Full-colony round-trips and held Thing lifecycle checks still need live evidence.
- Awakening commits pawn state before checking whether Hope Gu consumption succeeded. Regress both completed natural outcomes and internal failures before adding another consumer. See [GuAwakeningService](Source/ProjectGu/GuAwakeningService.cs).
- Installed 1.7.5014 exposes `ThingComp.PostDeSpawn(Map, DestroyMode)`, which deregisters springs and invalidates flora support for removals. Habitat ID collision diagnostics are map-local. Full map removal and transfers still require live validation before habitat IDs become cross-map endpoints. See [GuEcologyMapComponent](Source/ProjectGu/Springs/GuEcologyMapComponent.cs).

## Proposed boundaries

[RFC-0002](docs/RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md) compares record-, Pawn-, and Thing-owned persistence. Record-backed identity with physical projections is a candidate. Scribe, world Pawn retention, ThingOwner, projection replacement, aperture loss, and map transfer probes must establish a viable authority model. Individual Gu, aperture and host custody, and ability orchestration remain proposed.
