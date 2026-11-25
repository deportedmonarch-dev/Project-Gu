# Playable Alpha 3 — Liquor Worm

Alpha 3 adds a Rank 1 Liquor Worm on top of the accepted Rank 1 cultivation foundation `36721b4` (`feature/rank1-cultivation-foundation`, which contains Alpha 2 `a87c536`). A Gu Master lures a wild Liquor Worm with beer, refines it, stores it in their aperture, and channels **Liquor Worm refinement**. Each pulse turns essence of their current minor stage into the same volume of the next higher quality, using the foundation's `TryUpgradeEssence`. While the worm works, a small manifested Liquor Worm circles the cultivator's waist.

## Play it

1. Start a new **Southern Border Clan** game. The clan now starts with **4 beer**, and every newly generated spirit spring holds **2 mature Liquor Worms** beside its Hope Gu and Moonlight Gu. Awaken a disciple as in Alpha 1.
2. Carry beer to the spring. Use vanilla **Pick up** to put a bottle in a colonist's inventory before descending, or drop beer in the cavern. Right-click the spring with that colonist and choose **Lure Liquor Worm with beer**. The colonist sets the beer out and waits 10 seconds (600 ticks). A wild Liquor Worm then crawls out and the beer is consumed. Interrupting the wait keeps the beer. Without beer the option is listed but unavailable. Liquor Worms never appear through a plain **Search**.
3. **Capture Liquor Worm (unrefined)**, **Pick up dormant Liquor Worm**, then **Manage aperture Gu → Enforce will over Liquor Worm**. Rank 1 costs 10 essence and 7,000 base work ticks, scaled by aptitude as for every Gu. Then **Store Liquor Worm**. It can share the two-slot aperture with a Moonlight Gu.
4. Select the Gu Master and click **Liquor Worm refinement**. It requires an undrafted, conscious cultivator below Rank 1 Peak with essence of their current stage. The tooltip shows the full cultivation status.
5. A new session pays **2 essence** once, starts a **half-day cooldown** (30,000 ticks) and runs **10 pulses, one every 250 ticks** (about one in-game hour). Each pulse converts up to **1 raw essence** of the current stage into the next quality. The progress bar and the job report show the pulse count.
6. Drafting, ordering another job (including **Moonblade**), unconsciousness or withdrawing the Gu stops the session at once. Completed pulses are kept. The command then reads **Resume Liquor Worm refinement (n/10)**. Resuming is free and ignores the cooldown, as long as the same Gu is back in the same cultivator's aperture at the same stage.
7. Use the refined essence with **Nourish aperture walls**. Wall nourishment spends the highest quality first, so each refined Middle unit provides 2 nourishment instead of 1.

## Conversion rules

| Rule | Implementation |
| --- | --- |
| Correct Gu and cultivator | The same registry authorization as Moonblade: the exact refined individual, in the live aperture of the awakened cultivator it recognizes. `GuChannelService.StillAuthorized` re-checks it every channel tick. |
| Aperture storage required | A Gu in inventory, on the ground, wild, withdrawn or bonded to someone else grants nothing. |
| Same total raw volume | Each pulse calls `aperture.TryUpgradeEssence(source, amount)` once. That call is all-or-nothing and volume-preserving. |
| Stage unchanged | Conversion never touches stage or wall progress. Advancing a stage is still the player's explicit action. |
| No repeated upgrading | A session is bound to the stage it began in. `TryUpgradeEssence` only accepts the current stage as its source, so refined essence can't be refined again until the cultivator's own stage reaches that quality. |
| No Rank 2 quality, stop at Peak | At Upper, pulses produce Peak essence. At Peak the command is unavailable ("Rank 1 Peak essence has no higher Rank 1 quality to become"). |
| Bounded per pulse | Up to `amountPerPulse` (1.0), or whatever remains of the source quality. A session ends early when the source runs out or the stage changes. |
| Interruption keeps progress | Pulses are applied synchronously and recorded in the saved session in the same tick. A stop before the next pulse loses only the partial pulse timer, which resuming restores exactly. |
| Save/load | The job driver saves its session ID. The ledger saves the session (pulses done, ticks into the current pulse, refined total, payment, binding, active flag) and the cooldown. Reload resumes the same tick count, and nothing is charged or converted twice. |
| Activation cost | 2 raw essence, paid once per new session through `TrySpendEssence`, lowest quality first. |

There is no second essence pool. The aperture remains the only authority for balances, capacity, stage and wall nourishment.

## Architecture

- **Data:** `GuAbilityDef.channel` (`GuChannelProperties`) turns an ability into a self-use timed channel with a worker class, pulse interval, pulse count, amount per pulse and an optional manifestation profile. `GU_LiquorWormRefinement` is the first one.
- **Worker:** `GuChannelWorker_EssenceRefinement` holds the Liquor Worm rules: it can start below Peak, it is bound to the current stage, and each pulse makes one `TryUpgradeEssence` call. Workers change only state that already has an authority.
- **Service and job:** `GuChannelService` handles eligibility, resume detection, ordering and the one-tick commit. `JobDriver_GuChannelAbility` commits or resumes, re-authorizes each tick, applies pulses and suspends on any stop.
- **Ledger:** `GuCombatLedger` keeps at most one `GuChannelSession` per Gu ID, and its maintenance suspends any session no live job is driving.
- **Acquisition:** `GuSpeciesDef.encounterLures` and `encounterLureTicks`, the spring's lure option (`CompGuSpiritSpring.Lure.cs`) and `JobDriver_GuLureWildGu`. The individual comes from the spring cohort through the existing materialization, so identity and population stay conserved. The plain search and the lure now share `CompGuSpiritSpring.TryEncounterWildGu`.

### Reusable pawn-attached orbit

The existing `GuFxMapComponent` gained **sustained, keyed instances**. `TryAttachSustained(map, profile, pawn, key, keepAlive)` never duplicates a key. An instance ends the tick its keep-alive check fails or its anchor despawns or changes map. `Detach(key)` removes it immediately. Disabling effects retires every instance. `GuFxProfileDef` gained orbit fields: elliptical radii, centre offset, period, direction, near and far altitude in pawn render-layer units, and far-side scale. `OrbitAt(age)` is a pure function of the tick age, so the animation follows game time and stops when paused.

- **Front/back layering:** RimWorld draws pawn layers −10..100 at `DrawPos.y` + layer × 0.000366 (`PawnRenderUtility.AltitudeForLayer`). The far half of the orbit draws at layer −12.5, below the lowest pawn layer and the carried-item layer. The near half draws at layer 100.6, above the highest pawn layer and still below the next altitude band. The orbit's ground-plane z offset decides near versus far.
- **Derived from gameplay:** the channel job attaches the manifestation from its live session, retrying every 30 ticks, and only while that job is the pawn's current job and the session is active. A reload therefore rebuilds it from the saved session; nothing visual is saved. With effects off, no instance exists and the channel behaves identically.

A future Little Light Gu or aura Gu needs only a new `GuFxProfileDef` with `orbit` set and a caller that attaches it with a keep-alive derived from its own state.

## Art handoff

Replace any PNG in place, keeping its path. No C# change is needed. All images use 32-bit RGBA with straight (non-premultiplied) alpha and transparent padding.

### 1. World Liquor Worm sprites

| File (`Textures/ProjectGU/…`) | Placeholder size | Format | In game |
| --- | --- | --- | --- |
| `Gu/LiquorWorm/Wild_north.png` | 64×64 | `Graphic_Multi`, seen from behind with the head pointing up (north) | Wild pawn, drawn 0.8×0.8 cells (`drawSize` in `Defs/ThingDefs/GU_LiquorWorm.xml`) |
| `Gu/LiquorWorm/Wild_east.png` | 64×64 | Side view, head to the right; west is mirrored automatically | Same |
| `Gu/LiquorWorm/Wild_south.png` | 64×64 | Head toward the viewer (down) | Same |
| `Gu/LiquorWorm/Dormant.png` | 64×64 | `Graphic_Single`, curled worm, item art | Dormant item, standard 1-cell item |
| `UI/Abilities/LiquorWormRefinement.png` | 64×64 | Square command icon | Command button |

Any square resolution works (128×128 is a good target). World size comes from `drawSize`, not from pixels. Keep the worm centred with a little padding so the north, east and south views share one pivot.

### 2. Manifested Liquor Worm (small orbiting sprite)

| Property | Value |
| --- | --- |
| File | `Textures/ProjectGU/Effects/LiquorWorm/Manifest.png` |
| Placeholder | 256×64: **4 frames of 64×64 in one row** (4 columns × 1 row). Frame 0 is the left cell. |
| Orientation | **Head pointing up** (toward the top of each cell) in its direction of travel. The game rotates the sprite to follow the orbit tangent. Pivot is each cell's centre. |
| Animation | Loops at 6 ticks per frame (24-tick cycle, 0.4 s at speed 1). Frames follow game ticks and freeze when paused. |
| In-game size | 0.42 × 0.42 cells (about 30 px at the closest zoom). The far side of the orbit is drawn at 85%. |
| Orbit | Ellipse with radii 0.40 × 0.15 cells, centred 0.18 cells below the pawn's centre (the waist). One revolution every 90 ticks, counter-clockwise seen from above. |
| Shader | `Transparent` (normal alpha blending, not additive glow). A soft amber halo should be painted into the sprite if wanted. |
| To change | Edit `GU_Fx_LiquorWormOrbit` in `Defs/GuAbilityDefs/GU_LiquorWormRefinement.xml`: `columns`, `rows`, `frameCount`, `ticksPerFrame`, `drawSize`, `orbitRadius`, `orbitCenterOffset`, `orbitPeriodTicks`, `orbitBackScale`, `shaderType`. Larger frame cells such as 128×128 work unchanged. |

The placeholders are original and are regenerated by `scripts/generate-moonlight-placeholder-art.ps1`.

## Verification

Environment: Windows 11, .NET SDK 10.0.401, RimWorld 1.7.5014 rev1195, Core and Harmony.

```powershell
.\scripts\verify.ps1 -Focused
.\scripts\verify-priority0-live.ps1 -Profile Alpha3,Alpha2,Alpha1,RankOne,Slice3B3B,Slice3B5A,ClanStart -Parallel 3
.\scripts\verify-priority0-live.ps1 -Profile Alpha3Visual -Parallel 1 -StageTimeoutSeconds 900   # opens a game window
```

| Kind | Result |
| --- | --- |
| Structural | `verify-gu-framework.ps1` checks the Liquor Worm species, lure, projections, the channel ability with its orbit profile, and every texture. |
| Real compilation | Release build against the installed 1.7 assemblies: 0 warnings, 0 errors. |
| Managed (real assemblies, no game process) | `LiquorWormManagedChecks` runs the real worker against a real aperture, advancing stages through the genuine nourishment path. It covers exact Initial→Middle conversion with volume, stage and walls unchanged; fractional remainders; stopping when the source is exhausted without re-refining; Middle wall weighting of refined essence; stage-binding stop; Upper→Peak; the Peak ceiling; a session Scribe round trip; and orbit geometry, layer altitudes, tangent heading and validation. The full `verify.ps1 -Focused` suite passes. |
| Native headless `Alpha3` | Campaign `f900e3c3349643c5b0921751ba97ad86` passed new, reload and final with zero red errors. Earlier passes, before the drafted-refusal assertion was added: `511e5e8ac5474f2ab07d3869b2dc1d89`, `adc6b4f7a33c499eace9ef2dbffe87f5`, `ed4261ecfbc74f699ae9312af6fe9626`. |
| Native regression | Campaign `e997958656f24e3cbb3d5d4cb031016b` (`-Parallel 3`): `Alpha3`, `Alpha2` (Moonlight Gu), `Alpha1` (Hope Gu), `RankOne`, `Slice3B3B` (two Gu in one aperture), `Slice3B5A` (multi-cohort) and `ClanStart` all passed. |
| Native rendered `Alpha3Visual` | Campaign `0827a94df09044ab8da6523466e6110e` passed all four stages; screenshots inspected (below). |

What the `Alpha3` stages verify, all with Development Mode off:

- **Acquisition:**
  - No plain search, and the lure is listed but unavailable without beer.
  - Vanilla **Pick up** carries beer in inventory.
  - An interrupted lure keeps every beer and draws nothing.
  - A completed lure debits exactly one Liquor Worm adult and exactly one beer.
- **Ownership:**
  - Capture, pickup and will-enforcement use the ordinary routes.
  - An unrefined Liquor Worm grants nothing, and neither does a refined one before storage.
  - A Moonlight Gu is acquired and stored in the same aperture. Each Gu grants its own command to the disciple and nothing to the elder.
  - Every storage point asserts the exact occupants with no physical projection, with diagnostics. The spring encounter follow-up records an unbound Moonlight Gu pawn found before the search and the successful Alpha 3 retry. [Native follow-up](UNDERGROUND-SPIRIT-SPRING.md#wild-gu-encounters).
- **Commitment:** a new session pays exactly 2 essence once, records one commitment and starts the cooldown.
- **Conversion:** each pulse moves exactly 1 Initial unit to Middle. An independent account of recovery matches to 0.0001, and stage and walls are unchanged.
- **Stops:**
  - Drafting, withdrawing the Gu, anesthetic unconsciousness and a Moonblade order each stop the channel at once and suspend the session.
  - Each removes the manifestation in the same tick, keeps completed pulses, and refines or charges nothing more over the next 400 ticks.
  - A drafted cultivator can't start or resume.
  - A withdrawn Gu can't be resumed. Resuming is free, ignores the cooldown and continues from the exact partial pulse.
- **Effects off:** identical conversion with no manifestation. Re-enabling attaches exactly one.
- **Save/load:** the save is made part way into the sixth pulse. The fresh-process reload restores the active job, the exact pulse and tick counts and both balances, and rebuilds the manifestation only from the live session. The remaining 5 pulses refine exactly 5 units with no new charge. Completion ends the job and removes the manifestation.
- **Wall nourishment:** one pulse spends one refined Middle unit for 2 nourishment.
- **Final stage:** the finished session, the unchanged cooldown, the refined essence and both Gu grants persist through a third process.

### Rendered inspection

The rendered stage is a Vulkan windowed run at 2560×1600. The disciple walked to the surface through the ordinary passage and channeled at the closest zoom.

- **Orbit:** one full orbit was stepped one tick per frame for each of the four facings (8 shots each).
- **Front/back layering:** RimWorld's real render order confirms it. On the far half of the orbit the worm passes behind the torso and hair, fully hidden at the back-most point. On the near half it crosses in front of the lower torso at the waist. It never covers the head and stays a small, readable silhouette.
- **Speeds:** 17, 44 and 131 ticks advanced over 44 frames at Normal, Fast and Superfast, so the animation follows game ticks.
- **Other shots:** default and far zoom; drafting removes the worm in the same frame; no worm with effects off while refinement continues; nothing remains after completion. Exactly one manifestation existed throughout, and 2 were spawned and 2 retired over the run.

The first two rendered runs drew no humanlike pawns because of the test-harness issue listed under open issues.

## Balance (provisional)

| Value | Setting | Location |
| --- | --- | --- |
| Activation cost | 2 raw essence per new session | `essenceCost` |
| Session | 10 pulses × 250 ticks (about 1 in-game hour) | `channel.pulseCount`, `channel.pulseIntervalTicks` |
| Conversion | Up to 1 raw unit per pulse, so at most 10 per session | `channel.amountPerPulse` |
| Cooldown | 30,000 ticks (half a day) from a new session; resuming is exempt | `cooldownTicks` |
| Lure | 1 beer, 600-tick wait, 2 Liquor Worms per new spring | `encounterLures`, `encounterLureTicks`, spring `initialCohorts` |
| Will-enforcement | 7,000 base ticks, 10 essence | species `refinement*` fields |

At Initial, a full session turns 10 Initial units (10 nourishment) into 10 Middle units (20 nourishment) for 2 essence and an hour. That roughly halves the essence needed for the 24-point Initial wall. At Middle and Upper the doubling continues (2→4, 4→8). Open questions for play:

- Whether one session per half-day is too generous next to the 24/80/224 walls.
- Whether conversion should scale with Gu rank or aptitude.
- Whether D-grade apertures, with a 20-unit cap and 2 per hour recovery, can usefully fill a whole session.
- Whether the 4 starting beers are too many or too few.

## Future feeding requirements (not implemented)

In the source fiction, a Liquor Worm must be fed alcohol to stay healthy, and starvation weakens or kills it. Alpha 3 deliberately adds no feeding dependency. A future Gu-feeding framework should cover:

- Per-individual nourishment state saved on the Gu record.
- Item consumption from aperture-adjacent inventory or stockpiles.
- Starvation outcomes that use the existing lifecycle (unresolved and terminal) rather than ad hoc deletion.
- How hunger gates or weakens the refinement channel.

The lure already establishes beer as Liquor Worm's preferred food.

## Open issues

- Existing saves keep their old springs, so only new games offer the lure. The clan's starting beer is a scenario change.
- The first rendered runs drew no humanlike pawns. The cause was the test harness running tens of thousands of ticks between rendered frames, which left RimWorld's pawn draw positions stale. The visual stage now clears its leftover cooldown through a test-only shortcut instead of waiting. A player cannot trigger this.
- The earlier intermittent custody observation from Alpha 2 (sole aperture occupant with no physical projection) is now checked with diagnostics at every storage point in Alpha 3. See the results above.
