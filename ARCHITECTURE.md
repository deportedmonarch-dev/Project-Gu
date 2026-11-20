# Project GU architecture

This document describes the implementation on `feature/gu-framework-0.2.5`. See the [roadmap](docs/ROADMAP.md) for verification and sequencing and the [project vision](docs/PROJECT-VISION.md) for design requirements.

## Implemented subsystems and save ownership

| Subsystem | Source and authority |
|---|---|
| Mortal status and awakening | [GuWorldComponent](Source/ProjectGu/GuWorldComponent.cs) is a GameComponent that deep-saves `guPawnRecords`. Records link to pawns via Scribe references and own latent aptitude and awakening status. The lookup is rebuilt after loading. Loaded-map pawn discovery runs every 600 ticks. The [awakening job](Source/ProjectGu/Jobs/JobDriver_GuAwakenAtSpring.cs) reserves its spring and invokes the service in the final toil. |
| Aperture | [Hediff_Aperture](Source/ProjectGu/Hediff_Aperture.cs) owns its aptitude, rank, essence, and physique fields. The component's pawn records and the Hediff serve distinct roles. Aperture Gu custody is proposed. |
| Species definitions | [GuSpeciesDef](Source/ProjectGu/Domain/GuSpeciesDef.cs) and [XML Defs](Defs/GuSpeciesDefs/GU_GuSpecies.xml) supply rank ranges and descriptive tags through `DefDatabase`. The catalog contains Hope Gu, Moonlight Gu, and a developer test species. |
| Spring ecology and production | Each [spring ThingComp](Source/ProjectGu/Springs/CompGuSpiritSpring.cs) owns reserve, production and habitat fields, a persistent habitat ID, and the deep-saved `guPopulationRecords025` list. The [ledger](Source/ProjectGu/Springs/GuPopulationLedger.cs) operates on the spring's list. Legacy population scalars are migration inputs or derived shadows. Species keys preserve unresolved add-on counts. Current gameplay updates the Hope Gu cohort only. The spring produces haulable stones from its own reserve. |
| Map ecology | The [map component](Source/ProjectGu/Springs/GuEcologyMapComponent.cs) holds transient spring subscriptions. Flora spawn/destruction notifications and periodic local recounts refresh support. The map component owns no saved populations. |

The save-integrity follow-up preserves recoverable cohort data and disables mutation for ambiguous or invalid versioned lists. Its real save round-trips remain pending. See the [implementation journal](docs/IMPLEMENTATION-0.2.5.md).

## API evidence and open risks

- The player-tested 0.2.4 hotfix exercises a baseline using `ThingComp.PostSpawnSetup(bool)`, `PostDestroy(DestroyMode, Map)`, `PostExposeData()`, and `CompTickRare()`. The prior `PostDeSpawn(Map)` override failed in that target. Check signatures again on the game build used for a new change. See the [compatibility policy](docs/COMPATIBILITY-1.7.md) and [baseline release](docs/RELEASE-0.2.4.md).
- The 0.2.5 branch uses deep Scribe for cohorts and runs migration in post-load/spawn initialization. Player reports cover Slice 2 gameplay. The current branch's game-linked build, detailed callback order, missing-versus-empty collection handling, and held Thing round-trips still need direct evidence.
- Awakening commits pawn state before checking whether Hope Gu consumption succeeded. Regress both completed natural outcomes and internal failures before adding another consumer. See [GuAwakeningService](Source/ProjectGu/GuAwakeningService.cs).
- Non-destructive spring despawn can leave a subscription behind. A stale subscription can receive a flora notification after cross-map respawn. Habitat ID collision diagnostics are map-local. Test map removal and transfers before using habitat IDs as cross-map endpoints. See [GuEcologyMapComponent](Source/ProjectGu/Springs/GuEcologyMapComponent.cs).

## Proposed boundaries

[RFC-0002](docs/RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md) compares record-, Pawn-, and Thing-owned persistence. Record-backed identity with physical projections is a candidate. Scribe, world Pawn retention, ThingOwner, projection replacement, aperture loss, and map transfer probes must establish a viable authority model. Individual Gu, aperture and host custody, ability orchestration, and underground maps have no implementation on this branch.
