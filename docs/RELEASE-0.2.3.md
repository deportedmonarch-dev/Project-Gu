# 0.2.3 — Mortal visibility and first habitat dependency

## Implemented
- New mortal/unawakened and mortal/no aptitude Health Hediffs. Hidden aptitude remains undisclosed.
- The awakening service updates the Health status immediately after success or failure.
- Existing 600-tick discovery reconciles older records to the visible status (no new scans).
- `GU_MoonOrchidHabitat`: an individually placeable habitat marker, with a supplied original sprite.
- Spirit springs count nearby moon orchid habitat patches (radius 5) once each game day. Hope Gu now only replenish (+1 per game day, maximum 3) if >=1 patch is nearby. Existing spring production and save keys are retained.
- Long-term architecture documents for native Ideology ritual adapter, Gu ecology, golem integration, and underground Strata / Deep And Deeper decision.

## NOT implemented yet
- An actual Ideology RitualDef / multiplayer ceremonial gathering. The existing Core-only right-click awakening job remains. Ritual DLC integration needs separate test against Ideology game binaries.
- Moon orchids naturally growing/reproducing; currently habitat patches are spawned using Dev Mode. This is a first ecological dependency, NOT a full biome.
- Spawn of individual Hope Gu or clan-specific Gu. These remain future Gu ecology work.
- Strata/Deep And Deeper cross-map travel or Epochs Golems bridge.

## Compatibility
RimWorld 1.7; retains the same packageId, aperture, records, stone, spring definitions and save keys. New optional save keys default safely on legacy saves. No Harmony patches or additional hard dependencies.

## Quick test
1. Compile and install as before; use Core + Project GU.
2. On a new Crashlanded scenario, Health tab should show `mortal (unawakened)` rather than an aperture.
3. Spawn `GU_SpiritSpring` and a `GU_MoonOrchidHabitat` nearby (within 5 cells). Inspect spring, allow one day: it should report a habitat count >= 1.
4. Perform awakening, confirm the new Health status changes to `cultivation aperture (open)` on success or `mortal (no aptitude)` on failure.
5. After using all 3 Hope charges, advance a day with the habitat present: +1 Hope. Remove all patches, deplete Hope, wait another day: no replenishment.
6. Check stone production, hauling, save/load, and old awakened pawns. Any failure: send startup/game log and build output.

Note: if a loaded spring had its next Hope regeneration already scheduled, the timer is preserved. Check after a full in-game day rather than instantly.
