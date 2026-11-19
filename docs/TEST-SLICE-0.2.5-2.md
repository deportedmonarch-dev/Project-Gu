# Slice 2 — Species-independent populations: implementation and player test plan

**Status:** Committed on feature branch. **NOT YET COMPILED against user's RimWorld 1.7 game; NOT GAMEPLAY-VALIDATED.**  
**Base:** Slice 1 player-validated 12/12; main remains the 0.2.4 player-validated baseline.

## What was implemented

- `GuPopulationRecord`: one serializable, habitat-owned cohort per species Def name; mature/juvenile counts, next ecology tick, bounded maturation/birth/consumption operations.
- `GuPopulationLedger`: species-neutral lookup and mutation, duplicate species checking; no global population owner or magic Gu-name switch in ledger.
- `CompGuSpiritSpring` now stores `guPopulationRecords025` deep-saved list, `guPopulationSchema025` marker, `guHabitatId025` ID. All legacy save keys remain readable.
- Versioned migration: on the first old-save load, imported adult count is the old 0.2.4 `guMatureHope024` if non-negative, else the 0.2.3 `guHopeGuAvailable`. Juveniles, ecology schedule, stone reserve/production and habitat fields are maintained. An authoritative new empty list is not repopulated.
- Hope Gu remains spring-centered; the exact 0.2.4 reproduction, maturation and ceremony count are maintained. Generic population custody does *not* imply Gu require habitat objects; free-roaming/world cohorts will have other custodians in later slices.
- Two independently keyed cohorts can coexist on one spring. `GU_DevTestSpringGu` is a **developer-only**, nonspawning XML test species; a dev gizmo seeds 2 mature / 1 juvenile of this species once. It cannot be collected, awakened with, or autonomously reproduced.
- Distinct springs have distinct persisted IDs; map registry checks duplicate IDs but never merges cohorts. Natural random appearance, simulated dao-mark density, capture, Gu individuality, and environmental refinement **are not in this slice**.
- XML validation now permits wild Gu without a mandatory habitat tag (soft preference/independent roaming design).

## Build: Windows RimWorld 1.7

Close game, pull `feature/gu-framework-0.2.5`, then:

```powershell
git switch feature/gu-framework-0.2.5
git pull
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\verify.ps1
.\scripts\install.ps1
```

**STOP if verification or compilation fails.** The compiled DLL is generated locally and intentionally excluded from Git.

## Acceptance tests

Use an untouched backup of the 0.2.4 save, plus a fresh test game. Do not overwrite the original source save.

1. **Compilation:** `verify.ps1` passes XML, new cohort structural checks, and a .NET build against the installed game.
2. **Startup:** no Project GU red errors. Pay attention to Scribe errors, `GuPopulationRecord` deep loading, Def resolution and unknown fields.
3. **Fresh spring:** 3 mature, 0 juvenile Hope Gu, baseline capacity 3. Inspect reserve/purity/stones are unchanged.
4. **Flora:** one Moon Orchid raises capacity to 5; adding Spirit Grass raises it to 9; removing each reverses immediately.
5. **Development day:** with habitat, a juvenile appears; next step matures juveniles. No reproduction without habitat.
6. **Secondary species:** select spring and press **GU: seed secondary test cohort** once. Inspect shows **Dev test Gu: 2 mature + 1 juvenile**. Pressing twice should not duplicate.
7. **Species isolation:** after adding test Gu, use **GU: simulate ecology day**. Hope Gu progresses normally; developer cohort stays 2 mature + 1 juvenile. Awakening only consumes mature Hope Gu.
8. **Two springs:** one spring with the test cohort and one without; changes to one spring do not affect the other.
9. **Save/load new game:** save, exit to menu, reload; confirm both species' counts, habitat, reserve, and the new spring's identity-related registry behavior. Repeat two save/reload cycles; no duplication or reset.
10. **Legacy 0.2.4 migration:** load backed-up 0.2.4 save (including one spring with depleted adults), compare before/after Hope adults and juveniles. Empty=0 stays 0, not reset to 3. Save as a **new** file and reload twice.
11. **Awakening:** completed ceremony consumes exactly one mature Hope Gu, no dev test Gu; an interrupted ceremony consumes none. Failed natural awakening still consumes one Hope Gu. If an operation fails internally it should not consume (the existing service still has an outstanding atomicity issue; report anomalies).
12. **Stones and reserve:** one real production interval produces a haulable stone and decrements reserve; new ecology fields survive reload.
13. **Habitat removal:** population is not clipped even when cap falls below stored adult count; reproduction ceases without flora.
14. **Performance and errors:** 3× speed, multiple springs, save/load and dev-log inspection; no new Project GU errors or notable degradation.

## Explicit known limitations / engineering follow-up

- **Awakening atomicity risk is still open**: the legacy service invokes `TryAwaken` before `TryConsumeHopeGu`, without handling a consume failure. This is deliberately not silently refactored as part of storage migration; fix in the dedicated transaction slice and regression-test.
- The developer secondary cohort can be persisted but does not have an ecology policy yet, by design.
- Habitat IDs are stable once saved. Duplicates are diagnosed, not automatically renumbered (to avoid accidental data loss); validate behavior across transfers and pocket-map lifecycle later.
- The existence of a dev-only species Def does not mean a physical Gu can be spawned. Slice 2 stores cohorts only.
- No claim that a missing add-on species is simulated: records hold the species string and remain inert unless a matching behavior is registered.

## Failure report

Send the first compiler error or first Project GU red log line, plus whether the save was fresh or from 0.2.4. For behavioral mismatch report expected/observed counts, spring count, whether flora was present, and whether the issue happened before or after a save/load.
