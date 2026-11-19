# Prototype 0.2.4 — Spirit Spring Ecology Foundations

## What changed
- 0.2.3 pawn status, ceremonies, stone items and installed texture paths remain unchanged.
- Adds saved finite spring reserve, configurable purity and region-profile metadata; **regional modifiers are NOT automated yet**.
- Adds a generic spirit flora support component; both the pre-existing moon orchid habitat and the new spirit-grass habitat contribute support. No actual plant growth or spread yet.
- Changes Hope Gu availability from three fixed replenishing charges to saved **mature/juvenile population** with bounded carrying capacity.
- Each ecological day: juveniles mature up to available capacity; if flora exists, one juvenile is born up to carrying capacity. Initially a spring has three mature Gu, zero juveniles.
- One mature Hope Gu is consumed per completed awakening. Failed attempts consume Gu too; interrupted jobs do not.
- Event-driven habitat refresh when subscribed flora patches spawn or despawn; infrequent 10,000-tick audit handles external changes. No mapwide tick scan.
- Adds developer-only spring gizmo 'GU: simulate ecology day' (does not advance game time or stone production).

## Save compatibility
Old 'guHopeGuAvailable' is read and migrated once to 'guMatureHope024'. Old world record and hediff defNames remain. Production counters and their due ticks remain unchanged. New reserve initializes for old springs. Do not use saves made with 0.2.4 in 0.2.3.

## Deliberately NOT included
- Visible wandering Gu creatures, natural flora propagation, underground pocket maps, off-map ecology, automatic Southern Border/Northern Plains/Eastern Sea/Western Desert geographical assignment, region-specific depletion behavior, clan Gu species, true book-scale stone economy or native Ideology rituals.
- The region profile is currently **configuration metadata only**, default SouthernBorder. Purity is displayed, not yet a production multiplier.
- Reserve amount is a test placeholder, not canonical lifespan calibration.

## Build
From project directory in Windows PowerShell:
`Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`
`./scripts/verify.ps1` (runs structural checks and compiles against your installed RimWorld 1.7)
If successful: `./scripts/install.ps1`. Restart the game.

## Acceptance tests (use Core + Project GU only, disposable save)
1. Build, load: no new red errors and old pawns retain their prior statuses and apertures.
2. Spawn spring; note 3 mature, 0 juvenile, capacity 3 without flora; production unchanged.
3. Spawn Moon Orchid Habitat within 5 cells; inspect should update without waiting minutes; capacity becomes 5, support 1.
4. Spawn generic Spirit Grass Habitat within 5 cells; capacity becomes 9 (support 3 total). Both species count.
5. Use 'GU: simulate ecology day' once: juvenile becomes 1, mature stays 3. Once more: mature becomes 4, juvenile 1. Repeated steps do not exceed capacity.
6. Remove both flora patches: spring status switches to no reproduction quickly. Future developer-day steps should not create juveniles; existing juveniles MAY mature.
7. Complete an awakening: mature count decreases by exactly 1. Interrupt another attempt: unchanged.
8. Save/reload: mature, juvenile, reserve, stone count and pawn states unchanged. No duplicated Hope Gu or stones.
9. Leave spring active for one actual production day: physical primeval stone produced, selectable/haulable, reserve decreases by 1. Verify no errors.
10. Legacy save from 0.2.3: spring with depleted Hope Gu migrates mature count, keeps lifetime-produced and production due time. Back up the save first.

## QA gates
Static and XML validation run here. Full C# compile and RimWorld playtest **cannot** run without the user's 1.7 assemblies; don't mark 0.2.4 validated until they pass.
