# Project GU 0.2.1 — Spirit Spring Smoke Test (RimWorld 1.7)

1. **Back up** the 0.2.0 save. Extract, build and install 0.2.1; restart RimWorld completely.
2. With **Core + Project GU only**, load a disposable test map in Development Mode.
3. Debug Actions → **Spawn thing** → search `spirit spring (prototype)` or `GU_SpiritSpring`; place one spring on clear terrain.
4. Select the spring. Its inspect text should show total produced and the next cycle; watch for red log errors.
5. Run **one full in-game day (60,000 ticks)** after the spring begins ticking. Inspect nearby terrain for a physical `primeval stone` item. No material should generate in the first instant.
6. Run another day; confirm stones increase **once per cycle**, not every tick. Move/pick up stone items normally.
7. Save and reload; verify the spring's lifetime production number does not reset and that no immediate duplicate stone appears.
8. Recheck 0.2.0 awakening and legacy-save behavior for regressions.

## Constraints of this early implementation

- The map must be active. There is **no** off-map spring production or retroactive catch-up.
- Spawning the spring uses RimWorld's developer tools; there is no discovery, natural generation, ownership, or interaction yet.
- Stone value and production rate are temporary balance placeholders.
- Should placement fail due to blocked terrain, that production cycle is skipped; it never retries continuously.
- The build is source-validated only here; compile it against your installed RimWorld 1.7 before installing. If the game logs XML errors, send the log and stop testing.
